using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

public readonly record struct MacroInputEvent(
    MacroInput Input,
    bool IsDown,
    long TimestampMilliseconds)
{
    public bool IsMouseMove { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int WindowLeft { get; init; }
    public int WindowTop { get; init; }
    public int WindowWidth { get; init; }
    public int WindowHeight { get; init; }
    public MacroModifierKeys Modifiers { get; init; }
}
