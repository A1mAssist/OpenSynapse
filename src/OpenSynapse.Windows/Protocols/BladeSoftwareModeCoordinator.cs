using System.Collections.Concurrent;

namespace OpenSynapse.Windows.Protocols;

internal sealed class BladeSoftwareModeCoordinator
{
    private readonly ConcurrentDictionary<string, DeviceState> _devices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? _diagnostic;

    internal BladeSoftwareModeCoordinator(Action<string>? diagnostic = null)
    {
        _diagnostic = diagnostic;
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
                    await enterSoftwareMode(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Log($"acquire {devicePath}: software mode failed; owner=0: {Describe(exception)}");
                    throw;
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
            if (remaining == 0)
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
            }
            else
            {
                Log($"release {lease.DevicePath}: owner={remaining}; software mode remains");
            }
        }
        finally
        {
            lease.State.Gate.Release();
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
