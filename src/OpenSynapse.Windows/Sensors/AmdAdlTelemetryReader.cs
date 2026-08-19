using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Sensors;

internal sealed class AmdAdlTelemetryReader : IDisposable
{
    private const int Success = 0;
    private const int Integrated = 1 << 1;
    private const int SensorCount = 256;
    private const int GfxClock = 1;
    private const int EdgeTemperature = 8;
    private const int AsicPower = 23;
    private readonly AllocateMemory _allocator = Marshal.AllocCoTaskMem;
    private nint _context;
    private int _adapterIndex = -1;

    internal AmdAdlTelemetryReader()
    {
        try
        {
            if (Adl2MainControlCreate(_allocator, 1, out _context) != Success ||
                Adl2AdapterNumberOfAdaptersGet(_context, out var count) != Success)
            {
                Dispose();
                return;
            }

            for (var index = 0; index < count; index++)
            {
                if (Adl2AdapterAsicFamilyTypeGet(_context, index, out var family, out var valid) == Success &&
                    (family & valid & Integrated) != 0)
                {
                    _adapterIndex = index;
                    break;
                }
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Dispose();
        }
    }

    internal AmdGpuTelemetrySample Read()
    {
        if (_context == nint.Zero || _adapterIndex < 0)
        {
            return default;
        }

        try
        {
            var log = new AmdPmLogData
            {
                Size = Marshal.SizeOf<AmdPmLogData>(),
                Sensors = new AmdSensorData[SensorCount],
            };
            return Adl2NewQueryPmLogDataGet(_context, _adapterIndex, ref log) == Success
                ? Parse(log.Sensors)
                : default;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return default;
        }
    }

    internal static AmdGpuTelemetrySample Parse(IReadOnlyList<AmdSensorData> sensors)
    {
        static int? Read(IReadOnlyList<AmdSensorData> values, int index, int minimum, int maximum) =>
            index < values.Count && values[index].Supported != 0 && values[index].Value >= minimum &&
            values[index].Value <= maximum
                ? values[index].Value
                : null;

        return new AmdGpuTelemetrySample(
            Read(sensors, EdgeTemperature, 1, 125),
            Read(sensors, AsicPower, 1, 1000),
            Read(sensors, GfxClock, 1, 10000));
    }

    public void Dispose()
    {
        var context = Interlocked.Exchange(ref _context, nint.Zero);
        if (context != nint.Zero)
        {
            _ = Adl2MainControlDestroy(context);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint AllocateMemory(int size);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct AmdSensorData
    {
        internal readonly int Supported;
        internal readonly int Value;

        internal AmdSensorData(int supported, int value)
        {
            Supported = supported;
            Value = value;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AmdPmLogData
    {
        internal int Size;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = SensorCount)]
        internal AmdSensorData[] Sensors;
    }

    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Main_Control_Create", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Adl2MainControlCreate(
        AllocateMemory allocator,
        int enumerateConnectedAdapters,
        out nint context);

    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Main_Control_Destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Adl2MainControlDestroy(nint context);

    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Adapter_NumberOfAdapters_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Adl2AdapterNumberOfAdaptersGet(nint context, out int count);

    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Adapter_ASICFamilyType_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Adl2AdapterAsicFamilyTypeGet(
        nint context,
        int adapterIndex,
        out int family,
        out int valid);

    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_New_QueryPMLogData_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Adl2NewQueryPmLogDataGet(
        nint context,
        int adapterIndex,
        ref AmdPmLogData output);
}

internal readonly record struct AmdGpuTelemetrySample(
    int? TemperatureCelsius,
    int? PowerWatts,
    int? ClockMegahertz);
