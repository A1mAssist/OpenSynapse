using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// ProgramData\OpenSynapse holds the broker log and service-only caches. The
/// default ProgramData ACL lets Users create subdirectories, which is enough to
/// plant a junction the LocalSystem service would follow.
/// </summary>
internal static class ProgramDataSecurity
{
    internal static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OpenSynapse");

    /// <summary>
    /// Replaces the inherited root ACL with SYSTEM/Administrators full control
    /// and read-only Users. Safe to call from the service on every start;
    /// failures leave the previous ACL in place.
    /// </summary>
    internal static void EnsureProtected()
    {
        try
        {
            ProtectDirectory(Root, allowUsersRead: true);

            var service = Path.Combine(Root, "Service");
            if (Directory.Exists(service))
            {
                ProtectDirectory(service, allowUsersRead: false);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException or NotSupportedException or Win32Exception)
        {
        }
    }

    internal static void ProtectDirectory(string path, bool allowUsersRead)
    {
        Directory.CreateDirectory(path);
        // Open the directory itself, and deny rename/deletion while its ACL is replaced.
        using var handle = CreateFile(path, 0x000E0000, 3, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetFileInformationByHandleEx(handle, 9, out var info, 8))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The service cache cannot contain a reparse point: " + path);
        var security = CreateDirectorySecurity(allowUsersRead).GetSecurityDescriptorBinaryForm();
        if (!SetKernelObjectSecurity(handle, 0x80000005, security))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        GetKernelObjectSecurity(handle, 5, null, 0, out var needed);
        var actual = new byte[checked((int)needed)];
        if (needed == 0 || !GetKernelObjectSecurity(handle, 5, actual, needed, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var descriptor = new RawSecurityDescriptor(actual, 0);
        if (!OpenSynapseServiceConfiguration.IsProtectedDescriptor(
                descriptor.GetSddlForm(AccessControlSections.Owner | AccessControlSections.Access)))
            throw new UnauthorizedAccessException("The service cache permissions were not applied: " + path);
    }

    internal static DirectorySecurity CreateDirectorySecurity(bool allowUsersRead)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        // An owner keeps WRITE_DAC even when its ACEs are removed. Without this
        // the interactive user could re-grant write access to the directory.
        security.SetOwner(new SecurityIdentifier(
            WellKnownSidType.LocalSystemSid, null));
        const InheritanceFlags inheritance =
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        if (allowUsersRead)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute,
                inheritance,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        return security;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo { public FileAttributes Attributes; public uint ReparseTag; }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security,
        uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass,
        out AttributeTagInfo info, uint size);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(SafeFileHandle handle, uint information, byte[] descriptor);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKernelObjectSecurity(SafeFileHandle handle, uint information,
        byte[]? descriptor, uint length, out uint needed);
}
