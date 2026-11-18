using System.Text;

namespace OpenSynapse.Windows.Protocols;

/// <summary>One onboard profile slot with its stored color.</summary>
public readonly record struct RazerProfileColor(
    byte ProfileId,
    byte Red,
    byte Green,
    byte Blue);

/// <summary>
/// Class 0x05 onboard profile commands from the shared Razer middleware profile table.
/// The 0099 Basilisk V3 and 00B6 DeathAdder V3 Pro bundles both register the same tuples:
/// get max profiles supported [1,5,138], get number of profiles [1,5,128],
/// get profile id list [80,5,129],
/// create new profile [1,5,2], delete profile [1,5,3], set profile reset [1,5,11],
/// set active profile ID [1,5,4], get active profile ID [1,5,132],
/// set profile color [4,5,12], get profile color [4,5,140] and read profile name
/// [80,5,136] as 64-byte chunks of the 250-byte GUID/name structure.
/// The first tuple byte is the declared data size; the payload follows the command header.
/// </summary>
public static class RazerOnboardProfileProtocol
{
    private const byte CommandClass = 0x05;
    /// <summary>
    /// The DeathAdder V4 Pro family (00BE/00BF) exposes the same profile commands under
    /// class 0x03 instead of 0x05, so the firmware rejects a class 0x05 write with 0x03.
    /// The command ids differ as well: max profiles is 0x89 (not 0x8A), the profile count
    /// list is 0x80, the active id is 0x83 and the create/delete ids are 0x02/0x03.
    /// </summary>
    private const byte AltCommandClass = 0x03;
    private const byte AltGetMaxProfilesSupportedCommand = 0x89;
    private const byte AltGetNumberOfProfilesCommand = 0x80;
    private const byte AltCreateNewProfileCommand = 0x02;
    private const byte AltDeleteProfileCommand = 0x03;
    private const byte AltSetActiveProfileIdCommand = 0x04;
    private const byte AltGetActiveProfileIdCommand = 0x83;
    private const byte GetMaxProfilesSupportedCommand = 0x8A;
    private const byte GetNumberOfProfilesCommand = 0x80;
    private const byte GetProfileIdsCommand = 0x81;
    private const byte CreateNewProfileCommand = 0x02;
    private const byte DeleteProfileCommand = 0x03;
    private const byte SetProfileResetCommand = 0x0B;
    private const byte SetActiveProfileIdCommand = 0x04;
    private const byte GetActiveProfileIdCommand = 0x84;
    private const byte SetProfileColorCommand = 0x0C;
    private const byte GetProfileColorCommand = 0x8C;
    private const byte GetProfileNameCommand = 0x88;
    private const byte ProfileNameHeaderSize = 5;
    private const int ProfileNameGuidSize = 16;
    private const int ProfileNameTextSize = 100;

    /// <summary>
    /// The stored name structure is GUID[16] + UTF-8 name[100] + owner[128] + 6 reserved
    /// bytes, read 64 bytes at a time with the five-byte profile/offset/size header.
    /// shortcut: this is the rzDevice25 layout; rzDevice30 uses 78-byte chunks without
    /// the five-byte header, so it needs a separate branch when a product requires it.
    /// </summary>
    public const int ProfileNameStructureLength = 250;
    public const int ProfileNameChunkSize = 64;
    private const byte ProfileIdSize = 1;
    private const byte ProfileIdListSize = 80;
    private const byte ProfileColorSize = 4;

