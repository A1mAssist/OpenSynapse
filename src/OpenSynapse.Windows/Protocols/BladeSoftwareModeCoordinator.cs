using System.Collections.Concurrent;

namespace OpenSynapse.Windows.Protocols;

internal sealed class BladeSoftwareModeCoordinator
{
    private readonly ConcurrentDictionary<string, DeviceState> _devices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? _diagnostic;
    private readonly TimeSpan _releaseGrace;

    internal BladeSoftwareModeCoordinator(Action<string>? diagnostic = null)
        : this(diagnostic, TimeSpan.Zero)
    {
    }

    internal BladeSoftwareModeCoordinator(Action<string>? diagnostic, TimeSpan releaseGrace)
    {
        _diagnostic = diagnostic;
        _releaseGrace = releaseGrace < TimeSpan.Zero ? TimeSpan.Zero : releaseGrace;
    }

    internal async Task<BladeSoftwareModeLease> AcquireAsync(
        string devicePath,
        Func<CancellationToken, Task> enterSoftwareMode,
        Func<Task> restoreNormalMode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentNullException.ThrowIfNull(enterSoftwareMode);
        ArgumentNullException.ThrowIfNull(restoreNormalMode);

        // ponytail: attached HID paths are bounded; retain their tiny gate objects.
        var state = _devices.GetOrAdd(devicePath, static _ => new DeviceState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ownerCount = state.OwnerCount;
            if (ownerCount == 0)
            {
                try
                {
                    // Idempotent: a USB reset during the grace window may have
                    // returned the device to normal mode.
                    await enterSoftwareMode(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Log($"acquire {devicePath}: software mode failed; owner=0: {Describe(exception)}");
                    throw;
                }

                if (state.PendingRestore is { } pendingRestore)
                {
                    state.PendingRestore = null;
                    pendingRestore.Cancel();
                    pendingRestore.Dispose();
                }
            }

            state.OwnerCount = ownerCount + 1;
            Log(ownerCount == 0
                ? $"acquire {devicePath}: owner=1; software mode"
                : $"acquire {devicePath}: owner={state.OwnerCount}; software mode already active");

            return new BladeSoftwareModeLease(this, devicePath, state);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>
    /// Registers a restore action that does not depend on a lease-owned HID session.
    /// The delayed restore can run after the runtime that released the last lease
    /// has already disposed its session.
    /// </summary>
    internal void SetRestoreHandler(string devicePath, Func<Task> restoreNormalMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentNullException.ThrowIfNull(restoreNormalMode);
        var state = _devices.GetOrAdd(devicePath, static _ => new DeviceState());
        state.RestoreHandler = restoreNormalMode;
    }

    private async ValueTask ReleaseAsync(
        BladeSoftwareModeLease lease,
        Func<Task> restoreNormalMode)
    {
        if (!lease.TryBeginRelease())
        {
            return;
        }

        await lease.State.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var remaining = --lease.State.OwnerCount;
            if (remaining != 0)
            {
                Log($"release {lease.DevicePath}: owner={remaining}; software mode remains");
                return;
            }

            if (_releaseGrace == TimeSpan.Zero)
            {
                try
                {
                    await restoreNormalMode().ConfigureAwait(false);
                    Log($"release {lease.DevicePath}: owner=0; normal mode");
                }
                catch (Exception exception)
                {
                    Log($"release {lease.DevicePath}: normal mode failed; owner=0; device may still be in software mode: {Describe(exception)}");
                    throw;
                }
                return;
            }

            lease.State.PendingRestore?.Cancel();
            lease.State.PendingRestore?.Dispose();
            var pending = new CancellationTokenSource();
            lease.State.PendingRestore = pending;
            lease.State.RestoreNormalMode = lease.State.RestoreHandler ?? restoreNormalMode;
            _ = RestoreAfterGraceAsync(lease.DevicePath, lease.State, pending);
            Log($"release {lease.DevicePath}: owner=0; software mode held for {_releaseGrace.TotalMilliseconds:0} ms");
        }
        finally
        {
            lease.State.Gate.Release();
        }
    }

    /// <summary>
    /// Restores normal mode immediately, without waiting for the release grace.
    /// App shutdown must call this so the firmware is not left in software mode.
    /// </summary>
    internal async Task RestoreAllAsync()
    {
        foreach (var pair in _devices)
        {
            var state = pair.Value;
            await state.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (state.OwnerCount != 0 || state.PendingRestore is not { } pending)
                {
                    continue;
                }

                state.PendingRestore = null;
                var restore = state.RestoreNormalMode;
                state.RestoreNormalMode = null;
                pending.Cancel();
                if (restore is not null)
                {
                    try
                    {
                        await restore().ConfigureAwait(false);
                        Log($"release {pair.Key}: owner=0; normal mode");
                    }
                    catch (Exception exception)
                    {
                        Log($"release {pair.Key}: normal mode failed; owner=0; device may still be in software mode: {Describe(exception)}");
                    }
                }

                pending.Dispose();
            }
            finally
            {
                state.Gate.Release();
            }
        }
    }

    private async Task RestoreAfterGraceAsync(
        string devicePath,
        DeviceState state,
        CancellationTokenSource pending)
    {
        try
        {
            await Task.Delay(_releaseGrace, pending.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await state.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (state.OwnerCount != 0 || !ReferenceEquals(state.PendingRestore, pending))
            {
                return;
            }

            var restore = state.RestoreNormalMode;
            state.PendingRestore = null;
            state.RestoreNormalMode = null;
            if (restore is null)
            {
                return;
            }

            try
            {
                await restore().ConfigureAwait(false);
                Log($"release {devicePath}: owner=0; normal mode");
            }
            catch (Exception exception)
            {
                Log($"release {devicePath}: normal mode failed; owner=0; device may still be in software mode: {Describe(exception)}");
            }
        }
        finally
        {
            state.Gate.Release();
            pending.Dispose();
        }
    }

    private void Log(string message)
    {
        try
        {
            _diagnostic?.Invoke(message);
        }
        catch
        {
            // Diagnostics must not interfere with mode ownership.
        }
    }

    private static string Describe(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";

    internal sealed class DeviceState
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal int OwnerCount { get; set; }
        internal CancellationTokenSource? PendingRestore { get; set; }
        internal Func<Task>? RestoreNormalMode { get; set; }
        internal Func<Task>? RestoreHandler { get; set; }
    }

    internal sealed class BladeSoftwareModeLease(
        BladeSoftwareModeCoordinator owner,
        string devicePath,
        DeviceState state)
    {
        private int _released;

        internal string DevicePath { get; } = devicePath;
        internal DeviceState State { get; } = state;

        internal ValueTask ReleaseAsync(Func<Task> restoreNormalMode)
        {
            ArgumentNullException.ThrowIfNull(restoreNormalMode);
            return owner.ReleaseAsync(this, restoreNormalMode);
        }

        internal bool TryBeginRelease() =>
            Interlocked.Exchange(ref _released, 1) == 0;
    }
}
