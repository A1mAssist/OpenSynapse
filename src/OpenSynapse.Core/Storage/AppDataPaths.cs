namespace OpenSynapse.Core.Storage;

/// <summary>
/// User data deliberately lives outside the Velopack-managed install root.
/// Velopack removes %LOCALAPPDATA%\OpenSynapse during MSI uninstall/upgrade.
/// </summary>
public static class AppDataPaths
{
    public const string VendorName = "A1mAssist";
    public const string FolderName = "OpenSynapse";

    public static string Root =>
        Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE") is { Length: > 0 } instance
            ? Path.Combine(Path.GetTempPath(), VendorName, FolderName, "QA", instance)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                VendorName,
                FolderName);

    public static string TransitionalRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSynapseData");

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);
}
