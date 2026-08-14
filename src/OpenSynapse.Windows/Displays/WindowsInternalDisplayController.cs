using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Displays;

namespace OpenSynapse.Windows.Displays;

public sealed class WindowsInternalDisplayController : IInternalDisplayController
{
    private readonly IWindowsDisplayApi _api;

    public WindowsInternalDisplayController()
        : this(new WindowsDisplayApi())
    {
    }

    internal WindowsInternalDisplayController(IWindowsDisplayApi api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public InternalDisplaySnapshot Read() => Resolve().ToSnapshot();

    internal string ResolveSourceName() => Resolve().SourceName;

    public InternalDisplaySnapshot SetRefreshRate(int refreshRateHertz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refreshRateHertz);

        var original = Resolve();
        if (!original.SupportedRefreshRates.Contains(refreshRateHertz))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshRateHertz),
                refreshRateHertz,
                $"{refreshRateHertz} Hz is not supported by the active internal panel at " +
                $"{original.CurrentMode.Width} x {original.CurrentMode.Height}.");
        }

        if (original.CurrentMode.RefreshRateHertz == refreshRateHertz)
        {
            return original.ToSnapshot();
        }

        var requested = original.Modes
            .Where(mode => mode.RefreshRateHertz == refreshRateHertz)
            .OrderByDescending(mode => mode.BitsPerPixel == original.CurrentMode.BitsPerPixel)
            .First();

        ThrowForDisplayChange(
            _api.ChangeMode(original.SourceName, requested, testOnly: true),
            "Windows rejected the requested internal-panel mode during validation");

        var beforeApply = Resolve();
        if (beforeApply.Identity != original.Identity ||
            !beforeApply.SupportedRefreshRates.Contains(refreshRateHertz))
        {
            throw new InvalidOperationException(
                "The display topology changed while validating the refresh rate; no mode was applied.");
        }

        requested = beforeApply.Modes
            .Where(mode => mode.RefreshRateHertz == refreshRateHertz)
            .OrderByDescending(mode => mode.BitsPerPixel == beforeApply.CurrentMode.BitsPerPixel)
            .First();

        Exception? failure = null;
        var applyAttempted = false;
        try
        {
            applyAttempted = true;
            ThrowForDisplayChange(
                _api.ChangeMode(beforeApply.SourceName, requested, testOnly: false),
                "Windows could not apply the requested internal-panel mode");

            var readback = Resolve();
            if (readback.Identity != original.Identity ||
                readback.CurrentMode.RefreshRateHertz != refreshRateHertz)
            {
                throw new InvalidOperationException(
                    $"The internal panel did not read back as {refreshRateHertz} Hz after the mode change.");
            }

            return readback.ToSnapshot();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            failure = exception;
            throw;
        }
        finally
        {
            if (failure is not null && applyAttempted)
            {
                TryRestore(original);
            }
        }
    }

    private ResolvedInternalDisplay Resolve()
    {
        var paths = _api.QueryActivePaths();
        var internalPaths = paths
            .Where(path => path.IsActive && path.TargetAvailable && IsInternal(path.OutputTechnology))
            .ToArray();

        if (internalPaths.Length != 1)
        {
            throw new InvalidOperationException(
                internalPaths.Length == 0
                    ? "Windows did not report one active internal display path."
                    : "Windows reported multiple active internal display paths; refresh-rate writes are disabled.");
        }

        var path = internalPaths[0];
        if (paths.Any(other =>
                other.Identity != path.Identity &&
                other.IsActive &&
                other.SourceAdapterId == path.SourceAdapterId &&
                other.SourceId == path.SourceId))
        {
            throw new InvalidOperationException(
                "The internal panel shares a source with another active display; refresh-rate writes are disabled.");
        }

        var sourceName = _api.GetSourceName(path.SourceAdapterId, path.SourceId);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new InvalidOperationException("Windows returned an empty GDI source name for the internal panel.");
        }

        var currentMode = _api.GetCurrentMode(sourceName);
        ValidateMode(currentMode, "current");
        var modes = _api.EnumerateModes(sourceName)
            .Where(mode =>
                mode.Width == currentMode.Width &&
                mode.Height == currentMode.Height &&
                mode.RefreshRateHertz > 0)
            .ToArray();
        var rates = modes
            .Select(mode => mode.RefreshRateHertz)
            .Distinct()
            .Order()
            .ToArray();

        if (rates.Length == 0)
        {
            throw new InvalidOperationException(
                "Windows did not enumerate any refresh rate for the internal panel's current resolution.");
        }

        return new ResolvedInternalDisplay(path.Identity, sourceName, currentMode, modes, rates);
    }

    private void TryRestore(ResolvedInternalDisplay original)
    {
        try
        {
            var current = Resolve();
            if (current.Identity == original.Identity)
            {
                _api.ChangeMode(original.SourceName, original.CurrentMode, testOnly: false);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // The original failure remains authoritative; a changed topology must not trigger a blind write.
        }
    }

    private static void ThrowForDisplayChange(int result, string message)
    {
        if (result != WindowsDisplayApi.DispChangeSuccessful)
        {
            throw new Win32Exception(result, $"{message} (DISP_CHANGE {result}).");
        }
    }

    private static void ValidateMode(DisplayMode mode, string label)
    {
        if (mode.Width <= 0 || mode.Height <= 0 || mode.RefreshRateHertz <= 0)
        {
            throw new InvalidOperationException($"Windows returned an invalid {label} display mode.");
        }
    }

    private static bool IsInternal(uint technology) => technology is
        WindowsDisplayApi.OutputTechnologyLvds or
        WindowsDisplayApi.OutputTechnologyDisplayPortEmbedded or
        WindowsDisplayApi.OutputTechnologyUdiEmbedded or
        WindowsDisplayApi.OutputTechnologyInternal;

    private sealed record ResolvedInternalDisplay(
        DisplayPathIdentity Identity,
        string SourceName,
        DisplayMode CurrentMode,
        IReadOnlyList<DisplayMode> Modes,
        IReadOnlyList<int> SupportedRefreshRates)
    {
        public InternalDisplaySnapshot ToSnapshot() => new(
            CurrentMode.Width,
            CurrentMode.Height,
            CurrentMode.RefreshRateHertz,
            SupportedRefreshRates);
    }
}

