using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Displays;

namespace OpenSynapse.Windows.Lifecycle;

public sealed record DedicatedGpuModeStatus(
    bool IsSupported,
    bool IsActive,
    bool CanChange,
    DedicatedGpuModeBlockReason BlockReason);

public enum DedicatedGpuSwitchOutcome
{
    Success,
    RestartRequired,
    Cancelled,
    Blocked,
    NoDevice,
    Failed,
}

public readonly record struct DedicatedGpuSwitchResult(
    DedicatedGpuSwitchOutcome Outcome,
    int ExitCode = 0);

/// <summary>
/// The conservative integrated-graphics mode: disable the NVIDIA display adapter
/// with Windows' own PnP tool, exactly as Device Manager does. It is not a MUX
/// switch. The helper process runs before WinUI starts so UAC can relaunch this
/// executable without opening a second app window.
/// </summary>
public static class WindowsDedicatedGpuMode
{
    private const string HelperSwitch = "--set-dedicated-gpu";
    private const int HelperSuccess = 0;
    private const int HelperFailed = 1;
    private const int HelperRestartRequired = 2;
    private const int HelperNoDevice = 3;
    private const int HelperBlocked = 4;
    private const int UserCancelled = 1223;

    public static DedicatedGpuModeStatus GetStatus()
    {
        try
        {
            var adapters = WindowsDisplayAdapters.FindAll();
            var discreteAdapters = adapters.Where(adapter => adapter.IsNvidia).ToArray();
            if (discreteAdapters.Length != 1)
            {
                return Unsupported(DedicatedGpuModeBlockReason.NoDiscreteGraphics);
            }

            var integratedAdapters = adapters.Where(adapter => adapter.IsIntegratedCandidate).ToArray();
            if (integratedAdapters.Length != 1)
            {
                return Unsupported(DedicatedGpuModeBlockReason.NoIntegratedGraphics);
            }

            var discrete = discreteAdapters[0];
            var topology = WindowsDisplayTopology.Read();
            var internalUsesDiscrete = topology.InternalAdapterName is { } internalAdapter &&
                WindowsDisplayTopology.IsDiscreteAdapter(internalAdapter, discrete.Name);
            var snapshot = new DedicatedGpuModeSnapshot(
                HasIntegratedGraphics: true,
                HasDiscreteGraphics: true,
                DiscreteGpuEnabled: discrete.Enabled,
                DisplayTopologyKnown: topology.IsKnown,
                HasActiveInternalDisplay: topology.HasActiveInternalDisplay,
                InternalDisplayRouteKnown: topology.InternalDisplayRouteKnown,
                InternalDisplayUsesDiscreteGpu: internalUsesDiscrete,
                HasExternalDisplay: topology.HasExternalDisplay);
            var reason = DedicatedGpuModePolicy.GetBlockReason(snapshot);

            return new DedicatedGpuModeStatus(
                IsSupported: DedicatedGpuModePolicy.IsSupported(snapshot),
                IsActive: !discrete.Enabled,
                CanChange: reason == DedicatedGpuModeBlockReason.None,
                BlockReason: reason);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return Unsupported(DedicatedGpuModeBlockReason.DisplayTopologyUnavailable);
        }
    }

    public static async Task<DedicatedGpuSwitchResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "OpenSynapse.App.exe");
        if (!File.Exists(executablePath))
        {
            executablePath = Environment.ProcessPath ?? string.Empty;
        }
        if (executablePath.Length == 0)
        {
            return new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Failed);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add(HelperSwitch);
        startInfo.ArgumentList.Add(enabled ? "on" : "off");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Failed);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var outcome = process.ExitCode switch
            {
                HelperSuccess => DedicatedGpuSwitchOutcome.Success,
                HelperRestartRequired => DedicatedGpuSwitchOutcome.RestartRequired,
                HelperNoDevice => DedicatedGpuSwitchOutcome.NoDevice,
                HelperBlocked => DedicatedGpuSwitchOutcome.Blocked,
                _ => DedicatedGpuSwitchOutcome.Failed,
            };
            return new DedicatedGpuSwitchResult(outcome, process.ExitCode);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == UserCancelled)
        {
            return new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Cancelled);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException or
            OperationCanceledException)
        {
            return new DedicatedGpuSwitchResult(
                exception is OperationCanceledException
                    ? DedicatedGpuSwitchOutcome.Cancelled
                    : DedicatedGpuSwitchOutcome.Failed);
        }
    }

    /// <summary>
    /// Called from Program.Main before Velopack or WinUI starts. Returns null when
    /// the arguments are not the helper command, so normal startup continues.
    /// </summary>
    public static int? TryRunElevatedCommand(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || !StringComparer.Ordinal.Equals(args[0], HelperSwitch))
        {
            return null;
        }

        if (args.Count != 2 || args[1] is not ("on" or "off"))
        {
            return HelperFailed;
        }

        var enabled = StringComparer.OrdinalIgnoreCase.Equals(args[1], "on");
        try
        {
            var discrete = WindowsDisplayAdapters.FindAll()
                .Where(adapter => adapter.IsNvidia)
                .ToArray();
            if (discrete.Length != 1)
            {
                return HelperNoDevice;
            }

            if (!enabled)
            {
                var status = GetStatus();
                if (!status.IsSupported ||
                    status.BlockReason != DedicatedGpuModeBlockReason.None)
                {
                    return HelperBlocked;
                }
            }

            return WindowsDisplayAdapters.SetEnabled(discrete[0].InstanceId, enabled) switch
            {
                0 => HelperSuccess,
                3010 => HelperRestartRequired,
                _ => HelperFailed,
            };
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return HelperFailed;
        }
    }

    private static DedicatedGpuModeStatus Unsupported(DedicatedGpuModeBlockReason reason) =>
        new(false, false, false, reason);
}

