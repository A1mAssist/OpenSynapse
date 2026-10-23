using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed class MacroStepRowViewModel : INotifyPropertyChanged
{
    private readonly Action? _changed;
    private readonly Func<string, string?>? _macroNameLookup;

    public MacroStepRowViewModel(
        MacroStep step,
        IList<MacroStep> owner,
        MacroStep? parentLoop,
        int depth,
        Action? changed = null,
        Func<string, string?>? macroNameLookup = null)
    {
        Step = step ?? throw new ArgumentNullException(nameof(step));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ParentLoop = parentLoop;
        Depth = Math.Max(0, depth);
        _changed = changed;
        _macroNameLookup = macroNameLookup;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MacroStep Step { get; }

    internal IList<MacroStep> Owner { get; }

    internal MacroStep? ParentLoop { get; }

    public int Index { get; private set; }

    public int Depth { get; }

    public Thickness IndentMargin => new(Depth * 14, 0, 0, 0);

    public bool IsInput => Step.Kind == MacroStepKind.Input;

    public bool IsDelay => Step.Kind == MacroStepKind.Delay;

    public bool IsText => Step.Kind == MacroStepKind.Text;

    public bool IsRunCommand => Step.Kind == MacroStepKind.RunCommand;

    public bool IsLoop => Step.Kind == MacroStepKind.Loop;

    public bool IsMacroReference => Step.Kind == MacroStepKind.MacroReference;

    public bool IsMouseMove => Step.Kind == MacroStepKind.MouseMove;

    public bool IsReadOnly => IsMouseMove;

    public bool HasGroup => !string.IsNullOrWhiteSpace(Step.GroupId);

    public bool CanRecapture => IsInput && !HasGroup;

    public string StepGlyph => Step.Kind switch
    {
        MacroStepKind.Delay => "\uE823",
        MacroStepKind.Text => "\uE8D2",
        MacroStepKind.RunCommand => "\uE756",
        MacroStepKind.Loop => "\uE8EE",
        MacroStepKind.MacroReference => "\uE8F1",
        MacroStepKind.MouseMove => "\uE962",
        _ => "\uE765",
    };

    public Visibility HoldVisibility =>
        IsInput && Step.IsDown && Step.AutoRelease
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility AutoReleaseVisibility =>
        IsInput && Step.IsDown ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DelayVisibility => Visibility.Visible;

    public Visibility InputDirectionVisibility =>
        IsInput && Step.Input?.Kind != MacroInputKind.MouseWheel
            ? Visibility.Visible
            : Visibility.Collapsed;

    public int DirectionIndex
    {
        get => Step.IsDown ? 0 : 1;
        set
        {
            if (!IsInput ||
                Step.Input?.Kind == MacroInputKind.MouseWheel ||
                value is not (0 or 1))
            {
                return;
            }

            var isDown = value == 0;
            if (Step.IsDown == isDown)
            {
                return;
            }

            Step.IsDown = isDown;
            if (!isDown)
            {
                Step.AutoRelease = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(DirectionText));
            OnPropertyChanged(nameof(NameText));
            OnPropertyChanged(nameof(HoldVisibility));
            OnPropertyChanged(nameof(AutoReleaseVisibility));
            Changed();
        }
    }

    public Visibility TextVisibility => IsText ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CommandVisibility => IsRunCommand ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LoopVisibility => IsLoop ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MacroReferenceVisibility =>
        IsMacroReference ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MousePathVisibility =>
        IsMouseMove ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GroupVisibility =>
        HasGroup ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DirectionVisibility =>
        IsInput && Step.Input?.Kind != MacroInputKind.MouseWheel
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string GroupText => HasGroup && Step.GroupId is { Length: > 0 } groupId
        ? AppStrings.FormatText("MacroGroupBadge", groupId[..Math.Min(4, groupId.Length)])
        : string.Empty;

    public string DirectionText => IsInput
        ? Step.IsDown
            ? AppStrings.Text("MacroStepDown")
            : AppStrings.Text("MacroStepUp")
        : string.Empty;

    public string NameText => Step.Kind switch
    {
        MacroStepKind.Delay => AppStrings.FormatText(
            "MacroDelayStepName",
            Step.DelayMilliseconds),
        MacroStepKind.Text => AppStrings.FormatText(
            "MacroTextStepName",
            Preview(Step.Text)),
        MacroStepKind.RunCommand => AppStrings.FormatText(
            "MacroCommandStepName",
            Preview(Step.Command)),
        MacroStepKind.Loop => AppStrings.FormatText("MacroLoopStepName", Step.LoopCount),
        MacroStepKind.MacroReference => AppStrings.FormatText(
            "MacroReferenceStepName",
            ReferenceName()),
        MacroStepKind.MouseMove => AppStrings.FormatText(
            "MacroMouseMoveStepName",
            Step.MousePath.Count),
        MacroStepKind.Input when Step.Input is { } input => Step.Input.Kind == MacroInputKind.MouseWheel
            ? MacroRowViewModel.DescribeInput(input)
            : AppStrings.FormatText(
                Step.IsDown ? "MacroStepNameDown" : "MacroStepNameUp",
                MacroRowViewModel.DescribeInput(input)),
        _ => AppStrings.Text("MacroStepUnknownName"),
    };

    public string DelayText => IsDelay
        ? string.Empty
        : AppStrings.FormatText("MacroStepDelayText", Step.DelayMilliseconds);

    public string MousePathText => AppStrings.FormatText(
        "MacroMousePathText",
        Step.MousePath.Count,
        Step.MousePath.Count == 0 ? 0 : Step.MousePath[^1].OffsetMilliseconds);

    public string Text
    {
        get => Step.Text ?? string.Empty;
        set
        {
            if (StringComparer.Ordinal.Equals(Step.Text, value))
            {
                return;
            }

            Step.Text = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameText));
            Changed();
        }
    }

    public string Command
    {
        get => Step.Command ?? string.Empty;
        set
        {
            if (StringComparer.Ordinal.Equals(Step.Command, value))
            {
                return;
            }

            Step.Command = value ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameText));
            Changed();
        }
    }

    public string Arguments
    {
        get => Step.Arguments ?? string.Empty;
        set
        {
            if (StringComparer.Ordinal.Equals(Step.Arguments, value))
            {
                return;
            }

            Step.Arguments = value ?? string.Empty;
            OnPropertyChanged();
            Changed();
        }
    }

    public string WorkingDirectory
    {
        get => Step.WorkingDirectory ?? string.Empty;
        set
        {
            if (StringComparer.Ordinal.Equals(Step.WorkingDirectory, value))
            {
                return;
            }

            Step.WorkingDirectory = value ?? string.Empty;
            OnPropertyChanged();
            Changed();
        }
    }

    public string MacroId
    {
        get => Step.MacroId ?? string.Empty;
        set
        {
            var id = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (StringComparer.Ordinal.Equals(Step.MacroId, id))
            {
                return;
            }

            Step.MacroId = id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameText));
            Changed();
        }
    }

    public double LoopCount
    {
        get => Step.LoopCount;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var count = Math.Clamp(
                (int)Math.Round(value),
                1,
                MacroStep.MaxLoopCount);
            if (Step.LoopCount == count)
            {
                return;
            }

            Step.LoopCount = count;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameText));
            Changed();
        }
    }

    public bool AutoRelease
    {
        get => Step.AutoRelease;
        set
        {
            if (Step.AutoRelease == value)
            {
                return;
            }

            Step.AutoRelease = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HoldVisibility));
            Changed();
        }
    }

    public int HoldMilliseconds
    {
        get => Step.HoldMilliseconds;
        set
        {
            var hold = Math.Clamp(value, 0, MacroStep.MaxHoldMilliseconds);
            if (Step.HoldMilliseconds == hold)
            {
                return;
            }

            Step.HoldMilliseconds = hold;
            OnPropertyChanged();
            Changed();
        }
    }

    public double DelayMilliseconds
    {
        get => Step.DelayMilliseconds;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var delay = Math.Clamp(
                (int)Math.Round(value),
                0,
                MacroDefinition.MaxDelayMilliseconds);
            if (Step.DelayMilliseconds == delay)
            {
                return;
            }

            Step.DelayMilliseconds = delay;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameText));
            OnPropertyChanged(nameof(DelayText));
            Changed();
        }
    }

    public void SetIndex(int index)
    {
        if (Index == index)
        {
            return;
        }

        Index = index;
        OnPropertyChanged(nameof(Index));
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsInput));
        OnPropertyChanged(nameof(IsDelay));
        OnPropertyChanged(nameof(IsText));
        OnPropertyChanged(nameof(IsRunCommand));
        OnPropertyChanged(nameof(IsLoop));
        OnPropertyChanged(nameof(IsMacroReference));
        OnPropertyChanged(nameof(IsMouseMove));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(HasGroup));
        OnPropertyChanged(nameof(CanRecapture));
        OnPropertyChanged(nameof(StepGlyph));
        OnPropertyChanged(nameof(HoldVisibility));
        OnPropertyChanged(nameof(AutoReleaseVisibility));
        OnPropertyChanged(nameof(DelayVisibility));
        OnPropertyChanged(nameof(InputDirectionVisibility));
        OnPropertyChanged(nameof(DirectionIndex));
        OnPropertyChanged(nameof(TextVisibility));
        OnPropertyChanged(nameof(CommandVisibility));
        OnPropertyChanged(nameof(LoopVisibility));
        OnPropertyChanged(nameof(MacroReferenceVisibility));
        OnPropertyChanged(nameof(MousePathVisibility));
        OnPropertyChanged(nameof(GroupVisibility));
        OnPropertyChanged(nameof(DirectionVisibility));
        OnPropertyChanged(nameof(GroupText));
        OnPropertyChanged(nameof(DirectionText));
        OnPropertyChanged(nameof(NameText));
        OnPropertyChanged(nameof(DelayText));
        OnPropertyChanged(nameof(MousePathText));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Command));
        OnPropertyChanged(nameof(Arguments));
        OnPropertyChanged(nameof(WorkingDirectory));
        OnPropertyChanged(nameof(MacroId));
        OnPropertyChanged(nameof(LoopCount));
        OnPropertyChanged(nameof(AutoRelease));
        OnPropertyChanged(nameof(HoldMilliseconds));
        OnPropertyChanged(nameof(DelayMilliseconds));
    }

    private string ReferenceName()
    {
        if (string.IsNullOrWhiteSpace(Step.MacroId))
        {
            return AppStrings.Text("MacroReferenceMissing");
        }

        return _macroNameLookup?.Invoke(Step.MacroId) ?? Step.MacroId;
    }

    private static string Preview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AppStrings.Text("MacroEmptyValue");
        }

        var singleLine = string.Join(' ', value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return singleLine.Length <= 42 ? singleLine : $"{singleLine[..41]}…";
    }

    private void Changed() => _changed?.Invoke();

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class MacroStepTree
{
    public static int CountSteps(IEnumerable<MacroStep> steps)
    {
        var count = 0;
        foreach (var step in steps)
        {
            count++;
            if (step.Kind == MacroStepKind.Loop)
            {
                count += CountSteps(step.Children);
            }
        }

        return count;
    }

    public static IEnumerable<MacroStep> Enumerate(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            if (step.Kind == MacroStepKind.Loop)
            {
                foreach (var child in Enumerate(step.Children))
                {
                    yield return child;
                }
            }
        }
    }

    public static bool RemoveEmptyLoops(List<MacroStep> steps)
    {
        var changed = false;
        for (var index = steps.Count - 1; index >= 0; index--)
        {
            var step = steps[index];
            if (step.Kind != MacroStepKind.Loop)
            {
                continue;
            }

            changed |= RemoveEmptyLoops(step.Children);
            if (step.Children.Count > 0)
            {
                continue;
            }

            steps.RemoveAt(index);
            changed = true;
        }

        return changed;
    }
}
