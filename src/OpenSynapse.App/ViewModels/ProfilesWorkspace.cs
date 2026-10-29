using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Lifecycle;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// The profiles page: the saved profile catalog, the active profile, the application
/// bindings, and the startup settings that live beside them. The profile document and its
/// save path stay in the shell because the device pages write through them too.
/// </summary>
public sealed class ProfilesWorkspace : INotifyPropertyChanged
{
    private readonly ProfileStore _profileStore;
    private readonly Func<ProfileDocument> _getProfile;
    private readonly Action<ProfileDocument> _setProfile;
    private readonly Func<ProfileDefinition> _getActiveProfile;
    private readonly Func<CancellationToken, Task<bool>> _saveProfileAsync;
    private readonly MacroWorkspace _macros;
    private readonly Action<HashSet<BladePerformanceMode>, IReadOnlyList<int>?> _applyProfileShortcuts;
    private readonly Func<IReadOnlyList<BladePerformanceMode>> _bladePerformanceModes;
    private readonly Func<IReadOnlyList<int>> _displayRefreshRates;
    private readonly Func<IReadOnlyList<BladePerformanceMode>?> _legacyPerformanceCycleModes;
    private readonly Func<IReadOnlyList<int>?> _legacyRefreshRateCycleHertz;
    private readonly WindowsStartupManager? _startupManager;
    private readonly string? _executablePath;
    private readonly SemaphoreSlim _deviceOperationGate;
    private readonly Action<bool> _setBusy;
    private readonly Action<string> _setDeviceOperationError;
    private readonly Action<string> _reportApplicationError;
    private readonly Action _requestProfileApply;
    private string _profileStatusText = AppStrings.Text("Text_A9AE645B");
    private string _activeProfileName = ProfileCatalog.DefaultProfileName;
    private string _profileNameInput = string.Empty;
    private bool _isStartupEnabled;
    private bool _isSilentStartupEnabled;

    public ProfilesWorkspace(
        ProfileStore profileStore,
        Func<ProfileDocument> getProfile,
        Action<ProfileDocument> setProfile,
        Func<ProfileDefinition> getActiveProfile,
        Func<CancellationToken, Task<bool>> saveProfileAsync,
        MacroWorkspace macros,
        Action<HashSet<BladePerformanceMode>, IReadOnlyList<int>?> applyProfileShortcuts,
        Func<IReadOnlyList<BladePerformanceMode>> bladePerformanceModes,
        Func<IReadOnlyList<int>> displayRefreshRates,
        Func<IReadOnlyList<BladePerformanceMode>?> legacyPerformanceCycleModes,
        Func<IReadOnlyList<int>?> legacyRefreshRateCycleHertz,
        WindowsStartupManager? startupManager,
        string? executablePath,
        SemaphoreSlim deviceOperationGate,
        Action<bool> setBusy,
        Action<string> setDeviceOperationError,
        Action<string> reportApplicationError,
        Action requestProfileApply)
    {
        _profileStore = profileStore;
        _getProfile = getProfile;
        _setProfile = setProfile;
        _getActiveProfile = getActiveProfile;
        _saveProfileAsync = saveProfileAsync;
        _macros = macros;
        _applyProfileShortcuts = applyProfileShortcuts;
        _bladePerformanceModes = bladePerformanceModes;
        _displayRefreshRates = displayRefreshRates;
        _legacyPerformanceCycleModes = legacyPerformanceCycleModes;
        _legacyRefreshRateCycleHertz = legacyRefreshRateCycleHertz;
        _startupManager = startupManager;
        _executablePath = executablePath;
        _deviceOperationGate = deviceOperationGate;
        _setBusy = setBusy;
        _setDeviceOperationError = setDeviceOperationError;
        _reportApplicationError = reportApplicationError;
        _requestProfileApply = requestProfileApply;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> ProfileNames { get; } = new();
    public ObservableCollection<ApplicationBindingRowViewModel> ApplicationBindings { get; } = new();

    public string ProfileStatusText
    {
        get => _profileStatusText;
        internal set => SetField(ref _profileStatusText, value);
    }

    public string ActiveProfileName { get => _activeProfileName; private set => SetField(ref _activeProfileName, value); }
    public string ProfileNameInput { get => _profileNameInput; set => SetField(ref _profileNameInput, value); }
    public bool CanDeleteProfile => ProfileNames.Count > 1;

    public bool IsStartupEnabled
    {
        get => _isStartupEnabled;
        private set
        {
            if (SetField(ref _isStartupEnabled, value))
            {
                OnPropertyChanged(nameof(CanSetSilentStartup));
            }
        }
    }

    public bool IsSilentStartupEnabled { get => _isSilentStartupEnabled; private set => SetField(ref _isSilentStartupEnabled, value); }
    public bool CanSetStartup => _startupManager is not null && !string.IsNullOrWhiteSpace(_executablePath);
    public bool CanSetSilentStartup => CanSetStartup && IsStartupEnabled;

    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            _setProfile(await _profileStore.LoadAsync(cancellationToken));
            var migrated = EnsureActiveProfileExtensions();
            RefreshProfileState();
            if (migrated)
            {
                await _profileStore.SaveAsync(_getProfile(), cancellationToken);
            }
            RefreshStartupState();
            ProfileStatusText = AppStrings.FormatText("ProfileLoaded", ActiveProfileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _setProfile(ProfileDocument.CreateDefault());
            RefreshProfileState();
            RefreshStartupState();
            ProfileStatusText = AppStrings.FormatText("DefaultProfileLoaded", ActiveProfileName);
            _reportApplicationError(AppStrings.FormatText("ProfileLoadError", exception.Message));
        }
    }

