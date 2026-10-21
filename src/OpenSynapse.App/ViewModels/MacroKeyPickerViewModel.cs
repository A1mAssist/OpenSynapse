using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// One pickable key in the on-screen key grid.
/// </summary>
public sealed class MacroKeyOptionViewModel(MacroKeyChoice choice)
{
    public string Label => choice.Label;

    public MacroInput Input => choice.Input;
}

/// <summary>
/// The on-screen key picker, so a macro step can be added without capturing
/// global input. The keyboard is exposed as one board so the page can draw it
/// the way it sits on the desk.
/// </summary>
public sealed class MacroKeyPickerViewModel : INotifyPropertyChanged
{
    private MacroKeyOptionViewModel? _selected;

    public MacroKeyPickerViewModel()
    {
        Slots = MacroKeyCatalog.Board.Slots
            .Select(slot => new MacroKeySlotViewModel(slot))
            .ToList();
        Columns = MacroKeyCatalog.Board.Columns;
        Rows = MacroKeyCatalog.Board.Rows;
        MouseButtons = MacroKeyCatalog.MouseButtons
            .Select(choice => new MacroKeyOptionViewModel(choice))
            .ToList();
        Wheels = MacroKeyCatalog.Wheels
            .Select(choice => new MacroKeyOptionViewModel(choice))
            .ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<MacroKeySlotViewModel> Slots { get; }

    public int Columns { get; }

    public int Rows { get; }

    public IReadOnlyList<MacroKeyOptionViewModel> MouseButtons { get; }

    public IReadOnlyList<MacroKeyOptionViewModel> Wheels { get; }

    public MacroKeyOptionViewModel? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            _selected = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One key position on the drawn board. The column span is expressed in
/// quarter-key units so the wide keys line up with the square ones.
/// </summary>
public sealed class MacroKeySlotViewModel(MacroKeySlot slot)
{
    public MacroKeyOptionViewModel Key { get; } = new(slot.Key);

    public int Row => slot.Row;

    public int Column => slot.Column;

    public int ColumnSpan => slot.ColumnSpan;

    public int RowSpan => slot.RowSpan;
}