internal interface IWindowsDisplayApi
{
    IReadOnlyList<DisplayPath> QueryActivePaths();

    string GetSourceName(DisplayAdapterId adapterId, uint sourceId);

    DisplayMode GetCurrentMode(string sourceName);

    IReadOnlyList<DisplayMode> EnumerateModes(string sourceName);

    int ChangeMode(string sourceName, DisplayMode mode, bool testOnly);
}

internal readonly record struct DisplayAdapterId(uint LowPart, int HighPart);

internal readonly record struct DisplayPathIdentity(
    DisplayAdapterId SourceAdapterId,
    uint SourceId,
    DisplayAdapterId TargetAdapterId,
    uint TargetId);

internal readonly record struct DisplayPath(
    DisplayPathIdentity Identity,
    uint OutputTechnology,
    bool IsActive,
    bool TargetAvailable)
{
    public DisplayAdapterId SourceAdapterId => Identity.SourceAdapterId;
    public uint SourceId => Identity.SourceId;
}

internal readonly record struct DisplayMode(
    int Width,
    int Height,
    int RefreshRateHertz,
    int BitsPerPixel);

internal sealed class WindowsDisplayApi : IWindowsDisplayApi
{
    internal const int DispChangeSuccessful = 0;
    internal const uint OutputTechnologyLvds = 6;
    internal const uint OutputTechnologyDisplayPortEmbedded = 11;
    internal const uint OutputTechnologyUdiEmbedded = 13;
    internal const uint OutputTechnologyInternal = 0x80000000;

    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const uint QueryFlags = 0x00000002 | 0x00000010 | 0x00000040;
    private const uint DisplayConfigPathActive = 0x00000001;
    private const uint GetSourceNameType = 1;
    private const uint EnumCurrentSettings = 0xFFFFFFFF;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsTest = 0x00000002;
    private const uint DmBitsPerPel = 0x00040000;
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint DmDisplayFrequency = 0x00400000;
    private const int MaxTopologyAttempts = 3;

    static WindowsDisplayApi()
    {
        RequireSize<DisplayConfigPathInfo>(72);
        RequireSize<DisplayConfigModeInfo>(64);
        RequireSize<DisplayConfigSourceDeviceName>(84);
        RequireSize<DevMode>(220);
    }

    public IReadOnlyList<DisplayPath> QueryActivePaths()
    {
        for (var attempt = 0; attempt < MaxTopologyAttempts; attempt++)
        {
            var result = NativeMethods.GetDisplayConfigBufferSizes(
                QueryFlags,
                out var pathCount,
                out var modeCount);
            ThrowForResult(result, "GetDisplayConfigBufferSizes");

            var nativePaths = new DisplayConfigPathInfo[checked((int)pathCount)];
            var nativeModes = new DisplayConfigModeInfo[checked((int)modeCount)];
            result = NativeMethods.QueryDisplayConfig(
                QueryFlags,
                ref pathCount,
                nativePaths,
                ref modeCount,
                nativeModes,
                IntPtr.Zero);

            if (result == ErrorInsufficientBuffer)
            {
                continue;
            }

            ThrowForResult(result, "QueryDisplayConfig");
            return nativePaths
                .Take(checked((int)pathCount))
                .Select(path => new DisplayPath(
                    new DisplayPathIdentity(
                        path.SourceInfo.AdapterId.ToManaged(),
                        path.SourceInfo.Id,
                        path.TargetInfo.AdapterId.ToManaged(),
                        path.TargetInfo.Id),
                    path.TargetInfo.OutputTechnology,
                    (path.Flags & DisplayConfigPathActive) != 0,
                    path.TargetInfo.TargetAvailable != 0))
                .ToArray();
        }

        throw new Win32Exception(
            ErrorInsufficientBuffer,
            "The Windows display topology changed repeatedly while it was being queried.");
    }

