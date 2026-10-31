using System.Runtime.InteropServices;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The Razer AMD path uses the same Ryzen Master driver and SDK DLLs that Synapse
/// installs. It is preferred over direct SMU writes because Blade firmware can accept
/// the mailbox message and still keep its EC-controlled PPT values.
/// </summary>
internal sealed class RazerAmdPowerLimitController : ICpuPowerLimitController
{
    private const uint PptCapability = 13;
    private const int ParameterBufferSize = 128;
    private const int CapabilityBufferSize = 64;
    private const int LimitOffset = 0x10;
    private const uint CapabilityNotSupported = uint.MaxValue;
    private const string InstallFolder = "RazerAMDOverClock";

    private readonly IntPtr _deviceLibrary;
    private readonly IntPtr _platformLibrary;
    private readonly IntPtr _parameters;
    private readonly IntPtr _capability;
    private readonly GetPlatformDelegate _getPlatform;
    private readonly GetCpuParametersDelegate _getCpuParameters;
    private readonly GetCapabilitiesDelegate _getCapabilities;
    private readonly SetPptLimitDelegate _setPptLimit;
    private readonly CpuPowerLimit _firmware;
    private readonly double _minimumWatts;
    private readonly double _maximumWatts;
    private bool _disposed;

    private RazerAmdPowerLimitController(
        IntPtr deviceLibrary,
        IntPtr platformLibrary,
        IntPtr parameters,
        IntPtr capability,
        GetPlatformDelegate getPlatform,
        GetCpuParametersDelegate getCpuParameters,
        GetCapabilitiesDelegate getCapabilities,
        SetPptLimitDelegate setPptLimit,
        CpuPowerLimit firmware,
        double minimumWatts,
        double maximumWatts)
    {
        _deviceLibrary = deviceLibrary;
        _platformLibrary = platformLibrary;
        _parameters = parameters;
        _capability = capability;
        _getPlatform = getPlatform;
        _getCpuParameters = getCpuParameters;
        _getCapabilities = getCapabilities;
        _setPptLimit = setPptLimit;
        _firmware = firmware;
        _minimumWatts = minimumWatts;
        _maximumWatts = maximumWatts;
    }

    public static CpuPowerLimitAvailability TryOpen(
        out ICpuPowerLimitController? controller,
        out string? detail)
    {
        controller = null;
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Razer",
            InstallFolder);
        var devicePath = Path.Combine(folder, "Device.dll");
        var platformPath = Path.Combine(folder, "Platform.dll");
        if (!File.Exists(devicePath) || !File.Exists(platformPath))
        {
            detail = "The Razer AMD OverClock component is not installed.";
            return CpuPowerLimitAvailability.AmdDriverMissing;
        }

