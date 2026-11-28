using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Bounds the caller's wait and requests cancellation of synchronous I/O.
/// A driver may ignore cancellation; onPending transfers cleanup of that operation to its owner.
/// </summary>
internal static class CancellableNativeCall
{
    private static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(1);

    internal static Task<T> RunAsync<T>(
        Func<T> call,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<Task>? onPending = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        return RunAsync(_ => call(), timeout, cancellationToken, onPending);
    }

    internal static async Task<T> RunAsync<T>(
        Func<CancellationToken, T> call,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<Task>? onPending = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        cancellationToken.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var sync = new object();
        uint threadId = 0;
        var interrupted = false;
        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operationToken = operation.Token;
        // A pool thread, not LongRunning: this helper also runs on every lighting frame,
        // and creating an OS thread per frame costs more than the write itself.
        var task = Task.Run(
            () =>
            {
                lock (sync)
                {
                    if (interrupted) throw new OperationCanceledException();
                    threadId = NativeMethods.GetCurrentThreadId();
                }
                try { return call(operationToken); }
                finally
                {
                    // Do not cancel a pool thread after it has moved to another operation.
                    lock (sync) threadId = 0;
                }
            });

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var completed = await Task.WhenAny(task, Task.Delay(timeout, deadline.Token))
                .ConfigureAwait(false);
            if (ReferenceEquals(completed, task))
            {
                deadline.Cancel();
                return await task.ConfigureAwait(false);
            }

            operation.Cancel();
            lock (sync)
            {
                interrupted = true;
                CancelSynchronousIo(threadId);
            }
            try { await task.WaitAsync(CancelGrace).ConfigureAwait(false); }
            catch (Exception) { /* Preserve the timeout/cancellation which initiated cleanup. */ }
            if (!task.IsCompleted)
            {
                onPending?.Invoke(task);
                _ = task.ContinueWith(completedTask => _ = completedTask.Exception,
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"The HID call did not answer within {timeout.TotalMilliseconds:0} ms.");
        }
        finally
        {
            if (task.IsCompleted) operation.Dispose();
            else _ = task.ContinueWith(_ => operation.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    internal static uint CurrentThreadId => NativeMethods.GetCurrentThreadId();

    internal static void CancelSynchronousIo(uint threadId)
    {
        if (threadId == 0)
        {
            return;
        }

        using var thread = NativeMethods.OpenThread(NativeMethods.ThreadTerminate, false, threadId);
        if (!thread.IsInvalid)
        {
            NativeMethods.CancelSynchronousIo(thread);
        }
    }

    private static class NativeMethods
    {
        internal const uint ThreadTerminate = 0x0001;

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeWaitHandle OpenThread(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CancelSynchronousIo(SafeWaitHandle threadHandle);
    }
}
