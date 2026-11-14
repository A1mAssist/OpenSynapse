using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private const byte AnalogKeyDataType = 0x00;
    private const double AnalogKeyToleranceMillimeters = 0.05;
    // Razer's own UI only offers these three bottom-dead-zone presets.
    private static readonly AnalogDeadZoneChoice[] AnalogDeadZonePresets = [new(3), new(5), new(8)];
    private readonly ObservableCollection<AnalogKeyRowViewModel> _analogKeyRows = [];
    private readonly List<AnalogKeyKeyViewModel> _analogKeyKeys = [];
    private AnalogKeyboardLayout? _analogKeyLayout;
    private StudioKeyboardVisual? _analogKeyVisual;
    private bool _analogKeyLayoutLoaded;
    private bool _analogKeyVisualLoaded;
    private bool _updatingAnalogKeySelection;
    private byte? _lastFirstSelectedKeyId;
    private bool _isAnalogKeyBusy;
    private bool _analogKeyContinuousRapidTrigger;
    private byte? _analogKeyBottomDeadZone;
    private AnalogDeadZoneChoice? _selectedAnalogDeadZone;
    private double _analogKeyActuationPointMillimeters = 2.0;
    private bool _analogKeyRapidTriggerEnabled;
    private double _analogKeyRapidTriggerSensitivityMillimeters = 0.3;
    private string _analogKeyLayoutStatusText = AppStrings.Text("AnalogKeyLayoutPrompt");

    public Visibility AnalogKeyVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.AnalogKeyRead, RazerHidBackendCapability.AnalogKeyWrite));
    public Visibility AnalogKeyWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.AnalogKeyWrite));
    public Visibility AnalogKeyLayoutVisibility => VisibleWhen(_analogKeyLayout is not null);
    public Visibility AnalogKeyLayoutMissingVisibility => VisibleWhen(_analogKeyLayout is null);
    public Visibility AnalogKeyVisualVisibility => VisibleWhen(_analogKeyVisual is not null);
    public Visibility AnalogKeyFallbackVisibility => VisibleWhen(_analogKeyVisual is null);
    public StudioKeyboardVisual? AnalogKeyVisual => _analogKeyVisual;
    public string AnalogKeyLayoutName => _analogKeyLayout?.Name ?? string.Empty;
    public IReadOnlyList<AnalogKeyRowViewModel> AnalogKeyRows => _analogKeyRows;

    public bool AnalogKeyContinuousRapidTrigger
    {
        get => _analogKeyContinuousRapidTrigger;
        set => SetField(ref _analogKeyContinuousRapidTrigger, value);
    }

    public IReadOnlyList<AnalogDeadZoneChoice> AnalogDeadZoneChoices => AnalogDeadZonePresets;

    public AnalogDeadZoneChoice? SelectedAnalogDeadZone
    {
        get => _selectedAnalogDeadZone;
        set
        {
            if (!SetField(ref _selectedAnalogDeadZone, value) || value is null)
            {
                return;
            }

            _analogKeyBottomDeadZone = value.Value;
            NotifyAnalogDeadZone();
        }
    }

    public string AnalogDeadZoneStatusText =>
        _analogKeyBottomDeadZone is byte raw && AnalogDeadZonePresets.All(preset => preset.Value != raw)
            ? AppStrings.FormatText("RazerHidAnalogDeadZoneUnknown", raw)
            : string.Empty;

    public Visibility AnalogDeadZoneUnknownVisibility => VisibleWhen(AnalogDeadZoneStatusText.Length > 0);

    private void SetAnalogDeadZone(byte? value)
    {
        _analogKeyBottomDeadZone = value;
        _selectedAnalogDeadZone = value is byte raw
            ? AnalogDeadZonePresets.FirstOrDefault(preset => preset.Value == raw)
            : null;
        OnPropertyChanged(nameof(SelectedAnalogDeadZone));
        NotifyAnalogDeadZone();
    }

    private void NotifyAnalogDeadZone()
    {
        OnPropertyChanged(nameof(AnalogDeadZoneStatusText));
        OnPropertyChanged(nameof(AnalogDeadZoneUnknownVisibility));
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
        _ = LoadAnalogKeyVisualAsync(cancellationToken);
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

    private async Task LoadAnalogKeyVisualAsync(CancellationToken cancellationToken)
    {
        if (_analogKeyVisualLoaded || _analogKeyLayout is null)
        {
            return;
        }

        _analogKeyVisualLoaded = true;
        try
        {
            var visual = await RazerMappingAssetService.Shared.GetAsync(
                Connection.Definition.ProductId,
                RazerMappingDeviceKind.Keyboard,
                cancellationToken).ConfigureAwait(true);
            if (visual.Keyboard?.Visual is not { Paths.Count: > 0 } keyboard)
            {
                return;
            }

            _analogKeyVisual = keyboard;
            OnPropertyChanged(nameof(AnalogKeyVisual));
            OnPropertyChanged(nameof(AnalogKeyVisualVisibility));
            OnPropertyChanged(nameof(AnalogKeyFallbackVisibility));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _analogKeyVisualLoaded = false;
        }
        catch (Exception)
        {
            // The SVG is optional art. Keep the rectangular fallback when it is unavailable.
        }
    }

    [ProfileSetting("keyboard.analog-key", DeviceCategory.Keyboard, powerScoped: true)]
    public Task ApplyAnalogKeyAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.AnalogKeyWrite,
            () => IsAnalogKeyBusy, value => IsAnalogKeyBusy = value,
            async () =>
            {
                var previousRapidTrigger = AnalogKeyContinuousRapidTrigger;
                var previousDeadZone = _analogKeyBottomDeadZone;
                var previousProfile = ReadProfile(settings => settings.Keyboard.AnalogKeyEnabled);
                var profileId = await ResolveAnalogKeyProfileIdAsync(cancellationToken);
                await _service.SetAnalogKeyContinuousRapidTriggerAsync(
                    Connection, profileId, AnalogKeyContinuousRapidTrigger, cancellationToken);
                // An unrepresentable raw value is left untouched instead of being
                // rewritten with a preset the user never chose.
                if (SelectedAnalogDeadZone?.Value is { } deadZone)
                {
                    await _service.SetAnalogKeyBottomDeadZoneAsync(
                        Connection, profileId, deadZone, cancellationToken);
                }
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
                        SetAnalogDeadZone(previousDeadZone);
                    },
                    async token =>
                    {
                        await _service.SetAnalogKeyContinuousRapidTriggerAsync(
                            Connection, profileId, previousRapidTrigger, token);
                        if (previousDeadZone is { } deadZoneToRestore)
                        {
                            await _service.SetAnalogKeyBottomDeadZoneAsync(
                                Connection, profileId, deadZoneToRestore, token);
                        }
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

                var profileId = await ResolveAnalogKeyProfileIdAsync(cancellationToken);
                var actuation = RazerAnalogKeyProtocol.ToThreshold(AnalogKeyActuationPointMillimeters);
                var release = RazerAnalogKeyProtocol.ToThreshold(
                    Math.Max(0.1, AnalogKeyActuationPointMillimeters - 0.1));
                var actuationKeys = keys
                    .Where(key => Math.Abs(key.ActuationMillimeters - AnalogKeyActuationPointMillimeters)
                        > AnalogKeyToleranceMillimeters)
                    .ToArray();
                foreach (var batch in actuationKeys.Chunk(RazerAnalogKeyProtocol.MaxActuationPoints))
                {
                    await _service.SetAnalogKeyActuationPointsAsync(
                        Connection,
                        AnalogKeyDataType,
                        profileId,
                        batch.Select(key => new RazerAnalogActuationPoint(key.KeyId, release, actuation)).ToArray(),
                        cancellationToken);
                }

                var sensitivity = RazerAnalogKeyProtocol.ToThreshold(
                    AnalogKeyRapidTriggerSensitivityMillimeters);
                var rapidTriggerKeys = keys
                    .Where(key => key.RapidTriggerEnabled != AnalogKeyRapidTriggerEnabled ||
                        Math.Abs(key.RapidTriggerSensitivityMillimeters -
                            AnalogKeyRapidTriggerSensitivityMillimeters) > AnalogKeyToleranceMillimeters)
                    .ToArray();
                foreach (var batch in rapidTriggerKeys.Chunk(RazerAnalogKeyProtocol.MaxRapidTriggerKeys))
                {
                    await _service.SetAnalogKeyRapidTriggersAsync(
                        Connection,
                        AnalogKeyDataType,
                        profileId,
                        batch.Select(key => new RazerAnalogRapidTrigger(
                            key.KeyId,
                            AnalogKeyRapidTriggerEnabled,
                            sensitivity,
                            sensitivity)).ToArray(),
                        cancellationToken);
                }

                // The write is already on the wire. Reflect it now so a failed readback
                // cannot leave the editor showing the previous values.
                foreach (var key in keys)
                {
                    key.ActuationMillimeters = AnalogKeyActuationPointMillimeters;
                    key.RapidTriggerEnabled = AnalogKeyRapidTriggerEnabled;
                    key.RapidTriggerSensitivityMillimeters = AnalogKeyRapidTriggerSensitivityMillimeters;
                }

                int read;
                try
                {
                    read = await ReadAnalogKeyLayoutAsync(keys, cancellationToken);
                }
                catch (Exception exception) when (IsAnalogKeyReadbackFailure(exception))
                {
                    // A readback failure is not a write failure; keep the write capability
                    // so the user can retry instead of losing the whole analog card.
                    AnalogKeyLayoutStatusText = AppStrings.FormatText(
                        "AnalogKeyAppliedUnverified", keys.Count);
                    return;
                }

                AnalogKeyLayoutStatusText = read == keys.Count && keys.All(MatchesAnalogKeyRequest)
                    ? AppStrings.FormatText("AnalogKeyAppliedCount", keys.Count)
                    : AppStrings.FormatText("AnalogKeyApplyMismatch", keys.Count);
            }, cancellationToken);

    private async Task RefreshAnalogKeyAsync(CancellationToken cancellationToken)
    {
        // These two are optional settings. A firmware that does not answer one must
        // not remove the whole analog card; keep the last value and let the per-key
        // actuation/Rapid Trigger read below decide whether the layout is usable.
        var profileId = await ResolveAnalogKeyProfileIdAsync(cancellationToken);
        var continuous = await ReadOrKeepAsync(
            token => _service.GetAnalogKeyContinuousRapidTriggerAsync(Connection, profileId, token),
            new RazerAnalogKeyContinuousRapidTrigger(profileId, AnalogKeyContinuousRapidTrigger),
            cancellationToken);
        AnalogKeyContinuousRapidTrigger = continuous.Enabled;
        SetAnalogDeadZone(await ReadOrKeepAsync<byte?>(
            async token => (await _service.GetAnalogKeyBottomDeadZoneAsync(
                Connection, profileId, token)).Value,
            _analogKeyBottomDeadZone,
            cancellationToken));
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
        var read = await ReadAnalogKeyLayoutAsync(_analogKeyKeys, cancellationToken);
        AnalogKeyLayoutStatusText = AppStrings.FormatText("AnalogKeyReadCount", read);
    }

    private async Task<int> ReadAnalogKeyLayoutAsync(
        IReadOnlyList<AnalogKeyKeyViewModel> keys,
        CancellationToken cancellationToken)
    {
        var keyIds = keys.Select(key => key.KeyId).ToArray();
        var byId = keys.ToDictionary(key => key.KeyId);
        var seen = new HashSet<byte>();
        var profileId = await ResolveAnalogKeyProfileIdAsync(cancellationToken);
        foreach (var batch in keyIds.Chunk(RazerAnalogKeyProtocol.MaxActuationPoints))
        {
            var table = await _service.GetAnalogKeyActuationPointsAsync(
                Connection, AnalogKeyDataType, profileId, batch, cancellationToken);
            foreach (var point in table.Points)
            {
                if (!byId.TryGetValue(point.KeyId, out var key))
                {
                    continue;
                }

                var threshold = point.ThresholdL != 0 ? point.ThresholdL : point.ThresholdH;
                key.ActuationMillimeters = Math.Round(
                    RazerAnalogKeyProtocol.ToMillimeters(threshold), 1, MidpointRounding.AwayFromZero);
                seen.Add(point.KeyId);
            }
        }

        foreach (var batch in keyIds.Chunk(RazerAnalogKeyProtocol.MaxRapidTriggerKeys))
        {
            var table = await _service.GetAnalogKeyRapidTriggersAsync(
                Connection, AnalogKeyDataType, profileId, batch, cancellationToken);
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
        return seen.Count;
    }

    private bool MatchesAnalogKeyRequest(AnalogKeyKeyViewModel key) =>
        Math.Abs(key.ActuationMillimeters - AnalogKeyActuationPointMillimeters) <= AnalogKeyToleranceMillimeters &&
        key.RapidTriggerEnabled == AnalogKeyRapidTriggerEnabled &&
        (!AnalogKeyRapidTriggerEnabled ||
            Math.Abs(key.RapidTriggerSensitivityMillimeters - AnalogKeyRapidTriggerSensitivityMillimeters) <=
                AnalogKeyToleranceMillimeters);

    private Task<byte> ResolveAnalogKeyProfileIdAsync(CancellationToken cancellationToken) =>
        _service.ResolveActiveProfileIdAsync(Connection, cancellationToken);

    private static bool IsAnalogKeyReadbackFailure(Exception exception) =>
        exception is RazerCommandRejectedException or NotSupportedException or
            RazerDeviceTimeoutException or RazerReportMismatchException or
            InvalidDataException or InvalidOperationException or IOException;

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
