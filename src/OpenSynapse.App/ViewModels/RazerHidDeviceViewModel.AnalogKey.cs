using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    // The analog-key short commands address class id 1 on the Huntsman V3 Pro and Mako.
    private const byte AnalogKeyClassId = 0x01;
    private const byte AnalogKeyDataType = 0x00;
    private readonly ObservableCollection<AnalogKeyRowViewModel> _analogKeyRows = [];
    private readonly List<AnalogKeyKeyViewModel> _analogKeyKeys = [];
    private AnalogKeyboardLayout? _analogKeyLayout;
    private bool _analogKeyLayoutLoaded;
    private bool _updatingAnalogKeySelection;
    private byte? _lastFirstSelectedKeyId;
    private bool _isAnalogKeyBusy;
    private bool _analogKeyContinuousRapidTrigger;
    private byte _analogKeyBottomDeadZone;
    private byte _analogReportMode;
    private double _analogKeyActuationPointMillimeters = 2.0;
    private bool _analogKeyRapidTriggerEnabled;
    private double _analogKeyRapidTriggerSensitivityMillimeters = 0.3;
    private string _analogKeyLayoutStatusText = AppStrings.Text("AnalogKeyLayoutPrompt");

    public Visibility AnalogKeyVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.AnalogKeyRead, RazerHidBackendCapability.AnalogKeyWrite));
    public Visibility AnalogKeyWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.AnalogKeyWrite));
    public Visibility AnalogKeyLayoutVisibility => VisibleWhen(_analogKeyLayout is not null);
    public Visibility AnalogKeyLayoutMissingVisibility => VisibleWhen(_analogKeyLayout is null);
    public string AnalogKeyLayoutName => _analogKeyLayout?.Name ?? string.Empty;
    public IReadOnlyList<AnalogKeyRowViewModel> AnalogKeyRows => _analogKeyRows;

    public bool AnalogKeyContinuousRapidTrigger
    {
        get => _analogKeyContinuousRapidTrigger;
        set => SetField(ref _analogKeyContinuousRapidTrigger, value);
    }

    public byte AnalogKeyBottomDeadZone
    {
        get => _analogKeyBottomDeadZone;
        set => SetField(ref _analogKeyBottomDeadZone, value);
    }

    public byte AnalogReportMode
    {
        get => _analogReportMode;
        set => SetField(ref _analogReportMode, value);
    }

    public double AnalogKeyActuationPointMillimeters
    {
        get => _analogKeyActuationPointMillimeters;
        set => SetField(ref _analogKeyActuationPointMillimeters, Math.Clamp(value, 0.1, 4.0));
    }

    public bool AnalogKeyRapidTriggerEnabled
    {
        get => _analogKeyRapidTriggerEnabled;
        set => SetField(ref _analogKeyRapidTriggerEnabled, value);
    }

    public double AnalogKeyRapidTriggerSensitivityMillimeters
    {
        get => _analogKeyRapidTriggerSensitivityMillimeters;
        set => SetField(ref _analogKeyRapidTriggerSensitivityMillimeters, Math.Clamp(value, 0.1, 4.0));
    }

    public string AnalogKeyLayoutStatusText
    {
        get => _analogKeyLayoutStatusText;
        private set => SetField(ref _analogKeyLayoutStatusText, value);
    }

    public int SelectedAnalogKeyCount => _analogKeyKeys.Count(key => key.IsSelected);
    public string AnalogKeySelectionText => SelectedAnalogKeyCount == 0
        ? AppStrings.Text("AnalogKeyNoneSelected")
        : AppStrings.FormatText("AnalogKeySelectedCount", SelectedAnalogKeyCount);
    public bool CanApplyAnalogKeySelection => CanEditAnalogKey && SelectedAnalogKeyCount > 0;

    public bool IsAnalogKeyBusy
    {
        get => _isAnalogKeyBusy;
        private set
        {
            var changed = _isAnalogKeyBusy != value;
            SetBusy(ref _isAnalogKeyBusy, value,
                nameof(CanEditAnalogKey), nameof(CanApplyAnalogKeySelection));
            if (changed)
            {
                OnPropertyChanged(nameof(CanApplyAnalogSnapTap));
            }
        }
    }

    public bool CanEditAnalogKey => CanUse(RazerHidBackendCapability.AnalogKeyWrite) && !IsAnalogKeyBusy;

    public void SelectAllAnalogKeys() => SetAnalogKeySelection(true);

    public void ClearAnalogKeySelection() => SetAnalogKeySelection(false);

    public void SynchronizeAnalogKeySelection()
    {
        if (_updatingAnalogKeySelection)
        {
            return;
        }

        var first = _analogKeyKeys.FirstOrDefault(key => key.IsSelected);
        if (first is not null && (_lastFirstSelectedKeyId is null || first.KeyId != _lastFirstSelectedKeyId))
        {
            AnalogKeyActuationPointMillimeters = first.ActuationMillimeters;
            AnalogKeyRapidTriggerEnabled = first.RapidTriggerEnabled;
            AnalogKeyRapidTriggerSensitivityMillimeters = first.RapidTriggerSensitivityMillimeters;
        }

        _lastFirstSelectedKeyId = first?.KeyId;
        OnPropertyChanged(nameof(SelectedAnalogKeyCount));
        OnPropertyChanged(nameof(AnalogKeySelectionText));
        OnPropertyChanged(nameof(CanApplyAnalogKeySelection));
    }

    public async Task LoadAnalogKeyAsync(CancellationToken cancellationToken = default)
    {
        InitializeAnalogKeyLayout();
        if (!CanUse(RazerHidBackendCapability.AnalogKeyRead) || IsAnalogKeyBusy)
        {
            return;
        }

        IsAnalogKeyBusy = true;
        try
        {
            await RefreshAnalogKeyAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleOptionalFailure(exception, RazerHidBackendCapability.AnalogKeyRead);
        }
        finally
        {
            IsAnalogKeyBusy = false;
        }
    }

    [ProfileSetting("keyboard.analog-key", DeviceCategory.Keyboard, powerScoped: true)]
    public Task ApplyAnalogKeyAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.AnalogKeyWrite,
            () => IsAnalogKeyBusy, value => IsAnalogKeyBusy = value,
            async () =>
            {
                var previousRapidTrigger = AnalogKeyContinuousRapidTrigger;
                var previousDeadZone = AnalogKeyBottomDeadZone;
                var previousReportMode = AnalogReportMode;
                var previousProfile = ReadProfile(settings => settings.Keyboard.AnalogKeyEnabled);
                await _service.SetAnalogKeyContinuousRapidTriggerAsync(
                    Connection, AnalogKeyClassId, AnalogKeyContinuousRapidTrigger, cancellationToken);
                await _service.SetAnalogKeyBottomDeadZoneAsync(
                    Connection, AnalogKeyClassId, AnalogKeyBottomDeadZone, cancellationToken);
                await _service.SetAnalogReportModeAsync(Connection, AnalogReportMode, cancellationToken);
                if (Has(RazerHidBackendCapability.AnalogKeyRead))
                {
                    await RefreshAnalogKeyAsync(cancellationToken);
                }
                await SaveProfileAsync(
                    settings => settings.Keyboard.AnalogKeyEnabled = AnalogKeyContinuousRapidTrigger,
                    settings => settings.Keyboard.AnalogKeyEnabled = previousProfile,
                    () =>
                    {
                        AnalogKeyContinuousRapidTrigger = previousRapidTrigger;
                        AnalogKeyBottomDeadZone = previousDeadZone;
                        AnalogReportMode = previousReportMode;
                    },
                    async token =>
                    {
                        await _service.SetAnalogKeyContinuousRapidTriggerAsync(
                            Connection, AnalogKeyClassId, previousRapidTrigger, token);
                        await _service.SetAnalogKeyBottomDeadZoneAsync(
                            Connection, AnalogKeyClassId, previousDeadZone, token);
                        await _service.SetAnalogReportModeAsync(Connection, previousReportMode, token);
                    },
                    cancellationToken);
            }, cancellationToken);

    public Task ApplyAnalogKeyToSelectionAsync(CancellationToken cancellationToken = default) =>
        ApplyAnalogKeyToKeysAsync(_analogKeyKeys.Where(key => key.IsSelected).ToArray(), cancellationToken);

    public Task ApplyAnalogKeyToAllAsync(CancellationToken cancellationToken = default) =>
        ApplyAnalogKeyToKeysAsync(_analogKeyKeys, cancellationToken);

    private Task ApplyAnalogKeyToKeysAsync(
        IReadOnlyList<AnalogKeyKeyViewModel> keys,
        CancellationToken cancellationToken) =>
        RunWriteAsync(RazerHidBackendCapability.AnalogKeyWrite,
            () => IsAnalogKeyBusy, value => IsAnalogKeyBusy = value,
            async () =>
            {
                if (keys.Count == 0)
                {
                    return;
                }

                var actuation = RazerAnalogKeyProtocol.ToThreshold(AnalogKeyActuationPointMillimeters);
                var release = RazerAnalogKeyProtocol.ToThreshold(
                    Math.Max(0.1, AnalogKeyActuationPointMillimeters - 0.1));
                foreach (var batch in keys.Chunk(RazerAnalogKeyProtocol.MaxActuationPoints))
                {
                    await _service.SetAnalogKeyActuationPointsAsync(
                        Connection,
                        AnalogKeyDataType,
                        AnalogKeyClassId,
                        batch.Select(key => new RazerAnalogActuationPoint(key.KeyId, release, actuation)).ToArray(),
                        cancellationToken);
                }

                var sensitivity = RazerAnalogKeyProtocol.ToThreshold(
                    AnalogKeyRapidTriggerSensitivityMillimeters);
                foreach (var batch in keys.Chunk(RazerAnalogKeyProtocol.MaxRapidTriggerKeys))
                {
                    await _service.SetAnalogKeyRapidTriggersAsync(
                        Connection,
                        AnalogKeyDataType,
                        AnalogKeyClassId,
                        batch.Select(key => new RazerAnalogRapidTrigger(
                            key.KeyId,
                            AnalogKeyRapidTriggerEnabled,
                            sensitivity,
                            sensitivity)).ToArray(),
                        cancellationToken);
                }

                await RefreshAnalogKeyLayoutAsync(cancellationToken);
                AnalogKeyLayoutStatusText =
                    AppStrings.FormatText("AnalogKeyAppliedCount", keys.Count);
            }, cancellationToken);

    private async Task RefreshAnalogKeyAsync(CancellationToken cancellationToken)
    {
        // These three are optional settings. A firmware that does not answer one must
        // not remove the whole analog card; keep the last value and let the per-key
        // actuation/Rapid Trigger read below decide whether the layout is usable.
        var continuous = await ReadOrKeepAsync(
            token => _service.GetAnalogKeyContinuousRapidTriggerAsync(Connection, AnalogKeyClassId, token),
            new RazerAnalogKeyContinuousRapidTrigger(AnalogKeyClassId, AnalogKeyContinuousRapidTrigger),
            cancellationToken);
        AnalogKeyContinuousRapidTrigger = continuous.Enabled;
        var deadZone = await ReadOrKeepAsync(
            token => _service.GetAnalogKeyBottomDeadZoneAsync(Connection, AnalogKeyClassId, token),
            new RazerAnalogKeyBottomDeadZone(AnalogKeyClassId, AnalogKeyBottomDeadZone),
            cancellationToken);
        AnalogKeyBottomDeadZone = deadZone.Value;
        AnalogReportMode = await ReadOrKeepAsync(
            token => _service.GetAnalogReportModeAsync(Connection, token),
            AnalogReportMode,
            cancellationToken);
        if (_analogKeyLayout is not null && !_analogKeyLayoutLoaded)
        {
            try
            {
                await RefreshAnalogKeyLayoutAsync(cancellationToken);
                await RefreshAnalogSnapTapAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                AnalogKeyLayoutStatusText = LocalizeProtocolError(exception);
            }
        }
    }

    private async Task<T> ReadOrKeepAsync<T>(
        Func<CancellationToken, Task<T>> read,
        T fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            return await read(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private async Task RefreshAnalogKeyLayoutAsync(CancellationToken cancellationToken)
    {
        var keyIds = _analogKeyKeys.Select(key => key.KeyId).ToArray();
        var byId = _analogKeyKeys.ToDictionary(key => key.KeyId);
        foreach (var batch in keyIds.Chunk(RazerAnalogKeyProtocol.MaxActuationPoints))
        {
            var table = await _service.GetAnalogKeyActuationPointsAsync(
                Connection, AnalogKeyDataType, AnalogKeyClassId, batch, cancellationToken);
            foreach (var point in table.Points)
            {
                if (!byId.TryGetValue(point.KeyId, out var key))
                {
                    continue;
                }

                var threshold = point.ThresholdL != 0 ? point.ThresholdL : point.ThresholdH;
                key.ActuationMillimeters = Math.Round(
                    RazerAnalogKeyProtocol.ToMillimeters(threshold), 1, MidpointRounding.AwayFromZero);
            }
        }

        foreach (var batch in keyIds.Chunk(RazerAnalogKeyProtocol.MaxRapidTriggerKeys))
        {
            var table = await _service.GetAnalogKeyRapidTriggersAsync(
                Connection, AnalogKeyDataType, AnalogKeyClassId, batch, cancellationToken);
            foreach (var entry in table.Entries)
            {
                if (!byId.TryGetValue(entry.KeyId, out var key))
                {
                    continue;
                }

                key.RapidTriggerEnabled = entry.Enabled;
                var sensitivity = entry.DownSensitivity != 0
                    ? entry.DownSensitivity
                    : entry.UpSensitivity;
                key.RapidTriggerSensitivityMillimeters = Math.Round(
                    RazerAnalogKeyProtocol.ToMillimeters(sensitivity), 1, MidpointRounding.AwayFromZero);
            }
        }

        _analogKeyLayoutLoaded = true;
        AnalogKeyLayoutStatusText =
            AppStrings.FormatText("AnalogKeyReadCount", keyIds.Length);
    }

    private void InitializeAnalogKeyLayout()
    {
        if (_analogKeyLayout is not null ||
            !AnalogKeyboardLayouts.TryGet(Connection.Definition.ProductId, out var layout))
        {
            return;
        }

        _analogKeyLayout = layout;
        foreach (var group in layout.Keys.GroupBy(key => key.Row).OrderBy(group => group.Key))
        {
            var keys = group.Select(key => new AnalogKeyKeyViewModel(
                key.KeyId,
                key.Label,
                Math.Max(32, 44 * key.WidthUnits))).ToArray();
            foreach (var key in keys)
            {
                key.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(AnalogKeyKeyViewModel.IsSelected))
                    {
                        SynchronizeAnalogKeySelection();
                    }
                };
                _analogKeyKeys.Add(key);
            }

            _analogKeyRows.Add(new AnalogKeyRowViewModel(group.Key, keys));
        }

        SynchronizeAnalogKeySelection();
        OnPropertyChanged(nameof(AnalogKeyRows));
        OnPropertyChanged(nameof(AnalogKeyLayoutName));
        OnPropertyChanged(nameof(AnalogKeyLayoutVisibility));
        // AnalogSnapTapVisibility also depends on _analogKeyLayout; keep it in
        // sync even when the snap-tap probe has already completed.
        NotifyStateChanged();
    }

    private void SetAnalogKeySelection(bool selected)
    {
        _updatingAnalogKeySelection = true;
        try
        {
            foreach (var key in _analogKeyKeys)
            {
                key.IsSelected = selected;
            }
        }
        finally
        {
            _updatingAnalogKeySelection = false;
        }

        _lastFirstSelectedKeyId = null;
        SynchronizeAnalogKeySelection();
    }
}
