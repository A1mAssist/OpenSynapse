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
}

public readonly record struct BladeMappingRule(
    BladeMappingInputKind InputKind,
    int InputCode,
    bool HyperShiftLayer,
    BladeMappingOutputKind OutputKind,
    int OutputCode,
    int? SnapTapId = null,
    bool OutputExtended = false);

public readonly record struct BladeMappingInputEvent(
    BladeMappingInputKind Kind,
    int Code,
    bool IsDown);

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
    private readonly Dictionary<int, Dictionary<InputKey, long>> _snapTapPressed = [];
    private readonly Dictionary<int, InputKey> _snapTapOwners = [];
    private readonly Dictionary<OutputKey, int> _pressedOutputs = [];
    private long _sequence;
    private bool _hyperShift;
    private bool _snapTapEnabled;
    private bool _disposed;

    public static BladeMappingInputRuntime FromGraph(JsonObject graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph["mappings"] is not JsonArray mappings)
        {
            throw new ArgumentException("MappingEngine graph 缺少 mappings 数组。", nameof(graph));
        }

        var builders = new Dictionary<RuleKey, RuleBuilder>();
        foreach (var node in mappings)
        {
            if (node is not JsonObject mapping ||
                mapping["input"] is not JsonObject input ||
                mapping["output"] is not JsonObject output)
            {
                throw new ArgumentException("MappingEngine mapping 必须包含 input/output 对象。", nameof(graph));
            }

            var inputKey = ParseInputKey(input, out var flag);
            var builder = builders.GetValueOrDefault(inputKey);
            if (flag is 0 or 2)
            {
                if (builder.Press is not null)
                {
                    throw new ArgumentException("MappingEngine graph 包含重复的按下映射。", nameof(graph));
                }

                builders[inputKey] = new RuleBuilder(input, output, null);
            }
            else if (flag is 1 or 3)
            {
                if (builder.Press is null || builder.Release is not null)
                {
                    throw new ArgumentException("MappingEngine graph 的释放映射没有唯一的按下映射。", nameof(graph));
                }

                builders[inputKey] = builder with { Release = output };
            }
            else
            {
                throw new ArgumentException($"MappingEngine input flag 无效：{flag}。", nameof(graph));
            }
        }

        var rules = new List<BladeMappingRule>(builders.Count);
        foreach (var (key, builder) in builders)
        {
            if (builder.Press is null || builder.Release is null)
            {
                throw new ArgumentException(
                    $"MappingEngine graph 缺少 {key.Kind}:{key.Code} 的 press/release 对。",
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
            var key = new RuleKey(rule.InputKind, rule.InputCode, rule.HyperShiftLayer);
            if (!_rules.TryAdd(key, rule))
            {
                throw new ArgumentException(
                    $"重复的 Blade 映射输入 {rule.InputKind}:{rule.InputCode}（HyperShift={rule.HyperShiftLayer}）。",
                    nameof(rules));
            }
        }
    }

    public bool HyperShiftEnabled => _hyperShift;
    public bool SnapTapEnabled => _snapTapEnabled;

    public IReadOnlyList<BladeMappingOutputEvent> Process(BladeMappingInputEvent input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (input.Code < 0 || input.Code > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        var inputKey = new InputKey(input.Kind, input.Code);
        BladeMappingRule? rule;
        if (input.IsDown)
        {
            rule = ResolveRule(input);
            if (rule is null)
            {
                return [];
            }

            _activeRules[inputKey] = rule.Value;
        }
        else
        {
            if (!_activeRules.TryGetValue(inputKey, out var activeRule))
            {
                return [];
            }

            rule = activeRule;
            _activeRules.Remove(inputKey);
        }

        return rule.Value.SnapTapId is int snapTapId && _snapTapEnabled
            ? ProcessSnapTap(inputKey, rule.Value, input.IsDown, snapTapId)
            : ProcessOutput(rule.Value, input.IsDown);
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
            foreach (var (group, owner) in _snapTapOwners.ToArray())
            {
                if (_activeRules.TryGetValue(owner, out var ownerRule))
                {
                    output.AddRange(EmitKeyboard(
                        ownerRule.OutputCode,
                        false,
                        ownerRule.OutputExtended));
                }
            }

            _snapTapPressed.Clear();
            _snapTapOwners.Clear();
            return output;
        }

        return [];
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
        _snapTapPressed.Clear();
        _snapTapOwners.Clear();
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
        var key = new RuleKey(input.Kind, input.Code, _hyperShift);
        return _rules.TryGetValue(key, out var rule)
            ? rule
            : null;
    }

    private IReadOnlyList<BladeMappingOutputEvent> ProcessOutput(
        BladeMappingRule rule,
        bool isDown)
    {
        switch (rule.OutputKind)
        {
            case BladeMappingOutputKind.Keyboard:
                return EmitKeyboard(rule.OutputCode, isDown, rule.OutputExtended);
            case BladeMappingOutputKind.HyperShift:
                _hyperShift = isDown;
                return [];
            case BladeMappingOutputKind.SnapTapToggle when !isDown:
                return SetSnapTapEnabled(!_snapTapEnabled);
            case BladeMappingOutputKind.Disable:
                return [];
            default:
                return [];
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
            pressed[inputKey] = ++_sequence;
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
            throw new InvalidOperationException($"映射输出 scanCode 无效：{scanCode}。");
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
        if (rule.InputCode is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "输入代码必须是 0..65535。");
        }
        if (rule.OutputKind == BladeMappingOutputKind.Keyboard &&
            rule.OutputCode is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "输出 scanCode 必须是 0..65535。");
        }
        if (rule.SnapTapId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "Snap Tap id 必须为正数。");
        }
        if (rule.OutputKind != BladeMappingOutputKind.Keyboard && rule.SnapTapId is not null)
        {
            throw new ArgumentException("只有键盘输出可以携带 Snap Tap id。", nameof(rule));
        }
    }

    private static BladeMappingRule ParseRule(RuleKey key, RuleBuilder builder)
    {
        var press = builder.Press!;
        var release = builder.Release!;
        var pressType = GetString(press, "type");
        var releaseType = GetString(release, "type");
        var snapTapId = GetOptionalInt(builder.Input, "snaptapId");
        if (pressType == "keyboard" && releaseType == "keyboard")
        {
            var outputCode = GetRequiredInt(press, "scancode");
            var releaseCode = GetRequiredInt(release, "scancode");
            if (outputCode != releaseCode)
            {
                throw new ArgumentException("MappingEngine press/release 输出 scanCode 不一致。");
            }

            return new(
                key.Kind,
                key.Code,
                key.HyperShift,
                BladeMappingOutputKind.Keyboard,
                outputCode,
                snapTapId,
                IsExtendedFlag(GetRequiredInt(press, "flag")));
        }

        if (pressType == "hypershift" && releaseType == "hypershift")
        {
            return new(key.Kind, key.Code, key.HyperShift, BladeMappingOutputKind.HyperShift, 0);
        }

        if (pressType == "disable" &&
            releaseType == "snapTap" &&
            GetString(release, "id") == "toggle")
        {
            return new(key.Kind, key.Code, key.HyperShift, BladeMappingOutputKind.SnapTapToggle, 0);
        }

        throw new ArgumentException(
            $"不支持的 MappingEngine 输出配对：{pressType}/{releaseType}。");
    }

    private static RuleKey ParseInputKey(JsonObject input, out int flag)
    {
        var type = GetString(input, "type");
        var kind = type switch
        {
            "keyboard" => BladeMappingInputKind.Keyboard,
            "razerKey" => BladeMappingInputKind.RazerKey,
            _ => throw new ArgumentException($"不支持的 MappingEngine 输入类型：{type}。"),
        };
        var code = kind == BladeMappingInputKind.Keyboard
            ? GetRequiredInt(input, "scancode")
            : GetRequiredInt(input, "key");
        flag = GetRequiredInt(input, "flag");
        return new(kind, code, GetOptionalBool(input, "hypershift"));
    }

    private static bool IsExtendedFlag(int flag) => flag is 2 or 3;

    private static string GetString(JsonObject value, string name) =>
        value[name]?.GetValue<string>()
            ?? throw new ArgumentException($"MappingEngine 对象缺少字符串字段 {name}。");

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

        throw new ArgumentException($"MappingEngine 对象缺少有效整数字段 {name}。");
    }

    private static int? GetOptionalInt(JsonObject value, string name) =>
        value[name] is null ? null : GetRequiredInt(value, name);

    private static bool GetOptionalBool(JsonObject value, string name) =>
        value[name]?.GetValue<bool>() ?? false;

    private readonly record struct RuleKey(
        BladeMappingInputKind Kind,
        int Code,
        bool HyperShift);

    private readonly record struct InputKey(
        BladeMappingInputKind Kind,
        int Code);

    private readonly record struct OutputKey(
        int ScanCode,
        bool Extended);

    private readonly record struct RuleBuilder(
        JsonObject Input,
        JsonObject? Press,
        JsonObject? Release);
}
