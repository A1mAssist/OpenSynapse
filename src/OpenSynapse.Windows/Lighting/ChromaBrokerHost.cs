using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Receives decoded frames from the elevated Chroma shared-memory broker.
/// The broker is the pipe client because it must run outside the game process.
/// </summary>
internal sealed class ChromaBrokerHost : IAsyncDisposable
{
    private readonly Func<ChromaSdkEffect, CancellationToken, Task<bool>> _applyEffect;
    private readonly Func<Task> _restoreLighting;
    private readonly Func<bool> _restoreEnabled;
    private readonly Action<string>? _log;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _acceptLoop;
    private NamedPipeServerStream? _listener;
    private readonly ConcurrentDictionary<ChromaSdkDeviceType, ChromaSdkEffect> _latest = new();
    private int _sessionApplied;
    private int _connected;
    private int _disposed;

    internal ChromaBrokerHost(
        Func<ChromaSdkEffect, CancellationToken, Task<bool>> applyEffect,
        Func<Task> restoreLighting,
        Func<bool>? restoreEnabled = null,
        string pipeName = ChromaBrokerProtocol.PipeName,
        Action<string>? log = null)
    {
        _applyEffect = applyEffect ?? throw new ArgumentNullException(nameof(applyEffect));
        _restoreLighting = restoreLighting ?? throw new ArgumentNullException(nameof(restoreLighting));
        _restoreEnabled = restoreEnabled ?? (() => true);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _log = log;
    }

    internal bool IsRunning => _acceptLoop is not null;
    internal bool IsConnected => Volatile.Read(ref _connected) != 0;
    internal bool IsServingElsewhere { get; private set; }

    internal Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_acceptLoop is not null) return Task.CompletedTask;
        try
        {
            _listener = CreateListener();
            _acceptLoop = AcceptLoopAsync(_listener);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // A previous or elevated instance may still own the machine-wide pipe while
            // it is handing over. Starting a second broker would fail on the same name.
            IsServingElsewhere = true;
            _acceptLoop = Task.CompletedTask;
            _log?.Invoke($"pipe {_pipeName} is already in use; another instance owns the Chroma broker.");
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _listener?.Dispose();
        var loop = _acceptLoop;
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (Exception) when (_stop.IsCancellationRequested) { }
        }
        _stop.Dispose();
    }

    private NamedPipeServerStream CreateListener()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is { } user)
        {
            security.AddAccessRule(new PipeAccessRule(
                user,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
        }
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            security);
    }

    private async Task AcceptLoopAsync(NamedPipeServerStream listener)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await listener.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                Volatile.Write(ref _connected, 1);
                Interlocked.Exchange(ref _sessionApplied, 0);
                _latest.Clear();
                _log?.Invoke("broker connected");
                using var applyStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                var applyLoop = ApplyLoopAsync(applyStop.Token);
                try
                {
                    while (true)
                    {
                        var message = ChromaSdkPipe.Read(listener, _stop.Token);
                        if (message is ChromaSdkSetEffectMessage set)
                        {
                            _latest[set.Value.DeviceType] = set.Value;
                        }
                        else if (message is ChromaSdkByeMessage)
                        {
                            break;
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is EndOfStreamException or IOException or ObjectDisposedException or OperationCanceledException)
                {
                }
                finally
                {
                    applyStop.Cancel();
                    try
                    {
                        await applyLoop.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    _latest.Clear();
                    Volatile.Write(ref _connected, 0);
                    _log?.Invoke("broker disconnected");
                    if (Volatile.Read(ref _sessionApplied) != 0 && _restoreEnabled())
                    {
                        await _restoreLighting().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested) { }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                _log?.Invoke($"broker listen failed: {exception.Message}");
            }
            finally
            {
                listener.Dispose();
            }

            if (_stop.IsCancellationRequested) return;
            listener = CreateListener();
        }
    }

    private async Task ApplyLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(40));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var type in _latest.Keys.ToArray())
                {
                    if (!_latest.TryRemove(type, out var effect)) continue;
                    // Receiving a frame is enough to require a restore on Bye: a
                    // timed-out write may still have reached part of the device.
                    Interlocked.Exchange(ref _sessionApplied, 1);
                    try
                    {
                        await _applyEffect(effect, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        _log?.Invoke($"frame apply failed: {exception}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
