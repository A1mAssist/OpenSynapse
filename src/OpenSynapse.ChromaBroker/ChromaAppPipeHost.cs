using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace OpenSynapse.ChromaBroker;

/// <summary>
/// Chroma SDK 4.1 registers InitSDK app metadata on this pipe before it
/// writes shared-memory frames. The SDK closes the pipe immediately after
/// sending the registration, so the server only needs to accept and drain it.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ChromaAppPipeHost : IDisposable
{
    internal const string PipeName = "88756D22-EF7D-4755-8F3C-FAE2CFE2B34A";
    private const int MaxRegistrationBytes = 8192;
    private const int RegistrationLength = 3600;

    private readonly Action<uint, string>? _registered;
    private readonly SecurityIdentifier? _authorizedUser;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;
    private int _disposed;

    internal ChromaAppPipeHost(
        Action<uint, string>? registered = null,
        SecurityIdentifier? authorizedUser = null)
    {
        _registered = registered;
        _authorizedUser = authorizedUser;
        _acceptLoop = AcceptLoopAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _stop.Cancel();
        try
        {
            _acceptLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
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
                if (_authorizedUser is { } authorizedUser &&
                    (identity.User is not { } currentUser || !authorizedUser.Equals(currentUser)))
                {
                    security.AddAccessRule(new PipeAccessRule(
                        authorizedUser,
                        PipeAccessRights.ReadWrite,
                        AccessControlType.Allow));
                }
                security.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                    PipeAccessRights.FullControl,
                    AccessControlType.Deny));
                await using var pipe = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 4,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    security);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                BrokerLog.Info("app pipe connected.");
                await DrainAsync(pipe, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                BrokerLog.Error($"app registration pipe: {exception.Message}");
                try
                {
                    await Task.Delay(250, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task DrainAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRegistrationBytes];
        var length = 0;
        var processed = false;
        while (length < buffer.Length)
        {
            var read = await pipe.ReadAsync(buffer.AsMemory(length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            length += read;
            if (!processed && length >= RegistrationLength)
            {
                ProcessRegistration(buffer, length);
                processed = true;
            }
        }

        if (!processed && length >= 8)
        {
            ProcessRegistration(buffer, length);
        }
        else if (!processed)
        {
            BrokerLog.Info($"app registration bytes={length}");
        }
    }

    private void ProcessRegistration(byte[] buffer, int length)
    {
        var pid = BitConverter.ToInt32(buffer, 4);
        var title = Encoding.Unicode.GetString(buffer, 8, Math.Min(length - 8, 256));
        var end = title.IndexOf('\0');
        if (end >= 0) title = title[..end];
        BrokerLog.Info($"app registered: {title} pid={pid} bytes={length}");
        if (pid > 0)
        {
            _registered?.Invoke(unchecked((uint)pid), title);
        }
    }
}
