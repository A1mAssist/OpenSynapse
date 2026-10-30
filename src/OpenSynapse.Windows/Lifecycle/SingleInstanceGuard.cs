namespace OpenSynapse.Windows.Lifecycle;

public enum SingleInstanceSignal
{
    None,
    Activate,
    Shutdown,
}

public sealed class SingleInstanceGuard : IDisposable
{
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private EventWaitHandle? _shutdownEvent;
    private EventWaitHandle? _exitedEvent;

    private SingleInstanceGuard(
        Mutex mutex,
        EventWaitHandle activationEvent,
        EventWaitHandle shutdownEvent,
        EventWaitHandle exitedEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _shutdownEvent = shutdownEvent;
        _exitedEvent = exitedEvent;
    }

    public static bool TryAcquire(
        string name,
        out SingleInstanceGuard? guard,
        bool activateExisting = true,
        bool requestExistingShutdown = false,
        TimeSpan? shutdownTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var activationEventName = $"{name}.Activate";
        var shutdownEventName = $"{name}.Shutdown";
        var exitedEventName = $"{name}.Exited";
        var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            activationEventName);
        var shutdownEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            shutdownEventName);
        var exitedEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            exitedEventName);
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name, out createdNew);
        }
        catch
        {
            activationEvent.Dispose();
            shutdownEvent.Dispose();
            exitedEvent.Dispose();
            throw;
        }

        if (!createdNew)
        {
            if (requestExistingShutdown)
            {
                shutdownEvent.Set();
                if (exitedEvent.WaitOne(shutdownTimeout ?? TimeSpan.FromSeconds(10)))
                {
                    var deadline = DateTime.UtcNow + (shutdownTimeout ?? TimeSpan.FromSeconds(10));
                    while (!createdNew && DateTime.UtcNow < deadline)
                    {
                        mutex.Dispose();
                        try
                        {
                            mutex = new Mutex(initiallyOwned: false, name, out createdNew);
                        }
                        catch
                        {
                            activationEvent.Dispose();
                            shutdownEvent.Dispose();
                            exitedEvent.Dispose();
                            throw;
                        }

                        if (!createdNew)
                        {
                            Thread.Sleep(10);
                        }
                    }

                    if (createdNew)
                    {
                        exitedEvent.Reset();
                        guard = new SingleInstanceGuard(mutex, activationEvent, shutdownEvent, exitedEvent);
                        return true;
                    }
                }
            }

            try
            {
                if (activateExisting)
                {
                    activationEvent.Set();
                }
            }
            finally
            {
                activationEvent.Dispose();
                shutdownEvent.Dispose();
                exitedEvent.Dispose();
                mutex.Dispose();
            }

            guard = null;
            return false;
        }

        exitedEvent.Reset();
        guard = new SingleInstanceGuard(mutex, activationEvent, shutdownEvent, exitedEvent);
        return true;
    }

    public bool WaitForActivation(CancellationToken cancellationToken)
    {
        return WaitForSignal(cancellationToken) == SingleInstanceSignal.Activate;
    }

    public SingleInstanceSignal WaitForSignal(CancellationToken cancellationToken)
    {
        var activationEvent = Volatile.Read(ref _activationEvent);
        var shutdownEvent = Volatile.Read(ref _shutdownEvent);
        if (activationEvent is null || shutdownEvent is null)
        {
            return SingleInstanceSignal.None;
        }

        try
        {
            return WaitHandle.WaitAny(
                new[] { activationEvent, shutdownEvent, cancellationToken.WaitHandle }) switch
            {
                0 => SingleInstanceSignal.Activate,
                1 => SingleInstanceSignal.Shutdown,
                _ => SingleInstanceSignal.None,
            };
        }
        catch (ObjectDisposedException)
        {
            return SingleInstanceSignal.None;
        }
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        mutex?.Dispose();

        var exitedEvent = Volatile.Read(ref _exitedEvent);
        try
        {
            exitedEvent?.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        var activationEvent = Interlocked.Exchange(ref _activationEvent, null);
        activationEvent?.Dispose();

        var shutdownEvent = Interlocked.Exchange(ref _shutdownEvent, null);
        shutdownEvent?.Dispose();

        var lifecycleEvent = Interlocked.Exchange(ref _exitedEvent, null);
        lifecycleEvent?.Dispose();
    }
}
