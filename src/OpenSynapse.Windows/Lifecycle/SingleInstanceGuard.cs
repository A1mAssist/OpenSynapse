using System.Diagnostics;

namespace OpenSynapse.Windows.Lifecycle;

public enum SingleInstanceSignal
{
    None,
    Activate,
    Shutdown,
}

/// <summary>
/// An older instance only publishes the activation event, and an elevated instance
/// cannot always be opened from a medium process. The app shows a recovery message
/// instead of exiting silently.
/// </summary>
public sealed class ExistingInstanceNotTakeoverableException : Exception
{
    public ExistingInstanceNotTakeoverableException()
        : base("Another OpenSynapse instance is running without the shutdown channel.")
    {
    }
}

public sealed class SingleInstanceGuard : IDisposable
{
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ShutdownPollInterval = TimeSpan.FromMilliseconds(25);

    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private EventWaitHandle? _shutdownEvent;
    private EventWaitHandle? _exitedEvent;
    private readonly bool _ownsMutex;

    private SingleInstanceGuard(
        Mutex mutex,
        EventWaitHandle activationEvent,
        EventWaitHandle shutdownEvent,
        EventWaitHandle exitedEvent,
        bool ownsMutex)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _shutdownEvent = shutdownEvent;
        _exitedEvent = exitedEvent;
        _ownsMutex = ownsMutex;
    }

    public static bool TryAcquire(
        string name,
        out SingleInstanceGuard? guard,
        bool activateExisting = true,
        bool requestExistingShutdown = false,
        TimeSpan? shutdownTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Mutex mutex;
        bool createdNew;
        try
        {
            // Ownership lets a takeover distinguish a live instance from a crashed one.
            mutex = new Mutex(initiallyOwned: true, name, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ExistingInstanceNotTakeoverableException();
        }

        EventWaitHandle? activationEvent = null;
        EventWaitHandle? shutdownEvent = null;
        EventWaitHandle? exitedEvent = null;
        try
        {
            activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                $"{name}.Activate");
            shutdownEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                $"{name}.Shutdown",
                out var shutdownEventCreated);
            exitedEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.ManualReset,
                $"{name}.Exited");

            if (createdNew)
            {
                shutdownEvent.Reset();
                exitedEvent.Reset();
                guard = new SingleInstanceGuard(
                    mutex, activationEvent, shutdownEvent, exitedEvent, ownsMutex: true);
                return true;
            }

            if (requestExistingShutdown && shutdownEventCreated)
            {
                if (activateExisting)
                {
                    activationEvent.Set();
                }

                throw new ExistingInstanceNotTakeoverableException();
            }

            if (requestExistingShutdown && TryTakeOver(
                    name,
                    mutex,
                    activationEvent,
                    shutdownEvent,
                    exitedEvent,
                    shutdownTimeout ?? DefaultShutdownTimeout,
                    out guard))
            {
                return true;
            }

            if (activateExisting)
            {
                activationEvent.Set();
            }

            activationEvent.Dispose();
            shutdownEvent.Dispose();
            exitedEvent.Dispose();
            mutex.Dispose();
            guard = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            activationEvent?.Dispose();
            shutdownEvent?.Dispose();
            exitedEvent?.Dispose();
            mutex.Dispose();
            throw new ExistingInstanceNotTakeoverableException();
        }
        catch
        {
            activationEvent?.Dispose();
            shutdownEvent?.Dispose();
            exitedEvent?.Dispose();
            mutex.Dispose();
            throw;
        }
    }

    private static bool TryTakeOver(
        string name,
        Mutex mutex,
        EventWaitHandle activationEvent,
        EventWaitHandle shutdownEvent,
        EventWaitHandle exitedEvent,
        TimeSpan timeout,
        out SingleInstanceGuard? guard)
    {
        guard = null;
        Mutex? existing;
        try
        {
            existing = Mutex.OpenExisting(name);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        shutdownEvent.Set();
        var stopwatch = Stopwatch.StartNew();
        var ownsExisting = false;
        var oldExited = false;
        while (stopwatch.Elapsed < timeout && !ownsExisting)
        {
            if (exitedEvent.WaitOne(ShutdownPollInterval))
            {
                oldExited = true;
                break;
            }

            ownsExisting = TryTakeOwnership(existing);
        }

        if (!ownsExisting)
        {
            ownsExisting = TryTakeOwnership(existing);
        }

        if (!ownsExisting && !oldExited)
        {
            existing.Dispose();
            return false;
        }

        mutex.Dispose();
        shutdownEvent.Reset();
        exitedEvent.Reset();
        guard = new SingleInstanceGuard(
            existing, activationEvent, shutdownEvent, exitedEvent, ownsMutex: ownsExisting);
        return true;
    }

    private static bool TryTakeOwnership(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
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
        if (mutex is not null)
        {
            if (_ownsMutex)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Ownership is thread-affine; closing the last handle still frees the name.
                }
            }

            mutex.Dispose();
        }

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
