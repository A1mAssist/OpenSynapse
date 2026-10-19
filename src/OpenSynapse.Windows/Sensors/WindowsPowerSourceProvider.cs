using System.Runtime.InteropServices;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Windows.Sensors;

public sealed class WindowsPowerSourceProvider : IPowerSourceProvider
{
    private const byte BatteryFlagCharging = 8;
    private const byte BatteryFlagNoBattery = 128;
    private const byte BatteryPercentUnknown = 255;

    public bool? IsPluggedIn => ReadStatus() is { } status
        ? status.ACLineStatus switch
        {
            0 => false,
            1 => true,
            _ => null,
        }
        : null;

    public int? BatteryPercent => ReadStatus() is { } status
        ? status.BatteryLifePercent != BatteryPercentUnknown
            ? status.BatteryLifePercent
            : null
        : null;

    public bool? IsCharging => ReadStatus() is { } status
        ? status.BatteryFlag == 0 || status.BatteryFlag == BatteryFlagNoBattery
            ? null
            : (status.BatteryFlag & BatteryFlagCharging) != 0
        : null;

    private static SystemPowerStatus? ReadStatus() =>
        NativeMethods.GetSystemPowerStatus(out var status) ? status : null;

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
