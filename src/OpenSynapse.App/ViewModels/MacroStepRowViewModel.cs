using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed class MacroStepRowViewModel : INotifyPropertyChanged
{
    public MacroStepRowViewModel(int index, MacroStep step)
    {
        Step = step ?? throw new ArgumentNullException(nameof(step));
        Index = index;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MacroStep Step { get; }
    public int Index { get; private set; }

    public bool IsDelay => Step.Kind == MacroStepKind.Delay;

    public string StepGlyph => IsDelay ? "\uE823" : "\uE765";

    public bool CanRecapture => !IsDelay;

    /// <summary>
    /// A press step owns its own release, so only presses expose a hold time.
    /// </summary>
    public Visibility HoldVisibility =>
        !IsDelay && Step.IsDown
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string NameText => IsDelay
        ? AppStrings.FormatText("MacroDelayStepName", Step.DelayMilliseconds)
        : Step.Input is { } input
            ? MacroRowViewModel.DescribeInput(input)
            : "?";

    public string DelayText => IsDelay
        ? string.Empty
        : AppStrings.FormatText("MacroStepDelayText", Step.DelayMilliseconds);

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
        }
    }

    public double DelayMilliseconds
    {
        get => Step.DelayMilliseconds;
        set
        {
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
        OnPropertyChanged(nameof(NameText));
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsDelay));
        OnPropertyChanged(nameof(StepGlyph));
        OnPropertyChanged(nameof(CanRecapture));
        OnPropertyChanged(nameof(HoldVisibility));
        OnPropertyChanged(nameof(HoldMilliseconds));
        OnPropertyChanged(nameof(DelayMilliseconds));
        OnPropertyChanged(nameof(NameText));
        OnPropertyChanged(nameof(DelayText));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
