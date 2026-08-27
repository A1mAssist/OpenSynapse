using System.Text.Json.Nodes;

namespace OpenSynapse.Windows.Protocols;

public enum BladeMappingInputKind
{
    Keyboard,
    RazerKey,
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
    bool InputExtended = false);

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
public sealed class BladeMappingInputRuntime : IDisposable
{
    private readonly Dictionary<RuleKey, BladeMappingRule> _rules;
    private readonly Dictionary<InputKey, BladeMappingRule> _activeRules = [];
    private readonly Dictionary<InputKey, long> _activeInputSequence = [];
    private readonly Dictionary<int, Dictionary<InputKey, long>> _snapTapPressed = [];
    private readonly Dictionary<int, InputKey> _snapTapOwners = [];
    private readonly Dictionary<OutputKey, int> _pressedOutputs = [];
    private readonly HashSet<InputKey> _hyperShiftOwners = [];
    private long _sequence;
    private bool _hyperShift;
    private bool _snapTapEnabled;
    private bool _disposed;

    public static BladeMappingInputRuntime FromGraph(JsonObject graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph["mappings"] is not JsonArray mappings)
        {
            throw new ArgumentException("MappingEngine graph is missing the mappings array.", nameof(graph));
        }

        var builders = new Dictionary<RuleKey, RuleBuilder>();
        foreach (var node in mappings)
        {
            if (node is not JsonObject mapping ||
                mapping["input"] is not JsonObject input ||
                mapping["output"] is not JsonObject output)
            {
                throw new ArgumentException("MappingEngine mapping must contain input/output objects.", nameof(graph));
            }

            var inputKey = ParseInputKey(input, out var flag);
            var builder = builders.GetValueOrDefault(inputKey);
            if (flag is 0 or 2)
            {
                if (builder.Press is not null)
                {
                    throw new ArgumentException("MappingEngine graph contains a duplicate press mapping.", nameof(graph));
                }

                builders[inputKey] = new RuleBuilder(input, output, null, null);
            }
            else if (flag is 1 or 3)
            {
                if (builder.Press is null || builder.Release is not null)
                {
                    throw new ArgumentException("MappingEngine graph release mapping does not have a unique press mapping.", nameof(graph));
                }

                builders[inputKey] = builder with { ReleaseInput = input, Release = output };
            }
            else
            {
                throw new ArgumentException($"Invalid MappingEngine input flag: {flag}.", nameof(graph));
            }
        }

        var rules = new List<BladeMappingRule>(builders.Count);
        foreach (var (key, builder) in builders)
        {
            if (builder.Press is null || builder.ReleaseInput is null || builder.Release is null)
            {
                throw new ArgumentException(
                    $"MappingEngine graph is missing the press/release pair for {key.Kind}:{key.Code}.",
                    nameof(graph));
            }

            rules.Add(ParseRule(key, builder));
        }

