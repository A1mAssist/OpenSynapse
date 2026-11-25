namespace OpenSynapse.Core.Macros;

/// <summary>
/// Host-side stand-ins for the standard mouse buttons. A mapping that has to call a
/// button's native function without running that button's macro binds one of these
/// to the source key instead.
/// </summary>
public static class NativeMouseButtonCatalog
{
    public const string MacroIdPrefix = "sys-native:";

    public static IReadOnlyList<NativeMouseButtonAction> Actions { get; } =
        Enumerable.Range(1, HostRemapPolicy.MaxInjectableMouseButton)
            .Select(button => new NativeMouseButtonAction(button))
            .ToArray();

    public static string MacroId(int button) =>
        MacroIdPrefix + FormattableString.Invariant($"mouse-{button}");

    public static NativeMouseButtonAction? ActionOf(string? macroId)
    {
        if (string.IsNullOrEmpty(macroId) ||
            !macroId.StartsWith(MacroIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var id = macroId[MacroIdPrefix.Length..];
        return Actions.FirstOrDefault(action =>
            string.Equals(FormattableString.Invariant($"mouse-{action.Button}"), id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Rebuilds a stored entry with the current sequence but its saved triggers, so
    /// an older binding survives a change to how the button is injected.
    /// </summary>
    public static MacroDefinition Refresh(MacroDefinition stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var action = ActionOf(stored.Id) ?? throw new InvalidOperationException(
            $"Macro '{stored.Id}' is not a native mouse button.");
        var fresh = action.CreateMacro();
        fresh.Trigger = stored.Trigger;
        fresh.AdditionalTriggers = stored.AdditionalTriggers;
        fresh.SoftwareBinding = stored.SoftwareBinding;
        return fresh;
    }

    public static IReadOnlyList<MacroDefinition> Bindings(IEnumerable<MacroDefinition> stored) =>
        stored.Where(macro => ActionOf(macro.Id) is not null).Select(Refresh).ToArray();
}

public sealed record NativeMouseButtonAction(int Button)
{
    public string MacroId => NativeMouseButtonCatalog.MacroId(Button);

    public MacroDefinition CreateMacro() => new()
    {
        Id = MacroId,
        Name = FormattableString.Invariant($"Mouse button {Button}"),
        Enabled = true,
        PlaybackMode = MacroPlaybackMode.Once,
        RepeatCount = 1,
        Steps = [Step(isDown: true), Step(isDown: false)],
    };

    private MacroStep Step(bool isDown) => new()
    {
        Kind = MacroStepKind.Input,
        IsDown = isDown,
        // Both halves are spelled out; an auto release would close the press
        // before the injected press reaches the target window.
        AutoRelease = false,
        DelayMilliseconds = isDown ? 0 : 10,
        Input = new MacroInput
        {
            Kind = MacroInputKind.MouseButton,
            MouseButton = Button,
        },
    };
}
