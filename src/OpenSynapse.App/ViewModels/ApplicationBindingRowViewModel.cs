
namespace OpenSynapse.App.ViewModels;

public sealed class ApplicationBindingRowViewModel(string executablePath, string profileName)
{
    public string ExecutablePath { get; } = executablePath;
    public string ExecutableName { get; } = Path.GetFileName(executablePath);
    public string ProfileName { get; } = profileName;
}
