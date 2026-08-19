using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Pure builders for the Product 710 host-side MappingEngine JSON contract.
/// These records are not Blade HID feature reports and require a separate input runtime.
/// </summary>
public static class BladeMappingEngineProtocol
{
    public const int BladeProduct710Id = 710;
    public const int BladeVendorId = 0x1532;
    public const string DefaultBladeContainerId = "{00000000-0000-0000-FFFF-FFFFFFFFFFFF}";
    public const byte HyperShiftKey = 0x03;
    public const byte ApplicationKey = 0xD2;
    public const byte PerformanceKey = 0xD3;
    public const byte MicrophoneMuteKey = 0xD4;
    public const byte FunctionKeyLeft = 0x0A;
    public const byte FunctionKeyRight = 0x0B;
    public const int DefaultSnapTapId = 1;
    public const string Product710DefaultGraphHash = "2065e3ae21d8533d1af7d9b41142ceed";
    public const string EmptyGraphHash = "de2ccb2014607a84df08306567cd96f0";
    private static readonly byte[] RequiredDefaultKeyboardInputs =
        [0x30, 0x14, 0x19, 0x13, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58];
    private static readonly byte[] RequiredDefaultRazerKeyInputs =
        [0x03, 0xD2, 0xD3, 0xD4, FunctionKeyLeft, FunctionKeyRight];

