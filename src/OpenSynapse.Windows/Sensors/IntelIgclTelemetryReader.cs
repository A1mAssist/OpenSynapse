using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Sensors;

internal sealed class IntelIgclTelemetryReader : IDisposable
{
    private const uint IgclSuccess = 0;
    private const uint IgclAlreadyOpen = 1;
    private const uint IgclInitUseLevelZero = 1;
    private const uint IgclAppVersion = 0x00010001;
    private readonly object _gate = new();
    private nint _api;
    private nint _device;
    private nint _temperature;
    private nint _frequency;
    private nint _power;
    private ulong? _previousEnergy;
    private ulong? _previousTimestamp;

    internal IntelIgclTelemetryReader()
    {
        try
        {
            Initialize();
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Dispose();
        }
    }

    internal IntelGpuTelemetrySample Read()
    {
        lock (_gate)
        {
            if (_device == nint.Zero)
            {
                return default;
            }

            try
            {
                return new IntelGpuTelemetrySample(ReadTemperature(), ReadClock(), ReadPower());
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return default;
            }
        }
    }

    internal static (int InitArgs, int FrequencyState, int PowerEnergyCounter) NativeStructSizes =>
        (Marshal.SizeOf<CtlInitArgs>(),
            Marshal.SizeOf<CtlFrequencyState>(),
            Marshal.SizeOf<CtlPowerEnergyCounter>());

    internal static double? CalculatePowerWatts(
        ulong? previousEnergy,
        ulong? previousTimestamp,
        ulong energy,
        ulong timestamp)
    {
        if (previousEnergy is not ulong previousEnergyValue ||
            previousTimestamp is not ulong previousTimestampValue ||
            energy < previousEnergyValue ||
            timestamp <= previousTimestampValue)
        {
            return null;
        }

        var watts = (double)(energy - previousEnergyValue) / (timestamp - previousTimestampValue);
        return double.IsFinite(watts) && watts is >= 0 and <= 500 ? watts : null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            var api = _api;
            _api = _device = _temperature = _frequency = _power = nint.Zero;
            _previousEnergy = _previousTimestamp = null;
            if (api != nint.Zero)
            {
                try
                {
                    _ = ctlClose(api);
                }
                catch (Exception exception) when (
                    exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                }
            }
        }
    }

    private void Initialize()
    {
        // IGCL is installed with the Intel graphics driver and is only available to 64-bit callers.
        var init = new CtlInitArgs
        {
            Size = (uint)Marshal.SizeOf<CtlInitArgs>(),
            AppVersion = IgclAppVersion,
            Flags = IgclInitUseLevelZero,
        };
        if (!IsSuccess(ctlInit(ref init, out var api)) || api == nint.Zero)
        {
            return;
        }

        _api = api;
        var count = 0u;
        if (!IsSuccess(ctlEnumerateDevices(_api, ref count, nint.Zero)) || count == 0)
        {
            return;
        }

        var handles = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        try
        {
            if (!IsSuccess(ctlEnumerateDevices(_api, ref count, handles)) || count == 0)
            {
                return;
            }

            // A laptop with Arc 130T exposes one Intel adapter; use its first telemetry domains.
            _device = Marshal.ReadIntPtr(handles);
        }
        finally
        {
            Marshal.FreeHGlobal(handles);
        }

        if (_device == nint.Zero)
        {
            return;
        }

        _temperature = FirstHandle(ctlEnumTemperatureSensors, _device);
        _frequency = FirstHandle(ctlEnumFrequencyDomains, _device);
        _power = FirstHandle(ctlEnumPowerDomains, _device);
    }

    private static nint FirstHandle(EnumerateHandleList enumerate, nint device)
    {
        var count = 0u;
        if (!IsSuccess(enumerate(device, ref count, nint.Zero)) || count == 0)
        {
            return nint.Zero;
        }

        var handles = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
        try
        {
            return IsSuccess(enumerate(device, ref count, handles)) && count > 0
                ? Marshal.ReadIntPtr(handles)
                : nint.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(handles);
        }
    }

    private double? ReadTemperature()
    {
        if (_temperature == nint.Zero)
        {
            return null;
        }

        return IsSuccess(ctlTemperatureGetState(_temperature, out var value)) &&
            double.IsFinite(value) && value is >= 1 and <= 125
                ? value
                : null;
    }

    private int? ReadClock()
    {
        if (_frequency == nint.Zero)
        {
            return null;
        }

        var state = new CtlFrequencyState { Size = (uint)Marshal.SizeOf<CtlFrequencyState>() };
        if (!IsSuccess(ctlFrequencyGetState(_frequency, ref state)) || !double.IsFinite(state.Actual))
        {
            return null;
        }

        var megahertz = (int)Math.Round(state.Actual);
        return megahertz is > 0 and <= 10000 ? megahertz : null;
    }

    private double? ReadPower()
    {
        if (_power == nint.Zero)
        {
            return null;
        }

        var counter = new CtlPowerEnergyCounter { Size = (uint)Marshal.SizeOf<CtlPowerEnergyCounter>() };
        if (!IsSuccess(ctlPowerGetEnergyCounter(_power, ref counter)))
        {
            _previousEnergy = _previousTimestamp = null;
            return null;
        }

        var power = CalculatePowerWatts(
            _previousEnergy, _previousTimestamp, counter.Energy, counter.Timestamp);
        _previousEnergy = counter.Energy;
        _previousTimestamp = counter.Timestamp;
        return power;
    }

    private static bool IsSuccess(uint result) => result is IgclSuccess or IgclAlreadyOpen;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint EnumerateHandleList(nint device, ref uint count, nint handles);

    [StructLayout(LayoutKind.Sequential)]
    private struct CtlApplicationId
    {
        internal uint Data1;
        internal ushort Data2;
        internal ushort Data3;
        internal uint Data4Low;
        internal uint Data4High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CtlInitArgs
    {
        internal uint Size;
        internal byte Version;
        internal uint AppVersion;
        internal uint Flags;
        internal uint SupportedVersion;
        internal CtlApplicationId ApplicationUid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CtlFrequencyState
    {
        internal uint Size;
        internal byte Version;
        internal double CurrentVoltage;
        internal double Request;
        internal double Tdp;
        internal double Efficient;
        internal double Actual;
        internal uint ThrottleReasons;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CtlPowerEnergyCounter
    {
        internal uint Size;
        internal byte Version;
        internal ulong Energy;
        internal ulong Timestamp;
    }

    [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ctlInit(ref CtlInitArgs init, out nint api);

    [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ctlClose(nint api);

    [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ctlEnumerateDevices(nint api, ref uint count, nint devices);

    [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ctlEnumTemperatureSensors(nint device, ref uint count, nint sensors);

    [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ctlTemperatureGetState(nint sensor, out double temperature);

    [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ctlEnumFrequencyDomains(nint device, ref uint count, nint domains);

    [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ctlFrequencyGetState(nint domain, ref CtlFrequencyState state);

    [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ctlEnumPowerDomains(nint device, ref uint count, nint domains);

    [DllImport("ControlLib.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ctlPowerGetEnergyCounter(nint domain, ref CtlPowerEnergyCounter counter);
}

internal readonly record struct IntelGpuTelemetrySample(
    double? TemperatureCelsius,
    int? ClockMegahertz,
    double? PowerWatts);
