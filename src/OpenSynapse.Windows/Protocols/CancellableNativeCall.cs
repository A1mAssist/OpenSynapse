using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Runs a blocking HID call on a worker thread and actually stops it at the deadline.
/// WaitAsync alone leaves the native request queued in the HID driver, and every later
/// request queues behind it; on 0555 that turned a 250 ms status probe into minute-long stalls.
/// </summary>
internal static class CancellableNativeCall
{
    private static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(1);

    internal static async Task<T> RunAsync<T>(
        Func<T> call,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        cancellationToken.ThrowIfCancellationRequested();
        var started = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A pool thread, not LongRunning: this helper also runs on every lighting frame,
        // and creating an OS thread per frame costs more than the write itself.
        var task = Task.Run(
            () =>
            {
                started.TrySetResult(NativeMethods.GetCurrentThreadId());
                return call();
            });

        var completed = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken))
            .ConfigureAwait(false);
        if (ReferenceEquals(completed, task))
        {
            return await task.ConfigureAwait(false);
        }

        await CancelAsync(started, task).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException(
            $"The HID call did not answer within {timeout.TotalMilliseconds:0} ms.");
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

    private static async Task CancelAsync(TaskCompletionSource<uint> started, Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }

        // The thread id is published before the blocking call starts, so OpenThread can
        // reach the thread even when the call itself never returns.
        if (await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromMilliseconds(100)))
                .ConfigureAwait(false) == started.Task)
        {
            CancelSynchronousIo(started.Task.Result);
        }

        // Let the native call unwind before its handle is closed by the caller. Disposing
        // a handle under a live IOCTL is what left the request in the driver queue.
        try
        {
            await task.WaitAsync(CancelGrace).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
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
