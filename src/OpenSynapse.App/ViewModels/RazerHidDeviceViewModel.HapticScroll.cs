using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// One of the six Hapticore types (129–134); active stage is a position in the enabled list.
/// </summary>
public sealed class RazerHidHapticScrollStageRowViewModel(byte id, string name, bool isActive)
    : INotifyPropertyChanged
{
    private bool _isActive = isActive;

    public event PropertyChangedEventHandler? PropertyChanged;

    public byte Id { get; } = id;
    public string Name { get; } = name;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }
}

public sealed partial class RazerHidDeviceViewModel
{
    // The middleware writes profile 1 by default; the UI never offers a profile picker.
    private const byte HapticScrollProfileId = 0x01;

    private bool _isHapticScrollBusy;
    private byte _selectedHapticScrollStageType = RazerHidHapticScrollStageIds.Standard;
    private HapticScrollProfile? _confirmedHapticScroll;
    private bool _hapticCurveEdited;
    private bool _hapticCurvesLinked;
    private bool _loadingHapticCurve;

    public HapticScrollDirectionViewModel? HapticCurveUp { get; private set; }
    public HapticScrollDirectionViewModel? HapticCurveDown { get; private set; }
    public bool HapticCurvesLinked
    {
        get => _hapticCurvesLinked;
        set
        {
            if (_hapticCurvesLinked == value) return;
            SetField(ref _hapticCurvesLinked, value);
            if (!_loadingHapticCurve) _hapticCurveEdited = true;
            OnPropertyChanged(nameof(HapticCurveDownVisibility));
        }
    }
    public Visibility HapticCurveDownVisibility => VisibleWhen(!HapticCurvesLinked);
    public Visibility HapticCurveEditorVisibility => VisibleWhen(HapticCurveUp is not null);
    public bool CanCreateHapticCurve => CanApplyHapticScroll && _confirmedHapticScroll?.Custom is not null;
    public bool CanRestoreHapticCurve => CanApplyHapticScroll && ReadProfile(s => s.Viper.HapticScroll) is not null;