    public static byte[] CreateGetMaxProfilesSupportedRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            CommandClass,
            GetMaxProfilesSupportedCommand,
            []);

    /// <summary>
    /// The class 0x03 profile dialect used by the DeathAdder V4 Pro family.
    /// </summary>
    public static byte[] CreateAltGetMaxProfilesSupportedRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            AltCommandClass,
            AltGetMaxProfilesSupportedCommand,
            []);

    public static byte[] CreateAltGetNumberOfProfilesRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            AltCommandClass,
            AltGetNumberOfProfilesCommand,
            []);

    public static byte[] CreateAltCreateNewProfileRequest(byte transactionId, byte profileId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            AltCommandClass,
            AltCreateNewProfileCommand,
            [profileId]);

    public static byte[] CreateAltDeleteProfileRequest(byte transactionId, byte profileId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            AltCommandClass,
            AltDeleteProfileCommand,
            [profileId]);

    public static byte[] CreateAltSetActiveProfileIdRequest(byte transactionId, byte profileId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            AltCommandClass,
            AltSetActiveProfileIdCommand,
            [profileId]);

    public static byte[] CreateAltGetActiveProfileIdRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            AltCommandClass,
            AltGetActiveProfileIdCommand,
            []);

    public static byte[] CreateGetNumberOfProfilesRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            CommandClass,
            GetNumberOfProfilesCommand,
            []);

    /// <summary>
    /// The middleware asks for the id list with an 80-byte response buffer even though
    /// only the first byte (count) and the following ids are meaningful.
    /// </summary>
    public static byte[] CreateGetProfileIdsRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdListSize,
            CommandClass,
            GetProfileIdsCommand,
            []);

    public static byte[] CreateNewProfileRequest(byte transactionId, byte profileId) =>
        CreateProfileIdRequest(transactionId, CreateNewProfileCommand, profileId);

    public static byte[] CreateDeleteProfileRequest(byte transactionId, byte profileId) =>
        CreateProfileIdRequest(transactionId, DeleteProfileCommand, profileId);

    public static byte[] CreateSetProfileResetRequest(byte transactionId, byte profileId) =>
        CreateProfileIdRequest(transactionId, SetProfileResetCommand, profileId);

    public static byte[] CreateSetActiveProfileIdRequest(byte transactionId, byte profileId) =>
        CreateProfileIdRequest(transactionId, SetActiveProfileIdCommand, profileId);

    public static byte[] CreateGetActiveProfileIdRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            CommandClass,
            GetActiveProfileIdCommand,
            []);

    public static byte[] CreateSetProfileColorRequest(
        byte transactionId,
        byte profileId,
        byte red,
        byte green,
        byte blue) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileColorSize,
            CommandClass,
            SetProfileColorCommand,
            [profileId, red, green, blue]);

    public static byte[] CreateGetProfileColorRequest(byte transactionId, byte profileId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileColorSize,
            CommandClass,
            GetProfileColorCommand,
            // The middleware declares four bytes but only fills the profile id.
            [profileId]);

    public static byte[] CreateGetProfileNameRequest(
        byte transactionId,
        byte profileId,
        int offset,
        int chunkSize)
    {
        if (offset is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        if (chunkSize is < 0 or > 80 - ProfileNameHeaderSize)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        // rzDevice25 sends the chunk size plus the five-byte reply header as the
        // declared report length, while the read request's size field stays zero.
        var declared = chunkSize + ProfileNameHeaderSize;
        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)declared,
            CommandClass,
            GetProfileNameCommand,
            [
                profileId,
                (byte)(offset >> 8),
                (byte)offset,
                0,
                0,
            ]);
    }

    public static byte ParseNumberOfProfiles(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request) =>
        Arguments(response, request, ProfileIdSize, "Number of profiles")[0];

    public static byte ParseMaxProfilesSupported(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request) =>
        Arguments(response, request, ProfileIdSize, "Maximum profile count")[0];

    public static byte ParseProfileId(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request) =>
        Arguments(response, request, ProfileIdSize, "Profile id")[0];

    public static byte[] ParseProfileNameChunk(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte profileId,
        int offset)
    {
        var arguments = Arguments(response, request, ProfileNameHeaderSize, "Profile name");
        var payload = arguments[..response[6]];
        if (payload[0] != profileId || ((payload[1] << 8) | payload[2]) != offset)
        {
            throw new InvalidOperationException(
                "Profile name reply does not match the requested profile or offset.");
        }

        return payload[ProfileNameHeaderSize..].ToArray();
    }

    public static string DecodeProfileName(ReadOnlySpan<byte> structure)
    {
        if (structure.Length < ProfileNameGuidSize + ProfileNameTextSize)
        {
            return string.Empty;
        }

        var name = structure.Slice(ProfileNameGuidSize, ProfileNameTextSize);
        var end = name.IndexOf((byte)0);
        if (end >= 0)
        {
            name = name[..end];
        }

        return Encoding.UTF8.GetString(name);
    }

    /// <summary>
    /// Parses [count, id...] from the 0x81 response. rzDevice25 takes data[0] as the
    /// count and slices the ids that follow; the fixed 80-byte reply buffer can carry
    /// residual bytes after them, and Razer never inspects that tail.
    /// </summary>
    public static byte[] ParseProfileIds(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, 1, "Profile id list");
        var payload = arguments[..response[6]];
        return payload.Slice(1, Math.Min(payload[0], payload.Length - 1)).ToArray();
    }

    public static RazerProfileColor ParseProfileColor(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, ProfileColorSize, "Profile color");
        return new RazerProfileColor(arguments[0], arguments[1], arguments[2], arguments[3]);
    }

    private static byte[] CreateProfileIdRequest(
        byte transactionId,
        byte commandId,
        byte profileId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            CommandClass,
            commandId,
            [profileId]);

    private static ReadOnlySpan<byte> Arguments(
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

        return response[RazerFeatureReport.ArgumentsOffset..];
    }
}
