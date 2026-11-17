using System.Collections.Frozen;

namespace OpenSynapse.Core.Devices;

/// <summary>
/// Keys a Razer keyboard exposes in its firmware key table but not in the product
/// bundle's SVG hotspots. Only ids read back from real hardware belong here;
/// the 0x8F/0x8D tables accept any byte without proving the key exists.
/// </summary>
public static class RazerKeyboardExtraKeyCatalog
{
    private const byte M1 = 160;
    private const byte M5 = 164;
    private const byte MuteOrDialClick = 200;
    private const byte Previous = 201;
    private const byte Next = 202;
    private const byte PlayPause = 205;
    private const byte Side1 = 207;
    private const byte Side3 = 209;

    private static readonly byte[] Media = [MuteOrDialClick, Previous, Next, PlayPause];
    private static readonly byte[] MediaWithoutMute = [Previous, Next, PlayPause];
    private static readonly byte[] MediaWithVolumeOnly = [MuteOrDialClick, PlayPause];
    private static readonly byte[] PlayPauseOnly = [PlayPause];
    private static readonly byte[] MacroRow = [M1, 161, 162, 163, M5];
    private static readonly byte[] MacroRowAndMedia = [.. MacroRow, .. Media];
    private static readonly byte[] MacroRowMediaSides = [.. MacroRow, .. Media, Side1, 208, Side3];

    private static readonly FrozenDictionary<ushort, byte[]> ExtraKeys =
        new Dictionary<ushort, byte[]>
        {
            [0x0203] = MacroRow,
            [0x0221] = MacroRow,
            [0x0226] = Media,
            [0x0228] = Media,
            [0x024E] = [MuteOrDialClick],
            [0x025A] = Media,
            [0x025C] = Media,
            [0x025D] = Media,
            [0x025E] = MediaWithoutMute,
            [0x0266] = Media,
            [0x026C] = Media,
            [0x0287] = MacroRowAndMedia,
            [0x028D] = MacroRowMediaSides,
            [0x028F] = MediaWithVolumeOnly,
            [0x0290] = [MuteOrDialClick],
            [0x0292] = [MuteOrDialClick],
            [0x0293] = MacroRowAndMedia,
            [0x0294] = PlayPauseOnly,
            [0x0295] = [MuteOrDialClick],
            [0x0296] = [MuteOrDialClick],
            [0x0298] = [MuteOrDialClick],
            [0x02A1] = MediaWithVolumeOnly,
            [0x02A2] = PlayPauseOnly,
            [0x02A3] = MediaWithVolumeOnly,
            [0x02A5] = [MuteOrDialClick],
            [0x02A6] = [MuteOrDialClick, 208, Side3],
            [0x02A7] = [MuteOrDialClick],
            // 02CF rejects 0x9E for 0xD0/0xD1 with status 0x03; they are
            // reserved ids in its table, not readable side keys.
            [0x02CF] = [MuteOrDialClick],
            [0x02D5] = [MuteOrDialClick],
            [0x02D7] = [MuteOrDialClick],
        }.ToFrozenDictionary();

    public static IReadOnlyList<byte> ExtraButtonIds(ushort productId) =>
        ExtraKeys.TryGetValue(productId, out var ids) ? ids : [];

    /// <summary>
    /// Returns the App resource key for a named extra key, or null when this
    /// product does not expose the id. Core deliberately does not localize.
    /// </summary>
    public static string? DescribeResourceKey(ushort productId, byte id)
    {
        if (!ExtraKeys.TryGetValue(productId, out var ids) || Array.IndexOf(ids, id) < 0)
        {
            return null;
        }

        return id switch
        {
            >= M1 and <= M5 => $"RazerMappingKeyM{id - M1 + 1}",
            MuteOrDialClick => productId == 0x028D ? "RazerMappingDialClick" : "RazerMappingMute",
            Previous => "RazerMappingMediaPrev",
            Next => "RazerMappingMediaNext",
            PlayPause => "RazerMappingMediaPlay",
            Side1 => "RazerMappingSideKey1",
            208 => "RazerMappingSideKey2",
            Side3 => "RazerMappingSideKey3",
            _ => null,
        };
    }
}
