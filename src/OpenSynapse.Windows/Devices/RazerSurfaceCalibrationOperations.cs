namespace OpenSynapse.Windows.Devices;

/// <summary>Owns a complete calibration workflow without holding a HID transaction.</summary>
public static class RazerSurfaceCalibrationOperations
{
    private static readonly object Gate = new();
    private static readonly List<Lease> Active = [];
    private static TaskCompletionSource _changed = Signal();

    // Raised outside Gate on the acquiring/releasing thread. UI subscribers must dispatch
    // and re-read IsBusy because another workflow may acquire before the notification runs.
    public static event Action<RazerSurfaceCalibrationIdentity>? AvailabilityChanged;

    public static bool IsBusy(RazerSurfaceCalibrationIdentity identity)
    {
        Validate(identity);
        lock (Gate) return IsBusyCore(identity);
    }

    /// <summary>Waits for the exact device. Keep the lease through verification, rollback and saving.</summary>
    public static async ValueTask<IDisposable> AcquireAsync(RazerSurfaceCalibrationIdentity identity,
        CancellationToken cancellationToken = default)
    {
        Validate(identity);
        while (true)
        {
            Lease? lease;
            Task changed;
            lock (Gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lease = TryAcquireCore(identity);
                changed = _changed.Task;
            }
            if (lease is not null)
            {
                Notify(identity);
                return lease;
            }
            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Manual entry fails before I/O if another workflow already owns the device.</summary>
    public static IDisposable? TryAcquire(RazerSurfaceCalibrationIdentity identity)
    {
        Validate(identity);
        Lease? lease;
        lock (Gate) lease = TryAcquireCore(identity);
        if (lease is not null) Notify(identity);
        return lease;
    }

    private static Lease? TryAcquireCore(RazerSurfaceCalibrationIdentity identity)
    {
        if (IsBusyCore(identity)) return null;
        var lease = new Lease(identity);
        Active.Add(lease);
        return lease;
    }

    private static bool IsBusyCore(RazerSurfaceCalibrationIdentity identity) => Active.Any(lease =>
        StringComparer.OrdinalIgnoreCase.Equals(lease.Identity.InstanceId, identity.InstanceId) ||
        !string.IsNullOrWhiteSpace(identity.ControlDevicePath) &&
        StringComparer.OrdinalIgnoreCase.Equals(lease.Identity.ControlDevicePath, identity.ControlDevicePath));

    private static void Validate(RazerSurfaceCalibrationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.InstanceId);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Notify(RazerSurfaceCalibrationIdentity identity)
    {
        if (AvailabilityChanged is not { } handlers) return;
        foreach (Action<RazerSurfaceCalibrationIdentity> handler in handlers.GetInvocationList())
        {
            try { handler(identity); }
            catch (Exception) { /* An availability observer cannot abandon device ownership. */ }
        }
    }

    private sealed class Lease(RazerSurfaceCalibrationIdentity identity) : IDisposable
    {
        public RazerSurfaceCalibrationIdentity Identity { get; } = identity;

        public void Dispose()
        {
            TaskCompletionSource changed;
            lock (Gate)
            {
                if (!Active.Remove(this)) return;
                changed = _changed;
                _changed = Signal();
            }
            changed.TrySetResult();
            Notify(Identity);
        }
    }
}
