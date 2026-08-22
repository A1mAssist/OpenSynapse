using OpenSynapse.Core.Sensors;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App.ViewModels;

internal sealed class UnknownPowerSourceProvider : IPowerSourceProvider
{
    public static UnknownPowerSourceProvider Instance { get; } = new();

    public bool? IsPluggedIn => null;
}
internal sealed class UnknownActiveApplicationProvider : IActiveApplicationProvider
{
    public static UnknownActiveApplicationProvider Instance { get; } = new();

    public string? ExecutablePath => null;
}

