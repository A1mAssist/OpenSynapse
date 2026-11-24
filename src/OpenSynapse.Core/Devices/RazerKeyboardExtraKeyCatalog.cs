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
    private const byte Side2 = 208;
    private const byte Side3 = 209;
    // Virtual ids for the two dial-rotation inputs. 02CF stores them in the
    // mouse/button table as 0x09/0x0A, which collide with firmware key ids
    // 8 and 9, so the mapping page needs distinct ids.
    private const byte DialLeft = 0xE9;
    private const byte DialRight = 0xEA;

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
            [0x02A6] = [MuteOrDialClick, PlayPause, Side2, Side3],
            [0x02A7] = [MuteOrDialClick],
            // One physical media key drives 0xC9/0xCA/0xCD as single/double/triple
            // tap; only its primary id is a mapping row. 0xD0/0xD1 are the two
            // macro keys; this firmware has no OBM row for them, so they are
            // host-side triggers and must not be read through 0x9E.
            [0x02CF] = [MuteOrDialClick, PlayPause, Side2, Side3],
            [0x02D5] = [MuteOrDialClick],
            [0x02D7] = [MuteOrDialClick],
        }.ToFrozenDictionary();

    /// <summary>The two virtual rows for a volume dial's rotation directions.</summary>
    public static IReadOnlyList<byte> DialButtonIds { get; } = [DialLeft, DialRight];

    public static IReadOnlyList<byte> ExtraButtonIds(ushort productId) =>
        ExtraKeys.TryGetValue(productId, out var ids) ? ids : [];

    /// <summary>
    /// Returns the App resource key for a named extra key, or null when this
    /// product does not expose the id. Core deliberately does not localize.
    /// </summary>
    public static string? DescribeResourceKey(ushort productId, byte id)
    {
        if (TryGetDialProtocolId(productId, id, out _))
        {
            return id == DialLeft ? "RazerMappingDialLeft" : "RazerMappingDialRight";
        }

        if (!ExtraKeys.TryGetValue(productId, out var ids) || Array.IndexOf(ids, id) < 0)
        {
            return null;
        }

        return id switch
        {
            >= M1 and <= M5 => $"RazerMappingKeyM{id - M1 + 1}",
            MuteOrDialClick => productId is 0x028D or 0x02CF
                ? "RazerMappingDialClick"
                : "RazerMappingMute",
            Previous => "RazerMappingMediaPrev",
            Next => "RazerMappingMediaNext",
            PlayPause => "RazerMappingMediaPlay",
            Side1 => "RazerMappingSideKey1",
            Side2 when productId == 0x02CF => "RazerMappingMacroKey1",
            Side3 when productId == 0x02CF => "RazerMappingMacroKey2",
            Side2 => "RazerMappingSideKey2",
            Side3 => "RazerMappingSideKey3",
            _ => null,
        };
    }

    /// <summary>
    /// 02CF's two macro keys report private codes 100/101 and have no row in the
    /// firmware assignment table; their action lives in the host mapping engine.
    /// </summary>
    public static bool IsHostOnlyRow(ushort productId, byte id) =>
        productId == 0x02CF && id is Side2 or Side3;

    public static bool IsHostOnlyPrivateKey(int productId, int keyCode) =>
        productId == 0x02CF && keyCode is 100 or 101;

    /// <summary>
    /// Maps a virtual dial-rotation row id to the button-table id the firmware
    /// uses. Only products whose bundle wires the dial to ScrollUp/ScrollDown
    /// (02CF) take this path.
    /// </summary>
    public static bool TryGetDialProtocolId(ushort productId, byte id, out byte protocolId)
    {
        protocolId = id switch
        {
            DialLeft => 0x09,
            DialRight => 0x0A,
            _ => 0,
        };
        return productId == 0x02CF && protocolId != 0;
    }
}
