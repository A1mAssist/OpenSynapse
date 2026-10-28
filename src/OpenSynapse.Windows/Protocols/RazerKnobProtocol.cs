namespace OpenSynapse.Windows.Protocols;

/// <summary>Knob modes from the middleware's class 3 command table.</summary>
public enum RazerKnobMode : byte
{
    AdjustmentKeyboardBrightness = 0,
    CycleKnobLightingEffect = 1,
    MicVolumeUpDown = 2,
    TrackJogging = 3,
    WindowZoomInOut = 4,
    WindowScrollUpDown = 5,
    WindowScrollLeftRight = 6,
    SwitchApplication = 7,
}

/// <summary>One six-byte knob configuration row: disabled, stage, mode and RGB.</summary>
public readonly record struct RazerKnobStage(
    byte Disabled,
    byte Stage,
    RazerKnobMode Mode,
    byte Red,
    byte Green,
    byte Blue);

public sealed record RazerKnobConfiguration(
    byte LedClass,
    byte LedId,
    IReadOnlyList<RazerKnobStage> Stages);

public readonly record struct RazerKnobModeState(
    byte LedClass,
    byte LedId,
    RazerKnobStage Stage);

/// <summary>
/// Protocol 25 class 0x03 knob commands from the keyboard middleware. The 028D
/// BlackWidow V4 Pro, 02B3 BlackWidow V4 Pro 75% and 02A6 Huntsman V3 Pro bundles
/// all register:
/// get configuration [50, 3, 146], set configuration [50, 3, 18],
/// get mode [8, 3, 147], set mode [8, 3, 19].
/// Configuration payloads are ledClass, ledId plus eight six-byte rows.
/// </summary>
public static class RazerKnobProtocol
{
    public const int StageCount = 8;
    public const int StageSize = 6;

    private const byte CommandClass = 0x03;
    private const byte GetConfigurationCommandId = 0x92;
    private const byte SetConfigurationCommandId = 0x12;
    private const byte GetModeCommandId = 0x93;
    private const byte SetModeCommandId = 0x13;
    private const byte ConfigurationSize = 50;
    private const byte ModeSize = 8;

    public static byte[] CreateGetConfigurationRequest(
        byte transactionId,
        byte ledClass,
        byte ledId)
    {
        Span<byte> arguments = stackalloc byte[ConfigurationSize];
        arguments[0] = ledClass;
        arguments[1] = ledId;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            ConfigurationSize,
            CommandClass,
            GetConfigurationCommandId,
            arguments);
    }

    public static byte[] CreateSetConfigurationRequest(
        byte transactionId,
        byte ledClass,
        byte ledId,
        ReadOnlySpan<RazerKnobStage> stages)
    {
        if (stages.Length > StageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stages),
                stages.Length,
                $"Knob configuration carries at most {StageCount} stages.");
        }

        Span<byte> arguments = stackalloc byte[ConfigurationSize];
        arguments[0] = ledClass;
        arguments[1] = ledId;
        for (var index = 0; index < stages.Length; index++)
        {
            var stage = stages[index];
            var offset = 2 + (index * StageSize);
            arguments[offset] = stage.Disabled;
            arguments[offset + 1] = stage.Stage;
            arguments[offset + 2] = (byte)stage.Mode;
            arguments[offset + 3] = stage.Red;
            arguments[offset + 4] = stage.Green;
            arguments[offset + 5] = stage.Blue;
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            ConfigurationSize,
            CommandClass,
            SetConfigurationCommandId,
            arguments);
    }

    public static byte[] CreateGetModeRequest(
        byte transactionId,
        byte ledClass,
        byte ledId)
    {
        Span<byte> arguments = stackalloc byte[ModeSize];
        arguments[0] = ledClass;
        arguments[1] = ledId;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            ModeSize,
            CommandClass,
            GetModeCommandId,
            arguments);
    }

    public static byte[] CreateSetModeRequest(
        byte transactionId,
        byte ledClass,
        byte ledId,
        RazerKnobStage stage)
    {
        if (!Enum.IsDefined(stage.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(stage), stage.Mode, "Unknown knob mode.");
        }

        Span<byte> arguments = stackalloc byte[ModeSize];
        arguments[0] = ledClass;
        arguments[1] = ledId;
        arguments[2] = stage.Disabled;
        arguments[3] = stage.Stage;
        arguments[4] = (byte)stage.Mode;
        arguments[5] = stage.Red;
        arguments[6] = stage.Green;
        arguments[7] = stage.Blue;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            ModeSize,
            CommandClass,
            SetModeCommandId,
            arguments);
    }

    /// <summary>
    /// Parses the 50-byte configuration echo used by both the get and set commands.
    /// Pass the request that produced the response so the transaction and command
    /// echo are checked.
    /// </summary>
    public static RazerKnobConfiguration ParseConfiguration(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        Validate(response, request, ConfigurationSize, "Knob configuration");
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        var stages = new RazerKnobStage[StageCount];
        for (var index = 0; index < StageCount; index++)
        {
            var offset = 2 + (index * StageSize);
            stages[index] = new RazerKnobStage(
                arguments[offset],
                arguments[offset + 1],
                (RazerKnobMode)arguments[offset + 2],
                arguments[offset + 3],
                arguments[offset + 4],
                arguments[offset + 5]);
        }

        return new RazerKnobConfiguration(arguments[0], arguments[1], stages);
    }

    /// <summary>
    /// Parses the eight-byte current-mode echo used by both the get and set commands.
    /// </summary>
    public static RazerKnobModeState ParseMode(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        Validate(response, request, ModeSize, "Current knob mode");
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        return new RazerKnobModeState(
            arguments[0],
            arguments[1],
            new RazerKnobStage(
                arguments[2],
                arguments[3],
                (RazerKnobMode)arguments[4],
                arguments[5],
                arguments[6],
                arguments[7]));
    }

    private static void Validate(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte minimumArguments,
        string feature)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, minimumArguments))
        {
            throw new InvalidOperationException(
                $"{feature} returned an invalid or out-of-order feature report.");
        }
    }
}