    public static void ValidateCompleteProduct710Storage(string storageValueJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageValueJson);
        try
        {
            var storage = JsonNode.Parse(storageValueJson)?.AsObject()
                ?? throw new ArgumentException("Product 710 存储根节点必须是对象。", nameof(storageValueJson));
            if (storage["productId"]?.GetValue<int>() != BladeProduct710Id ||
                storage["reportIDs"] is not JsonObject reportIds ||
                reportIds["4"]?.GetValue<string>() != "razerKeyReportID" ||
                reportIds["5"]?.GetValue<string>() != "hardwareEventReportID" ||
                storage["profiles"] is not JsonArray profiles ||
                profiles.Count == 0 ||
                storage["defaultMappings"]?["appEngine"] is not JsonObject graph ||
                graph["mappings"] is not JsonArray mappings ||
                mappings.Count < 64)
            {
                throw new ArgumentException(
                    "Product 710 存储缺少完整的官方默认 Fn/HyperShift 映射。",
                    nameof(storageValueJson));
            }

            var hash = graph["hash"]?.GetValue<string>();
            if (!string.Equals(hash, Product710DefaultGraphHash, StringComparison.Ordinal) ||
                !string.Equals(hash, ComputeGraphHash(graph), StringComparison.Ordinal))
            {
                throw new ArgumentException("Product 710 默认映射 hash 不是已验证的官方版本。", nameof(storageValueJson));
            }

            foreach (var profile in profiles)
            {
                if (profile?["appEngine"] is not JsonObject profileGraph ||
                    profileGraph["mappings"] is not JsonArray profileMappings ||
                    profileMappings.Count != 0 ||
                    profileGraph["hash"]?.GetValue<string>() != EmptyGraphHash ||
                    ComputeGraphHash(profileGraph) != EmptyGraphHash)
                {
                    throw new ArgumentException(
                        "Product 710 活动 Profile 含有未验证的自定义映射。",
                        nameof(storageValueJson));
                }
            }

            foreach (var scanCode in RequiredDefaultKeyboardInputs)
            {
                if (!HasInput(mappings, "keyboard", "scancode", scanCode))
                {
                    throw new ArgumentException(
                        $"Product 710 默认映射缺少 Fn 输入 scanCode 0x{scanCode:X2}。",
                        nameof(storageValueJson));
                }
            }
            foreach (var key in RequiredDefaultRazerKeyInputs)
            {
                if (!HasInput(mappings, "razerKey", "key", key))
                {
                    throw new ArgumentException(
                        $"Product 710 默认映射缺少 RazerKey 0x{key:X2}。",
                        nameof(storageValueJson));
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new ArgumentException("Product 710 存储格式无效。", nameof(storageValueJson), exception);
        }
    }

    /// <summary>
    /// Builds the minimum device identity accepted by the verified Product 710
    /// MappingEngine ABI. The container id is the stable device-local storage
    /// namespace; the guid identifies this native registration instance.
    /// </summary>
    public static string CreateBladeMediaMappingDeviceInfoJson(
        Guid? guid = null,
        string containerId = DefaultBladeContainerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        return new JsonObject
        {
            ["vendorId"] = BladeVendorId,
            ["containerId"] = containerId,
            ["productId"] = BladeProduct710Id,
            ["guid"] = (guid ?? Guid.NewGuid()).ToString("D"),
        }.ToJsonString();
    }

    public static JsonArray CreateKeyboardToKeyboardPair(
        byte inputScanCode,
        byte inputMakeFlag,
        bool hyperShiftLayer,
        byte outputScanCode,
        byte outputMakeFlag,
        ushort? inputModifiers = null)
    {
        return CreatePair(
            CreateKeyboardInput(inputScanCode, inputMakeFlag, hyperShiftLayer, inputModifiers),
            CreateKeyboardInput(inputScanCode, ReleaseFlag(inputMakeFlag), hyperShiftLayer, inputModifiers),
            CreateKeyboardOutput(outputScanCode, outputMakeFlag),
            CreateKeyboardOutput(outputScanCode, ReleaseFlag(outputMakeFlag)));
    }

    public static JsonArray CreateRazerKeyToKeyboardPair(
        byte razerKey,
        bool hyperShiftLayer,
        byte outputScanCode,
        byte outputMakeFlag)
    {
        return CreatePair(
            CreateRazerKeyInput(razerKey, hyperShiftLayer, 0),
            CreateRazerKeyInput(razerKey, hyperShiftLayer, 1),
            CreateKeyboardOutput(outputScanCode, outputMakeFlag),
            CreateKeyboardOutput(outputScanCode, ReleaseFlag(outputMakeFlag)));
    }

    public static JsonArray CreateRazerKeyHyperShiftPair(
        byte razerKey = HyperShiftKey,
        bool hyperShiftLayer = false)
    {
        return CreatePair(
            CreateRazerKeyInput(razerKey, hyperShiftLayer, 0),
            CreateRazerKeyInput(razerKey, hyperShiftLayer, 1),
            new JsonObject { ["type"] = "hypershift", ["flag"] = 0 },
            new JsonObject { ["type"] = "hypershift", ["flag"] = 1 });
    }

    public static JsonArray CreateRazerKeyMicrophoneMutePair(
        byte razerKey = MicrophoneMuteKey)
    {
        return CreatePair(
            CreateRazerKeyInput(razerKey, hyperShiftLayer: false, flag: 0),
            CreateRazerKeyInput(razerKey, hyperShiftLayer: false, flag: 1),
            new JsonObject
            {
                ["type"] = "audio",
                ["id"] = "mic",
                ["mute"] = 2,
                ["repeat"] = 1,
            },
            new JsonObject { ["type"] = "disabled" });
    }

    public static JsonArray CreateSnapTapKeyboardPassthroughPair(
        byte scanCode,
        byte makeFlag,
        int snapTapId = DefaultSnapTapId,
        bool analogInput = false)
    {
        if (snapTapId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(snapTapId));
        }

        var inputType = analogInput ? "analogKey" : "keyboard";
        return CreatePair(
            new JsonObject
            {
                ["type"] = inputType,
                ["scancode"] = scanCode,
                ["flag"] = makeFlag,
                ["snaptapId"] = snapTapId,
            },
            new JsonObject
            {
                ["type"] = inputType,
                ["scancode"] = scanCode,
                ["flag"] = ReleaseFlag(makeFlag),
                ["snaptapId"] = snapTapId,
            },
            CreateKeyboardOutput(scanCode, makeFlag),
            CreateKeyboardOutput(scanCode, ReleaseFlag(makeFlag)));
    }

