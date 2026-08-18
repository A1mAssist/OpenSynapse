using System.Collections.Concurrent;

namespace OpenSynapse.Windows.Protocols;

internal sealed class BladeSoftwareModeCoordinator
{
    private readonly ConcurrentDictionary<string, DeviceState> _devices =
        new(StringComparer.OrdinalIgnoreCase);

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
            state.OwnerCount++;
            try
            {
                await enterSoftwareMode(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                state.OwnerCount--;
                if (state.OwnerCount == 0)
                {
                    try
                    {
                        await restoreNormalMode().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Preserve the takeover failure as the useful error.
                    }
                }
                throw;
            }

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
            if (--lease.State.OwnerCount == 0)
            {
                await restoreNormalMode().ConfigureAwait(false);
            }
        }
        finally
        {
            lease.State.Gate.Release();
        }
    }

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
