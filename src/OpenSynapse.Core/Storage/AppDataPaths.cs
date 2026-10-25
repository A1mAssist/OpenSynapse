namespace OpenSynapse.Core.Storage;

/// <summary>
/// User data deliberately lives outside the Velopack-managed install root.
/// Velopack removes %LOCALAPPDATA%\OpenSynapse during MSI uninstall/upgrade.
/// </summary>
public static class AppDataPaths
{
    public const string VendorName = "A1mAssist";
    public const string FolderName = "OpenSynapse";

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        VendorName,
        FolderName);

    public static string TransitionalRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSynapseData");

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);
}