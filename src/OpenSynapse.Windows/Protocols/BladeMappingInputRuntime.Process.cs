using System.Text.Json.Nodes;

namespace OpenSynapse.Windows.Protocols;

public sealed partial class BladeMappingInputRuntime
{
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

    public IReadOnlyList<BladeMappingOutputEvent> Process(BladeMappingInputEvent input)
        => ProcessCore(input, false, out _, out _);

    internal IReadOnlyList<BladeMappingOutputEvent> Process(
        BladeMappingInputEvent input,
        out BladeMappingAction? action)
        => Process(input, out action, out _);

    internal IReadOnlyList<BladeMappingOutputEvent> Process(
        BladeMappingInputEvent input,
        out BladeMappingAction? action,
        out bool passThrough)
        => ProcessCore(input, true, out action, out passThrough);

    private IReadOnlyList<BladeMappingOutputEvent> ProcessCore(
        BladeMappingInputEvent input,
        bool allowAppAction,
        out BladeMappingAction? action,
        out bool passThrough)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        action = null;
        passThrough = false;
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
                _passThroughInputs.Add(inputKey);
                passThrough = true;
            }

        }
        else
        {
            if (!_activeRules.TryGetValue(inputKey, out var activeRule))
            {
                return [];
            }

            rule = activeRule;
            passThrough = _passThroughInputs.Contains(inputKey);
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
            _passThroughInputs.Remove(inputKey);
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

    public void SetGameModeEnabled(bool enabled) => _gameModeEnabled = enabled;

    /// <summary>
    /// Synapse adds scancode 91 (left Windows) to the driver mapping only while
    /// game mode is active. The hardware mode byte alone drives the indicator.
    /// </summary>
    public void AddGameModeWindowsKeySuppression()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var scanCode in new[] { 0x5B, 0x5C })
        {
            var key = new RuleKey(BladeMappingInputKind.Keyboard, scanCode, false, true);
            var rule = new BladeMappingRule(
                BladeMappingInputKind.Keyboard,
                scanCode,
                HyperShiftLayer: false,
                BladeMappingOutputKind.Disable,
                OutputCode: 0,
                PressAction: new BladeDisabledMappingAction(),
                ReleaseAction: new BladeDisabledMappingAction(),
                InputExtended: true,
                GameModeOnly: true);
            Validate(rule);
            _rules.TryAdd(key, rule);
        }
    }

    private BladeMappingRule? ResolveRule(BladeMappingInputEvent input)
    {
        var key = new RuleKey(input.Kind, input.Code, _hyperShift, input.Extended);
        if (!_rules.TryGetValue(key, out var rule))
        {
            return null;
        }

        return rule.GameModeOnly && !_gameModeEnabled ? null : rule;
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
}