    public async Task SelectProfileAsync(string? name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            StringComparer.OrdinalIgnoreCase.Equals(name, ActiveProfileName))
        {
            return;
        }

        if (!await _macros.CommitMacroEditsBeforeProfileSwitchAsync(cancellationToken))
        {
            return;
        }

        await RunProfileOperationAsync(AppStrings.FormatText("SwitchProfile", name), () =>
        {
            ProfileCatalog.Select(_getProfile(), name);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task CreateProfileAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameInput))
        {
            return;
        }

        var name = ProfileNameInput;
        if (!await _macros.CommitMacroEditsBeforeProfileSwitchAsync(cancellationToken))
        {
            return;
        }

        await RunProfileOperationAsync(AppStrings.FormatText("CreateProfile", name), () =>
        {
            ProfileCatalog.Create(_getProfile(), name);
            ProfileCatalog.Select(_getProfile(), name);
            ProfileNameInput = string.Empty;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task DeleteActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDeleteProfile)
        {
            return;
        }

        await RunProfileOperationAsync(AppStrings.FormatText("DeleteProfile", ActiveProfileName), () =>
        {
            ProfileCatalog.Delete(_getProfile(), ActiveProfileName);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task CloneActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameInput))
        {
            return;
        }

        var name = ProfileNameInput;
        await RunProfileOperationAsync(AppStrings.FormatText("CloneProfile", name), () =>
        {
            ProfileCatalog.Clone(_getProfile(), ActiveProfileName, name);
            ProfileCatalog.Select(_getProfile(), name);
            ProfileNameInput = string.Empty;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task RenameActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameInput))
        {
            return;
        }

        var name = ProfileNameInput;
        await RunProfileOperationAsync(AppStrings.FormatText("RenameProfile", name), () =>
        {
            ProfileCatalog.Rename(_getProfile(), ActiveProfileName, name);
            ProfileNameInput = string.Empty;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public Task BindApplicationAsync(string executablePath, CancellationToken cancellationToken = default) =>
        RunProfileOperationAsync(AppStrings.FormatText("BindApplication", Path.GetFileName(executablePath)), () =>
        {
            ApplicationProfileBinding.Bind(_getProfile(), executablePath, ActiveProfileName);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);

    public Task UnbindApplicationAsync(string executablePath, CancellationToken cancellationToken = default) =>
        RunProfileOperationAsync(AppStrings.FormatText("UnbindApplication", Path.GetFileName(executablePath)), () =>
        {
            ApplicationProfileBinding.Unbind(_getProfile(), executablePath);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);

    public async Task ImportProfilesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ProfileDocument imported;
        try
        {
            imported = await ProfileStore.ImportAsync(filePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _setDeviceOperationError(AppStrings.FormatText("ProfileImportError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("ProfileImportFailed", exception.Message);
            return;
        }

        await RunProfileOperationAsync(AppStrings.Text("Text_2A08EC21"), () =>
        {
            _setProfile(imported);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task ExportProfilesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }
        _setBusy(true);
        try
        {
            await ProfileStore.ExportAsync(_getProfile().Clone(), filePath, cancellationToken);
            ProfileStatusText = AppStrings.FormatText("ProfileExported", Path.GetFileName(filePath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _setDeviceOperationError(AppStrings.FormatText("ProfileExportError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("ProfileExportFailed", exception.Message);
        }
        finally
        {
            _setBusy(false);
            _deviceOperationGate.Release();
        }
    }

    public async Task SetStartupEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!CanSetStartup || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }
        _setBusy(true);
        try
        {
            _startupManager!.SetEnabled(enabled, _executablePath!, IsSilentStartupEnabled);
            IsStartupEnabled = enabled;
            if (!enabled)
            {
                IsSilentStartupEnabled = false;
            }
            ProfileStatusText = enabled ? AppStrings.Text("Text_62E1155B") : AppStrings.Text("Text_8FAB6EC0");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            _setDeviceOperationError(AppStrings.FormatText("StartupError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("StartupSettingFailed", exception.Message);
            RefreshStartupState();
        }
        finally
        {
            _setBusy(false);
            _deviceOperationGate.Release();
        }
    }

    public async Task SetSilentStartupEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!CanSetSilentStartup || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }
        _setBusy(true);
        try
        {
            _startupManager!.SetEnabled(true, _executablePath!, enabled);
            IsSilentStartupEnabled = enabled;
            ProfileStatusText = AppStrings.Text(
                enabled ? "SilentStartupEnabled" : "SilentStartupDisabled");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            _setDeviceOperationError(AppStrings.FormatText("StartupError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("StartupSettingFailed", exception.Message);
            RefreshStartupState();
        }
        finally
        {
            _setBusy(false);
            _deviceOperationGate.Release();
        }
    }

    /// <summary>Rebuilds the profile-derived UI and hands the shortcut settings to the shell.</summary>
    internal void RefreshProfileState()
    {
        EnsureActiveProfileExtensions();
        var profile = _getProfile();
        ProfileNames.Clear();
        foreach (var name in ProfileCatalog.GetNames(profile))
        {
            ProfileNames.Add(name);
        }

        ActiveProfileName = profile.ActiveProfileName;
        var shortcuts = _getActiveProfile().Shortcuts;
        var cycleModes = shortcuts.PerformanceCycleModes!
            .Where(_bladePerformanceModes().Contains)
            .ToHashSet();
        if (cycleModes.Count == 0)
        {
            cycleModes = [.. _bladePerformanceModes()];
        }
        _applyProfileShortcuts(cycleModes, shortcuts.RefreshRateCycleHertz);
        ApplicationBindings.Clear();
        foreach (var binding in profile.ApplicationBindings.OrderBy(binding => binding.Key, StringComparer.OrdinalIgnoreCase))
        {
            ApplicationBindings.Add(new ApplicationBindingRowViewModel(binding.Key, binding.Value));
        }
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(CanDeleteProfile));
    }

    private async Task RunProfileOperationAsync(
        string label,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        if (!await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }
        var previous = _getProfile().Clone();
        _setBusy(true);
        try
        {
            await operation();
            await _profileStore.SaveAsync(_getProfile(), cancellationToken);
            _requestProfileApply();
            ProfileStatusText = AppStrings.FormatText("ProfileOperationSucceeded", label, ActiveProfileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _setProfile(previous);
            RefreshProfileState();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            _setDeviceOperationError(AppStrings.FormatText("LabeledError", label, exception.Message));
            ProfileStatusText = AppStrings.FormatText("ProfileOperationFailed", label, exception.Message);
            _setProfile(previous);
            RefreshProfileState();
        }
        finally
        {
            _setBusy(false);
            _deviceOperationGate.Release();
        }
    }

    private bool EnsureActiveProfileExtensions()
    {
        var profile = _getActiveProfile();
        var changed = false;
        if (profile.Shortcuts.PerformanceCycleModes is null)
        {
            profile.Shortcuts.PerformanceCycleModes = (_legacyPerformanceCycleModes() is { Count: > 0 } legacy
                    ? legacy
                    : BladePerformanceModes)
                .Distinct()
                .ToList();
            changed = true;
        }
        if (profile.Shortcuts.RefreshRateCycleHertz is null)
        {
            var defaults = _legacyRefreshRateCycleHertz() is { Count: > 0 } legacy
                ? legacy
                : _displayRefreshRates();
            if (defaults.Count > 0)
            {
                profile.Shortcuts.RefreshRateCycleHertz = defaults
                    .Where(hertz => hertz > 0)
                    .Distinct()
                    .Order()
                    .ToList();
                changed = true;
            }
        }
        if (profile.Global.Blade.MappingPreset is null)
        {
            profile.Global.Blade.MappingPreset = BladeProfileSettings.Product710DefaultMappingPreset;
            changed = true;
        }
        if (profile.Global.Blade.SnapTapEnabled is null)
        {
            profile.Global.Blade.SnapTapEnabled = false;
            changed = true;
        }
        return changed;
    }

    private void RefreshStartupState()
    {
        try
        {
            IsStartupEnabled = _startupManager is not null &&
                !string.IsNullOrWhiteSpace(_executablePath) &&
                _startupManager.IsEnabled(_executablePath);
            IsSilentStartupEnabled = IsStartupEnabled &&
                _startupManager!.IsSilentEnabled(_executablePath!);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            IsStartupEnabled = false;
            IsSilentStartupEnabled = false;
            _setDeviceOperationError(AppStrings.FormatText("StartupError", exception.Message));
        }
        OnPropertyChanged(nameof(CanSetStartup));
    }

    private async Task<bool> TryEnterOperationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _deviceOperationGate.WaitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
