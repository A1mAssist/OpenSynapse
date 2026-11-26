using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using OpenSynapse.App.Runtime;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed class DeviceApplicationRuleRowViewModel : INotifyPropertyChanged
{
    public DeviceApplicationRuleRowViewModel(DeviceApplicationRule rule)
    {
        Rule = rule;
        Enabled = rule.Enabled;
        _profileName = rule.ProfileName;
    }
    private string _profileName;
    public DeviceApplicationRule Rule { get; }
    public string Id => Rule.Id;
    public string ExecutableName => Path.GetFileName(Rule.ExecutablePath);
    public string ExecutablePath => Rule.ExecutablePath;
    public DeviceBindingKey Device => Rule.Device;
    public string DeviceName { get; internal set; } = string.Empty;
    public string StatusText { get; internal set; } = string.Empty;
    public bool Enabled { get; set; }
    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (string.IsNullOrEmpty(value) || StringComparer.Ordinal.Equals(_profileName, value)) return;
            _profileName = value;
            PropertyChanged?.Invoke(this, new(nameof(ProfileName)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record DeviceRuleDeviceOption(DeviceBindingKey Device, string DisplayName);

public sealed partial class DeviceApplicationRulesWorkspace : INotifyPropertyChanged
{
    private readonly DeviceApplicationRuleStore _store;
    private readonly Func<ProfileDocument> _profiles;
    private readonly Func<IReadOnlyList<RazerHidDeviceConnection>> _connections;
    private readonly Func<DeviceApplicationSwitchCoordinator> _coordinator;
    private readonly Action _changed;
    private readonly DispatcherQueue? _dispatcher;
    private DeviceApplicationRuleDocument _document = new(1, true, []);
    private DeviceBindingKey? _selected;
    private DeviceBindingKey? _renderedSelected;
    private bool _renderedEnabled;
    private string[] _renderedProfileNames = [];
    private string _bindableDevicesSignature = string.Empty;
    private string _runningApplicationsSignature = string.Empty;
    private bool _migrated;

    internal DeviceApplicationRulesWorkspace(
        DeviceApplicationRuleStore store,
        Func<ProfileDocument> profiles,
        Func<IReadOnlyList<RazerHidDeviceConnection>> connections,
        Func<DeviceApplicationSwitchCoordinator> coordinator,
        Action changed)
    {
        _store = store;
        _profiles = profiles;
        _connections = connections;
        _coordinator = coordinator;
        _changed = changed;
        // A null dispatcher means there is no UI thread to marshal to; refresh synchronously.
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DeviceApplicationRuleRowViewModel> Rules { get; } = new();
    public ObservableCollection<DeviceApplicationRuleRowViewModel> DeviceRules { get; } = new();
    public ObservableCollection<DeviceRuleDeviceOption> BindableDevices { get; } = new();
    public string StatusText { get; internal set; } = string.Empty;
    public ObservableCollection<string> RunningApplications { get; } = new();
    public IReadOnlyList<string> ProfileNames => _profiles().Profiles.Keys.Order().ToArray();
    public bool Enabled { get => _document.Enabled; set => _ = SetEnabledAsync(value); }
    public bool CanAdd => _selected is not null;
    public bool HasBindableDevices => BindableDevices.Count > 0;
    public string DeviceIdentityUnavailable => CanAdd ? string.Empty : AppStrings.Text("DeviceIdentityUnavailable");
    internal DeviceApplicationRuleDocument Document => _document;

    internal async Task LoadAsync(CancellationToken token) =>
        await UpdateAsync(await _store.LoadAsync(token));

    internal async Task MigrateAsync(CancellationToken token)
    {
        if (_migrated) return;
        _migrated = true;
        var result = await DeviceApplicationRuleMigration.MigrateAsync(_store, _profiles(), _connections()
            .Where(connection => connection.Definition.Category is DeviceCategory.Mouse or DeviceCategory.Keyboard)
            .Select(connection => new DeviceBindingKey(RazerHidDeviceDefinition.VendorId,
                connection.Definition.ProductId, connection.InstanceId)).ToArray(), token);
        await UpdateAsync(result.Document);
        if (result.Completed && result.Added > 0)
            AppStrings.Get("Legacy application bindings were migrated to keyboards and mice. Adjust them on each device or in the profile library.");
    }

    internal void Select(DeviceBindingKey? key)
    {
        _selected = key;
        RefreshRunningApplications(new WindowsActiveApplicationProvider().RunningExecutablePaths());
        Refresh();
    }

    internal async Task AddAsync(string executablePath, string profileName, CancellationToken token)
    {
        if (_selected is not { } device) throw new InvalidOperationException(AppStrings.Text("DeviceIdentityUnavailable"));
        await AddAsync(device, executablePath, profileName, token);
    }

    internal async Task AddAsync(
        DeviceBindingKey device,
        string executablePath,
        string profileName,
        CancellationToken token)
    {
        await SaveAsync(DeviceApplicationRuleCatalog.AddOrUpdate(_document,
            new DeviceApplicationRule(Guid.NewGuid().ToString("N"), device, profileName,
                executablePath, true)), token);
    }

    internal async Task SaveAsync(DeviceApplicationRuleRowViewModel row, CancellationToken token)
    {
        var profileName = (row.ProfileName ?? string.Empty).Split(" · ")[0].Trim();
        if (row.Enabled == row.Rule.Enabled &&
            StringComparer.Ordinal.Equals(profileName, row.Rule.ProfileName))
        {
            return;
        }

        await SaveAsync(DeviceApplicationRuleCatalog.AddOrUpdate(_document,
            row.Rule with { ProfileName = profileName, Enabled = row.Enabled }), token);
    }

    internal async Task RemoveAsync(string id, CancellationToken token) =>
        await SaveAsync(DeviceApplicationRuleCatalog.Remove(_document, id), token);

    [NotProfileScoped]
    internal async Task SetEnabledAsync(bool enabled, CancellationToken token = default)
    {
        await SaveAsync(_document with { Enabled = enabled }, token);
    }

    internal async Task RenameProfile(string oldName, string newName, CancellationToken token = default) =>
        await SaveAsync(DeviceApplicationRuleCatalog.RenameProfileReferences(_document, oldName, newName), token);

    internal int CountProfileReferences(string profileName) =>
        DeviceApplicationRuleCatalog.CountProfileReferences(_document, profileName);

    internal int CountMissingProfileReferences(IEnumerable<string> profileNames) =>
        DeviceApplicationRuleCatalog.CountMissingProfileReferences(_document, profileNames);

    internal void RefreshRunningApplications(IEnumerable<string> paths)
    {
        var ordered = paths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var signature = string.Join('\n', ordered);
        if (StringComparer.Ordinal.Equals(signature, _runningApplicationsSignature)) return;
        _runningApplicationsSignature = signature;
        // Clear only runs from user interaction (drop-down opened / device selected) while the page is alive.
        RunningApplications.Clear();
        foreach (var path in ordered) RunningApplications.Add(path);
    }

    internal void RefreshRunningApplications() =>
        RefreshRunningApplications(new WindowsActiveApplicationProvider().RunningExecutablePaths());

    private async Task SaveAsync(DeviceApplicationRuleDocument document, CancellationToken token)
    {
        DeviceApplicationRuleCatalog.Validate(document);
        await _store.SaveAsync(document, token);
        await UpdateAsync(document);
    }

    private Task UpdateAsync(DeviceApplicationRuleDocument document)
    {
        _document = document;
        Refresh();
        _changed();
        EnabledChanged?.Invoke(document.Enabled);
        return Task.CompletedTask;
    }

    internal event Action<bool>? EnabledChanged;
    internal void RefreshView() => Refresh();

    private void Refresh()
    {
        if (_dispatcher is { HasThreadAccess: false } dispatcher)
        {
            dispatcher.TryEnqueue(Refresh);
            return;
        }

        static bool Fill(ObservableCollection<DeviceApplicationRuleRowViewModel> target, IEnumerable<DeviceApplicationRule> source)
        {
            var rules = source as IReadOnlyList<DeviceApplicationRule> ?? source.ToArray();
            // These collections are bound to controls on pages that are being unloaded.
            // Clearing an unchanged collection makes WinUI raise CollectionChanged on the
            // departing ItemsRepeater, which throws 0x80004005 during fast navigation.
            if (target.Count == rules.Count &&
                target.Select(row => row.Rule).SequenceEqual(rules))
            {
                return false;
            }

            target.Clear();
            foreach (var rule in rules) target.Add(new(rule));
            return true;
        }
        var changed = Fill(Rules, _document.Rules.OrderBy(rule => rule.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.Device.InstanceId, StringComparer.OrdinalIgnoreCase));
        changed |= Fill(DeviceRules, _document.Rules.Where(rule => _selected is { } device && rule.Device.Equals(device)));
        changed |= RefreshBindableDevices();
        changed |= !Nullable.Equals(_renderedSelected, _selected);
        changed |= _renderedEnabled != _document.Enabled;
        // A new or renamed profile changes ProfileNames without touching any rule.
        var profileNames = ProfileNames.ToArray();
        changed |= !_renderedProfileNames.SequenceEqual(profileNames, StringComparer.Ordinal);
        _renderedProfileNames = profileNames;
        var states = _coordinator().States;
        foreach (var row in Rules.Concat(DeviceRules))
        {
            var deviceName = _connections().FirstOrDefault(connection =>
                connection.Definition.ProductId == row.Device.ProductId &&
                connection.InstanceId.Equals(row.Device.InstanceId, StringComparison.OrdinalIgnoreCase))?.Definition.DisplayName ?? row.Device.InstanceId;
            if (!StringComparer.Ordinal.Equals(row.DeviceName, deviceName))
            {
                row.DeviceName = deviceName;
                changed = true;
            }

            if (states.TryGetValue($"{row.Device.VendorId:X4}:{row.Device.ProductId:X4}:{row.Device.InstanceId.Trim()}", out var state))
            {
                var statusText = state.Status.ToString();
                if (!StringComparer.Ordinal.Equals(row.StatusText, statusText))
                {
                    row.StatusText = statusText;
                    changed = true;
                }

                var missingProfileName = row.Rule.ProfileName + " · " + AppStrings.Text("DeviceRuleProfileMissing");
                var displayedProfileName = state.Status == DeviceApplicationSwitchStatus.ProfileMissing
                    ? missingProfileName
                    : row.Rule.ProfileName;
                if ((StringComparer.Ordinal.Equals(row.ProfileName, row.Rule.ProfileName) ||
                     StringComparer.Ordinal.Equals(row.ProfileName, missingProfileName)) &&
                    !StringComparer.Ordinal.Equals(row.ProfileName, displayedProfileName))
                {
                    row.ProfileName = displayedProfileName;
                    changed = true;
                }
            }
        }
        var selectedStatusText = _selected is { } selected && states.TryGetValue(
            $"{selected.VendorId:X4}:{selected.ProductId:X4}:{selected.InstanceId.Trim()}", out var selectedState)
            ? selectedState.Status switch
            {
                DeviceApplicationSwitchStatus.Applied when selectedState.AppliedProfileName is { } profile =>
                    AppStrings.FormatText("DeviceRuleApplied", profile),
                DeviceApplicationSwitchStatus.WaitingForApplication => AppStrings.Text("DeviceRuleWaiting"),
                _ => string.Empty,
            }
            : string.Empty;
        if (!StringComparer.Ordinal.Equals(StatusText, selectedStatusText))
        {
            StatusText = selectedStatusText;
            changed = true;
        }
        if (changed)
        {
            _renderedSelected = _selected;
            _renderedEnabled = _document.Enabled;
            OnPropertyChanged(null);
        }
    }

    private bool RefreshBindableDevices()
    {
        var connections = _connections()
            .Where(connection => connection.Definition.Category is DeviceCategory.Mouse or DeviceCategory.Keyboard)
            .GroupBy(connection => connection.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(connection => connection.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicateNames = connections
            .GroupBy(connection => connection.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = connections.Select(connection => new DeviceRuleDeviceOption(
            new DeviceBindingKey(RazerHidDeviceDefinition.VendorId, connection.Definition.ProductId, connection.InstanceId),
            duplicateNames.Contains(connection.Definition.DisplayName)
                ? $"{connection.Definition.DisplayName} · {ShortId(connection.InstanceId)}"
                : connection.Definition.DisplayName)).ToArray();
        var signature = string.Join('\n', options.Select(option =>
            $"{option.Device.ProductId:X4}:{option.Device.InstanceId}:{option.DisplayName}"));
        if (StringComparer.Ordinal.Equals(signature, _bindableDevicesSignature)) return false;
        _bindableDevicesSignature = signature;
        BindableDevices.Clear();
        foreach (var option in options) BindableDevices.Add(option);
        return true;
    }

    private static string ShortId(string instanceId) =>
        instanceId.Length <= 8 ? instanceId : instanceId[^8..];

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
