using System.Runtime.InteropServices;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Windows.Sensors;

public sealed class WindowsPowerSourceProvider : IPowerSourceProvider
{
    public bool? IsPluggedIn
    {
        get
        {
            return NativeMethods.GetSystemPowerStatus(out var status)
                ? status.ACLineStatus switch
                {
                    0 => false,
                    1 => true,
                    _ => null,
                }
                : null;
        }
    }

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