    public Visibility HapticScrollVisibility => VisibleWhen(RazerHidDeviceService.SupportsHapticCurve(Connection.Definition.ProductId) && HasAny(
        RazerHidBackendCapability.HapticScrollRead, RazerHidBackendCapability.HapticScrollWrite));
    public Visibility HapticScrollWriteVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.HapticScrollWrite));

    public ObservableCollection<RazerHidHapticScrollStageRowViewModel> HapticScrollStages { get; } =
        new(StageDefinitions());

    public IReadOnlyList<string> HapticScrollStageNames => HapticScrollStages.Where(stage => stage.IsActive).Select(stage => stage.Name).ToArray();

    public int SelectedHapticScrollStageIndex
    {
        get => HapticScrollStages.Where(stage => stage.IsActive).ToList().FindIndex(stage => stage.Id == _selectedHapticScrollStageType);
        set
        {
            var active = HapticScrollStages.Where(stage => stage.IsActive).ToArray();
            if (value < 0 || value >= active.Length) return;
            SetField(ref _selectedHapticScrollStageType, active[value].Id, nameof(SelectedHapticScrollStageIndex));
        }
    }

    public bool IsHapticScrollBusy
    {
        get => _isHapticScrollBusy;
        private set
        {
            SetBusy(ref _isHapticScrollBusy, value, nameof(CanEditHapticScroll), nameof(CanApplyHapticScroll));
            OnPropertyChanged(nameof(CanCreateHapticCurve));
            OnPropertyChanged(nameof(CanRestoreHapticCurve));
        }
    }

    public bool CanEditHapticScroll => CanApplyHapticScroll && _confirmedHapticScroll is not null;
    public bool CanApplyHapticScroll =>
        CanUse(RazerHidBackendCapability.HapticScrollWrite) && !IsHapticScrollBusy;

    public async Task LoadHapticScrollAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(RazerHidBackendCapability.HapticScrollRead) || IsHapticScrollBusy)
        {
            return;
        }

        IsHapticScrollBusy = true;
        try
        {
            var state = await _service.ReadHapticScrollProfileAsync(Connection, HapticScrollProfileId, cancellationToken: cancellationToken);
            ApplyHapticProfileState(state);
            ErrorText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
        finally
        {
            IsHapticScrollBusy = false;
        }
    }

    [ProfileSetting("mouse.haptic-scroll-level", DeviceCategory.Mouse, powerScoped: true)]
    public Task ApplyHapticScrollAsync(CancellationToken cancellationToken = default) =>
        ApplyHapticScrollDraftAsync(null, cancellationToken);

    [NotProfileScoped]
    private Task ApplyHapticScrollDraftAsync(HapticScrollProfile? captured, CancellationToken cancellationToken) =>
        RunHapticWriteAsync(
            async () =>
            {
                var draft = captured ?? CaptureHapticProfile();
                var applied = await _service.ApplyHapticScrollProfileAsync(Connection, draft, HapticScrollProfileId,
                    PersistHapticProfileAsync, cancellationToken);
                ApplyHapticProfileState(applied);
            }, cancellationToken);

    private HapticScrollProfile CaptureHapticProfile()
    {
        if (_confirmedHapticScroll is null) throw new InvalidOperationException(AppStrings.Text("HapticCurveReadFirst"));
        var draft = new HapticScrollProfile
        {
            ActiveStage = (byte)(SelectedHapticScrollStageIndex + 1),
            StageTypes = HapticScrollStages.Where(stage => stage.IsActive).Select(stage => stage.Id).ToList(),
            Custom = _confirmedHapticScroll.Custom?.Clone(),
        };
        if (_hapticCurveEdited && HapticCurveUp is not null && HapticCurveDown is not null)
            draft.Custom = new HapticScrollCurve { Linked = HapticCurvesLinked, Up = HapticCurveUp.Capture(),
                Down = HapticCurvesLinked ? HapticCurveUp.Capture() : HapticCurveDown.Capture() }.Generate(HapticScrollProfileId);
        draft.Validate();
        return draft;
    }

    private void ApplyHapticProfileState(HapticScrollProfile state)
    {
        _confirmedHapticScroll = state.Clone();
        var definitions = StageDefinitions();
        HapticScrollStages.Clear();
        foreach (var id in state.StageTypes.Concat(definitions.Select(s => s.Id).Except(state.StageTypes)))
        {
            var row = definitions.Single(s => s.Id == id);
            row.IsActive = state.StageTypes.Contains(id);
            row.PropertyChanged += (_, _) =>
            {
                if (!HapticScrollStages.Any(s => s.IsActive && s.Id == _selectedHapticScrollStageType))
                    _selectedHapticScrollStageType = HapticScrollStages.FirstOrDefault(s => s.IsActive)?.Id ?? 0;
                OnPropertyChanged(nameof(HapticScrollStageNames));
                OnPropertyChanged(nameof(SelectedHapticScrollStageIndex));
            };
            HapticScrollStages.Add(row);
        }
        OnPropertyChanged(nameof(HapticScrollStageNames));
        SelectedHapticScrollStageIndex = state.ActiveStage - 1;
        OnPropertyChanged(nameof(SelectedHapticScrollStageIndex));
        HapticScrollCurve? curve = null;
        if (state.Custom is { } custom)
            try { curve = HapticScrollCurve.FromMetadata(custom.Metadata); } catch (InvalidDataException) { }
        SetHapticCurve(curve);
        _hapticCurveEdited = false;
    }

    private void SetHapticCurve(HapticScrollCurve? curve)
    {
        _loadingHapticCurve = true;
        HapticCurveUp = curve is null ? null : new(AppStrings.Text("HapticCurveUp"), curve.Up);
        HapticCurveDown = curve is null ? null : new(AppStrings.Text("HapticCurveDown"), curve.Down);
        HapticCurvesLinked = curve?.Linked ?? true;
        if (HapticCurveUp is not null) HapticCurveUp.PropertyChanged += (_, _) => _hapticCurveEdited = true;
        if (HapticCurveDown is not null) HapticCurveDown.PropertyChanged += (_, _) => _hapticCurveEdited = true;
        _loadingHapticCurve = false;
        OnPropertyChanged(nameof(HapticCurveUp));
        OnPropertyChanged(nameof(HapticCurveDown));
        OnPropertyChanged(nameof(HapticCurveEditorVisibility));
        OnPropertyChanged(nameof(CanEditHapticScroll));
        OnPropertyChanged(nameof(CanCreateHapticCurve));
        OnPropertyChanged(nameof(CanRestoreHapticCurve));
    }

    public void CreateDefaultHapticCurve()
    {
        if (!CanCreateHapticCurve) return;
        SetHapticCurve(new HapticScrollCurve());
        _hapticCurveEdited = true;
        HapticScrollStages.Single(stage => stage.Id == RazerHidHapticScrollStageIds.Custom).IsActive = true;
        SelectedHapticScrollStageIndex = HapticScrollStages.Where(s => s.IsActive).ToList().FindIndex(s => s.Id == RazerHidHapticScrollStageIds.Custom);
    }

    public Task RestoreSavedHapticCurveAsync(CancellationToken token = default) =>
        RunHapticWriteAsync(async () =>
            {
                if (ReadProfile(s => s.Viper.HapticScroll)?.Clone() is not { } saved) return;
                ApplyHapticProfileState(await _service.ApplyHapticScrollProfileAsync(Connection, saved, HapticScrollProfileId, cancellationToken: token));
            }, token);

    private async Task RunHapticWriteAsync(Func<Task> action, CancellationToken token)
    {
        if (!CanApplyHapticScroll) return;
        IsHapticScrollBusy = true;
        _operationSucceeded = false;
        _operationStatusOverride = null;
        try
        {
            await action();
            ErrorText = string.Empty;
            _operationSucceeded = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { ErrorText = error.Message; }
        finally
        {
            IsHapticScrollBusy = false;
            OnPropertyChanged(nameof(OperationStatusText));
        }
    }

    internal Func<Task<string?>>? PrepareHapticAutoApply(CancellationToken token)
    {
        if (!CanEditHapticScroll) return null;
        HapticScrollProfile draft;
        try { draft = CaptureHapticProfile(); }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        { return () => Task.FromResult<string?>(exception.Message); }
        if (_confirmedHapticScroll!.ContentEquals(draft)) return null;
        return CaptureMouseApply(() => CanApplyHapticScroll, () => { }, ct => ApplyHapticScrollDraftAsync(draft, ct), token);
    }

    private async Task PersistHapticProfileAsync(HapticScrollProfile state, CancellationToken token)
    {
        if (_getActiveProfile is null || _saveProfileAsync is null) return;
        var profile = _getActiveProfile();
        var key = ProfileResolver.GetDeviceKey(Connection.ToDescriptor());
        var created = !profile.Devices.ContainsKey(key);
        var settings = ResolveDeviceProfileSettings()!;
        var previous = settings.Viper.HapticScroll;
        var previousLevel = settings.Viper.HapticScrollLevel;
        settings.Viper.HapticScroll = state.Clone();
        settings.Viper.HapticScrollLevel = state.ActiveStage;
        try
        {
            if (!await _saveProfileAsync(token)) throw new IOException(AppStrings.Text("ViperSettingAppliedProfileSaveFailed"));
        }
        catch
        {
            settings.Viper.HapticScroll = previous;
            settings.Viper.HapticScrollLevel = previousLevel;
            if (created) profile.Devices.Remove(key);
            throw;
        }
    }

    private static RazerHidHapticScrollStageRowViewModel[] StageDefinitions() =>
    [
        new(RazerHidHapticScrollStageIds.Standard, AppStrings.Text("RazerHidHapticScrollStandard"), true),
        new(RazerHidHapticScrollStageIds.Distinct, AppStrings.Text("RazerHidHapticScrollDistinct"), true),
        new(RazerHidHapticScrollStageIds.UltraFine, AppStrings.Text("RazerHidHapticScrollUltraFine"), true),
        new(RazerHidHapticScrollStageIds.Adaptive, AppStrings.Text("RazerHidHapticScrollAdaptive"), true),
        new(RazerHidHapticScrollStageIds.SmoothScroll, AppStrings.Text("RazerHidHapticScrollSmooth"), true),
        new(RazerHidHapticScrollStageIds.Custom, AppStrings.Text("RazerHidHapticScrollCustom"), false),
    ];
}
