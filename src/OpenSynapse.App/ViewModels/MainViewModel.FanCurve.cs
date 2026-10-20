using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// How the Blade fans are driven, matching what Synapse exposes: firmware automatic,
/// a fixed speed, or a temperature curve the app evaluates.
/// </summary>
public enum BladeFanControlMode
{
    Automatic = 0,
    Fixed = 1,
    Curve = 2,
}

public sealed partial class MainViewModel
{
    private BladeFanCurveEditorViewModel? _bladeFanCurveEditor;
    private int _bladeFanFixedRpm = BladeFanLimits.MinimumRpm;
    private BladeFanControlMode? _pendingMode;

    /// <summary>Draggable curve editor. Created lazily so non-Blade sessions pay nothing.</summary>
    public BladeFanCurveEditorViewModel BladeFanCurveEditor =>
        _bladeFanCurveEditor ??= new BladeFanCurveEditorViewModel();

    /// <summary>
    /// Which of the three fan control surfaces is showing. Curve mode only exists on models
    /// with audited fan-target evidence, so the picker never offers a mode we cannot drive.
    /// This is a display of the stored profile, not an editable selection: the radio buttons
    /// each own their own commit, so picking one must never silently reinterpret the others.
    /// </summary>
    public int BladeFanControlModeIndex => (int)(_pendingMode ?? StoredBladeFanControlMode);

    private BladeFanControlMode StoredBladeFanControlMode =>
        EditablePerformanceBladeProfile.FanCurve is not null
            ? BladeFanControlMode.Curve
            : EditablePerformanceBladeProfile.FanMode is byte
                ? BladeFanControlMode.Fixed
                : BladeFanControlMode.Automatic;

    /// <summary>
    /// Fixed-speed target, snapped to the protocol step so the stored value is always one
    /// the device accepts.
    /// </summary>
    public int BladeFanFixedRpm
    {
        get => EditablePerformanceBladeProfile.FanTargetRpm ?? _bladeFanFixedRpm;
        set
        {
            var clamped = Math.Clamp(value, BladeFanLimits.MinimumRpm, BladeFanLimits.MaximumRpm);
            var snapped = BladeFanLimits.MinimumRpm +
                ((clamped - BladeFanLimits.MinimumRpm) / BladeFanLimits.StepRpm) * BladeFanLimits.StepRpm;
            SetField(ref _bladeFanFixedRpm, snapped);
        }
    }

    public IReadOnlyList<string> BladeFanControlModeOptions =>
        _blade.SupportsFanCurve
            ? AppStrings.Texts("BladeFanModeAutomatic", "BladeFanModeFixed", "BladeFanModeCurve")
            : _blade.SupportsFixedFan
                ? AppStrings.Texts("BladeFanModeAutomatic", "BladeFanModeFixed")
                : AppStrings.Texts("BladeFanModeAutomatic");

    public bool BladeFanCurveModeSelected => BladeFanControlModeIndex == (int)BladeFanControlMode.Curve;
    public bool BladeFanFixedModeSelected => BladeFanControlModeIndex == (int)BladeFanControlMode.Fixed;
    public bool BladeFanAutomaticModeSelected => BladeFanControlModeIndex == (int)BladeFanControlMode.Automatic;

    public Visibility BladeFanFixedVisibility =>
        _blade.SupportsFixedFan && CanSetBladeFan ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BladeFanFixedRpmVisibility =>
        BladeFanFixedVisibility == Visibility.Visible && BladeFanFixedModeSelected
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Collapses the curve block on models with no audited fan-target protocol.</summary>
    public Visibility BladeFanCurveVisibility =>
        _blade.SupportsFanCurve && CanSetBladeFan ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The editor itself only shows while curve mode is the selected control.</summary>
    public Visibility BladeFanCurvePanelVisibility =>
        BladeFanCurveVisibility == Visibility.Visible && BladeFanCurveModeSelected
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>True once a curve is applied, so the card can say so instead of showing defaults.</summary>
    public bool BladeFanCurveActive =>
        EditablePerformanceBladeProfile.FanCurve is not null;

    public IReadOnlyList<string> BladeFanCurveModeOptions =>
        AppStrings.Texts("BladeFanCurveModeCpu", "BladeFanCurveModeGpu", "BladeFanCurveModeBoth");

    public int BladeFanCurveModeIndex
    {
        get => (int)BladeFanCurveEditor.TemperatureMode;
        set
        {
            if (!Enum.IsDefined(typeof(BladeFanCurveTemperatureMode), value))
            {
                return;
            }

            BladeFanCurveEditor.TemperatureMode = (BladeFanCurveTemperatureMode)value;
            OnPropertyChanged();
        }
    }