    public string GetSourceName(DisplayAdapterId adapterId, uint sourceId)
    {
        var packet = new DisplayConfigSourceDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = GetSourceNameType,
                Size = checked((uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>()),
                AdapterId = DisplayConfigLuid.FromManaged(adapterId),
                Id = sourceId,
            },
            ViewGdiDeviceName = string.Empty,
        };
        ThrowForResult(NativeMethods.DisplayConfigGetDeviceInfo(ref packet), "DisplayConfigGetDeviceInfo");
        return packet.ViewGdiDeviceName?.TrimEnd('\0') ?? string.Empty;
    }

    public DisplayMode GetCurrentMode(string sourceName)
    {
        var mode = CreateDevMode();
        if (!NativeMethods.EnumDisplaySettingsEx(sourceName, EnumCurrentSettings, ref mode, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"EnumDisplaySettingsExW could not read the current mode for {sourceName}.");
        }

        return mode.ToManaged();
    }

    public IReadOnlyList<DisplayMode> EnumerateModes(string sourceName)
    {
        var modes = new List<DisplayMode>();
        for (uint index = 0; ; index++)
        {
            var mode = CreateDevMode();
            if (!NativeMethods.EnumDisplaySettingsEx(sourceName, index, ref mode, 0))
            {
                break;
            }

            modes.Add(mode.ToManaged());
        }

        return modes;
    }

    public int ChangeMode(string sourceName, DisplayMode mode, bool testOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var nativeMode = CreateDevMode();
        if (!NativeMethods.EnumDisplaySettingsEx(sourceName, EnumCurrentSettings, ref nativeMode, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"EnumDisplaySettingsExW could not prepare a mode change for {sourceName}.");
        }

        nativeMode.Fields |= DmBitsPerPel | DmPelsWidth | DmPelsHeight | DmDisplayFrequency;
        nativeMode.BitsPerPel = checked((uint)mode.BitsPerPixel);
        nativeMode.PelsWidth = checked((uint)mode.Width);
        nativeMode.PelsHeight = checked((uint)mode.Height);
        nativeMode.DisplayFrequency = checked((uint)mode.RefreshRateHertz);
        return NativeMethods.ChangeDisplaySettingsEx(
            sourceName,
            ref nativeMode,
            IntPtr.Zero,
            testOnly ? CdsTest : CdsUpdateRegistry,
            IntPtr.Zero);
    }

    private static DevMode CreateDevMode() => new()
    {
        DeviceName = string.Empty,
        FormName = string.Empty,
        Size = checked((ushort)Marshal.SizeOf<DevMode>()),
    };

    private static void ThrowForResult(int result, string operation)
    {
        if (result != ErrorSuccess)
        {
            throw new Win32Exception(result, $"{operation} failed with Win32 error {result}.");
        }
    }

    private static void RequireSize<T>(int expected) where T : struct
    {
        var actual = Marshal.SizeOf<T>();
        if (actual != expected)
        {
            throw new TypeLoadException(
                $"The managed {typeof(T).Name} layout is {actual} bytes; Windows requires {expected} bytes.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigLuid
    {
        public uint LowPart;
        public int HighPart;

        public readonly DisplayAdapterId ToManaged() => new(LowPart, HighPart);

        public static DisplayConfigLuid FromManaged(DisplayAdapterId value) => new()
        {
            LowPart = value.LowPart,
            HighPart = value.HighPart,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public DisplayConfigLuid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public DisplayConfigLuid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct DisplayConfigModeInfo
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public DisplayConfigLuid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevModeDisplayUnion
    {
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public DevModeDisplayUnion Display;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;

        public readonly DisplayMode ToManaged() => new(
            checked((int)PelsWidth),
            checked((int)PelsHeight),
            checked((int)DisplayFrequency),
            checked((int)BitsPerPel));
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern int GetDisplayConfigBufferSizes(
            uint flags,
            out uint pathCount,
            out uint modeCount);

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern int QueryDisplayConfig(
            uint flags,
            ref uint pathCount,
            [Out] DisplayConfigPathInfo[] paths,
            ref uint modeCount,
            [Out] DisplayConfigModeInfo[] modes,
            IntPtr currentTopologyId);

        [DllImport("user32.dll", ExactSpelling = true)]
        internal static extern int DisplayConfigGetDeviceInfo(
            ref DisplayConfigSourceDeviceName packet);

        [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplaySettingsEx(
            string deviceName,
            uint modeNumber,
            ref DevMode mode,
            uint flags);

        [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
        internal static extern int ChangeDisplaySettingsEx(
            string deviceName,
            ref DevMode mode,
            IntPtr window,
            uint flags,
            IntPtr parameter);
    }
}
