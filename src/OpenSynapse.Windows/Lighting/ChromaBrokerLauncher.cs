using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.Windows.Lighting;

internal static class ChromaBrokerLauncher
{
    internal static string PipeName => ChromaBrokerProtocol.PipeName;
    internal static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "OpenSynapse.ChromaBroker.exe");
    internal static bool IsAvailable => File.Exists(ExecutablePath);
    internal static string OfficialDllPath => Path.Combine(Environment.SystemDirectory, "RzChromaSDK64.dll");
    internal static bool IsOfficialDllInstalled => File.Exists(OfficialDllPath);
}
