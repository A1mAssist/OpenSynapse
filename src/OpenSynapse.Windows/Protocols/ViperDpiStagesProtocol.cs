namespace OpenSynapse.Windows.Protocols;

public sealed record ViperDpiStage(byte Number, int X, int Y);

public sealed record ViperDpiStagesState(
    byte ActiveStage,
    IReadOnlyList<ViperDpiStage> Stages);

/// <summary>
/// Parser for the source-backed Viper 00B8 persistent DPI-stage GET response.
/// The source-backed SET builder remains hardware-validation-only until write/readback/restore succeeds.
/// </summary>
public static class ViperDpiStagesProtocol
{
    private const byte TransactionId = 0x1F;
    private const byte DataSize = 0x26;
    private const byte CommandClass = 0x04;
    private const byte CommandId = 0x86;
    private const byte VariableStorage = 0x01;
    private const int MaximumStages = 5;
    private const int StageSize = 7;

    public static ViperDpiStagesState Parse(ReadOnlySpan<byte> response) =>
        Parse(response, RazerFeatureReport.CreateRequest(
            TransactionId, DataSize, CommandClass, CommandId, new byte[] { VariableStorage }));

    internal static ViperDpiStagesState Parse(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, DataSize))
        {
            throw new InvalidOperationException("Viper DPI stages returned an invalid or out-of-order feature report.");
        }

        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != VariableStorage)
        {
            throw new InvalidOperationException($"Viper DPI stages returned an incorrect storage ID: 0x{arguments[0]:X2}.");
        }

        var activeStage = arguments[1];
        var count = arguments[2];
        if (count is < 1 or > MaximumStages)
        {
            throw new InvalidOperationException($"Viper returned an invalid DPI stage count: {count}.");
        }
        if (activeStage is < 1 || activeStage > count)
        {
            throw new InvalidOperationException($"Viper returned an invalid active DPI stage: {activeStage}/{count}.");
        }

        var stages = new ViperDpiStage[count];
        var rawNumberBase = arguments[3];
        if (rawNumberBase is not (0x00 or 0x01))
        {
            throw new InvalidOperationException($"Viper returned an unknown DPI stage number base: {rawNumberBase}.");
        }
        for (var index = 0; index < count; index++)
        {
            var offset = 3 + (index * StageSize);
            var expectedRawNumber = (byte)(rawNumberBase + index);
            var number = (byte)(index + 1);
            if (arguments[offset] != expectedRawNumber)
            {
                throw new InvalidOperationException(
                    $"Viper DPI stage numbers are not contiguous: expected {expectedRawNumber}, received {arguments[offset]}.");
            }
            if (arguments[offset + 5] != 0x00 || arguments[offset + 6] != 0x00)
            {
                throw new InvalidOperationException($"Viper DPI stage {number} has a nonzero reserved field.");
            }

            var x = (arguments[offset + 1] << 8) | arguments[offset + 2];
            var y = (arguments[offset + 3] << 8) | arguments[offset + 4];
            if (!IsValidDpi(x) || !IsValidDpi(y))
            {
                throw new InvalidOperationException(
                    $"Viper DPI stage {number} is outside 100..30000 with a step of 50: {x} x {y}.");
            }

            stages[index] = new ViperDpiStage(number, x, y);
        }

        return new ViperDpiStagesState(activeStage, stages);
    }

    public static string Format(ViperDpiStagesState state) =>
        $"Active {state.ActiveStage}/{state.Stages.Count}: " +
        string.Join(", ", state.Stages.Select(stage => $"{stage.Number}={stage.X}x{stage.Y}"));

    private static bool IsValidDpi(int value) => value is >= 100 and <= 30000 && value % 50 == 0;
}
