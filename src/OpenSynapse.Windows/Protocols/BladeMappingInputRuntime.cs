using System.Text.Json.Nodes;

namespace OpenSynapse.Windows.Protocols;

public enum BladeMappingInputKind
{
    Keyboard,
    RazerKey,
    MouseButton,
    MouseWheel,
}

public enum BladeMappingOutputKind
{
    Keyboard,
    HyperShift,
    Disable,
    SnapTapToggle,
    BladeBattery,
    BladeTrackpad,
    BladePerformance,
    ScreenRefresh,
    Multi,
    Delay,
    Turbo,
    Display,
    Backlight,
    GameMode,
    Audio,
}

public enum BladeMappingCommand
{
    Toggle,
    NextPerformanceMode,
    NextRefreshRate,
    DriverBrightnessDown,
    DriverBrightnessUp,
    DriverBrightnessStop,
    BrightnessDown,
    BrightnessUp,
    Microphone,
}

public abstract record BladeMappingAction(BladeMappingOutputKind Kind);

public sealed record BladeKeyboardMappingAction(
    int ScanCode,
    bool IsDown,
    bool Extended)
    : BladeMappingAction(BladeMappingOutputKind.Keyboard);

public sealed record BladeDisabledMappingAction()
    : BladeMappingAction(BladeMappingOutputKind.Disable);

public sealed record BladeCommandMappingAction(
    BladeMappingOutputKind CommandKind,
    BladeMappingCommand Command)
    : BladeMappingAction(CommandKind);

public sealed record BladeMultiMappingAction(
    IReadOnlyList<BladeMappingAction> Actions)
    : BladeMappingAction(BladeMappingOutputKind.Multi);

public sealed record BladeDelayMappingAction(int Milliseconds)
    : BladeMappingAction(BladeMappingOutputKind.Delay);

public sealed record BladeTurboMappingAction(
    Guid Id,
    bool IsDown,
    int? DelayMilliseconds,
    int? Repeat)
    : BladeMappingAction(BladeMappingOutputKind.Turbo);

public sealed record BladeBacklightMappingAction(
    BladeMappingCommand Command,
    bool IsDown)
    : BladeMappingAction(BladeMappingOutputKind.Backlight);

public sealed record BladeAudioMappingAction(
    BladeMappingCommand Command,
    int Mute,
    int? Repeat)
    : BladeMappingAction(BladeMappingOutputKind.Audio);

public sealed record BladeHyperShiftMappingAction(bool IsDown)
    : BladeMappingAction(BladeMappingOutputKind.HyperShift);

public sealed record BladeSnapTapMappingAction(BladeMappingCommand Command)
    : BladeMappingAction(BladeMappingOutputKind.SnapTapToggle);

public readonly record struct BladeMappingRule(
    BladeMappingInputKind InputKind,
    int InputCode,
    bool HyperShiftLayer,
    BladeMappingOutputKind OutputKind,
    int OutputCode,
    int? SnapTapId = null,
    bool OutputExtended = false,
    BladeMappingAction? PressAction = null,
    BladeMappingAction? ReleaseAction = null,
    bool InputExtended = false,
    bool GameModeOnly = false);

public readonly record struct BladeMappingInputEvent(
    BladeMappingInputKind Kind,
    int Code,
    bool IsDown,
    bool Extended = false);

public readonly record struct BladeMappingOutputEvent(
    int ScanCode,
    bool IsDown,
    bool Extended = false);

/// <summary>
/// Owns the stateful part of Product 710 host-side mappings. The caller must
/// feed only events decoded from the internal Blade collection; external
/// keyboard events must never enter this runtime.
/// </summary>
public sealed partial class BladeMappingInputRuntime : IDisposable
{
    private readonly Dictionary<RuleKey, BladeMappingRule> _rules;
    private readonly Dictionary<InputKey, BladeMappingRule> _activeRules = [];
    private readonly Dictionary<InputKey, long> _activeInputSequence = [];
    private readonly Dictionary<int, Dictionary<InputKey, long>> _snapTapPressed = [];
    private readonly Dictionary<int, InputKey> _snapTapOwners = [];
    private readonly Dictionary<OutputKey, int> _pressedOutputs = [];
    private readonly HashSet<InputKey> _hyperShiftOwners = [];
    private readonly HashSet<InputKey> _passThroughInputs = [];
    private long _sequence;
    private bool _hyperShift;
    private bool _snapTapEnabled;
    private volatile bool _gameModeEnabled;
    private bool _disposed;

    public bool HyperShiftEnabled => _hyperShift;
    public bool SnapTapEnabled => _snapTapEnabled;
    public bool GameModeEnabled => _gameModeEnabled;
    public int MappingCount => checked(_rules.Count * 2);
    public IReadOnlyCollection<BladeMappingRule> Rules => _rules.Values;

    public IReadOnlyList<BladeMappingOutputEvent> Stop()
    {
        if (_disposed)
        {
            return [];
        }

        var output = _pressedOutputs.Keys
            .OrderBy(key => key.ScanCode)
            .ThenBy(key => key.Extended)
            .Select(key => new BladeMappingOutputEvent(key.ScanCode, false, key.Extended))
            .ToArray();
        _pressedOutputs.Clear();
        _activeRules.Clear();
        _activeInputSequence.Clear();
        _passThroughInputs.Clear();
        _snapTapPressed.Clear();
        _snapTapOwners.Clear();
        _hyperShiftOwners.Clear();
        _hyperShift = false;
        _snapTapEnabled = false;
        return output;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private readonly record struct RuleKey(
        BladeMappingInputKind Kind,
        int Code,
        bool HyperShift,
        bool Extended);

    private readonly record struct InputKey(
        BladeMappingInputKind Kind,
        int Code,
        bool Extended);

    private readonly record struct OutputKey(
        int ScanCode,
        bool Extended);

    private readonly record struct RuleBuilder(
        JsonObject Input,
        JsonObject? Press,
        JsonObject? ReleaseInput,
        JsonObject? Release);
}
