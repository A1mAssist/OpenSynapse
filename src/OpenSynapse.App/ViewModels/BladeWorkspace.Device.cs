using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    public async ValueTask DisposeAsync()
    {
        Task brightnessWriter;
        lock (_bladeBrightnessGate)
        {
            _bladeBrightnessRequests.Clear();
            brightnessWriter = _bladeBrightnessWriter;
        }
        await brightnessWriter.ConfigureAwait(false);
        Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
        await _bladeBrightnessVerification.ConfigureAwait(false);
    }

    internal void OnDisplayAvailabilityChanged(bool available)
    {
        if (!available)
        {
            Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
        }
    }

    internal async Task RefreshCoolingPadPresenceAsync(CancellationToken cancellationToken)
    {
        var connected = await RazerCoolingPad.IsConnectedAsync(cancellationToken);
        if (_bladeCoolingPadConnected == connected)
        {
            return;
        }

        _bladeCoolingPadConnected = connected;
        OnPropertyChanged(nameof(HyperboostOptionVisibility));
        OnPropertyChanged(nameof(HyperboostNeedsCoolingPadWarning));
    }

    internal bool CoolingPadConnected => _bladeCoolingPadConnected;

    internal bool PadlessHyperBoostAccepted => _padlessHyperBoostAccepted;

    internal void UpdateDevice(DeviceDescriptor? blade, string statusText)
    {
        DeviceName = blade?.Name ?? "Razer Blade";
        SetControlDevice(blade);
        if (_blade._bladeProductId != (blade?.ProductId ?? 0))
        {
            _blade._bladeProductId = blade?.ProductId ?? 0;
            _blade._bladeFanRange = null;
            OnPropertyChanged(nameof(BladeProductId));
            _gpuModeRecoveryReconciled = false;
            RefreshModelCapabilityUi();
        }

        var deviceChanged = !StringComparer.OrdinalIgnoreCase.Equals(_statusDeviceId, blade?.Id);
        _statusDeviceId = blade?.Id;
        // A rescan must not replace a live status with the discovery placeholder.
        if (deviceChanged || blade is null || blade.Access != DeviceAccessState.Available)
        {
            StatusText = statusText;
        }
        if (blade is not null)
        {
            ProbeGpuClockOffset();
        }
        NotifyStateChanged();
    }

    internal void SetControlDevice(DeviceDescriptor? blade)
    {
        var value = blade is
        {
            Access: DeviceAccessState.Available,
            FeatureReportByteLength: RazerFeatureReport.Length,
        }
            ? blade.Id
            : null;
        var productId = blade?.ProductId ?? 0;
        if (StringComparer.OrdinalIgnoreCase.Equals(_bladeControlDevicePath, value) &&
            _bladeControlProductId == productId)
        {
            return;
        }

        _bladeControlDevicePath = value;
        _bladeControlProductId = productId;
        SetFilterDriverMissing(false);
        ControlDeviceChanged?.Invoke(value, productId);
    }

    internal void SetStatus(string statusText)
    {
        _statusDeviceId = null;
        StatusText = statusText;
    }

    internal void SetSerial(string? serial) => Serial = serial;

    internal void RefreshDeviceOverview()
    {
        OnPropertyChanged(nameof(ProtocolText));
        OnPropertyChanged(nameof(IdentityText));
    }

    internal void ApplyProfileShortcuts(
        HashSet<BladePerformanceMode> cycleModes,
        BladeProfileSettings bladeSettings)
    {
        _bladePerformanceCycleModes = cycleModes;
        var snapTap = bladeSettings.SnapTapEnabled == true;
        var mappingPreset = bladeSettings.MappingPreset ??
            BladeProfileSettings.Product710DefaultMappingPreset;
        var bladeInputChanged = snapTap != _activeSnapTapEnabled ||
            !StringComparer.Ordinal.Equals(mappingPreset, _activeBladeMappingPreset);
        _activeSnapTapEnabled = snapTap;
        _activeBladeMappingPreset = mappingPreset;
        RefreshLightingEditor();
        RefreshPerformanceEditor();
        OnPropertyChanged(nameof(PerformanceCycleModes));
        if (bladeInputChanged)
        {
            InputProfileChanged?.Invoke();
        }
    }

    internal void SetPerformanceCycleModes(IEnumerable<BladePerformanceMode> modes)
    {
        var selected = modes.Where(_blade.PerformanceModes.Contains).ToHashSet();
        if (selected.Count == 0)
        {
            throw new ArgumentException(
                "At least one performance mode must remain in the shortcut cycle.",
                nameof(modes));
        }
        _bladePerformanceCycleModes = selected;
    }

    internal async Task<bool> SavePerformanceCycleModesAsync(
        IEnumerable<BladePerformanceMode> modes,
        CancellationToken cancellationToken = default)
    {
        var selected = modes.Where(_blade.PerformanceModes.Contains).Distinct().ToArray();
        if (selected.Length == 0)
        {
            return false;
        }

        var previous = _bladePerformanceCycleModes;
        _bladePerformanceCycleModes = selected.ToHashSet();
        GetActiveProfile().Shortcuts.PerformanceCycleModes = selected.ToList();
        if (await SaveProfileAsync(cancellationToken))
        {
            OnPropertyChanged(nameof(PerformanceCycleModes));
            return true;
        }

        _bladePerformanceCycleModes = previous;
        GetActiveProfile().Shortcuts.PerformanceCycleModes = previous.ToList();
        return false;
    }

    internal async Task SetSnapTapEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var previous = Profile.Global.Blade.SnapTapEnabled;
        Profile.Global.Blade.SnapTapEnabled = enabled;
        if (!await SaveProfileAsync(cancellationToken))
        {
            Profile.Global.Blade.SnapTapEnabled = previous;
            return;
        }
        _activeSnapTapEnabled = enabled;
    }

    internal IReadOnlySet<BladePerformanceMode> PerformanceCycleModes => _bladePerformanceCycleModes;

    internal bool ActiveSnapTapEnabled => _activeSnapTapEnabled;

    internal string ActiveMappingPreset => _activeBladeMappingPreset;

    internal BladeViewModel State => _blade;

    internal IReadOnlyList<BladePerformanceMode> PerformanceModes => _blade.PerformanceModes;

    /// <summary>
    /// HyperBoost without a cooling pad needs the main window's disclaimer, which
    /// cannot be shown from the tray menu.
    /// </summary>
    public bool CanApplyPerformanceModeFromTray(BladePerformanceMode mode) =>
        mode != BladePerformanceMode.Hyperboost ||
        !HyperboostNeedsCoolingPadWarning ||
        _padlessHyperBoostAccepted;

    internal BladeLightingDialect LightingDialect =>
        BladeLightingRuntimeDialect.Get(_blade._bladeProductId);

    internal bool UsesFirmwareKeyboardLighting =>
        BladeLighting2023Admitted.UsesFirmwareKeyboardLighting(LightingDialect);

    private bool CanApplyLightingDialect =>
        LightingDialect == BladeLightingDialect.FirmwareEffect ||
        (_bladeLightingController is not null &&
         LightingDialect is BladeLightingDialect.LedMatrixBlade or
             BladeLightingDialect.LedMatrix710 or BladeLightingDialect.LedMatrixBlade2 or
             BladeLightingDialect.LedMatrix2026);
}
