using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private DeviceApplicationSwitchCoordinator _applicationSwitch = null!;
    private ForegroundApplicationEvents _foregroundEvents = null!;

    public DeviceApplicationRulesWorkspace DeviceRules { get; private set; } = null!;
    public HomeWorkspace Home { get; private set; } = null!;

    private void InitializeDeviceHome(IActiveApplicationProvider? activeApplicationProvider)
    {
        _applicationSwitch = new DeviceApplicationSwitchCoordinator();
        _foregroundEvents = new ForegroundApplicationEvents(
            activeApplicationProvider ?? UnknownActiveApplicationProvider.Instance,
            () => Refresh?.RequestApplicationSwitch());
        Home = new HomeWorkspace();
        DeviceRules = new DeviceApplicationRulesWorkspace(
            new DeviceApplicationRuleStore(),
            () => _profile,
            () => RazerHid.CurrentConnections,
            () => _applicationSwitch,
            () => Refresh.RequestApplicationSwitch());
        Profiles.ProfileDeleted += _ => OnPropertyChanged(nameof(DeviceRules));
    }

    internal async Task InitializeDeviceHomeAsync(CancellationToken cancellationToken = default)
    {
        Refresh.ConfigureDeviceApplicationSwitch(
            _applicationSwitch,
            () => DeviceRules.Document,
            () => DeviceRules.RefreshView());
        await DeviceRules.LoadAsync(cancellationToken);
        await DeviceRules.MigrateAsync(cancellationToken);
        if (DeviceRules.Enabled) _foregroundEvents.Start();
        else _foregroundEvents.Dispose();
        await Home.LoadAsync(cancellationToken);
    }

    internal void SetApplicationSwitchingEnabled(bool enabled)
    {
        if (enabled) _foregroundEvents.Start(); else _foregroundEvents.Dispose();
    }

    private void DisposeDeviceHome()
    {
        _foregroundEvents.Dispose();
    }
}
