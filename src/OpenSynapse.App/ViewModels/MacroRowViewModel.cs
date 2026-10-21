using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed class MacroRowViewModel : INotifyPropertyChanged
{
    internal MacroDefinition Definition { get; }

    public MacroRowViewModel(MacroDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? EnabledChanged;

    public string Id => Definition.Id;

    public string Name
    {
        get => Definition.Name;
        set
        {
            if (StringComparer.Ordinal.Equals(Definition.Name, value))
            {
                return;
            }

            Definition.Name = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public bool Enabled
    {
        get => Definition.Enabled;
        set
        {
            if (Definition.Enabled == value)
            {
                return;
            }

            Definition.Enabled = value;
            OnPropertyChanged();
            EnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string TriggerText => Definition.Trigger?.Input is { } input
        ? AppStrings.FormatText("MacroTriggerValue", DescribeInput(input))
        : AppStrings.Text("MacroTriggerUnset");

    public string StepsText => AppStrings.FormatText("MacroStepCount", Definition.Steps.Count);

    public bool IsRepeating
    {
        get => Definition.PlaybackMode == MacroPlaybackMode.Repeat;
        set
        {
            var mode = value ? MacroPlaybackMode.Repeat : MacroPlaybackMode.Once;
            if (Definition.PlaybackMode == mode)
            {
                return;
            }

            Definition.PlaybackMode = mode;
            if (!value)
            {
                Definition.RepeatCount = 1;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(RepeatCount));
            OnPropertyChanged(nameof(RepeatCountVisibility));
        }
    }

    public double RepeatCount
    {
        get => Definition.RepeatCount;
        set
        {
            var count = Math.Clamp(
                (int)Math.Round(value),
                1,
                MacroDefinition.MaxRepeatCount);
            if (Definition.RepeatCount == count)
            {
                return;
            }

            Definition.RepeatCount = count;
            OnPropertyChanged();
        }
    }

    public Visibility RepeatCountVisibility =>
        IsRepeating ? Visibility.Visible : Visibility.Collapsed;

    public void SetTrigger(MacroTrigger? trigger)
    {
        Definition.Trigger = trigger;
        OnPropertyChanged(nameof(TriggerText));
    }

    public void SetSteps(IReadOnlyList<MacroStep> steps)
    {
        Definition.Steps = steps.ToList();
        OnPropertyChanged(nameof(StepsText));
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(TriggerText));
        OnPropertyChanged(nameof(StepsText));
        OnPropertyChanged(nameof(IsRepeating));
        OnPropertyChanged(nameof(RepeatCount));
        OnPropertyChanged(nameof(RepeatCountVisibility));
    }

    internal static string DescribeInput(MacroInput input) =>
        input.Kind switch
        {
            MacroInputKind.MouseButton => AppStrings.Text($"MacroMouseButton{input.MouseButton}"),
            MacroInputKind.MouseWheel when input.WheelHorizontal =>
                AppStrings.FormatText("MacroWheelHorizontal", input.WheelDelta),
            MacroInputKind.MouseWheel when input.WheelDelta > 0 =>
                AppStrings.FormatText("MacroWheelUp", input.WheelDelta),
            MacroInputKind.MouseWheel =>
                AppStrings.FormatText("MacroWheelDown", Math.Abs(input.WheelDelta)),
            _ => MacroInputFormatter.Describe(input),
        };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
