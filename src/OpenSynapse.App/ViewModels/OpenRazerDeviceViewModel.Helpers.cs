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

public sealed partial class OpenRazerDeviceViewModel : INotifyPropertyChanged
{
    public void RefreshLocalization()
    {
        foreach (var row in DpiStages) row.RefreshLocalization();
        foreach (var cell in MatrixCells) cell.RefreshLocalization();
        OnPropertyChanged(string.Empty);
    }

    private void ReplaceDpiStages(OpenRazerDpiStages state)
    {
        DpiStages.Clear();
        foreach (var stage in state.Stages)
        {
            var row = new OpenRazerDpiStageRowViewModel(stage.Number, stage.X, stage.Y, CanWriteDpiStages);
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanEditDpiStages));
            DpiStages.Add(row);
        }
        ActiveDpiStage = state.ActiveStage;
        OnPropertyChanged(nameof(CanEditDpiStages));
    }

    private OpenRazerLightingZoneCapabilities? SelectedZoneCapabilities =>
        Connection.LightingZones.GetValueOrDefault(SelectedLightingZone);

    private bool IsVisibleLightingZone(OpenRazerLedZone zone)
    {
        if (RequiresRescan) return false;
        if (!Connection.LightingZones.TryGetValue(zone, out var capabilities)) return false;
        return Has(OpenRazerBackendCapability.LightingEffectWrite) &&
                capabilities.LightingEffects.Any(effect => effect != OpenRazerLightingEffect.Custom &&
                !_unsupportedLighting.Contains((zone, effect))) ||
            capabilities.CanReadBrightness && !_unsupportedBrightnessReads.Contains(zone) ||
            capabilities.CanWriteBrightness && !_unsupportedBrightnessWrites.Contains(zone) ||
            capabilities.CanReadState && !_unsupportedLedStateReads.Contains(zone) ||
            capabilities.CanWriteState && !_unsupportedLedStateWrites.Contains(zone);
    }


    private bool Has(OpenRazerBackendCapability capability) =>
        !RequiresRescan && Connection.Capabilities.Contains(capability) && !_unsupported.Contains(capability);

    private bool HasAny(params OpenRazerBackendCapability[] capabilities) => capabilities.Any(Has);

    private bool CanUse(OpenRazerBackendCapability capability) => CanWrite && Has(capability);

    private static Visibility VisibleWhen(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private async Task RunWriteAsync(
        OpenRazerBackendCapability capability,
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

    private async Task ReadOptionalAsync(OpenRazerBackendCapability capability, Func<Task> read)
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

    private async Task ReadZoneAsync(OpenRazerLedZone zone, HashSet<OpenRazerLedZone> unsupported, Func<Task> read)
    {
        try
        {
            await read();
        }
        catch (NotSupportedException)
        {
            unsupported.Add(zone);
            OnLightingSelectionChanged();
        }
    }

    private void HandleFailure(Exception exception, OpenRazerBackendCapability? capability = null)
    {
        _operationSucceeded = false;
        OnPropertyChanged(nameof(OperationStatusText));
        if (exception is (NotSupportedException or InvalidDataException) && capability is { } unsupported)
        {
            _unsupported.Add(unsupported);
            OnCapabilityChanged();
        }
        if (exception is IOException or Win32Exception or InvalidOperationException)
        {
            MarkRequiresRescan();
            return;
        }
        ErrorText = LocalizeProtocolError(exception, capability);
    }

    private void MarkRequiresRescan()
    {
        ErrorText = AppStrings.Text("OpenRazerProtocolRescanRequired");
        RequiresRescan = true;
        OnCapabilityChanged();
    }

    private static string LocalizeProtocolError(
        Exception exception,
        OpenRazerBackendCapability? capability = null) =>
        exception is InvalidDataException && capability == OpenRazerBackendCapability.DpiStagesRead
            ? AppStrings.Text("OpenRazerDpiStagesMalformedResponse")
            : exception switch
            {
                InvalidDataException => AppStrings.Text("OpenRazerProtocolMalformedResponse"),
                NotSupportedException => AppStrings.Text("OpenRazerProtocolUnsupported"),
                _ => AppStrings.Text("OpenRazerProtocolQueryFailed"),
            };

    private void UpdateBasic(Func<OpenRazerBasicState, OpenRazerBasicState> update)
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
        OnPropertyChanged(nameof(BrightnessText));
    }

    private void OnLightingSelectionChanged()
    {
        if (!LightingZones.Contains(_selectedLightingZone))
        {
            _selectedLightingZone = LightingZones.FirstOrDefault();
            _selectedLightingEffect = LightingEffects.FirstOrDefault();
            _brightnessKnown = false;
            _ledStateKnown = false;
        }
        if (!LightingEffects.Contains(_selectedLightingEffect))
            _selectedLightingEffect = LightingEffects.FirstOrDefault();
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
        OnPropertyChanged(nameof(SecondaryColorVisibility));
        OnPropertyChanged(nameof(LightingSpeedVisibility));
        OnPropertyChanged(nameof(MaximumLightingSpeed));
        OnPropertyChanged(nameof(LightingDirectionVisibility));
        OnPropertyChanged(nameof(CanApplyLighting));
        OnPropertyChanged(nameof(LightingSaveVisibility));
        OnPropertyChanged(nameof(CanEditLightingSettings));
    }

    private static string FormatZone(OpenRazerLedZone zone) => AppStrings.Text($"OpenRazerZone{zone}");
    private static string FormatEffect(OpenRazerLightingEffect effect) => AppStrings.Text($"OpenRazerEffect{effect}");

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
        OnPropertyChanged(nameof(CanWrite));
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
        OnPropertyChanged(nameof(CanEditScrollMode));
        OnPropertyChanged(nameof(CanWriteScrollMode));
        OnPropertyChanged(nameof(CanEditScrollAcceleration));
        OnPropertyChanged(nameof(CanWriteScrollAcceleration));
        OnPropertyChanged(nameof(CanEditSmartReel));
        OnPropertyChanged(nameof(CanWriteSmartReel));
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
        OnLightingSelectionChanged();
    }

    private static OpenRazerColor ToOpenRazerColor(Color color) => new(color.R, color.G, color.B);
    private static Color ToColor(OpenRazerColor color) => Color.FromArgb(255, color.Red, color.Green, color.Blue);

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
