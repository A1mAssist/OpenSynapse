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
    public event EventHandler? DefinitionChanged;

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
            OnDefinitionChanged();
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
            OnDefinitionChanged();
            EnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HasTrigger => Definition.Trigger is not null;

    public string TriggerText => Definition.Trigger is { } trigger
        ? AppStrings.FormatText("MacroTriggerValue", DescribeTrigger(trigger))
        : AppStrings.Text("MacroTriggerUnset");

    public string StepsText => AppStrings.FormatText(
        "MacroStepCount",
        MacroStepTree.CountSteps(Definition.Steps));

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
            OnPropertyChanged(nameof(PlaybackModeIndex));
            OnPropertyChanged(nameof(RepeatCount));
            OnPropertyChanged(nameof(RepeatCountVisibility));
            OnDefinitionChanged();
        }
    }

    public int PlaybackModeIndex
    {
        get => (int)Definition.PlaybackMode;
        set
        {
            if (Definition.Trigger?.Mode == MacroTriggerMode.HoldRepeat ||
                !Enum.IsDefined(typeof(MacroPlaybackMode), value) ||
                (int)Definition.PlaybackMode == value)
            {
                return;
            }

            Definition.PlaybackMode = (MacroPlaybackMode)value;
            if (Definition.PlaybackMode != MacroPlaybackMode.Repeat)
            {
                Definition.RepeatCount = 1;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRepeating));
            OnPropertyChanged(nameof(RepeatCount));
            OnPropertyChanged(nameof(RepeatCountVisibility));
            OnDefinitionChanged();
        }
    }

    public double RepeatCount
    {
        get => Definition.RepeatCount;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

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
            OnDefinitionChanged();
        }
    }

    public double PlaybackSpeed
    {
        get => Definition.PlaybackSpeed;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var speed = Math.Clamp(
                Math.Round(value, 2),
                MacroDefinition.MinPlaybackSpeed,
                MacroDefinition.MaxPlaybackSpeed);
            if (Math.Abs(Definition.PlaybackSpeed - speed) < 0.0001)
            {
                return;
            }

            Definition.PlaybackSpeed = speed;
            OnPropertyChanged();
            OnDefinitionChanged();
        }
    }

    public double TriggerModeIndex
    {
        get => Definition.Trigger is { } trigger ? (int)trigger.Mode : -1;
        set
        {
            if (Definition.Trigger is not { } trigger)
            {
                return;
            }

            var index = (int)Math.Round(value);
            if (!Enum.IsDefined(typeof(MacroTriggerMode), index) ||
                (int)trigger.Mode == index)
            {
                return;
            }

            trigger.Mode = (MacroTriggerMode)index;
            if (trigger.Mode == MacroTriggerMode.HoldRepeat)
            {
                Definition.PlaybackMode = MacroPlaybackMode.RepeatUntilStopped;
                Definition.RepeatCount = 1;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPlaybackModeEditable));
            OnPropertyChanged(nameof(IsRepeating));
            OnPropertyChanged(nameof(PlaybackModeIndex));
            OnPropertyChanged(nameof(RepeatCount));
            OnPropertyChanged(nameof(RepeatCountVisibility));
            OnDefinitionChanged();
        }
    }

    public bool IsPlaybackModeEditable =>
        Definition.Trigger?.Mode != MacroTriggerMode.HoldRepeat;

    public Visibility RepeatCountVisibility =>
        Definition.PlaybackMode == MacroPlaybackMode.Repeat
            ? Visibility.Visible
            : Visibility.Collapsed;

    public void SetTrigger(MacroTrigger? trigger)
    {
        if (Definition.Trigger is null && trigger is null)
        {
            return;
        }

        Definition.Trigger = trigger;
        OnPropertyChanged(nameof(TriggerText));
        OnPropertyChanged(nameof(HasTrigger));
        OnPropertyChanged(nameof(TriggerModeIndex));
        OnPropertyChanged(nameof(IsPlaybackModeEditable));
        OnDefinitionChanged();
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
        OnPropertyChanged(nameof(HasTrigger));
        OnPropertyChanged(nameof(StepsText));
        OnPropertyChanged(nameof(IsRepeating));
        OnPropertyChanged(nameof(PlaybackModeIndex));
        OnPropertyChanged(nameof(RepeatCount));
        OnPropertyChanged(nameof(PlaybackSpeed));
        OnPropertyChanged(nameof(TriggerModeIndex));
        OnPropertyChanged(nameof(IsPlaybackModeEditable));
        OnPropertyChanged(nameof(RepeatCountVisibility));
    }

    public static string DescribeTrigger(MacroTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var input = DescribeInput(trigger.Input);
        if (trigger.Modifiers == MacroModifierKeys.None)
        {
            return input;
        }

        var parts = new List<string>(5);
        AddModifier(MacroModifierKeys.Control, "MacroModifierControl");
        AddModifier(MacroModifierKeys.Shift, "MacroModifierShift");
        AddModifier(MacroModifierKeys.Alt, "MacroModifierAlt");
        AddModifier(MacroModifierKeys.Windows, "MacroModifierWindows");

        parts.Add(input);
        return string.Join(" + ", parts);

        void AddModifier(MacroModifierKeys modifier, string key)
        {
            if ((trigger.Modifiers & modifier) == modifier)
            {
                parts.Add(AppStrings.Text(key));
            }
        }
    }

    public static string DescribeInput(MacroInput input) =>
        input.Kind switch
        {
            MacroInputKind.MouseButton => AppStrings.Text($"MacroMouseButton{input.MouseButton}"),
            MacroInputKind.NagaSideButton =>
                AppStrings.FormatText("MacroNagaSideButton", input.SideButton),
            MacroInputKind.MouseWheel when input.WheelHorizontal =>
                AppStrings.FormatText("MacroWheelHorizontal", input.WheelDelta),
            MacroInputKind.MouseWheel when input.WheelDelta > 0 =>
                AppStrings.FormatText("MacroWheelUp", input.WheelDelta),
            MacroInputKind.MouseWheel =>
                AppStrings.FormatText("MacroWheelDown", Math.Abs(input.WheelDelta)),
            _ => MacroInputFormatter.Describe(input),
        };

    private void OnDefinitionChanged() =>
        DefinitionChanged?.Invoke(this, EventArgs.Empty);

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record MacroReferenceOption(string Id, string Name);
