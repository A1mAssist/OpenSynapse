using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel : INotifyPropertyChanged
{
    public void RefreshLocalization()
    {
        foreach (var row in DpiStages) row.RefreshLocalization();
        foreach (var cell in MatrixCells) cell.RefreshLocalization();
        foreach (var row in KnobStages) row.RefreshLocalization();
        foreach (var row in WirelessDevices) row.RefreshLocalization();
        Soundbar?.RefreshLocalization();
        OnPropertyChanged(string.Empty);
    }

    private void ReplaceDpiStages(RazerHidDpiStages state)
    {
        DpiStages.Clear();
        foreach (var stage in state.Stages)
        {
            var row = new RazerHidDpiStageRowViewModel(stage.Number, stage.X, stage.Y, stage.Z, CanWriteDpiStages);
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanEditDpiStages));
            DpiStages.Add(row);
        }
        ActiveDpiStage = state.ActiveStage;
        OnPropertyChanged(nameof(CanEditDpiStages));
    }

    private RazerHidLightingZoneCapabilities? SelectedZoneCapabilities =>
        Connection.LightingZones.GetValueOrDefault(SelectedLightingZone);

    private bool IsVisibleLightingZone(RazerHidLedZone zone)
    {
        if (RequiresRescan) return false;
        if (!Connection.LightingZones.TryGetValue(zone, out var capabilities)) return false;
        return Has(RazerHidBackendCapability.LightingEffectWrite) &&
                capabilities.LightingEffects.Any(effect => effect != RazerHidLightingEffect.Custom &&
                !_unsupportedLighting.Contains((zone, effect))) ||
            capabilities.CanReadBrightness && !_unsupportedBrightnessReads.Contains(zone) ||
            capabilities.CanWriteBrightness && !_unsupportedBrightnessWrites.Contains(zone) ||
            capabilities.CanReadState && !_unsupportedLedStateReads.Contains(zone) ||
            capabilities.CanWriteState && !_unsupportedLedStateWrites.Contains(zone);
    }


    private bool Has(RazerHidBackendCapability capability) =>
        !RequiresRescan && Connection.Capabilities.Contains(capability) && !_unsupported.Contains(capability);

    public int ProtocolAvailableCount
    {
        get
        {
            // A capability can appear in both sets if a later write fails after an
            // earlier optional read marked it unsupported. Count distinct names.
            var unavailable = new HashSet<string>(
                _unsupported.Select(capability => capability.ToString()), StringComparer.Ordinal);
            if (_basicState is not null)
            {
                unavailable.UnionWith(_basicState.Errors.Keys);
            }
            return Math.Max(0, Connection.Capabilities.Count - unavailable.Count);
        }
    }

    private bool HasAny(params RazerHidBackendCapability[] capabilities) => capabilities.Any(Has);

    private bool CanUse(RazerHidBackendCapability capability) => CanWrite && Has(capability);

    private static Visibility VisibleWhen(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private async Task RunWriteAsync(
        RazerHidBackendCapability capability,
        Func<bool> isBusy,
        Action<bool> setBusy,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        if (!CanUse(capability) || isBusy())
        {
            return;
        }
        setBusy(true);
        _operationSucceeded = false;
        _operationStatusOverride = null;
        OnPropertyChanged(nameof(OperationStatusText));
        try
        {
            await operation();
            ErrorText = string.Empty;
            _operationSucceeded = true;
            OnPropertyChanged(nameof(OperationStatusText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception, capability);
        }
        finally
        {
            setBusy(false);
        }
    }

    private async Task RunZoneWriteAsync(
        Func<bool> isSupported,
        Func<bool> isBusy,
        Action<bool> setBusy,
        Func<Task> operation,
        CancellationToken cancellationToken,
        Action? onNotSupported = null)
    {
        if (!CanWrite || !isSupported() || isBusy())
        {
            return;
        }
        setBusy(true);
        _operationSucceeded = false;
        _operationStatusOverride = null;
        OnPropertyChanged(nameof(OperationStatusText));
        try
        {
            await operation();
            ErrorText = string.Empty;
            _operationSucceeded = true;
            OnPropertyChanged(nameof(OperationStatusText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (NotSupportedException exception)
        {
            onNotSupported?.Invoke();
            ErrorText = LocalizeProtocolError(exception);
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
        }
        finally
        {
            setBusy(false);
        }
    }

    private async Task ReadOptionalAsync(RazerHidBackendCapability capability, Func<Task> read)
    {
        if (!Has(capability) || RequiresRescan)
        {
            return;
        }
        try
        {
            await read();
        }
        catch (Exception exception)
        {
            HandleFailure(exception, capability);
        }
    }

    private async Task ReadZoneAsync(RazerHidLedZone zone, HashSet<RazerHidLedZone> unsupported, Func<Task> read)
    {
        try
        {
            await read();
        }
        catch (Exception exception) when (
            (exception is NotSupportedException || exception is InvalidOperationException) &&
            exception is not RazerDeviceTimeoutException)
        {
            unsupported.Add(zone);
            OnLightingSelectionChanged();
        }
    }

    private void HandleFailure(Exception exception, RazerHidBackendCapability? capability = null)
    {
        _operationSucceeded = false;
        _operationStatusOverride = null;
        OnPropertyChanged(nameof(OperationStatusText));
        if (RazerHidDeviceService.IsWirelessLinkDownFailure(exception))
        {
            ErrorText = AppStrings.Text("RazerHidDeviceWirelessIdle");
            return;
        }
        if (exception is (RazerDeviceTimeoutException or RazerReportMismatchException) &&
            capability is { } unanswered)
        {
            // A silent read or a stale busy receipt is transient; keep the capability
            // enabled so the next poll retries it.
            ErrorText = AppStrings.FormatText("RazerHidCapabilityUnanswered",
                AppStrings.Text($"RazerHidCapability{unanswered}"));
            return;
        }
        if (exception is (NotSupportedException or InvalidDataException or InvalidOperationException) &&
            capability is { } unsupported)
        {
            _unsupported.Add(unsupported);
            OnCapabilityChanged();
        }
        if (exception is RazerHidProfileSaveException)
        {
            ErrorText = AppStrings.Text("RazerHidLightingProfileSaveFailed");
            return;
        }
        if (exception is RazerHidSettingProfileSaveException)
        {
            ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
            return;
        }
        if (exception is IOException or Win32Exception or UnauthorizedAccessException)
        {
            MarkRequiresRescan();
            return;
        }
        ErrorText = LocalizeProtocolError(exception, capability);
    }

    private void MarkRequiresRescan()
    {
        ErrorText = AppStrings.Text("RazerHidProtocolRescanRequired");
        RequiresRescan = true;
        OnCapabilityChanged();
    }

    /// <summary>
    /// Optional cards must not turn a transport hiccup into a device-wide pause. The main
    /// state read already proved the endpoint answers, so disable only the failing card.
    /// </summary>
    private void HandleOptionalFailure(Exception exception, RazerHidBackendCapability capability)
    {
        if (exception is RazerDeviceTimeoutException or RazerReportMismatchException)
        {
            HandleFailure(exception, capability);
            return;
        }

        _operationSucceeded = false;
        _operationStatusOverride = null;
        OnPropertyChanged(nameof(OperationStatusText));
        _unsupported.Add(capability);
        ErrorText = LocalizeProtocolError(exception, capability);
        OnCapabilityChanged();
    }

    private static string LocalizeProtocolError(
        Exception exception,
        RazerHidBackendCapability? capability = null) =>
        exception is InvalidDataException && capability == RazerHidBackendCapability.DpiStagesRead
            ? AppStrings.Text("RazerHidDpiStagesMalformedResponse")
            : exception switch
            {
                RazerDeviceTimeoutException { WirelessLinkDown: true } =>
                    AppStrings.Text("RazerHidDeviceWirelessIdle"),
                InvalidDataException => AppStrings.Text("RazerHidProtocolMalformedResponse"),
                NotSupportedException => AppStrings.Text("RazerHidProtocolUnsupported"),
                RazerDeviceTimeoutException or RazerReportMismatchException when capability is { } unanswered =>
                    AppStrings.FormatText("RazerHidCapabilityUnanswered",
                        AppStrings.Text($"RazerHidCapability{unanswered}")),
                RazerDeviceTimeoutException or RazerReportMismatchException =>
                    AppStrings.Text("RazerHidProtocolQueryFailed"),
                _ => $"{AppStrings.Text("RazerHidProtocolQueryFailed")} " +
                    $"{exception.GetType().Name}: {exception.Message}".TrimEnd(),
            };

    private void UpdateBasic(Func<RazerHidBasicState, RazerHidBasicState> update)
    {
        if (_basicState is not null)
        {
            _basicState = update(_basicState);
            OnBasicStateChanged();
        }
    }

    private void OnBasicStateChanged()
    {
        OnPropertyChanged(nameof(BasicErrors));
        OnPropertyChanged(nameof(BasicVisibility));
        OnPropertyChanged(nameof(BatteryBasicVisibility));
        OnPropertyChanged(nameof(BatteryPollingVisibility));
        OnPropertyChanged(nameof(SerialVisibility));
        OnPropertyChanged(nameof(FirmwareText));
        OnPropertyChanged(nameof(SerialText));
        OnPropertyChanged(nameof(SoftwareModeText));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(ChargingText));
        OnPropertyChanged(nameof(PollingRateText));
        OnPropertyChanged(nameof(DpiText));
        OnPropertyChanged(nameof(IdleTimeoutText));
        OnPropertyChanged(nameof(LowBatteryThresholdText));
        OnPropertyChanged(nameof(AutoSleepText));
        OnPropertyChanged(nameof(ChargeLimiterText));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(ProtocolAvailableCount));
        OnPropertyChanged(nameof(ProtocolDetails));
        NotifyMiddlewareStateChanged();
    }

    private void OnLightingSelectionChanged()
    {
        if (!LightingZones.Contains(_selectedLightingZone))
        {
            _selectedLightingZone = LightingZones.FirstOrDefault();
            _selectedLightingEffect = DefaultLightingEffect;
            _brightnessKnown = false;
            _ledStateKnown = false;
        }
        if (!LightingEffects.Contains(_selectedLightingEffect))
            _selectedLightingEffect = DefaultLightingEffect;
        OnPropertyChanged(nameof(LightingVisibility));
        OnPropertyChanged(nameof(LightingControlsVisibility));
        OnPropertyChanged(nameof(ChromaOverrideVisibility));
        OnPropertyChanged(nameof(LightingZones));
        OnPropertyChanged(nameof(LightingZoneSelectorVisibility));
        OnPropertyChanged(nameof(LightingPowerProfileVisibility));
        OnPropertyChanged(nameof(LightingEffectVisibility));
        OnPropertyChanged(nameof(LightingZoneOptions));
        OnPropertyChanged(nameof(SelectedLightingZoneIndex));
        OnPropertyChanged(nameof(LightingEffects));
        OnPropertyChanged(nameof(LightingEffectOptions));
        OnPropertyChanged(nameof(SelectedLightingEffectIndex));
        OnPropertyChanged(nameof(LightingRenderModeVisibility));
        OnPropertyChanged(nameof(LightingRenderModeIndex));
        OnPropertyChanged(nameof(SelectedLightingEffect));
        OnPropertyChanged(nameof(BrightnessVisibility));
        OnPropertyChanged(nameof(BrightnessWriteVisibility));
        OnPropertyChanged(nameof(BrightnessEditorVisibility));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(CanWriteBrightness));
        OnPropertyChanged(nameof(CanEditBrightness));
        OnPropertyChanged(nameof(LedStateVisibility));
        OnPropertyChanged(nameof(LedStateWriteVisibility));
        OnPropertyChanged(nameof(LedStateEditorVisibility));
        OnPropertyChanged(nameof(LedStateReadOnlyVisibility));
        OnPropertyChanged(nameof(LedStateText));
        OnPropertyChanged(nameof(CanWriteLedState));
        OnPropertyChanged(nameof(PrimaryColorVisibility));
        OnPropertyChanged(nameof(LightingPresetVisibility));
        OnPropertyChanged(nameof(SecondaryColorVisibility));
        OnPropertyChanged(nameof(TertiaryColorVisibility));
        OnPropertyChanged(nameof(LightingSpeedVisibility));
        OnPropertyChanged(nameof(MaximumLightingSpeed));
        OnPropertyChanged(nameof(LightingDirectionVisibility));
        OnPropertyChanged(nameof(LightingDirectionOptions));
        OnPropertyChanged(nameof(CanApplyLighting));
        OnPropertyChanged(nameof(LightingSaveVisibility));
        OnPropertyChanged(nameof(CanEditLightingSettings));
    }

    private static string FormatZone(RazerHidLedZone zone) => AppStrings.Text($"RazerHidZone{zone}");
    private static string FormatEffect(RazerHidLightingEffect effect) => AppStrings.Text($"RazerHidEffect{effect}");

    private static bool IsSoftwareLightingEffect(RazerHidLightingEffect effect) => effect is
        RazerHidLightingEffect.SoftwareSpectrum or
        RazerHidLightingEffect.SoftwareWave or
        RazerHidLightingEffect.SoftwareFire or
        RazerHidLightingEffect.SoftwareAudioMeter or
        RazerHidLightingEffect.SoftwareWheel or
        RazerHidLightingEffect.SoftwareGradient or
        RazerHidLightingEffect.SoftwareRipple or
        RazerHidLightingEffect.SoftwareTidal or
        RazerHidLightingEffect.SoftwareRadiate;

    private static RazerHidLightingEffect? SoftwareCounterpart(RazerHidLightingEffect effect) => effect switch
    {
        RazerHidLightingEffect.Spectrum => RazerHidLightingEffect.SoftwareSpectrum,
        RazerHidLightingEffect.Wave => RazerHidLightingEffect.SoftwareWave,
        RazerHidLightingEffect.Wheel => RazerHidLightingEffect.SoftwareWheel,
        _ => null,
    };

    private static RazerHidLightingEffect? NativeCounterpart(RazerHidLightingEffect effect) => effect switch
    {
        RazerHidLightingEffect.SoftwareSpectrum => RazerHidLightingEffect.Spectrum,
        RazerHidLightingEffect.SoftwareWave => RazerHidLightingEffect.Wave,
        RazerHidLightingEffect.SoftwareWheel => RazerHidLightingEffect.Wheel,
        _ => null,
    };

    private static int IndexOf<T>(IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value)) return index;
        }
        return -1;
    }

    private void OnCapabilityChanged()
    {
        foreach (var row in DpiStages) row.SetEditable(CanWriteDpiStages);
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ProtocolAvailableCount));
        OnPropertyChanged(nameof(ProtocolTotalCount));
        OnPropertyChanged(nameof(ProtocolDetails));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(MappingVisibility));
        OnPropertyChanged(nameof(BasicVisibility));
        OnPropertyChanged(nameof(FirmwareVisibility));
        OnPropertyChanged(nameof(SerialVisibility));
        OnPropertyChanged(nameof(SoftwareModeVisibility));
        OnPropertyChanged(nameof(BatteryVisibility));
        OnPropertyChanged(nameof(BatteryBasicVisibility));
        OnPropertyChanged(nameof(BatteryPollingVisibility));
        OnPropertyChanged(nameof(BatteryPercentVisibility));
        OnPropertyChanged(nameof(ChargingVisibility));
        OnPropertyChanged(nameof(PollingVisibility));
        OnPropertyChanged(nameof(PollingReadVisibility));
        OnPropertyChanged(nameof(PollingWriteVisibility));
        OnPropertyChanged(nameof(CanEditPolling));
        OnPropertyChanged(nameof(CanWritePolling));
        OnPropertyChanged(nameof(DpiVisibility));
        OnPropertyChanged(nameof(DpiReadVisibility));
        OnPropertyChanged(nameof(DpiWriteVisibility));
        OnPropertyChanged(nameof(CanEditDpi));
        OnPropertyChanged(nameof(CanWriteDpi));
        OnPropertyChanged(nameof(DpiStagesVisibility));
        OnPropertyChanged(nameof(DpiStagesWriteVisibility));
        OnPropertyChanged(nameof(CanEditDpiStages));
        OnPropertyChanged(nameof(CanWriteDpiStages));
        OnPropertyChanged(nameof(PowerVisibility));
        OnPropertyChanged(nameof(IdleVisibility));
        OnPropertyChanged(nameof(IdleWriteVisibility));
        OnPropertyChanged(nameof(LowBatteryVisibility));
        OnPropertyChanged(nameof(LowBatteryWriteVisibility));
        OnPropertyChanged(nameof(BrightnessVisibility));
        OnPropertyChanged(nameof(BrightnessWriteVisibility));
        OnPropertyChanged(nameof(LedStateVisibility));
        OnPropertyChanged(nameof(LedStateWriteVisibility));
        OnPropertyChanged(nameof(CanEditIdle));
        OnPropertyChanged(nameof(CanWriteIdle));
        OnPropertyChanged(nameof(CanEditLowBattery));
        OnPropertyChanged(nameof(CanWriteLowBattery));
        OnPropertyChanged(nameof(CanEditBrightness));
        OnPropertyChanged(nameof(CanWriteBrightness));
        OnPropertyChanged(nameof(CanWriteLedState));
        OnPropertyChanged(nameof(ReactiveTriggerVisibility));
        OnPropertyChanged(nameof(CanTriggerReactive));
        OnPropertyChanged(nameof(MatrixVisibility));
        OnPropertyChanged(nameof(LightingVisibility));
        OnPropertyChanged(nameof(LightingControlsVisibility));
        OnPropertyChanged(nameof(ChromaOverrideVisibility));
        OnPropertyChanged(nameof(LightingZoneSelectorVisibility));
        OnPropertyChanged(nameof(LightingSaveVisibility));
        OnPropertyChanged(nameof(CanApplyMatrix));
        OnPropertyChanged(nameof(CanApplyLighting));
        OnPropertyChanged(nameof(CanEditLightingSettings));
        OnPropertyChanged(nameof(ScrollVisibility));
        OnPropertyChanged(nameof(ScrollModeVisibility));
        OnPropertyChanged(nameof(ScrollModeWriteVisibility));
        OnPropertyChanged(nameof(ScrollAccelerationVisibility));
        OnPropertyChanged(nameof(ScrollAccelerationWriteVisibility));
        OnPropertyChanged(nameof(SmartReelVisibility));
        OnPropertyChanged(nameof(SmartReelWriteVisibility));
        OnPropertyChanged(nameof(SmartReelThresholdVisibility));
        OnPropertyChanged(nameof(SmartReelThresholdWriteVisibility));
        OnPropertyChanged(nameof(ScrollFreeNotchedModeVisibility));
        OnPropertyChanged(nameof(ScrollFreeNotchedModeWriteVisibility));
        OnPropertyChanged(nameof(ScrollAccelerationLevelVisibility));
        OnPropertyChanged(nameof(ScrollAccelerationLevelWriteVisibility));
        NotifyMiddlewareStateChanged();
        OnPropertyChanged(nameof(CanEditScrollFreeNotched));
        OnPropertyChanged(nameof(CanWriteScrollFreeNotched));
        OnPropertyChanged(nameof(CanEditScrollAccelerationLevel));
        OnPropertyChanged(nameof(CanWriteScrollAccelerationLevel));
        OnPropertyChanged(nameof(CanEditScrollMode));
        OnPropertyChanged(nameof(CanWriteScrollMode));
        OnPropertyChanged(nameof(CanEditScrollAcceleration));
        OnPropertyChanged(nameof(CanWriteScrollAcceleration));
        OnPropertyChanged(nameof(CanEditSmartReel));
        OnPropertyChanged(nameof(CanWriteSmartReel));
        OnPropertyChanged(nameof(CanEditSmartReelThreshold));
        OnPropertyChanged(nameof(CanWriteSmartReelThreshold));
        RebuildScrollWheelModeChoices();
        OnPropertyChanged(nameof(CanEditScrollWheelMode));
        OnPropertyChanged(nameof(CanApplyScrollWheelMode));
        OnPropertyChanged(nameof(KeyswitchVisibility));
        OnPropertyChanged(nameof(KeyswitchWriteVisibility));
        OnPropertyChanged(nameof(CanEditKeyswitch));
        OnPropertyChanged(nameof(CanWriteKeyswitch));
        OnPropertyChanged(nameof(FnPrimaryVisibility));
        OnPropertyChanged(nameof(CanSetFnPrimary));
        OnPropertyChanged(nameof(HyperPollingVisibility));
        OnPropertyChanged(nameof(HyperPollingIndicatorVisibility));
        OnPropertyChanged(nameof(HyperPollingPairVisibility));
        OnPropertyChanged(nameof(HyperPollingUnpairVisibility));
        OnPropertyChanged(nameof(CanUseHyperPolling));
        OnPropertyChanged(nameof(CanSetHyperPollingIndicator));
        OnPropertyChanged(nameof(CanPairHyperPolling));
        OnPropertyChanged(nameof(CanUnpairHyperPolling));
        OnPropertyChanged(nameof(WirelessVisibility));
        OnPropertyChanged(nameof(WirelessWriteVisibility));
        OnPropertyChanged(nameof(CanEditWireless));
        OnPropertyChanged(nameof(CanRefreshWireless));
        OnPropertyChanged(nameof(CanUnpairWireless));
        OnPropertyChanged(nameof(KnobVisibility));
        OnPropertyChanged(nameof(KnobWriteVisibility));
        OnPropertyChanged(nameof(CanEditKnob));
        OnPropertyChanged(nameof(CanApplyKnob));
        OnPropertyChanged(nameof(GameModeVisibility));
        OnPropertyChanged(nameof(GameModeWriteVisibility));
        OnPropertyChanged(nameof(CanEditGameMode));
        OnPropertyChanged(nameof(AnalogKeyVisibility));
        OnPropertyChanged(nameof(AnalogKeyWriteVisibility));
        OnPropertyChanged(nameof(CanEditAnalogKey));
        OnPropertyChanged(nameof(OledVisibility));
        OnPropertyChanged(nameof(OledWriteVisibility));
        OnPropertyChanged(nameof(CanEditOled));
        NotifySmartTrackingChanged();
        OnLightingSelectionChanged();
    }

    private static RazerHidColor ToRazerHidColor(Color color) => new(color.R, color.G, color.B);
    private static Color ToColor(RazerHidColor color) => Color.FromArgb(255, color.Red, color.Green, color.Blue);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(ErrorText)) OnPropertyChanged(nameof(HasError));
        return true;
    }

    private void SetBusy(ref bool field, bool value, string dependentProperty, [CallerMemberName] string? propertyName = null)
    {
        if (SetField(ref field, value, propertyName))
        {
            OnPropertyChanged(dependentProperty);
        }
    }

    private void SetBusy(ref bool field, bool value, string canEditProperty, string canWriteProperty, [CallerMemberName] string? propertyName = null)
    {
        if (SetField(ref field, value, propertyName))
        {
            OnPropertyChanged(canEditProperty);
            OnPropertyChanged(canWriteProperty);
        }
    }

    private void SetBusy(
        ref bool field,
        bool value,
        string canEditProperty,
        string canApplyProperty,
        string canCreateProperty,
        string canDeleteProperty,
        string canResetProperty,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetField(ref field, value, propertyName))
        {
            OnPropertyChanged(canEditProperty);
            OnPropertyChanged(canApplyProperty);
            OnPropertyChanged(canCreateProperty);
            OnPropertyChanged(canDeleteProperty);
            OnPropertyChanged(canResetProperty);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Re-evaluates every binding after a low-frequency state change. High-frequency
    /// paths (lighting frames, polling) must keep notifying individual properties.
    /// </summary>
    private void NotifyStateChanged() => OnPropertyChanged(string.Empty);
}

internal sealed class RazerHidProfileSaveException(string message) : IOException(message);
