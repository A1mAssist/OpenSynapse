using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    private readonly Func<RazerCoolingPadViewModel?> _coolingPad;
    private int _coolingPadMissedRefreshes;

    public async ValueTask DisposeAsync()
    {
        _touchpadSyncStop.Cancel();
        Task indicatorWriter;
        lock (_touchpadSyncGate)
        {
            _touchpadPending = null;
            indicatorWriter = _touchpadSyncTask;
        }
        await indicatorWriter.ConfigureAwait(false);
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
        var pad = _coolingPad();
        var ready = pad?.IsReady ??
            await RazerCoolingPad.IsConnectedAsync(cancellationToken);
        if (ready)
        {
            _coolingPadMissedRefreshes = 0;
            if (_bladeCoolingPadConnected)
            {
                return;
            }

            _bladeCoolingPadConnected = true;
            OnPropertyChanged(nameof(HyperboostOptionVisibility));
            OnPropertyChanged(nameof(HyperboostNeedsCoolingPadWarning));
            return;
        }

        _coolingPadMissedRefreshes++;
        if (!_bladeCoolingPadConnected)
        {
            return;
        }

        // A pad that is gone is gone. A pad that is still connected but missed one read
        // gets a second refresh: a single HID timeout must not switch the performance mode.
        if (pad is not null && pad.IsConnected && _coolingPadMissedRefreshes < 2)
        {
            return;
        }

        _bladeCoolingPadConnected = false;
        OnPropertyChanged(nameof(HyperboostOptionVisibility));
        OnPropertyChanged(nameof(HyperboostNeedsCoolingPadWarning));
        await RevertHyperBoostAfterPadLossAsync(cancellationToken);
    }

    /// <summary>
    /// A HyperBoost entry that needed the pad must not keep running unmanaged once the
    /// pad stops answering: either roll back to the previous mode, or — when the user
    /// accepted pad-less HyperBoost — hand the airflow back to the laptop fans.
    /// This runs inside the refresh gate, so the write can go straight to the reader.
    /// </summary>
    private async Task RevertHyperBoostAfterPadLossAsync(CancellationToken cancellationToken)
    {
        if (_blade._confirmedBladePerformanceModeIndex < 0 ||
            _blade.PerformanceModes[_blade._confirmedBladePerformanceModeIndex] !=
                BladePerformanceMode.Hyperboost)
        {
            return;
        }

        if (_padlessHyperBoostAccepted)
        {
            // HyperBoost started with the pad attached, so the Max Fan pin was never set.
            // Re-sending the mode pad-less is what pins the laptop fans on models with
            // that bit; on the others the EC preset curve already owns them.
            if (!_blade.SupportsMaxFan)
            {
                return;
            }

            try
            {
                SetPerformanceMode(await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                    ActiveDescriptors,
                    BladePerformanceMode.Hyperboost,
                    cancellationToken,
                    coolingPadConnected: false));
            }
            catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
            {
                _diagnosticLog.TryWrite(
                    "blade-performance",
                    $"HyperBoost could not pin the laptop fans after the pad left: {exception.Message}");
            }

            return;
        }

        var modes = _blade.PerformanceModes;
        var target = modes.Contains(_blade.LastNonHyperboostMode)
            ? _blade.LastNonHyperboostMode
            : modes.Contains(BladePerformanceMode.Performance)
                ? BladePerformanceMode.Performance
                : BladePerformanceMode.Balanced;
        try
        {
            SetPerformanceMode(await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                ActiveDescriptors, target, cancellationToken, coolingPadConnected: false));
            // Keep the profile in step, or every refresh would try HyperBoost again and
            // rewrite the same fallback mode.
            await BladePerformanceProfileEdit.SaveModeAsync(
                EditablePerformanceProfile, (byte)target, SaveProfileAsync, cancellationToken,
                AppStrings.Text("Text_CC12A6F5"));
            _diagnosticLog.TryWrite(
                "blade-performance",
                $"HyperBoost left for {target}: the cooling pad stopped answering.");
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            _diagnosticLog.TryWrite(
                "blade-performance",
                $"HyperBoost fallback to {target} failed: {exception.Message}");
            SetDeviceOperationError(AppStrings.FormatText(
                "LabeledError",
                AppStrings.Text("BladeHyperboostCoolingPadLost"),
                exception.Message));
        }
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
        var available = blade is
        {
            Access: DeviceAccessState.Available,
            FeatureReportByteLength: RazerFeatureReport.Length,
        };
        var value = available ? blade!.Id : null;
        var productId = blade?.ProductId ?? 0;
        if (available)
        {
            _bladeControlUnavailableCount = 0;
        }
        else if (_bladeControlDevicePath is { Length: > 0 } &&
                 (blade is null || productId == _bladeControlProductId) &&
                 // One unverified snapshot is not a removal; two in a row is.
                 ++_bladeControlUnavailableCount < 2)
        {
            // A scan that omits the interface entirely (blade == null) is the same
            // kind of unverified snapshot as a descriptor that failed its probe.
            return;
        }
        else
        {
            _bladeControlUnavailableCount = 0;
        }

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
        (PowerSourceResolver.IsVerifiedFullPower(CurrentPowerSourceEvidence) &&
         (!HyperboostNeedsCoolingPadWarning || _padlessHyperBoostAccepted));

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
