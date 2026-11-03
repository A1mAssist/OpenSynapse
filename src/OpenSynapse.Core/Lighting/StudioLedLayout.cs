using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

/// <summary>One LED in both matrix and device coordinates.</summary>
public readonly record struct StudioLedMap(int MatrixRow, int MatrixCol, int DeviceRow, int DeviceCol);

/// <summary>Keyboard input that triggers one matrix LED.</summary>
public readonly record struct StudioInputMap(
    string InputType,
    int InputCode,
    int InputModifier,
    int MatrixRow,
    int MatrixCol);

/// <summary>
/// Parses the Chroma DeviceManifest "ledConfig" object. SVG paths use matrix
/// coordinates; the hardware frame uses device coordinates.
/// </summary>
public sealed class StudioLedLayout
{
    private readonly Dictionary<(int Row, int Col), StudioLedMap> _byMatrix = new();
    private readonly Dictionary<(string Type, int Code), StudioInputMap> _byInput = new();

    private StudioLedLayout(
        int matrixRows,
        int matrixColumns,
        int deviceRows,
        int deviceColumns,
        int maxRegion,
        IReadOnlyList<StudioLedMap> leds,
        IReadOnlyList<StudioInputMap> inputs)
    {
        MatrixRows = matrixRows;
        MatrixColumns = matrixColumns;
        DeviceRows = deviceRows;
        DeviceColumns = deviceColumns;
        MaxRegion = maxRegion;
        Leds = leds;
        Inputs = inputs;
        foreach (var led in leds) _byMatrix[(led.MatrixRow, led.MatrixCol)] = led;
        foreach (var input in inputs) _byInput[(input.InputType, input.InputCode)] = input;
    }

    public int MatrixRows { get; }

    public int MatrixColumns { get; }

    public int DeviceRows { get; }

    public int DeviceColumns { get; }

    public int MaxRegion { get; }

    public IReadOnlyList<StudioLedMap> Leds { get; }

    public IReadOnlyList<StudioInputMap> Inputs { get; }

    public static StudioLedLayout Parse(string ledConfigJson, int editionId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledConfigJson);
        using var document = JsonDocument.Parse(ledConfigJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The ledConfig payload is not an object.");
        }

        var config = SelectConfig(root, editionId);
        // These fields are counts, not max indices: Blade reports 7x16 with
        // matrix rows 0..6 and columns 0..15, and 102 device columns 0..101.
        var matrixRows = ReadInt(config, "MatrixMaxRow");
        var matrixColumns = ReadInt(config, "MatrixMaxCol");
        var deviceRows = ReadInt(config, "DeviceMaxRow");
        var deviceColumns = ReadInt(config, "DeviceMaxCol");
        var maxRegion = ReadInt(config, "MaxRegion");

        var leds = new List<StudioLedMap>();
        if (config.TryGetProperty("LedMatrix", out var matrix) && matrix.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in matrix.EnumerateArray())
            {
                if (!item.TryGetProperty("MatrixPos", out var matrixPos) || matrixPos.GetArrayLength() < 2) continue;
                if (!item.TryGetProperty("DevicePos", out var devicePos) || devicePos.GetArrayLength() < 2) continue;
                leds.Add(new StudioLedMap(
                    matrixPos[0].GetInt32(),
                    matrixPos[1].GetInt32(),
                    devicePos[0].GetInt32(),
                    devicePos[1].GetInt32()));
            }
        }

        var inputs = new List<StudioInputMap>();
        if (config.TryGetProperty("LedInputMap", out var inputMap) && inputMap.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in inputMap.EnumerateArray())
            {
                if (!item.TryGetProperty("MatrixPos", out var matrixPos) || matrixPos.GetArrayLength() < 2) continue;
                var type = item.TryGetProperty("InputType", out var typeValue)
                    ? typeValue.ToString()
                    : "unknown";
                var (code, modifier) = ReadInputData(item, "InputData");
                inputs.Add(new StudioInputMap(
                    type,
                    code,
                    modifier,
                    matrixPos[0].GetInt32(),
                    matrixPos[1].GetInt32()));
            }
        }

        return new StudioLedLayout(matrixRows, matrixColumns, deviceRows, deviceColumns, maxRegion, leds, inputs);
    }

    public StudioLedMap? FindMatrix(int row, int col) =>
        _byMatrix.TryGetValue((row, col), out var led) ? led : null;

    public StudioInputMap? FindInput(string inputType, int code) =>
        _byInput.TryGetValue((inputType, code), out var input) ? input : null;

    public (int Row, int Col)? ToDevice(int matrixRow, int matrixCol) =>
        FindMatrix(matrixRow, matrixCol) is { } led ? (led.DeviceRow, led.DeviceCol) : null;

    private static JsonElement SelectConfig(JsonElement root, int editionId)
    {
        var preferred = $"ledConfig_{editionId}_1";
        if (root.TryGetProperty(preferred, out var exact)) return exact;

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.StartsWith($"ledConfig_{editionId}_", StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.StartsWith("ledConfig_", StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return root;
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static (int Code, int Modifier) ReadInputData(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return (0, 0);
        if (value.ValueKind == JsonValueKind.Array)
        {
            var code = value.GetArrayLength() > 0 && value[0].TryGetInt32(out var first) ? first : 0;
            var modifier = value.GetArrayLength() > 1 && value[1].TryGetInt32(out var second) ? second : 0;
            return (code, modifier);
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return (number, 0);
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return (parsed, 0);
        return (0, 0);
    }
}
