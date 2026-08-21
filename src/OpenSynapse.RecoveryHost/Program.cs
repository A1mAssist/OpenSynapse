using System.Diagnostics;
using OpenSynapse.Windows.Protocols;

return await RecoveryHost.RunAsync(args);

internal static class RecoveryHost
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var marker = await BladeRecoveryProtocol.ReadMarkerAsync(options.MarkerPath).ConfigureAwait(false);
            using var ready = OpenEvent(options.ReadyEvent);
            using var shutdown = OpenEvent(options.ShutdownEvent);
            using var state = BladeRecoverySharedState.CreateOwner(options.SharedState);

            var owner = TryOpenOriginalOwner(marker);
            if (owner is not null)
            {
                using (owner)
                {
                    ready.Set();
                    using var waitCancellation = new CancellationTokenSource();
                    var ownerExit = owner.WaitForExitAsync(waitCancellation.Token);
                    var shutdownRequest = WaitOneAsync(shutdown, waitCancellation.Token);
                    var completed = await Task.WhenAny(ownerExit, shutdownRequest).ConfigureAwait(false);
                    if (completed == shutdownRequest && !File.Exists(options.MarkerPath))
                    {
                        waitCancellation.Cancel();
                        return 0;
                    }
                    await ownerExit.ConfigureAwait(false);
                    waitCancellation.Cancel();
                }
            }

            await using var gate = await BladeRecoveryClient.AcquireMarkerGateAsync(
                    options.MarkerPath,
                    TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            if (!File.Exists(options.MarkerPath)) return 0;
            var current = await BladeRecoveryProtocol.ReadMarkerAsync(options.MarkerPath)
                .ConfigureAwait(false);
            if (current != marker)
            {
                Console.Error.WriteLine("Recovery marker ownership changed; stale host refused to recover the new session.");
                return 6;
            }

            var stateValid = state.TryRead(out var keys);
            if (!stateValid)
            {
                Console.Error.WriteLine("Shared synthetic-key state was invalid; device recovery continued without guessing key releases.");
                keys = [];
            }
            try
            {
                await BladeRecoveryCoordinator.RecoverAsync(marker, keys).ConfigureAwait(false);
                File.Delete(options.MarkerPath);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 5;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static Process? TryOpenOriginalOwner(BladeRecoveryMarker marker)
    {
        try
        {
            var process = Process.GetProcessById(marker.OwnerPid);
            if (process.HasExited || process.StartTime.ToUniversalTime() > marker.StartedAtUtc.UtcDateTime)
            {
                process.Dispose();
                return null;
            }
            return process;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static EventWaitHandle OpenEvent(string name) =>
        new(false, EventResetMode.ManualReset, name);

    private static Task WaitOneAsync(WaitHandle handle, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisteredWaitHandle? registration = null;
        registration = ThreadPool.RegisterWaitForSingleObject(
            handle,
            static (state, _) => ((TaskCompletionSource)state!).TrySetResult(),
            completion,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: true);
        var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return CompleteAsync();

        async Task CompleteAsync()
        {
            try { await completion.Task.ConfigureAwait(false); }
            finally
            {
                cancellation.Dispose();
                registration.Unregister(null);
            }
        }
    }

    private sealed record Options(string MarkerPath, string ReadyEvent, string ShutdownEvent, string SharedState)
    {
        internal static Options Parse(string[] args)
        {
            if (args.Length != 8) throw new ArgumentException("Expected four named RecoveryHost arguments.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index += 2)
            {
                if (args[index] is not ("--marker" or "--ready-event" or "--shutdown-event" or "--shared-state") ||
                    string.IsNullOrWhiteSpace(args[index + 1]) ||
                    !values.TryAdd(args[index], args[index + 1]))
                    throw new ArgumentException("Unknown, missing, or duplicate RecoveryHost argument.");
            }
            if (values.Count != 4) throw new ArgumentException("RecoveryHost arguments are incomplete.");
            var marker = Path.GetFullPath(values["--marker"]);
            BladeRecoveryProtocol.ValidateObjectName(values["--ready-event"], "RecoveryReady");
            BladeRecoveryProtocol.ValidateObjectName(values["--shutdown-event"], "RecoveryShutdown");
            BladeRecoveryProtocol.ValidateObjectName(values["--shared-state"], "RecoveryKeys");
            return new(marker, values["--ready-event"], values["--shutdown-event"], values["--shared-state"]);
        }
    }
}
