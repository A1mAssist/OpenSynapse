using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private BladeFanCurveEditorViewModel? _bladeFanCurveEditor;

    /// <summary>Draggable curve editor. Created lazily so non-Blade sessions pay nothing.</summary>
    public BladeFanCurveEditorViewModel BladeFanCurveEditor =>
        _bladeFanCurveEditor ??= new BladeFanCurveEditorViewModel();

    /// <summary>Collapses the whole curve block on models with no audited fan-target protocol.</summary>
    public Visibility BladeFanCurveVisibility =>
        _blade.SupportsFanCurve && IsBladeCustomMode ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True once a curve is applied, so the card can say so instead of showing defaults.</summary>
    public bool BladeFanCurveActive =>
        EditablePerformanceBladeProfile.FanCurve is not null && IsBladeCustomMode;

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
        var curve = resolved.FanCurve is null ? null : TryCreateCurve(resolved);
        _bladeFanCurveEditor = new BladeFanCurveEditorViewModel(curve);
        OnPropertyChanged(nameof(BladeFanCurveEditor));
        OnPropertyChanged(nameof(BladeFanCurveModeIndex));
        OnPropertyChanged(nameof(BladeFanCurveActive));
        OnPropertyChanged(nameof(BladeFanCurveVisibility));
    }

    public void AddBladeFanCurvePoint() => BladeFanCurveEditor.AddPoint();

    public void RemoveBladeFanCurvePoint() => BladeFanCurveEditor.RemoveSelected();

    public void ResetBladeFanCurveToDefault()
    {
        BladeFanCurveEditor.ResetToDefault();
        OnPropertyChanged(nameof(BladeFanCurveModeIndex));
    }

    public async Task ApplyBladeFanCurveEditorAsync(CancellationToken cancellationToken = default)
    {
        if (!BladeFanCurveVisibility.Equals(Visibility.Visible))
        {
            return;
        }

        // BuildCurve normalises temperatures, so a rejected edit still reaches the device
        // in a valid shape instead of failing the whole apply.
        var curve = BladeFanCurveEditor.BuildCurve();
        await ApplyBladeFanCurveAsync(curve, cancellationToken);
        OnPropertyChanged(nameof(BladeFanCurveActive));
    }

    private static BladeFanCurve? TryCreateCurve(BladeProfileSettings resolved)
    {
        try
        {
            return resolved.FanCurve?.CreateCurve();
        }
        catch (ArgumentException)
        {
            // A hand-edited profile file can hold tiers the model rejects; fall back to the
            // default curve rather than blocking the card.
            return null;
        }
    }
}