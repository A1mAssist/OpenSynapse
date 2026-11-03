using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace OpenSynapse.Windows.Lifecycle;

internal sealed record DedicatedGpuServiceConfiguration(
    string BinaryPath, string Account, uint StartType, uint ServiceType, uint ErrorControl, string Dacl, bool IsRunning)
{
    internal bool MatchesSettings(string command) =>
        string.Equals(BinaryPath, command, StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(Account, "LocalSystem", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Account, @"NT AUTHORITY\SYSTEM", StringComparison.OrdinalIgnoreCase)) &&
        StartType == 3 && ServiceType == 0x10 && ErrorControl == 1;

    internal bool MatchesDacl(string expected) => CanonicalDacl(Dacl) == CanonicalDacl(expected);
    internal bool Matches(string command, string dacl) => MatchesSettings(command) && MatchesDacl(dacl);
    private static string CanonicalDacl(string sddl) =>
        new RawSecurityDescriptor(sddl).GetSddlForm(AccessControlSections.Access);

    internal static bool IsValidSid(string sid)
    {
        try { return new SecurityIdentifier(sid).Value == sid; }
        catch (ArgumentException) { return false; }
    }

    internal static bool IsProtectedExecutable(string path)
    {
        if (!DedicatedGpuModeServiceInstaller.IsMachineInstallPath(path) || !File.Exists(path) ||
            !Path.GetFileName(path).Equals("OpenSynapse.App.exe", StringComparison.OrdinalIgnoreCase)) return false;
        // Parent delete/create rights can replace a read-only executable too. Reject junctions and writable ancestors.
        for (FileSystemInfo? item = new FileInfo(Path.GetFullPath(path)); item is not null;
            item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            var sections = AccessControlSections.Access | AccessControlSections.Owner;
            FileSystemSecurity security = item is FileInfo target ? target.GetAccessControl(sections)
                : ((DirectoryInfo)item).GetAccessControl(sections);
            if (!IsProtectedDescriptor(security.GetSecurityDescriptorSddlForm(sections))) return false;
            if (item.FullName.Equals(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase) ||
                item.FullName.Equals(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    internal static bool IsProtectedDescriptor(string sddl)
    {
        var descriptor = new RawSecurityDescriptor(sddl);
        if (descriptor.Owner is null || !IsTrusted(descriptor.Owner) || descriptor.DiscretionaryAcl is null) return false;
        const int writeMask = (int)(FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership) | 0x10000000 | 0x40000000;
        foreach (GenericAce entry in descriptor.DiscretionaryAcl)
        {
            if ((entry.AceFlags & AceFlags.InheritOnly) != 0) continue;
            if (entry is not QualifiedAce ace) return false;
            if (ace.AceQualifier == AceQualifier.AccessAllowed && (ace.AccessMask & writeMask) != 0 && !IsTrusted(ace.SecurityIdentifier))
                return false;
        }
        return true;
    }

    private static bool IsTrusted(SecurityIdentifier sid) => sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
        sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
        sid.Value == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"; // Windows Modules Installer

    internal static DedicatedGpuServiceConfiguration? Read()
    {
        var manager = Native.OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = Native.OpenService(manager, DedicatedGpuModeServiceProtocol.ServiceName, 0x0001 | 0x0004 | 0x20000);
            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1060) return null;
                throw new Win32Exception(error);
            }
            try
            {
                Native.QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
                if (Marshal.GetLastWin32Error() != 122 || needed == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                var buffer = Marshal.AllocHGlobal(checked((int)needed));
                try
                {
                    if (!Native.QueryServiceConfig(service, buffer, needed, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var config = Marshal.PtrToStructure<Config>(buffer);
                    Native.QueryServiceObjectSecurity(service, 4, null, 0, out var securitySize);
                    if (Marshal.GetLastWin32Error() != 122 || securitySize == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var security = new byte[checked((int)securitySize)];
                    if (!Native.QueryServiceObjectSecurity(service, 4, security, securitySize, out _) ||
                        !Native.QueryServiceStatus(service, out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    return new(Marshal.PtrToStringUni(config.BinaryPath) ?? string.Empty,
                        Marshal.PtrToStringUni(config.Account) ?? string.Empty, config.StartType, config.ServiceType, config.ErrorControl,
                        new RawSecurityDescriptor(security, 0).GetSddlForm(AccessControlSections.Access), status.CurrentState != 1);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { Native.CloseServiceHandle(service); }
        }
        finally { Native.CloseServiceHandle(manager); }
    }

    // Windows SDK 10.0.26100.0/um/winsvc.h:723-730,827-837,1409-1420,1511-1531.
    [StructLayout(LayoutKind.Sequential)]
    private struct Config
    {
        public uint ServiceType, StartType, ErrorControl;
        public IntPtr BinaryPath, LoadOrderGroup;
        public uint TagId;
        public IntPtr Dependencies, Account, DisplayName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Status
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }
    private static class Native
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenSCManagerW")]
        internal static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenServiceW")]
        internal static extern IntPtr OpenService(IntPtr manager, string service, uint access);
        [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "QueryServiceConfigW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryServiceConfig(IntPtr service, IntPtr config, uint size, out uint needed);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryServiceObjectSecurity(IntPtr service, uint information, [Out] byte[]? descriptor, uint size, out uint needed);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryServiceStatus(IntPtr service, out Status status);
        [DllImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseServiceHandle(IntPtr handle);
    }
}
