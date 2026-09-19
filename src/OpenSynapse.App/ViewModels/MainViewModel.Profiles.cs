using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task<bool> SaveProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _profileStore.SaveAsync(_profile, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError(AppStrings.FormatText("ProfileSaveError", exception.Message));
            return false;
        }
    }
    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            _profile = await _profileStore.LoadAsync(cancellationToken);
            var migrated = EnsureActiveProfileExtensions();
            RefreshProfileState();
            if (migrated)
            {
                await _profileStore.SaveAsync(_profile, cancellationToken);
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
            _profile = ProfileDocument.CreateDefault();
            RefreshProfileState();
            RefreshStartupState();
            ProfileStatusText = AppStrings.FormatText("DefaultProfileLoaded", ActiveProfileName);
            ReportApplicationError(AppStrings.FormatText("ProfileLoadError", exception.Message));
        }
    }

    public async Task SelectProfileAsync(string? name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            StringComparer.OrdinalIgnoreCase.Equals(name, ActiveProfileName))
        {
            return;
        }

        await RunProfileOperationAsync(AppStrings.FormatText("SwitchProfile", name), () =>
        {
            ProfileCatalog.Select(_profile, name);
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
        await RunProfileOperationAsync(AppStrings.FormatText("CreateProfile", name), () =>
        {
            ProfileCatalog.Create(_profile, name);
            ProfileCatalog.Select(_profile, name);
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
            ProfileCatalog.Delete(_profile, ActiveProfileName);
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
            ProfileCatalog.Clone(_profile, ActiveProfileName, name);
            ProfileCatalog.Select(_profile, name);
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
            ProfileCatalog.Rename(_profile, ActiveProfileName, name);
            ProfileNameInput = string.Empty;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public Task BindApplicationAsync(string executablePath, CancellationToken cancellationToken = default) =>
        RunProfileOperationAsync(AppStrings.FormatText("BindApplication", Path.GetFileName(executablePath)), () =>
        {
            ApplicationProfileBinding.Bind(_profile, executablePath, ActiveProfileName);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);

    public Task UnbindApplicationAsync(string executablePath, CancellationToken cancellationToken = default) =>
        RunProfileOperationAsync(AppStrings.FormatText("UnbindApplication", Path.GetFileName(executablePath)), () =>
        {
            ApplicationProfileBinding.Unbind(_profile, executablePath);
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
            SetDeviceOperationError(AppStrings.FormatText("ProfileImportError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("ProfileImportFailed", exception.Message);
            return;
        }

        await RunProfileOperationAsync(AppStrings.Text("Text_2A08EC21"), () =>
        {
            _profile = imported;
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
        IsBusy = true;
        try
        {
            await ProfileStore.ExportAsync(_profile.Clone(), filePath, cancellationToken);
            ProfileStatusText = AppStrings.FormatText("ProfileExported", Path.GetFileName(filePath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError(AppStrings.FormatText("ProfileExportError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("ProfileExportFailed", exception.Message);
        }
        finally
        {
            IsBusy = false;
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
        IsBusy = true;
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
            SetDeviceOperationError(AppStrings.FormatText("StartupError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("StartupSettingFailed", exception.Message);
            RefreshStartupState();
        }
        finally
        {
            IsBusy = false;
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
        IsBusy = true;
        try
        {
            _startupManager!.SetEnabled(true, _executablePath!, enabled);
            IsSilentStartupEnabled = enabled;
            ProfileStatusText = AppStrings.Text(
                enabled ? "SilentStartupEnabled" : "SilentStartupDisabled");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            SetDeviceOperationError(AppStrings.FormatText("StartupError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("StartupSettingFailed", exception.Message);
            RefreshStartupState();
        }
        finally
        {
            IsBusy = false;
            _deviceOperationGate.Release();
        }
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
        var previous = _profile.Clone();
        IsBusy = true;
        try
        {
            await operation();
            await _profileStore.SaveAsync(_profile, cancellationToken);
            RequestProfileApply();
            ProfileStatusText = AppStrings.FormatText("ProfileOperationSucceeded", label, ActiveProfileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _profile = previous;
            RefreshProfileState();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError(AppStrings.FormatText("LabeledError", label, exception.Message));
            ProfileStatusText = AppStrings.FormatText("ProfileOperationFailed", label, exception.Message);
            _profile = previous;
            RefreshProfileState();
        }
        finally
        {
            IsBusy = false;
            _deviceOperationGate.Release();
        }
    }

    private void RefreshProfileState()
    {
        EnsureActiveProfileExtensions();
        var snapTap = _profile.Global.Blade.SnapTapEnabled == true;
        var mappingPreset = _profile.Global.Blade.MappingPreset ??
            BladeProfileSettings.Product710DefaultMappingPreset;
        var bladeInputChanged = snapTap != _activeSnapTapEnabled ||
            !StringComparer.Ordinal.Equals(mappingPreset, _activeBladeMappingPreset);
        _activeSnapTapEnabled = snapTap;
        _activeBladeMappingPreset = mappingPreset;
        ProfileNames.Clear();
        foreach (var name in ProfileCatalog.GetNames(_profile))
        {
            ProfileNames.Add(name);
        }

        ActiveProfileName = _profile.ActiveProfileName;
        var shortcuts = GetActiveProfile().Shortcuts;
        _bladePerformanceCycleModes = shortcuts.PerformanceCycleModes!.ToHashSet();
        _internalDisplayRefreshRateCycleHertz = shortcuts.RefreshRateCycleHertz?.ToHashSet();
        RefreshBladeLightingEditor();
        RefreshBladePerformanceEditor();
        ApplicationBindings.Clear();
        foreach (var binding in _profile.ApplicationBindings.OrderBy(binding => binding.Key, StringComparer.OrdinalIgnoreCase))
        {
            ApplicationBindings.Add(new ApplicationBindingRowViewModel(binding.Key, binding.Value));
        }
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(CanDeleteProfile));
        OnPropertyChanged(nameof(BladePerformanceCycleModes));
        OnPropertyChanged(nameof(InternalDisplayRefreshRateCycleHertz));
        if (bladeInputChanged)
        {
            BladeInputProfileChanged?.Invoke();
        }
    }

    private ProfileDefinition GetActiveProfile() =>
        _profile.Profiles.TryGetValue(_profile.ActiveProfileName, out var profile)
            ? profile
            : throw new InvalidOperationException("The active profile is missing from the profile catalog.");

    private bool EnsureActiveProfileExtensions()
    {
        var profile = GetActiveProfile();
        var changed = false;
        if (profile.Shortcuts.PerformanceCycleModes is null)
        {
            profile.Shortcuts.PerformanceCycleModes = (_legacyPerformanceCycleModes is { Count: > 0 }
                    ? _legacyPerformanceCycleModes
                    : BladePerformanceModes)
                .Distinct()
                .ToList();
            changed = true;
        }
        if (profile.Shortcuts.RefreshRateCycleHertz is null)
        {
            var defaults = _legacyRefreshRateCycleHertz is { Count: > 0 }
                ? _legacyRefreshRateCycleHertz
                : InternalDisplayRefreshRates;
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
            SetDeviceOperationError(AppStrings.FormatText("StartupError", exception.Message));
        }
        OnPropertyChanged(nameof(CanSetStartup));
    }

    private async Task<ProfileApplyResult?> ApplyLoadedProfileAsync(
        RazerDeviceTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        if (_deviceDescriptors.Count == 0)
        {
            return null;
        }

        var result = await _profileApplier.ApplyAsync(
            _profile,
            _deviceDescriptors,
            telemetry,
            _deviceTelemetryReader,
            _powerSourceProvider.IsPluggedIn,
            cancellationToken,
            applyKeyboardBrightness: Volatile.Read(ref _displayAvailable) != 0);
        return result;
    }

    private async Task<string?> ApplyLoadedViperMappingProfileAsync(
        DeviceDescriptor? viper,
        bool? powerState,
        CancellationToken cancellationToken)
    {
        if (viper is null || viper.Access != DeviceAccessState.Available)
        {
            _viper._viperMappingProfileFingerprint = string.Empty;
            return null;
        }

        var profile = ProfileResolver.Resolve(_profile, viper, powerState).Viper;
        var fingerprint = CreateViperMappingFingerprint(viper.Id, profile.ButtonAssignments);
        if (StringComparer.Ordinal.Equals(_viper._viperMappingProfileFingerprint, fingerprint))
        {
            return null;
        }

        try
        {
            IReadOnlyList<ViperButtonAssignment> actual;
            if (profile.ButtonAssignments is null)
            {
                actual = await _deviceTelemetryReader.ReadViperButtonAssignmentsAsync(
                    _deviceDescriptors, cancellationToken);
                var previous = _profile.Global.Viper.ButtonAssignments;
                _profile.Global.Viper.ButtonAssignments = actual.Select(ToProfileAssignment).ToList();
                if (!await SaveProfileAsync(cancellationToken))
                {
                    _profile.Global.Viper.ButtonAssignments = previous;
                    return AppStrings.Text("ViperMappingReadProfileSaveFailed");
                }
                fingerprint = CreateViperMappingFingerprint(
                    viper.Id,
                    _profile.Global.Viper.ButtonAssignments);
            }
            else
            {
                actual = await _deviceTelemetryReader.SetViperButtonAssignmentsAsync(
                    _deviceDescriptors,
                    profile.ButtonAssignments.Select(ToDeviceAssignment).ToArray(),
                    cancellationToken);
            }

            SetViperButtonAssignments(actual);
            _viper._viperMappingProfileFingerprint = fingerprint;
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _viper._viperMappingProfileFingerprint = string.Empty;
            return AppStrings.FormatText("ViperMappingProfileError", exception.Message);
        }
    }

    private string CreateViperMappingFingerprint(
        string devicePath,
        IReadOnlyList<ViperButtonAssignmentProfile>? assignments) =>
        string.Join('|',
            _profile.ActiveProfileName,
            devicePath,
            assignments is null
                ? "unmanaged"
                : string.Join(';', assignments
                    .OrderBy(item => item.ButtonId)
                    .ThenBy(item => item.Layer)
                    .Select(item => $"{item.ProfileId:X2}:{item.ButtonId:X2}:{(byte)item.Layer:X2}:{(byte)item.Function:X2}:{Convert.ToHexString(item.FunctionData.ToArray())}")));

    private async Task<BladeFanProfileApplyResult> ApplyLoadedFanProfileAsync(
        DeviceDescriptor? blade,
        bool? powerState,
        CancellationToken cancellationToken)
    {
        if (blade is null || blade.Access != DeviceAccessState.Available)
        {
            return new(await StopBladeFanControlAsync("device-unavailable"), Changed: true);
        }

        var profile = ProfileResolver.Resolve(_profile, blade, powerState).Blade;
        BladeFanCurve? curve = null;
        try
        {
            if (profile.FanCurve is not null)
            {
                if (profile.FanMode is not null || profile.FanTargetRpm is not null)
                {
                    return new(AppStrings.Text("Text_780EB5E5"), Changed: false);
                }

                curve = profile.FanCurve.CreateCurve();
            }
            else if (profile.FanMode is byte rawMode)
            {
                if (!Enum.IsDefined(typeof(BladeFanMode), rawMode))
                {
                    return new(AppStrings.FormatText("InvalidBladeFanMode", rawMode), Changed: false);
                }

                var mode = (BladeFanMode)rawMode;
                if (mode == BladeFanMode.Manual && profile.FanTargetRpm is null ||
                    mode == BladeFanMode.Automatic && profile.FanTargetRpm is not null)
                {
                    return new(AppStrings.Text("Text_F76B82D3"), Changed: false);
                }
            }
            else if (profile.FanTargetRpm is not null)
            {
                return new(AppStrings.Text("Text_FF784DE9"), Changed: false);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new(exception.Message, Changed: false);
        }

        var fingerprint = CreateBladeFanFingerprint(profile, blade.Id, powerState);
        if (StringComparer.Ordinal.Equals(_bladeFanControlFingerprint, fingerprint) &&
            _bladeFanControlCompletion is { IsCompleted: false })
        {
            return new(null, Changed: false);
        }

        var hadControl = _bladeFanRuntime.IsRunning || _bladeFanControlFingerprint.Length > 0;
        var stopError = await StopBladeFanControlAsync("profile-change");
        if (stopError is not null)
        {
            return new(stopError, Changed: true);
        }

        if (fingerprint.Length == 0)
        {
            return new(null, Changed: hadControl);
        }

        try
        {
            if (curve is not null)
            {
                await _bladeFanRuntime.StartAsync(
                    _deviceDescriptors,
                    curve,
                    cancellationToken);
            }
            else
            {
                var mode = (BladeFanMode)profile.FanMode!.Value;
                await _bladeFanRuntime.StartFixedAsync(
                    _deviceDescriptors,
                    mode,
                    profile.FanTargetRpm,
                    cancellationToken);
            }

            _bladeFanControlFingerprint = fingerprint;
            _bladeFanControlCompletion = _bladeFanRuntime.Completion;
            _ = ObserveBladeFanControlAsync(_bladeFanControlCompletion);
            return new(null, Changed: true);
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            _bladeFanControlFingerprint = string.Empty;
            _bladeFanControlCompletion = null;
            return new(FormatOperationException(exception), Changed: true);
        }
    }


}