    /// <summary>Loads the resolved curve into the editor so the UI shows the configured tiers.</summary>
    public void RefreshBladeFanCurveEditor()
    {
        if (_blade._bladeProductId == 0)
        {
            return;
        }

        var blade = _deviceDescriptors.FirstOrDefault(device =>
            device.ProtocolFamily == DeviceProtocolFamilies.Blade);
        var resolved = blade is null
            ? EditablePerformanceBladeProfile
            : ProfileResolver.Resolve(_profile, blade, SelectedPerformancePowerState).Blade;
        _bladeFanCurveEditor = new BladeFanCurveEditorViewModel(TryCreateCurve(resolved));
        OnPropertyChanged(nameof(BladeFanCurveEditor));
        OnPropertyChanged(nameof(BladeFanCurveModeIndex));
        OnPropertyChanged(nameof(BladeFanCurveActive));
        OnPropertyChanged(nameof(BladeFanCurveVisibility));
        RaiseBladeFanModeSelection();
    }

    /// <summary>Re-raises the radio bindings after a stored mode changes.</summary>
    private void RaiseBladeFanModeSelection()
    {
        OnPropertyChanged(nameof(BladeFanControlModeIndex));
        OnPropertyChanged(nameof(BladeFanAutomaticModeSelected));
        OnPropertyChanged(nameof(BladeFanFixedModeSelected));
        OnPropertyChanged(nameof(BladeFanCurveModeSelected));
        OnPropertyChanged(nameof(BladeFanFixedVisibility));
        OnPropertyChanged(nameof(BladeFanFixedRpmVisibility));
        OnPropertyChanged(nameof(BladeFanCurvePanelVisibility));
    }

    public void AddBladeFanCurvePoint() => BladeFanCurveEditor.AddPoint();

    public void RemoveBladeFanCurvePoint() => BladeFanCurveEditor.RemoveSelected();

    public void ResetBladeFanCurveToDefault()
    {
        BladeFanCurveEditor.ResetToDefault();
        OnPropertyChanged(nameof(BladeFanCurveModeIndex));
    }

    /// <summary>Applies the editor tiers and switches the fans over to curve control.</summary>
    public async Task ApplyBladeFanCurveEditorAsync(CancellationToken cancellationToken = default)
    {
        if (BladeFanCurveVisibility != Visibility.Visible)
        {
            return;
        }

        // BuildCurve normalises temperatures, so a rejected edit still reaches the device
        // in a valid shape instead of failing the whole apply.
        var curve = BladeFanCurveEditor.BuildCurve();
        await ApplyBladeFanCurveAsync(curve, cancellationToken);
        _pendingMode = null;
        RaiseBladeFanModeSelection();
        OnPropertyChanged(nameof(BladeFanCurveActive));
    }

    /// <summary>
    /// Shows the curve surface. Choosing the mode only reveals the editor; the tiers still
    /// need Apply, so the radio holds a pending selection the profile does not know about yet.
    /// </summary>
    public void ShowBladeFanCurveMode()
    {
        if (BladeFanCurveVisibility != Visibility.Visible)
        {
            return;
        }

        _pendingMode = BladeFanControlMode.Curve;
        RaiseBladeFanModeSelection();
    }

    /// <summary>Shows the fixed-speed surface without committing a speed yet.</summary>
    public void ShowBladeFanFixedMode()
    {
        if (!_blade.SupportsFixedFan)
        {
            return;
        }

        _pendingMode = BladeFanControlMode.Fixed;
        RaiseBladeFanModeSelection();
    }

    /// <summary>Hands the fans back to firmware automatic control.</summary>
    public async Task SelectBladeFanAutomaticModeAsync(CancellationToken cancellationToken = default)
    {
        _pendingMode = null;
        await ApplyBladeFanFixedAsync(BladeFanMode.Automatic, null, cancellationToken);
        RaiseBladeFanModeSelection();
        OnPropertyChanged(nameof(BladeFanCurveActive));
    }

    /// <summary>
    /// Commits the target RPM the user typed. Switching into the fixed row without a commit
    /// would leave the device on the previous mode while the radio showed a new one.
    /// </summary>
    public async Task SelectBladeFanFixedModeAsync(CancellationToken cancellationToken = default)
    {
        if (!_blade.SupportsFixedFan)
        {
            return;
        }

        _pendingMode = null;
        await ApplyBladeFanFixedAsync(BladeFanMode.Manual, BladeFanFixedRpm, cancellationToken);
        RaiseBladeFanModeSelection();
        OnPropertyChanged(nameof(BladeFanCurveActive));
    }

    private static BladeFanCurve? TryCreateCurve(BladeProfileSettings resolved)
    {
        if (resolved.FanCurve is not { } profile)
        {
            return null;
        }

        try
        {
            return profile.CreateCurve();
        }
        catch (ArgumentException)
        {
            // A hand-edited profile file can hold tiers the model rejects; fall back to the
            // default curve rather than blocking the card.
            return null;
        }
    }
}