using System.Security.AccessControl;
using System.Security.Principal;

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

            // Service was created under the default ProgramData ACL, so drop
            // its inherited Users:Write ACE and re-inherit the protected root.
            var service = Path.Combine(Root, "Service");
            if (Directory.Exists(service))
            {
                var security = new DirectoryInfo(service).GetAccessControl();
                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                new DirectoryInfo(service).SetAccessControl(security);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
        }
    }

    internal static void ProtectDirectory(string path, bool allowUsersRead)
    {
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
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

        new DirectoryInfo(path).SetAccessControl(security);
    }
}
