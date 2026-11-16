using System.Diagnostics;
using OpenSynapse.Core.Diagnostics;

namespace OpenSynapse.App.Runtime;

/// <summary>Opt-in memory snapshots; set OPENSSYNAPSE_MEMORY_TRACE=1 to enable.</summary>
internal static class MemoryTrace
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("OPENSSYNAPSE_MEMORY_TRACE") == "1";
    private static readonly object Gate = new();
    private static LocalDiagnosticLog? _log;
    private static Timer? _timer;
    private static int _mappingVisuals;

    public static int PageCacheCount { get; set; }
    public static int DeviceImageCacheCount { get; set; }
    public static int MappingVisualCount => Volatile.Read(ref _mappingVisuals);

    public static void Start(LocalDiagnosticLog log)
    {
        if (!Enabled)
        {
            return;
        }

        lock (Gate)
        {
            _log = log;
            _timer ??= new Timer(
                _ => Snapshot("periodic"),
                null,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30));
        }

        Snapshot("startup");
    }

    public static void MappingVisualLoaded() => Interlocked.Increment(ref _mappingVisuals);

    public static void MappingVisualReleased() => Interlocked.Decrement(ref _mappingVisuals);

    public static void Snapshot(string reason)
    {
        if (!Enabled || _log is not { } log)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        const double megabyte = 1024 * 1024;
        log.TryWrite(
            "memory",
            $"reason={reason} workingSet={process.WorkingSet64 / megabyte:0.0}MB " +
            $"private={process.PrivateMemorySize64 / megabyte:0.0}MB " +
            $"managed={GC.GetTotalMemory(false) / megabyte:0.0}MB " +
            $"pages={PageCacheCount} cardImages={DeviceImageCacheCount} " +
            $"mappingVisuals={MappingVisualCount} " +
            $"gc0={GC.CollectionCount(0)} gc1={GC.CollectionCount(1)} gc2={GC.CollectionCount(2)}");
    }
}
