namespace OpenSynapse.Windows.Devices;

/// <summary>
/// PawnIO is the signed kernel driver we use for MSR and SMN access. Only an elevated
/// process can open it, but any process can see whether it is installed.
/// </summary>
public static class PawnIoAvailability
{
    private static readonly string LibraryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "PawnIO",
        "PawnIOLib.dll");

    public static bool IsInstalled => File.Exists(LibraryPath);
}
