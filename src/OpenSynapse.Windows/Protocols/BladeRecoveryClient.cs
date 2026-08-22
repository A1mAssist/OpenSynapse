using System.Diagnostics;

namespace OpenSynapse.Windows.Protocols;

public sealed class BladeRecoveryClient : IAsyncDisposable
{
    private readonly Process _host;
    private readonly EventWaitHandle _shutdown;
    private readonly BladeRecoverySharedState _state;
    private readonly string _markerPath;
    private readonly BladeRecoveryMarker _marker;
    private readonly Task _completion;
    private int _completed;

    private BladeRecoveryClient(Process host, EventWaitHandle shutdown, BladeRecoverySharedState state,
        string markerPath, BladeRecoveryMarker marker)
    {
        _host = host; _shutdown = shutdown; _state = state; _markerPath = markerPath; _marker = marker;
        _completion = host.WaitForExitAsync();
    }

    public Task Completion => _completion;

    public static async Task<BladeRecoveryClient> StartAsync(string recoveryHostPath, string markerPath,
        string featureDevicePath, string filterDevicePath, TimeSpan readyTimeout,
        CancellationToken cancellationToken = default)
    {
        if (readyTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(readyTimeout));
        markerPath = Path.GetFullPath(markerPath);
        BladeRecoveryMarker marker;
        await using (await AcquireMarkerGateAsync(markerPath, readyTimeout, cancellationToken)
                         .ConfigureAwait(false))
        {
            await EnsurePreviousMarkerClearedAsync(
                markerPath,
                TimeSpan.FromSeconds(2),
                IsOriginalOwnerAlive,
                stale => BladeRecoveryCoordinator.RecoverAsync(stale, []),
                cancellationToken).ConfigureAwait(false);
            marker = new BladeRecoveryMarker(BladeRecoveryProtocol.CurrentMarkerVersion,
                Environment.ProcessId, featureDevicePath, filterDevicePath, DateTimeOffset.UtcNow);
            await BladeRecoveryProtocol.WriteMarkerAtomicAsync(markerPath, marker, cancellationToken)
                .ConfigureAwait(false);
        }
        var id = Guid.NewGuid();
        var readyName = BladeRecoveryProtocol.CreateObjectName("RecoveryReady", id);
        var shutdownName = BladeRecoveryProtocol.CreateObjectName("RecoveryShutdown", id);
        var stateName = BladeRecoveryProtocol.CreateObjectName("RecoveryKeys", id);
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
        var shutdown = new EventWaitHandle(false, EventResetMode.ManualReset, shutdownName);
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(Path.GetFullPath(recoveryHostPath))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var value in new[] { "--marker", Path.GetFullPath(markerPath), "--ready-event", readyName,
                         "--shutdown-event", shutdownName, "--shared-state", stateName })
                start.ArgumentList.Add(value);
            process = Process.Start(start) ?? throw new InvalidOperationException("RecoveryHost did not start.");
            var readyTask = WaitOneAsync(ready, cancellationToken);
            var exitTask = process.WaitForExitAsync(cancellationToken);
            var completed = await Task.WhenAny(readyTask, exitTask, Task.Delay(readyTimeout, cancellationToken)).ConfigureAwait(false);
            if (completed != readyTask || !await readyTask.ConfigureAwait(false))
                throw new InvalidOperationException("RecoveryHost exited or timed out before ready.");
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"RecoveryHost exited with code {process.ExitCode} immediately after ready.");
            return new(process, shutdown, BladeRecoverySharedState.OpenExisting(stateName),
                Path.GetFullPath(markerPath), marker);
        }
        catch
        {
            await DeleteMatchingMarkerUnderGateAsync(Path.GetFullPath(markerPath), marker)
                .ConfigureAwait(false);
            shutdown.Set();
            if (process is not null)
            {
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                catch (TimeoutException) { }
                process.Dispose();
            }
            shutdown.Dispose();
            throw;
        }
    }

    public void UpdateSyntheticKeys(IReadOnlyCollection<BladeRecoverySyntheticKey> keys) => _state.Write(keys);

    public async Task CompleteNormalShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0) return;
        try
        {
            if (!await DeleteMatchingMarkerUnderGateAsync(
                    _markerPath,
                    _marker,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Recovery marker ownership changed before normal shutdown.");
            }
            _shutdown.Set();
            await _host.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (_host.ExitCode != 0) throw new InvalidOperationException($"RecoveryHost exited with code {_host.ExitCode}.");
        }
        catch
        {
            if (!_host.HasExited) Volatile.Write(ref _completed, 0);
            throw;
        }
    }

    public async Task RecoverAndShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
        {
            return;
        }

        try
        {
            // Keep the marker so RecoveryHost performs Normal Mode and key cleanup
            // before it exits; this is the failure path, not a normal shutdown.
            _shutdown.Set();
            await _host.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (_host.ExitCode != 0)
            {
                throw new InvalidOperationException($"RecoveryHost exited with code {_host.ExitCode}.");
            }
        }
        catch
        {
            if (!_host.HasExited)
            {
                Volatile.Write(ref _completed, 0);
            }
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        _state.Dispose(); _shutdown.Dispose(); _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private static Task<bool> WaitOneAsync(WaitHandle handle, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisteredWaitHandle? registration = null;
        registration = ThreadPool.RegisterWaitForSingleObject(handle,
            static (state, _) => ((TaskCompletionSource<bool>)state!).TrySetResult(true), completion,
            Timeout.InfiniteTimeSpan, executeOnlyOnce: true);
        var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return CompleteAsync();

        async Task<bool> CompleteAsync()
        {
            try { return await completion.Task.ConfigureAwait(false); }
            finally { cancellation.Dispose(); registration.Unregister(null); }
        }
    }

    private static async Task DeleteMatchingMarkerAsync(string path, BladeRecoveryMarker expected)
    {
        try
        {
            if (await BladeRecoveryProtocol.ReadMarkerAsync(path).ConfigureAwait(false) == expected) File.Delete(path);
        }
        catch (FileNotFoundException) { }
    }

    internal static async Task EnsurePreviousMarkerClearedAsync(
        string markerPath,
        TimeSpan waitTime,
        Func<BladeRecoveryMarker, bool> ownerAlive,
        Func<BladeRecoveryMarker, Task> recover,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        ArgumentNullException.ThrowIfNull(ownerAlive);
        ArgumentNullException.ThrowIfNull(recover);
        if (waitTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(waitTime));
        var deadline = DateTimeOffset.UtcNow + waitTime;
        while (File.Exists(markerPath) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        if (!File.Exists(markerPath)) return;

        var stale = await BladeRecoveryProtocol.ReadMarkerAsync(markerPath, cancellationToken).ConfigureAwait(false);
        if (ownerAlive(stale))
            throw new InvalidOperationException("An existing OpenSynapse owner still owns the recovery marker.");
        await recover(stale).ConfigureAwait(false);
        await DeleteMatchingMarkerAsync(markerPath, stale).ConfigureAwait(false);
        if (File.Exists(markerPath))
            throw new InvalidOperationException("The stale recovery marker changed or could not be removed.");
    }

    public static async Task<FileStream> AcquireMarkerGateAsync(
        string markerPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        var fullPath = Path.GetFullPath(markerPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Recovery marker path has no directory.", nameof(markerPath));
        Directory.CreateDirectory(directory);
        var gatePath = $"{fullPath}.lock";
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    gatePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(started) < timeout)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (Stopwatch.GetElapsedTime(started) >= timeout)
            {
                throw new TimeoutException("Timed out waiting for the recovery marker ownership gate.");
            }
        }
    }

    public static async Task<bool> DeleteMatchingMarkerUnderGateAsync(
        string markerPath,
        BladeRecoveryMarker expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        markerPath = Path.GetFullPath(markerPath);
        await using var gate = await AcquireMarkerGateAsync(
                markerPath,
                TimeSpan.FromSeconds(5),
                cancellationToken)
            .ConfigureAwait(false);
        if (!File.Exists(markerPath)) return true;
        var current = await BladeRecoveryProtocol.ReadMarkerAsync(markerPath, cancellationToken)
            .ConfigureAwait(false);
        if (current != expected) return false;
        File.Delete(markerPath);
        return true;
    }

    private static bool IsOriginalOwnerAlive(BladeRecoveryMarker marker)
    {
        try
        {
            using var process = Process.GetProcessById(marker.OwnerPid);
            return !process.HasExited &&
                   process.StartTime.ToUniversalTime() <= marker.StartedAtUtc.UtcDateTime;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
