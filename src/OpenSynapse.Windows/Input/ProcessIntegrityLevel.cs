using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Reads the integrity level of the current process.
///
/// The macro agent only helps when it runs at a higher integrity level than
/// the app, so the app checks the value the agent reports instead of
/// assuming the scheduled task delivered the intended level.
/// </summary>
public static class ProcessIntegrityLevel
{
    public const int Medium = 0x2000;
    public const int High = 0x3000;
    public const int System = 0x4000;

    /// <summary>
    /// The last Win32 error recorded by a failed call, for diagnostics.
    /// </summary>
    public static int LastError { get; private set; }

    private const int TokenIntegrityLevel = 25;
    private const uint TokenQuery = 0x0008;

    /// <summary>
    /// Returns the integrity level as a RID value, or 0 when it cannot be read.
    /// </summary>
    public static int Current()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            if (!OpenProcessToken(process.Handle, TokenQuery, out var token))
            {
                LastError = Marshal.GetLastWin32Error();
                return 0;
            }

            try
            {
                // The size query reports ERROR_INSUFFICIENT_BUFFER on success,
                // so the returned length is the signal, not the error code.
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var length);
                if (length == 0)
                {
                    return 0;
                }

                var pointer = Marshal.AllocHGlobal(length);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, pointer, length, out _))
                    {
                        LastError = Marshal.GetLastWin32Error();
                        return 0;
                    }

                    var sid = Marshal.ReadIntPtr(pointer);
                    var subAuthorityCount = Marshal.ReadByte(sid, 1);
                    return Marshal.ReadInt32(sid, 8 + (subAuthorityCount - 1) * 4);
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
                UnauthorizedAccessException or InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary>
    /// True when the level is higher than a normal application, which is what
    /// lets the agent write to an elevated window.
    /// </summary>
    public static bool IsElevated(int integrityLevel) => integrityLevel >= High;

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