        var device = IntPtr.Zero;
        var platform = IntPtr.Zero;
        var parameters = IntPtr.Zero;
        var capability = IntPtr.Zero;
        try
        {
            device = NativeLibrary.Load(devicePath);
            platform = NativeLibrary.Load(platformPath);
            var getPlatform = GetDelegate<GetPlatformDelegate>(platform, "GetPlatform");
            var getCpuParameters = GetDelegate<GetCpuParametersDelegate>(
                platform, "GetRmCpuParameters");
            var getCapabilities = GetDelegate<GetCapabilitiesDelegate>(
                platform, "GetCapabilities");
            var setPptLimit = GetDelegate<SetPptLimitDelegate>(platform, "SetPPTLimit");

            if (getPlatform() == IntPtr.Zero)
            {
                throw new InvalidOperationException("GetPlatform returned no platform.");
            }

            parameters = Marshal.AllocHGlobal(ParameterBufferSize);
            capability = Marshal.AllocHGlobal(CapabilityBufferSize);
            if (getCapabilities(PptCapability, capability) == CapabilityNotSupported)
            {
                throw new InvalidOperationException("PPT control is not supported.");
            }

            var info = Marshal.PtrToStructure<CapabilityInfo>(capability);
            if (info.IsSupported == 0 || !double.IsFinite(info.Maximum) ||
                info.Maximum < CpuPowerLimit.MinimumWatts)
            {
                throw new InvalidOperationException("The CPU does not expose a usable PPT range.");
            }

            var minimumWatts = Math.Max(CpuPowerLimit.MinimumWatts, Math.Ceiling(info.Minimum));
            var maximumWatts = Math.Min(CpuPowerLimit.MaximumWatts, Math.Floor(info.Maximum));
            if (maximumWatts < minimumWatts)
            {
                throw new InvalidOperationException("The CPU PPT range is empty.");
            }
            if (!TryReadParameters(getCpuParameters, parameters, maximumWatts, out var current, out var readError))
            {
                throw new InvalidOperationException(readError);
            }

            var firmware = new CpuPowerLimit(
                current,
                current,
                CpuPowerLimit.MinimumTimeSeconds,
                CpuPowerLimit.MaximumTimeSeconds);
            controller = new RazerAmdPowerLimitController(
                device,
                platform,
                parameters,
                capability,
                getPlatform,
                getCpuParameters,
                getCapabilities,
                setPptLimit,
                firmware,
                minimumWatts,
                maximumWatts);
            detail = null;
            return CpuPowerLimitAvailability.Available;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException or IOException or InvalidOperationException or
                UnauthorizedAccessException)
        {
            detail = $"The Razer AMD OverClock path could not be opened: {exception.Message}";
            return CpuPowerLimitAvailability.AmdDriverMissing;
        }
        finally
        {
            if (controller is null)
            {
                if (parameters != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(parameters);
                }

                if (capability != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(capability);
                }

                if (platform != IntPtr.Zero)
                {
                    NativeLibrary.Free(platform);
                }

                if (device != IntPtr.Zero)
                {
                    NativeLibrary.Free(device);
                }
            }
        }
    }

    public CpuPowerLimitReport Read()
    {
        ThrowIfDisposed();
        return TryReadParameters(
                _getCpuParameters,
                _parameters,
                _maximumWatts,
                out var current,
                out var error)
            ? BuildReport(current)
            : CpuPowerLimitReport.Unavailable(CpuPowerLimitAvailability.Failed, error);
    }

    public CpuPowerLimitReport Apply(CpuPowerLimit limit)
    {
        ThrowIfDisposed();
        var requested = Math.Clamp(
            limit.Clamp(_maximumWatts).Pl1Watts,
            _minimumWatts,
            _maximumWatts);
        var status = _setPptLimit((uint)Math.Round(requested), 1);
        if (status != 0)
        {
            return CpuPowerLimitReport.Unavailable(
                CpuPowerLimitAvailability.Failed,
                $"The AMD Ryzen Master driver rejected the PPT write ({status}).");
        }

        Thread.Sleep(250);
        if (!TryReadParameters(
                _getCpuParameters,
                _parameters,
                _maximumWatts,
                out var actual,
                out _))
        {
            return BuildReport(requested);
        }

        if (Math.Abs(actual - requested) > 1)
        {
            return CpuPowerLimitReport.Unavailable(
                CpuPowerLimitAvailability.Failed,
                $"The AMD driver accepted the PPT write but reports {actual:0.#} W.");
        }

        return BuildReport(actual);
    }

    public CpuPowerLimitReport RestoreTo(double pl1Watts, double pl2Watts)
    {
        ThrowIfDisposed();
        return Apply(new CpuPowerLimit(
            pl1Watts,
            pl2Watts,
            CpuPowerLimit.MinimumTimeSeconds,
            CpuPowerLimit.MaximumTimeSeconds));
    }

    public CpuPowerLimitReport RestoreFirmware()
    {
        ThrowIfDisposed();
        return RestoreTo(_firmware.Pl1Watts, _firmware.Pl2Watts);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Marshal.FreeHGlobal(_parameters);
        Marshal.FreeHGlobal(_capability);
        NativeLibrary.Free(_platformLibrary);
        NativeLibrary.Free(_deviceLibrary);
    }

    private CpuPowerLimitReport BuildReport(double current) => new(
        CpuPowerLimitAvailability.Available,
        null,
        current,
        current,
        _firmware.Pl1Watts,
        _firmware.Pl2Watts,
        _maximumWatts,
        false);

    private static bool TryReadParameters(
        GetCpuParametersDelegate read,
        IntPtr buffer,
        double maximumWatts,
        out double watts,
        out string? error)
    {
        watts = 0;
        if (read(buffer) != 0)
        {
            error = "The AMD Ryzen Master driver did not return CPU power data.";
            return false;
        }

        var raw = BitConverter.Int32BitsToSingle(
            Marshal.ReadInt32(buffer, LimitOffset));
        if (!float.IsFinite(raw) || raw <= 0)
        {
            error = $"The AMD CPU power data is not usable ({raw}).";
            return false;
        }

        // Ryzen Master reports PPT in tenths of a watt on some families and watts on
        // others. Normalize only values above the published PPT ceiling.
        var candidate = raw;
        if (candidate > maximumWatts)
        {
            candidate /= 10;
        }

        if (!(candidate >= 1 && candidate < 500))
        {
            error = $"The AMD CPU power data is not plausible ({candidate} W).";
            return false;
        }

        watts = candidate;
        error = null;
        return true;
    }

    private static TDelegate GetDelegate<TDelegate>(IntPtr library, string name)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(
            NativeLibrary.GetExport(library, name));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    [StructLayout(LayoutKind.Sequential)]
    private struct CapabilityInfo
    {
        public double Minimum;
        public double Maximum;
        public double Step;
        public short IsSupported;
        public short IsEnabled;
        public int Padding;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetPlatformDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetCpuParametersDelegate(IntPtr buffer);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetCapabilitiesDelegate(uint capability, IntPtr information);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetPptLimitDelegate(uint watts, byte enabled);
}
