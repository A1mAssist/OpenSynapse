namespace OpenSynapse.Windows.Lifecycle;

public sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
    }

    public static bool TryAcquire(string name, out SingleInstanceGuard? guard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var activationEventName = $"{name}.Activate";
        var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            activationEventName);
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name, out createdNew);
        }
        catch
        {
            activationEvent.Dispose();
            throw;
        }

        if (!createdNew)
        {
            try
            {
                activationEvent.Set();
            }
            finally
            {
                activationEvent.Dispose();
                mutex.Dispose();
            }

            guard = null;
            return false;
        }

        guard = new SingleInstanceGuard(mutex, activationEvent);
        return true;
    }

    public bool WaitForActivation(CancellationToken cancellationToken)
    {
        var activationEvent = Volatile.Read(ref _activationEvent);
        if (activationEvent is null)
        {
            return false;
        }

        try
        {
            return WaitHandle.WaitAny(new[] { activationEvent, cancellationToken.WaitHandle }) == 0;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        mutex?.Dispose();

        var activationEvent = Interlocked.Exchange(ref _activationEvent, null);
        activationEvent?.Dispose();
    }
}