    public static JsonArray CreateSnapTapTogglePair(
        byte scanCode,
        byte makeFlag,
        bool hyperShiftLayer)
    {
        return CreatePair(
            CreateKeyboardInput(scanCode, makeFlag, hyperShiftLayer, null),
            CreateKeyboardInput(scanCode, ReleaseFlag(makeFlag), hyperShiftLayer, null),
            new JsonObject { ["type"] = "disable" },
            new JsonObject { ["type"] = "snapTap", ["id"] = "toggle" });
    }

    public static string ComputeGraphHash(JsonObject graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        using var stream = new MemoryStream();
        using (var writer = CreateCanonicalWriter(stream))
        {
            WriteCanonical(writer, graph, isRoot: true);
        }

        var canonicalJson = Encoding.UTF8.GetString(stream.ToArray())
            .Replace("<", "\\u003C", StringComparison.Ordinal);
        var digest = MD5.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexStringLower(digest);
    }

    public static bool IsGameModeToggleInput(string inputJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputJson);
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            var input = document.RootElement;
            return input.ValueKind == JsonValueKind.Object &&
                input.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                type.GetString() == "razerKey" &&
                input.TryGetProperty("key", out var key) &&
                key.TryGetInt32(out var keyValue) &&
                keyValue == 0x03 &&
                input.TryGetProperty("flag", out var flag) &&
                flag.TryGetInt32(out var flagValue) &&
                flagValue == 0 &&
                (!input.TryGetProperty("hypershift", out var hyperShift) ||
                    hyperShift.ValueKind == JsonValueKind.False);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string SerializeCanonicalForHash(JsonObject graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        using var stream = new MemoryStream();
        using (var writer = CreateCanonicalWriter(stream))
        {
            WriteCanonical(writer, graph, isRoot: true);
        }

        return Encoding.UTF8.GetString(stream.ToArray())
            .Replace("<", "\\u003C", StringComparison.Ordinal);
    }

    private static JsonArray CreatePair(
        JsonObject downInput,
        JsonObject upInput,
        JsonObject downOutput,
        JsonObject upOutput)
    {
        return new JsonArray
        {
            new JsonObject { ["input"] = downInput, ["output"] = downOutput },
            new JsonObject { ["input"] = upInput, ["output"] = upOutput },
        };
    }

    private static bool HasInput(JsonArray mappings, string type, string valueName, byte value) =>
        mappings.Any(mapping =>
            mapping?["input"] is JsonObject input &&
            input["type"]?.GetValue<string>() == type &&
            input[valueName]?.GetValue<int>() == value);

    private static JsonObject CreateKeyboardInput(
        byte scanCode,
        byte flag,
        bool hyperShiftLayer,
        ushort? modifiers)
    {
        var input = new JsonObject
        {
            ["type"] = "keyboard",
            ["scancode"] = scanCode,
            ["hypershift"] = hyperShiftLayer,
            ["flag"] = flag,
        };
        if (modifiers is not null)
        {
            input["modifiers"] = modifiers.Value;
        }

        return input;
    }

    private static JsonObject CreateRazerKeyInput(byte razerKey, bool hyperShiftLayer, byte flag) =>
        new()
        {
            ["type"] = "razerKey",
            ["key"] = razerKey,
            ["hypershift"] = hyperShiftLayer,
            ["flag"] = flag,
        };

    private static JsonObject CreateKeyboardOutput(byte scanCode, byte flag) =>
        new() { ["type"] = "keyboard", ["scancode"] = scanCode, ["flag"] = flag };

    private static byte ReleaseFlag(byte makeFlag)
    {
        if (makeFlag == byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(makeFlag));
        }

        return (byte)(makeFlag + 1);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node, bool isRoot = false)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject jsonObject:
                writer.WriteStartObject();
                foreach (var property in jsonObject.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    if (isRoot && property.Key is "hash" or "gamemode")
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonArray jsonArray:
                writer.WriteStartArray();
                foreach (var item in jsonArray)
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static Utf8JsonWriter CreateCanonicalWriter(Stream stream) =>
        new(
            stream,
            new JsonWriterOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
}