internal sealed record WindowsDisplayAdapter(string InstanceId, string Name, bool Enabled)
{
    public bool IsNvidia => InstanceId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase);

    public bool IsIntegratedCandidate =>
        InstanceId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ||
        InstanceId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase);
}

internal static class WindowsDisplayAdapters
{
    private const int DigcfPresent = 0x2;
    private const int SpdrpDeviceDesc = 0x0;
    private const int SpdrpFriendlyName = 0xC;
    private const uint DnHasProblem = 0x400;
    private const uint CmProbDisabled = 22;
    private const int CrSuccess = 0;
    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");

    public static IReadOnlyList<WindowsDisplayAdapter> FindAll()
    {
        var classGuid = DisplayClass;
        var set = NativeMethods.SetupDiGetClassDevs(
            ref classGuid,
            null,
            IntPtr.Zero,
            DigcfPresent);
        if (set == new IntPtr(-1))
        {
            return [];
        }

        var adapters = new List<WindowsDisplayAdapter>();
        try
        {
            var info = new SpDevinfoData { Size = Marshal.SizeOf<SpDevinfoData>() };
            for (uint index = 0; NativeMethods.SetupDiEnumDeviceInfo(set, index, ref info); index++)
            {
                var instanceId = InstanceId(set, ref info);
                if (instanceId is null ||
                    !instanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Property(set, ref info, SpdrpFriendlyName) ??
                    Property(set, ref info, SpdrpDeviceDesc) ??
                    "Display adapter";
                var disabled = NativeMethods.CM_Get_DevNode_Status(
                    out var status,
                    out var problem,
                    info.DeviceInstance,
                    0) == CrSuccess &&
                    (status & DnHasProblem) != 0 &&
                    problem == CmProbDisabled;
                adapters.Add(new WindowsDisplayAdapter(instanceId, name, Enabled: !disabled));
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }

        return adapters;
    }

    public static int SetEnabled(string instanceId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(enabled ? "/enable-device" : "/disable-device");
        startInfo.ArgumentList.Add(instanceId);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("pnputil could not be started.");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string? InstanceId(IntPtr set, ref SpDevinfoData info)
    {
        var buffer = new StringBuilder(512);
        return NativeMethods.SetupDiGetDeviceInstanceId(
            set,
            ref info,
            buffer,
            buffer.Capacity,
            out _)
            ? buffer.ToString()
            : null;
    }

    private static string? Property(IntPtr set, ref SpDevinfoData info, int property)
    {
        var buffer = new byte[1024];
        if (!NativeMethods.SetupDiGetDeviceRegistryProperty(
                set,
                ref info,
                property,
                out _,
                buffer,
                buffer.Length,
                out var size) ||
            size < 2)
        {
            return null;
        }

        return Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0');
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int Size;
        public Guid ClassGuid;
        public uint DeviceInstance;
        public IntPtr Reserved;
    }

    private static class NativeMethods
    {
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            string? enumerator,
            IntPtr parent,
            int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInfo(
            IntPtr deviceInfoSet,
            uint memberIndex,
            ref SpDevinfoData deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInstanceId(
            IntPtr deviceInfoSet,
            ref SpDevinfoData deviceInfoData,
            StringBuilder deviceInstanceId,
            int deviceInstanceIdSize,
            out int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SpDevinfoData deviceInfoData,
            int property,
            out int propertyRegDataType,
            byte[] propertyBuffer,
            int propertyBufferSize,
            out int requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("cfgmgr32.dll")]
        internal static extern int CM_Get_DevNode_Status(
            out uint status,
            out uint problemNumber,
            uint deviceInstance,
            int flags);
    }
}