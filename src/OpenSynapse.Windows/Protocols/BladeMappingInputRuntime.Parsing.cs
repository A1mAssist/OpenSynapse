using System.Text.Json.Nodes;

namespace OpenSynapse.Windows.Protocols;

public sealed partial class BladeMappingInputRuntime
{
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
}
