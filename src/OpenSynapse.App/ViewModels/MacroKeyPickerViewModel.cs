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
        var mediaKeys = MacroKeyCatalog.MediaKeys
            .Select(choice => new MacroKeyChoice(
                MediaLabel(choice.Input.VirtualKey),
                choice.Input))
            .ToList();
        MediaKeys = mediaKeys
            .Select(choice => new MacroKeyOptionViewModel(choice))
            .ToList();
        KeyboardKeys = MacroKeyCatalog.Board.Slots
            .Select(slot => slot.Key)
            .Concat(MacroKeyCatalog.ExtendedFunctionKeys)
            .Concat(mediaKeys)
            .Where(choice => !IsModifier(choice.Input))
            .DistinctBy(choice => (choice.Input.VirtualKey, choice.Input.Extended))
            .Select(choice => new MacroKeyOptionViewModel(choice))
            .ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<MacroKeySlotViewModel> Slots { get; }

    public int Columns { get; }

    public int Rows { get; }

    public IReadOnlyList<MacroKeyOptionViewModel> MouseButtons { get; }

    public IReadOnlyList<MacroKeyOptionViewModel> Wheels { get; }

    public IReadOnlyList<MacroKeyOptionViewModel> MediaKeys { get; }

    public IReadOnlyList<MacroKeyOptionViewModel> KeyboardKeys { get; }

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

    private static bool IsModifier(MacroInput input) =>
        input.Kind == MacroInputKind.Keyboard &&
        input.VirtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or
            0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static string MediaLabel(int virtualKey) => AppStrings.Text(virtualKey switch
    {
        0xAD => "MacroMediaMute",
        0xAE => "MacroMediaVolumeDown",
        0xAF => "MacroMediaVolumeUp",
        0xB0 => "MacroMediaNext",
        0xB1 => "MacroMediaPrevious",
        0xB2 => "MacroMediaStop",
        0xB4 => "MacroMediaMail",
        0xAC => "MacroMediaHomePage",
        _ => "MacroMediaPlayPause",
    });

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
