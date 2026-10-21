using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

public readonly record struct MacroInputEvent(
    MacroInput Input,
    bool IsDown,
    long TimestampMilliseconds);
