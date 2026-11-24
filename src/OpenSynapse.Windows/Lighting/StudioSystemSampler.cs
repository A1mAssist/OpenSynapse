using System.Runtime.InteropServices;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.Windows.Lighting;

/// <summary>Local wall clock and one-second Windows CPU/memory samples; no synthetic fallback.</summary>
internal sealed class StudioSystemSampler
{
    private readonly TimeProvider _time;
    private readonly Func<(ulong Idle, ulong Kernel, ulong User)?> _readTimes;
    private readonly Func<double?> _readMemory;
    private readonly object _gate = new();
    private long? _lastPoll;
    private (ulong Idle, ulong Kernel, ulong User)? _previous;
    private double? _cpu;
    private double? _memory;

    internal StudioSystemSampler() : this(TimeProvider.System, ReadTimes, ReadMemory) { }

    internal StudioSystemSampler(TimeProvider time,
        Func<(ulong Idle, ulong Kernel, ulong User)?> readTimes, Func<double?> readMemory)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _readTimes = readTimes ?? throw new ArgumentNullException(nameof(readTimes));
        _readMemory = readMemory ?? throw new ArgumentNullException(nameof(readMemory));
    }

    internal StudioSystemInput Sample()
    {
        lock (_gate)
        {
            var timestamp = _time.GetTimestamp();
            if (_lastPoll is null || _time.GetElapsedTime(_lastPoll.Value, timestamp).TotalSeconds >= 1)
            {
                var current = _readTimes();
                _cpu = null;
                if (current is { } next && _previous is { } previous &&
                    next.Idle >= previous.Idle && next.Kernel >= previous.Kernel && next.User >= previous.User)
                {
                    // GetSystemTimes includes idle in kernel time; count it only once.
                    var total = (double)(next.Kernel - previous.Kernel) + (next.User - previous.User);
                    var idle = (double)(next.Idle - previous.Idle);
                    if (total > 0 && idle <= total) _cpu = (total - idle) / total;
                }
                _previous = current;
                _memory = _readMemory();
                _lastPoll = timestamp;
            }
            return new StudioSystemInput(_time.GetLocalNow(), _cpu) { MemoryLoad = _memory };
        }
    }

    private static (ulong Idle, ulong Kernel, ulong User)? ReadTimes() =>
        GetSystemTimes(out var idle, out var kernel, out var user) ? (idle, kernel, user) : null;

    private static double? ReadMemory()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref memory) ? memory.Load / 100d : null;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
}
