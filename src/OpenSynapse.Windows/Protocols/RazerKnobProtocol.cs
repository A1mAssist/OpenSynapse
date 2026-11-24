using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>Semantic mode names used by the product-specific command-dial tables.</summary>
public enum RazerKnobModeKind
{
    KeyboardLightingBrightness,
    WindowsZoom,
    SwitchApps,
    TrackJogging,
    TrackSelector,
    VerticalScroll,
    HorizontalScroll,
    SwitchBrowserTabs,
    Volume,
    OledBrightness,
}

/// <summary>One mode in a product's firmware command-dial table.</summary>
public readonly record struct RazerKnobModeDefinition(
    byte ModeId,
    RazerKnobModeKind Kind,
    byte StateFlagIndex)
{
    public string ResourceKey => $"RazerHidKnobMode{Kind}";
}

public sealed record RazerKnobModeTable(IReadOnlyList<RazerKnobModeDefinition> Modes)
{
    public RazerKnobModeDefinition? Find(byte modeId)
    {
        foreach (var mode in Modes)
        {
            if (mode.ModeId == modeId)
            {
                return mode;
            }
        }

        return null;
    }
}

public readonly record struct RazerKnobActiveMode(
    byte ModeId,
    byte DisplayOrder,
    byte TotalEnabledFunctions);

/// <summary>One six-byte knob configuration row: disabled, stage, mode and RGB.</summary>
public readonly record struct RazerKnobStage(
    byte Disabled,
    byte Stage,
    byte ModeId,
    byte Red,
    byte Green,
    byte Blue);

public sealed record RazerKnobConfiguration(
    byte ProfileId,
    byte KnobId,
    IReadOnlyList<RazerKnobStage> Stages);

public readonly record struct RazerKnobModeState(
    byte ProfileId,
    byte KnobId,
    RazerKnobStage Stage);

/// <summary>
/// Protocol 25 class 0x03 knob commands from the keyboard middleware. Every bundle
/// with a command dial registers:
/// get configuration [50, 3, 146], set configuration [50, 3, 18],
/// get mode [8, 3, 147], set mode [8, 3, 19].
/// The first two payload bytes are the onboard profile id and the knob id (0x24),
/// followed by eight six-byte rows. They are not an LED class: the middleware calls
/// getCurrentKnobMode(profileIdList[o], 36) once per onboard profile.
/// </summary>
public static class RazerKnobProtocol
{
    public const int StageCount = 8;
    public const int StageSize = 6;
    public const int ModeStateFlagCount = 10;

    /// <summary>The default onboard profile every dial write targets.</summary>
    public const byte DefaultProfileId = 0x01;
    public const byte KnobId = 0x24;
    private const byte CommandClass = 0x03;
    private const byte GetConfigurationCommandId = 0x92;
    private const byte SetConfigurationCommandId = 0x12;
    private const byte GetModeCommandId = 0x93;
    private const byte SetModeCommandId = 0x13;
    private const byte ConfigurationSize = 50;
    private const byte ModeSize = 8;

    private static readonly RazerKnobModeTable EightStageModes = new(
    [
        new(0, RazerKnobModeKind.KeyboardLightingBrightness, 3),
        new(1, RazerKnobModeKind.WindowsZoom, 5),
        new(2, RazerKnobModeKind.SwitchApps, 4),
        new(3, RazerKnobModeKind.TrackJogging, 6),
        new(4, RazerKnobModeKind.TrackSelector, 1),
        new(5, RazerKnobModeKind.VerticalScroll, 7),
        new(6, RazerKnobModeKind.HorizontalScroll, 8),
        new(7, RazerKnobModeKind.SwitchBrowserTabs, 9),
    ]);

    private static readonly RazerKnobModeTable NineStageModes = new(
    [
        new(0, RazerKnobModeKind.Volume, 0),
        new(1, RazerKnobModeKind.TrackSelector, 1),
        new(2, RazerKnobModeKind.OledBrightness, 2),
        new(3, RazerKnobModeKind.KeyboardLightingBrightness, 3),
        new(4, RazerKnobModeKind.SwitchApps, 4),
        new(5, RazerKnobModeKind.WindowsZoom, 5),
        new(6, RazerKnobModeKind.TrackJogging, 6),
        new(7, RazerKnobModeKind.VerticalScroll, 7),
        new(8, RazerKnobModeKind.HorizontalScroll, 8),
    ]);

    /// <summary>
    /// The middleware uses different dial tables even between the two BlackWidow V4 Pro
    /// sizes; never infer a mode byte from a shared enum.
    /// </summary>
    public static RazerKnobModeTable? GetModeTable(ushort productId) =>
        RazerProductAliases.Canonical(productId) switch
        {
            0x028D or 0x02C4 => EightStageModes,
            0x02B3 => NineStageModes,
            _ => null,
        };

    public static byte[] CreateModeStateFlags(
        RazerKnobModeTable table,
        IReadOnlyList<RazerKnobStage> stages)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(stages);
        var flags = new byte[ModeStateFlagCount];
        foreach (var stage in stages)
        {
            if (stage.Disabled != 0 || table.Find(stage.ModeId) is not { } mode)
            {
                continue;
            }

            flags[mode.StateFlagIndex] = 1;
        }

        return flags;
    }

    public static RazerKnobActiveMode? FindActiveMode(
        RazerKnobModeTable table,
        IReadOnlyList<RazerKnobStage> stages,
        byte modeId)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(stages);
        var enabled = stages
            .Where(stage => stage.Disabled == 0 && table.Find(stage.ModeId) is not null)
            .OrderBy(stage => stage.Stage)
            .ToArray();
        for (var index = 0; index < enabled.Length; index++)
        {
            if (enabled[index].ModeId == modeId)
            {
                return new RazerKnobActiveMode(
                    modeId,
                    (byte)(index + 1),
                    (byte)enabled.Length);
            }
        }

        return null;
    }

    public static byte[] CreateGetConfigurationRequest(
        byte transactionId,
        byte profileId,
        byte knobId)
    {
        Span<byte> arguments = stackalloc byte[ConfigurationSize];
        arguments[0] = profileId;
        arguments[1] = knobId;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            ConfigurationSize,
            CommandClass,
            GetConfigurationCommandId,
            arguments);
    }

    public static byte[] CreateSetConfigurationRequest(
        byte transactionId,
        byte profileId,
        byte knobId,
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
        arguments[0] = profileId;
        arguments[1] = knobId;
        for (var index = 0; index < stages.Length; index++)
        {
            var stage = stages[index];
            var offset = 2 + (index * StageSize);
            arguments[offset] = stage.Disabled;
            arguments[offset + 1] = stage.Stage;
            arguments[offset + 2] = stage.ModeId;
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
        byte profileId,
        byte knobId)
    {
        Span<byte> arguments = stackalloc byte[ModeSize];
        arguments[0] = profileId;
        arguments[1] = knobId;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            ModeSize,
            CommandClass,
            GetModeCommandId,
            arguments);
    }

    public static byte[] CreateSetModeRequest(
        byte transactionId,
        byte profileId,
        byte knobId,
        RazerKnobStage stage)
    {
        Span<byte> arguments = stackalloc byte[ModeSize];
        arguments[0] = profileId;
        arguments[1] = knobId;
        arguments[2] = stage.Disabled;
        arguments[3] = stage.Stage;
        arguments[4] = stage.ModeId;
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
                arguments[offset + 2],
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
                arguments[4],
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