        return new BladeMappingInputRuntime(rules);
    }

    internal static BladeMappingInputRuntime FromProduct710Graph(JsonObject graph)
    {
        var runtime = FromGraph(graph);
        if (graph["mappings"] is not JsonArray { Count: 64 } ||
            runtime.Rules.Count != 32 ||
            runtime.Rules
                .Where(static rule => rule.InputKind == BladeMappingInputKind.Keyboard)
                .Select(static rule => (rule.InputCode, rule.InputExtended))
                .Distinct()
                .Count() != 23)
        {
            runtime.Dispose();
            throw new ArgumentException("The Product 710 default mapping must contain all 64 records.", nameof(graph));
        }

        return runtime;
    }

    public BladeMappingInputRuntime(IEnumerable<BladeMappingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = new Dictionary<RuleKey, BladeMappingRule>();
        foreach (var rule in rules)
        {
            Validate(rule);
            var key = new RuleKey(
                rule.InputKind,
                rule.InputCode,
                rule.HyperShiftLayer,
                rule.InputExtended);
            if (!_rules.TryAdd(key, rule))
            {
                throw new ArgumentException(
                    $"Duplicate Blade mapping input {rule.InputKind}:{rule.InputCode} (HyperShift={rule.HyperShiftLayer}).",
                    nameof(rules));
            }
        }
    }

    public bool HyperShiftEnabled => _hyperShift;
    public bool SnapTapEnabled => _snapTapEnabled;
    public int MappingCount => checked(_rules.Count * 2);
    public IReadOnlyCollection<BladeMappingRule> Rules => _rules.Values;

    public IReadOnlyList<BladeMappingOutputEvent> Process(BladeMappingInputEvent input)
        => ProcessCore(input, false, out _);

    internal IReadOnlyList<BladeMappingOutputEvent> Process(
        BladeMappingInputEvent input,
        out BladeMappingAction? action)
        => ProcessCore(input, true, out action);

    private IReadOnlyList<BladeMappingOutputEvent> ProcessCore(
        BladeMappingInputEvent input,
        bool allowAppAction,
        out BladeMappingAction? action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        action = null;
        if (input.Code < 0 || input.Code > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        var inputKey = new InputKey(input.Kind, input.Code, input.Extended);
        BladeMappingRule? rule;
        if (input.IsDown)
        {
            if (_activeRules.ContainsKey(inputKey))
            {
                return [];
            }

            rule = ResolveRule(input);
            if (rule is null)
            {
                if (input.Kind != BladeMappingInputKind.Keyboard)
                {
                    return [];
                }

                // The filter suppresses installed hook keys even when the active
                // layer has no mapping, so preserve their normal keyboard behavior.
                rule = new BladeMappingRule(
                    input.Kind,
                    input.Code,
                    _hyperShift,
                    BladeMappingOutputKind.Keyboard,
                    input.Code,
                    OutputExtended: input.Extended,
                    InputExtended: input.Extended);
            }

        }
        else
        {
            if (!_activeRules.TryGetValue(inputKey, out var activeRule))
            {
                return [];
            }

            rule = activeRule;
        }

        if (!allowAppAction && RequiresAppExecutor(rule.Value.OutputKind))
        {
            throw new InvalidOperationException(
                $"Mapping action {rule.Value.OutputKind} is compiled but must be handled by the app action executor.");
        }

        if (input.IsDown)
        {
            _activeRules[inputKey] = rule.Value;
            _activeInputSequence[inputKey] = ++_sequence;
        }
        else
        {
            _activeRules.Remove(inputKey);
            _activeInputSequence.Remove(inputKey);
        }

        return rule.Value.SnapTapId is int snapTapId && _snapTapEnabled
            ? ProcessSnapTap(inputKey, rule.Value, input.IsDown, snapTapId)
            : ProcessOutput(inputKey, rule.Value, input.IsDown, out action);
    }

    public IReadOnlyList<BladeMappingOutputEvent> SetSnapTapEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_snapTapEnabled == enabled)
        {
            return [];
        }

        _snapTapEnabled = enabled;
        if (!enabled)
        {
            var output = new List<BladeMappingOutputEvent>();
            foreach (var (input, rule) in _activeRules)
            {
                if (rule.SnapTapId is int group &&
                    (!_snapTapOwners.TryGetValue(group, out var owner) || owner != input))
                {
                    output.AddRange(EmitKeyboard(
                        rule.OutputCode,
                        true,
                        rule.OutputExtended));
                }
            }
            _snapTapPressed.Clear();
            _snapTapOwners.Clear();
            return output;
        }

        var enabledOutput = new List<BladeMappingOutputEvent>();
        foreach (var group in _activeRules
                     .Where(static item => item.Value.SnapTapId is not null)
                     .GroupBy(static item => item.Value.SnapTapId!.Value))
        {
            var pressed = group.ToDictionary(
                static item => item.Key,
                item => _activeInputSequence[item.Key]);
            var owner = pressed.MaxBy(static item => item.Value).Key;
            _snapTapPressed[group.Key] = pressed;
            _snapTapOwners[group.Key] = owner;
            foreach (var (input, rule) in group)
            {
                if (input != owner)
                {
                    enabledOutput.AddRange(EmitKeyboard(
                        rule.OutputCode,
                        false,
                        rule.OutputExtended));
                }
            }
        }
        return enabledOutput;
    }

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

    private BladeMappingRule? ResolveRule(BladeMappingInputEvent input)
    {
        var key = new RuleKey(input.Kind, input.Code, _hyperShift, input.Extended);
        return _rules.TryGetValue(key, out var rule)
            ? rule
            : null;
    }

    private IReadOnlyList<BladeMappingOutputEvent> ProcessOutput(
        InputKey inputKey,
        BladeMappingRule rule,
        bool isDown,
        out BladeMappingAction? action)
    {
        action = null;
        switch (rule.OutputKind)
        {
            case BladeMappingOutputKind.Keyboard:
                return EmitKeyboard(rule.OutputCode, isDown, rule.OutputExtended);
            case BladeMappingOutputKind.HyperShift:
                if (isDown)
                    _hyperShiftOwners.Add(inputKey);
                else
                    _hyperShiftOwners.Remove(inputKey);
                _hyperShift = _hyperShiftOwners.Count != 0;
                return [];
            case BladeMappingOutputKind.SnapTapToggle when !isDown:
                return SetSnapTapEnabled(!_snapTapEnabled);
            case BladeMappingOutputKind.Disable:
                return [];
            case BladeMappingOutputKind.BladeBattery:
            case BladeMappingOutputKind.BladeTrackpad:
            case BladeMappingOutputKind.BladePerformance:
            case BladeMappingOutputKind.ScreenRefresh:
            case BladeMappingOutputKind.Multi:
            case BladeMappingOutputKind.Delay:
            case BladeMappingOutputKind.Turbo:
            case BladeMappingOutputKind.Display:
            case BladeMappingOutputKind.Backlight:
            case BladeMappingOutputKind.GameMode:
            case BladeMappingOutputKind.Audio:
                action = isDown ? rule.PressAction : rule.ReleaseAction;
                if (action is null)
                {
                    throw new InvalidOperationException(
                        $"Mapping action {rule.OutputKind} is missing compiled press/release actions.");
                }
                return [];
            default:
                throw new InvalidOperationException($"Undefined mapping output type: {rule.OutputKind}.");
        }
    }

    private IReadOnlyList<BladeMappingOutputEvent> ProcessSnapTap(
        InputKey inputKey,
        BladeMappingRule rule,
        bool isDown,
        int snapTapId)
    {
        var pressed = _snapTapPressed.GetValueOrDefault(snapTapId);
        if (pressed is null)
        {
            pressed = new Dictionary<InputKey, long>();
            _snapTapPressed[snapTapId] = pressed;
        }

        if (isDown)
        {
            pressed[inputKey] = _activeInputSequence[inputKey];
            var output = new List<BladeMappingOutputEvent>(2);
            if (_snapTapOwners.TryGetValue(snapTapId, out var previous) && previous != inputKey)
            {
                if (_activeRules.TryGetValue(previous, out var previousRule))
                {
                    output.AddRange(EmitKeyboard(
                        previousRule.OutputCode,
                        false,
                        previousRule.OutputExtended));
                }
            }

            _snapTapOwners[snapTapId] = inputKey;
            output.AddRange(EmitKeyboard(rule.OutputCode, true, rule.OutputExtended));
            return output;
        }

        pressed.Remove(inputKey);
        if (!_snapTapOwners.TryGetValue(snapTapId, out var owner) || owner != inputKey)
        {
            return [];
        }

        var outputEvents = new List<BladeMappingOutputEvent>(2);
        outputEvents.AddRange(EmitKeyboard(rule.OutputCode, false, rule.OutputExtended));
        if (pressed.Count == 0)
        {
            _snapTapOwners.Remove(snapTapId);
            return outputEvents;
        }

        var next = pressed.MaxBy(item => item.Value).Key;
        _snapTapOwners[snapTapId] = next;
        if (_activeRules.TryGetValue(next, out var nextRule))
        {
            outputEvents.AddRange(EmitKeyboard(
                nextRule.OutputCode,
                true,
                nextRule.OutputExtended));
        }

        return outputEvents;
    }

    private IReadOnlyList<BladeMappingOutputEvent> EmitKeyboard(
        int scanCode,
        bool isDown,
        bool extended)
    {
        if (scanCode is < 0 or > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Invalid mapping output scanCode: {scanCode}.");
        }

        var outputKey = new OutputKey(scanCode, extended);
        if (isDown)
        {
            var count = _pressedOutputs.GetValueOrDefault(outputKey);
            _pressedOutputs[outputKey] = checked(count + 1);
            if (count != 0)
            {
                return [];
            }
        }
        else if (!_pressedOutputs.TryGetValue(outputKey, out var count))
        {
            return [];
        }
        else if (count > 1)
        {
            _pressedOutputs[outputKey] = count - 1;
            return [];
        }
        else
        {
            _pressedOutputs.Remove(outputKey);
        }

        return [new BladeMappingOutputEvent(scanCode, isDown, extended)];
    }

    private static void Validate(BladeMappingRule rule)
    {
        if (!Enum.IsDefined(rule.InputKind))
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "Undefined mapping input type.");
        }
        if (!Enum.IsDefined(rule.OutputKind))
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "Undefined mapping output type.");
        }
        if (rule.InputCode is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "Input code must be in the range 0..65535.");
        }
        if (rule.InputKind != BladeMappingInputKind.Keyboard && rule.InputExtended)
        {
            throw new ArgumentException("Only keyboard input can carry the extended flag.", nameof(rule));
        }
        if (rule.OutputKind == BladeMappingOutputKind.Keyboard &&
            rule.OutputCode is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "Output scanCode must be in the range 0..65535.");
        }
        if (rule.OutputKind != BladeMappingOutputKind.Keyboard &&
            (rule.OutputCode != 0 || rule.OutputExtended))
        {
            throw new ArgumentException("Non-keyboard mappings cannot carry a scanCode or extended flag.", nameof(rule));
        }
        if (rule.SnapTapId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "Snap Tap id must be positive.");
        }
        if (rule.OutputKind != BladeMappingOutputKind.Keyboard && rule.SnapTapId is not null)
        {
            throw new ArgumentException("Only keyboard output can carry a Snap Tap id.", nameof(rule));
        }
        if ((rule.PressAction is null) != (rule.ReleaseAction is null))
        {
            throw new ArgumentException("Mapping actions must contain both press and release actions.", nameof(rule));
        }
        if (rule.OutputKind is not (
                BladeMappingOutputKind.Keyboard or
                BladeMappingOutputKind.HyperShift or
                BladeMappingOutputKind.Disable or
                BladeMappingOutputKind.SnapTapToggle) &&
            rule.PressAction is null)
        {
            throw new ArgumentException("Advanced mappings must contain compiled press and release actions.", nameof(rule));
        }
        if (rule.PressAction is not null && rule.ReleaseAction is not null)
        {
            ValidateAction(rule.PressAction);
            ValidateAction(rule.ReleaseAction);
            if (GetPairedOutputKind(rule.PressAction, rule.ReleaseAction) != rule.OutputKind)
            {
                throw new ArgumentException("Mapping actions do not match the output type.", nameof(rule));
            }
            if (rule.PressAction is BladeKeyboardMappingAction keyboard &&
                (keyboard.ScanCode != rule.OutputCode || keyboard.Extended != rule.OutputExtended))
            {
                throw new ArgumentException("Keyboard actions do not match the rule output.", nameof(rule));
            }
        }
    }

    private static BladeMappingRule ParseRule(RuleKey key, RuleBuilder builder)
    {
        var press = builder.Press!;
        var release = builder.Release!;
        ValidateInputPair(builder);
        var pressAction = ParseAction(press);
        var releaseAction = ParseAction(release);
        var snapTapId = GetOptionalInt(builder.Input, "snaptapId");
        if (pressAction is BladeKeyboardMappingAction pressKeyboard &&
            releaseAction is BladeKeyboardMappingAction releaseKeyboard)
        {
            if (pressKeyboard.ScanCode != releaseKeyboard.ScanCode ||
                !pressKeyboard.IsDown || releaseKeyboard.IsDown ||
                pressKeyboard.Extended != releaseKeyboard.Extended)
            {
                throw new ArgumentException("MappingEngine press/release keyboard outputs do not match.");
            }

            return new(
                key.Kind,
                key.Code,
                key.HyperShift,
                BladeMappingOutputKind.Keyboard,
                pressKeyboard.ScanCode,
                snapTapId,
                pressKeyboard.Extended,
                pressAction,
                releaseAction,
                key.Extended);
        }

        if (pressAction is BladeHyperShiftMappingAction { IsDown: true } &&
            releaseAction is BladeHyperShiftMappingAction { IsDown: false })
        {
            return new(
                key.Kind,
                key.Code,
                key.HyperShift,
                BladeMappingOutputKind.HyperShift,
                0,
                PressAction: pressAction,
                ReleaseAction: releaseAction,
                InputExtended: key.Extended);
        }

        if (pressAction is BladeDisabledMappingAction &&
            releaseAction is BladeSnapTapMappingAction { Command: BladeMappingCommand.Toggle })
        {
            return new(
                key.Kind,
                key.Code,
                key.HyperShift,
                BladeMappingOutputKind.SnapTapToggle,
                0,
                PressAction: pressAction,
                ReleaseAction: releaseAction,
                InputExtended: key.Extended);
        }

        var outputKind = GetPairedOutputKind(pressAction, releaseAction);
        if (outputKind == BladeMappingOutputKind.Disable &&
            (GetString(press, "type") != "disabled" || GetString(release, "type") != "disabled"))
        {
            throw new ArgumentException("Product 710 empty mappings must use a disabled/disabled pair.");
        }

        return new(
            key.Kind,
            key.Code,
            key.HyperShift,
            outputKind,
            0,
            PressAction: pressAction,
            ReleaseAction: releaseAction,
            InputExtended: key.Extended);
    }

    private static void ValidateInputPair(RuleBuilder builder)
    {
        var pressFlag = GetFlag(builder.Input);
        var releaseFlag = GetFlag(builder.ReleaseInput!);
        if (releaseFlag != pressFlag + 1 ||
            GetOptionalInt(builder.Input, "snaptapId") != GetOptionalInt(builder.ReleaseInput!, "snaptapId") ||
            builder.Input["modifiers"] is not null ||
            builder.ReleaseInput!["modifiers"] is not null)
        {
            throw new ArgumentException("MappingEngine press/release inputs do not match.");
        }
    }

    private static BladeMappingOutputKind GetPairedOutputKind(
        BladeMappingAction press,
        BladeMappingAction release) => (press, release) switch
    {
        (BladeKeyboardMappingAction down, BladeKeyboardMappingAction up)
            when down.IsDown && !up.IsDown &&
                 down.ScanCode == up.ScanCode && down.Extended == up.Extended =>
            BladeMappingOutputKind.Keyboard,
        (BladeHyperShiftMappingAction { IsDown: true }, BladeHyperShiftMappingAction { IsDown: false }) =>
            BladeMappingOutputKind.HyperShift,
        (BladeDisabledMappingAction, BladeSnapTapMappingAction { Command: BladeMappingCommand.Toggle }) =>
            BladeMappingOutputKind.SnapTapToggle,
        (BladeDisabledMappingAction, BladeDisabledMappingAction) => BladeMappingOutputKind.Disable,
        (BladeDisabledMappingAction, BladeCommandMappingAction command)
            when command.Kind is BladeMappingOutputKind.BladeBattery or
                                 BladeMappingOutputKind.BladeTrackpad or
                                 BladeMappingOutputKind.BladePerformance or
                                 BladeMappingOutputKind.ScreenRefresh => command.Kind,
        (BladeCommandMappingAction { Kind: BladeMappingOutputKind.GameMode }, BladeDisabledMappingAction) =>
            BladeMappingOutputKind.GameMode,
        (BladeAudioMappingAction { Mute: 2, Repeat: 1 }, BladeDisabledMappingAction) =>
            BladeMappingOutputKind.Audio,
        (BladeMultiMappingAction pressMulti, BladeDisabledMappingAction) when IsKeyTap(pressMulti) =>
            BladeMappingOutputKind.Multi,
        (BladeMultiMappingAction pressMulti, BladeMultiMappingAction releaseMulti)
            when IsBalancedKeyboardSequence(pressMulti, releaseMulti) => BladeMappingOutputKind.Multi,
        (BladeTurboMappingAction down, BladeTurboMappingAction up)
            when down.IsDown && !up.IsDown && down.Id == up.Id &&
                 down.DelayMilliseconds == 100 && down.Repeat is null &&
                 up.DelayMilliseconds is null && up.Repeat is null => BladeMappingOutputKind.Turbo,
        (BladeTurboMappingAction
            {
                IsDown: true,
                DelayMilliseconds: null,
                Repeat: 1,
            },
         BladeDisabledMappingAction) =>
            BladeMappingOutputKind.Turbo,
        (BladeCommandMappingAction
            {
                Kind: BladeMappingOutputKind.Display,
                Command: BladeMappingCommand.DriverBrightnessDown or BladeMappingCommand.DriverBrightnessUp,
            },
         BladeCommandMappingAction
            {
                Kind: BladeMappingOutputKind.Display,
                Command: BladeMappingCommand.DriverBrightnessStop,
            }) => BladeMappingOutputKind.Display,
        (BladeBacklightMappingAction down, BladeBacklightMappingAction up)
            when down.IsDown && !up.IsDown && down.Command == up.Command => BladeMappingOutputKind.Backlight,
        _ => throw new ArgumentException(
            $"Unsupported MappingEngine output pair: {press.Kind}/{release.Kind}."),
    };

    private static bool IsKeyTap(BladeMultiMappingAction action) => action.Actions is
    [
        BladeKeyboardMappingAction { IsDown: true } down,
        BladeDelayMappingAction { Milliseconds: 10 },
        BladeKeyboardMappingAction { IsDown: false } up,
    ] && down.ScanCode == up.ScanCode && down.Extended == up.Extended;

    private static bool IsBalancedKeyboardSequence(
        BladeMultiMappingAction press,
        BladeMultiMappingAction release)
    {
        var pressed = press.Actions.OfType<BladeKeyboardMappingAction>().ToArray();
        var released = release.Actions.OfType<BladeKeyboardMappingAction>().ToArray();
        return pressed.Length == press.Actions.Count &&
               released.Length == release.Actions.Count &&
               pressed.All(static action => action.IsDown) &&
               released.All(static action => !action.IsDown) &&
               pressed.Select(static action => (action.ScanCode, action.Extended)).Order()
                   .SequenceEqual(released.Select(static action => (action.ScanCode, action.Extended)).Order());
    }

    private static BladeMappingAction ParseAction(JsonObject action)
    {
        var type = GetString(action, "type");
        return type switch
        {
            "keyboard" => ParseKeyboardAction(action),
            "disable" or "disabled" => new BladeDisabledMappingAction(),
            "bladeBattery" => ParseCommandAction(
                action,
                BladeMappingOutputKind.BladeBattery,
                "toggle",
                BladeMappingCommand.Toggle),
            "bladeTrackpad" => ParseCommandAction(
                action,
                BladeMappingOutputKind.BladeTrackpad,
                "toggle",
                BladeMappingCommand.Toggle),
            "bladePerformance" => ParseCommandAction(
                action,
                BladeMappingOutputKind.BladePerformance,
                "nextPerformanceMode",
                BladeMappingCommand.NextPerformanceMode),
            "screenRefresh" => ParseCommandAction(
                action,
                BladeMappingOutputKind.ScreenRefresh,
                "nextRefreshRate",
                BladeMappingCommand.NextRefreshRate),
            "multi" => ParseMultiAction(action),
            "delay" => new BladeDelayMappingAction(GetNonNegativeInt(action, "ms")),
            "turbo" => ParseTurboAction(action),
            "display" => ParseDisplayAction(action),
            "backlight" => ParseBacklightAction(action),
            "gameMode" => new BladeCommandMappingAction(
                BladeMappingOutputKind.GameMode,
                BladeMappingCommand.Toggle),
            "audio" => ParseAudioAction(action),
            "hypershift" => new BladeHyperShiftMappingAction(ParseDownFlag(action)),
            "snapTap" => ParseSnapTapAction(action),
            _ => throw new ArgumentException($"Unsupported MappingEngine output type: {type}."),
        };
    }

    private static BladeKeyboardMappingAction ParseKeyboardAction(JsonObject action)
    {
        var flag = GetFlag(action);
        return new(
            GetRequiredInt(action, "scancode"),
            flag is 0 or 2,
            IsExtendedFlag(flag));
    }

    private static BladeCommandMappingAction ParseCommandAction(
        JsonObject action,
        BladeMappingOutputKind kind,
        string expectedId,
        BladeMappingCommand command)
    {
        var id = GetString(action, "id");
        if (!string.Equals(id, expectedId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid MappingEngine {kind} id: {id}.");
        }

        return new(kind, command);
    }

    private static BladeMultiMappingAction ParseMultiAction(JsonObject action)
    {
        if (action["outputs"] is not JsonArray { Count: > 0 } outputs)
        {
            throw new ArgumentException("MappingEngine multi is missing a non-empty outputs array.");
        }

        return new(outputs.Select(node =>
            node is JsonObject child
                ? ParseAction(child)
                : throw new ArgumentException("MappingEngine multi outputs must be objects.")).ToArray());
    }

    private static BladeTurboMappingAction ParseTurboAction(JsonObject action)
    {
        var guidText = GetString(action, "guid");
        if (!Guid.TryParseExact(guidText, "D", out var id))
        {
            throw new ArgumentException($"Invalid MappingEngine turbo guid: {guidText}.");
        }
        if (!BladeProduct710TurboCatalog.TryGet(id, out _))
        {
            throw new ArgumentException($"Unknown Product 710 turbo guid: {guidText}.");
        }

        return new(
            id,
            ParseDownFlag(action),
            GetOptionalNonNegativeInt(action, "delay"),
            GetOptionalPositiveInt(action, "repeat"));
    }

    private static BladeCommandMappingAction ParseDisplayAction(JsonObject action)
    {
        var id = GetString(action, "id");
        var command = id switch
        {
            "driverBrightnessDown" => BladeMappingCommand.DriverBrightnessDown,
            "driverBrightnessUp" => BladeMappingCommand.DriverBrightnessUp,
            "driverBrightnessStop" => BladeMappingCommand.DriverBrightnessStop,
            _ => throw new ArgumentException($"Invalid MappingEngine display id: {id}."),
        };
        return new(BladeMappingOutputKind.Display, command);
    }

    private static BladeBacklightMappingAction ParseBacklightAction(JsonObject action)
    {
        var name = GetString(action, "name");
        var command = name switch
        {
            "BrightnessDown" => BladeMappingCommand.BrightnessDown,
            "BrightnessUp" => BladeMappingCommand.BrightnessUp,
            _ => throw new ArgumentException($"Invalid MappingEngine backlight name: {name}."),
        };
        return new(command, ParseDownFlag(action));
    }

    private static BladeAudioMappingAction ParseAudioAction(JsonObject action)
    {
        var id = GetString(action, "id");
        if (!string.Equals(id, "mic", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid MappingEngine audio id: {id}.");
        }

        return new(
            BladeMappingCommand.Microphone,
            GetRequiredInt(action, "mute"),
            GetOptionalPositiveInt(action, "repeat"));
    }

    private static BladeSnapTapMappingAction ParseSnapTapAction(JsonObject action)
    {
        var id = GetString(action, "id");
        if (!string.Equals(id, "toggle", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid MappingEngine snapTap id: {id}.");
        }

        return new(BladeMappingCommand.Toggle);
    }

    private static void ValidateAction(BladeMappingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!Enum.IsDefined(action.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(action), "Undefined mapping action type.");
        }

        switch (action)
        {
            case BladeKeyboardMappingAction { ScanCode: < 0 or > ushort.MaxValue }:
                throw new ArgumentOutOfRangeException(nameof(action), "Output scanCode must be in the range 0..65535.");
            case BladeMultiMappingAction { Actions.Count: 0 }:
                throw new ArgumentException("Multi actions cannot be empty.", nameof(action));
            case BladeMultiMappingAction multi:
                foreach (var child in multi.Actions)
                {
                    ValidateAction(child);
                }
                break;
            case BladeCommandMappingAction command when !IsValidCommand(command):
                throw new ArgumentException("The command does not match the mapping action type.", nameof(action));
            case BladeDelayMappingAction { Milliseconds: < 0 }:
                throw new ArgumentOutOfRangeException(nameof(action), "Delay must be non-negative.");
            case BladeTurboMappingAction { Id: var id } when id == Guid.Empty:
                throw new ArgumentException("Turbo guid cannot be empty.", nameof(action));
            case BladeTurboMappingAction { DelayMilliseconds: < 0 }:
                throw new ArgumentOutOfRangeException(nameof(action), "Turbo delay must be non-negative.");
            case BladeTurboMappingAction { Repeat: <= 0 }:
                throw new ArgumentOutOfRangeException(nameof(action), "Turbo repeat must be positive.");
            case BladeAudioMappingAction { Mute: < 0 }:
                throw new ArgumentOutOfRangeException(nameof(action), "Audio mute must be non-negative.");
            case BladeAudioMappingAction { Repeat: <= 0 }:
                throw new ArgumentOutOfRangeException(nameof(action), "Audio repeat must be positive.");
            case BladeBacklightMappingAction { Command: not (
                BladeMappingCommand.BrightnessDown or BladeMappingCommand.BrightnessUp) }:
                throw new ArgumentException("Invalid backlight command.", nameof(action));
            case BladeAudioMappingAction { Command: not BladeMappingCommand.Microphone }:
                throw new ArgumentException("Invalid audio command.", nameof(action));
            case BladeSnapTapMappingAction { Command: not BladeMappingCommand.Toggle }:
                throw new ArgumentException("Invalid Snap Tap command.", nameof(action));
            case not (BladeKeyboardMappingAction or
                      BladeDisabledMappingAction or
                      BladeCommandMappingAction or
                      BladeMultiMappingAction or
                      BladeDelayMappingAction or
                      BladeTurboMappingAction or
                      BladeBacklightMappingAction or
                      BladeAudioMappingAction or
                      BladeHyperShiftMappingAction or
                      BladeSnapTapMappingAction):
                throw new ArgumentException("Unknown mapping action implementation.", nameof(action));
        }
    }

    private static bool IsValidCommand(BladeCommandMappingAction action) => action switch
    {
        { Kind: BladeMappingOutputKind.BladeBattery or
                BladeMappingOutputKind.BladeTrackpad or
                BladeMappingOutputKind.GameMode,
          Command: BladeMappingCommand.Toggle } => true,
        { Kind: BladeMappingOutputKind.BladePerformance,
          Command: BladeMappingCommand.NextPerformanceMode } => true,
        { Kind: BladeMappingOutputKind.ScreenRefresh,
          Command: BladeMappingCommand.NextRefreshRate } => true,
        { Kind: BladeMappingOutputKind.Display,
          Command: BladeMappingCommand.DriverBrightnessDown or
                   BladeMappingCommand.DriverBrightnessUp or
                   BladeMappingCommand.DriverBrightnessStop } => true,
        _ => false,
    };

    private static int GetFlag(JsonObject value)
    {
        var flag = GetRequiredInt(value, "flag");
        return flag is 0 or 1 or 2 or 3
            ? flag
            : throw new ArgumentException($"Invalid MappingEngine flag: {flag}.");
    }

    private static bool ParseDownFlag(JsonObject value)
    {
        var flag = GetFlag(value);
        return flag switch
        {
            0 => true,
            1 => false,
            _ => throw new ArgumentException($"Invalid MappingEngine action flag: {flag}."),
        };
    }

    private static int GetNonNegativeInt(JsonObject value, string name)
    {
        var result = GetRequiredInt(value, name);
        return result >= 0
            ? result
            : throw new ArgumentException($"MappingEngine {name} must be non-negative.");
    }

    private static int? GetOptionalNonNegativeInt(JsonObject value, string name) =>
        value[name] is null ? null : GetNonNegativeInt(value, name);

    private static int? GetOptionalPositiveInt(JsonObject value, string name)
    {
        if (value[name] is null)
        {
            return null;
        }

        var result = GetRequiredInt(value, name);
        return result > 0
            ? result
            : throw new ArgumentException($"MappingEngine {name} must be positive.");
    }

    private static RuleKey ParseInputKey(JsonObject input, out int flag)
    {
        var type = GetString(input, "type");
        var kind = type switch
        {
            "keyboard" => BladeMappingInputKind.Keyboard,
            "razerKey" => BladeMappingInputKind.RazerKey,
            _ => throw new ArgumentException($"Unsupported MappingEngine input type: {type}."),
        };
        var code = kind == BladeMappingInputKind.Keyboard
            ? GetRequiredInt(input, "scancode")
            : GetRequiredInt(input, "key");
        flag = GetFlag(input);
        if (kind == BladeMappingInputKind.RazerKey && flag is 2 or 3)
        {
            throw new ArgumentException("Product 710 RazerKey does not support the extended flag.", nameof(input));
        }
        return new(kind, code, GetOptionalBool(input, "hypershift"), IsExtendedFlag(flag));
    }

    private static bool RequiresAppExecutor(BladeMappingOutputKind kind) => kind is
        BladeMappingOutputKind.BladeBattery or
        BladeMappingOutputKind.BladeTrackpad or
        BladeMappingOutputKind.BladePerformance or
        BladeMappingOutputKind.ScreenRefresh or
        BladeMappingOutputKind.Multi or
        BladeMappingOutputKind.Delay or
        BladeMappingOutputKind.Turbo or
        BladeMappingOutputKind.Display or
        BladeMappingOutputKind.Backlight or
        BladeMappingOutputKind.GameMode or
        BladeMappingOutputKind.Audio;

    private static bool IsExtendedFlag(int flag) => flag is 2 or 3;

    private static string GetString(JsonObject value, string name) =>
        value[name]?.GetValue<string>()
            ?? throw new ArgumentException($"MappingEngine object is missing string field {name}.");

    private static int GetRequiredInt(JsonObject value, string name)
    {
        if (value[name] is JsonValue json)
        {
            if (json.TryGetValue<int>(out var integer))
            {
                return integer;
            }
            if (json.TryGetValue<byte>(out var unsignedByte))
            {
                return unsignedByte;
            }
            if (json.TryGetValue<ushort>(out var unsignedShort))
            {
                return unsignedShort;
            }
            if (json.TryGetValue<long>(out var longValue) &&
                longValue is >= int.MinValue and <= int.MaxValue)
            {
                return (int)longValue;
            }
        }

        throw new ArgumentException($"MappingEngine object is missing valid integer field {name}.");
    }

    private static int? GetOptionalInt(JsonObject value, string name) =>
        value[name] is null ? null : GetRequiredInt(value, name);

    private static bool GetOptionalBool(JsonObject value, string name) =>
        value[name]?.GetValue<bool>() ?? false;

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
