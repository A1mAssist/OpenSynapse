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
/// create new profile [1,5,2], delete profile [1,5,3], set profile reset [1,5,11],
/// set active profile ID [1,5,4], get active profile ID [1,5,132],
/// set profile color [4,5,12] and get profile color [4,5,140].
/// The first tuple byte is the declared data size; the payload follows the command header.
/// </summary>
public static class RazerOnboardProfileProtocol
{
    private const byte CommandClass = 0x05;
    private const byte GetMaxProfilesSupportedCommand = 0x8A;
    private const byte GetNumberOfProfilesCommand = 0x80;
    private const byte CreateNewProfileCommand = 0x02;
    private const byte DeleteProfileCommand = 0x03;
    private const byte SetProfileResetCommand = 0x0B;
    private const byte SetActiveProfileIdCommand = 0x04;
    private const byte GetActiveProfileIdCommand = 0x84;
    private const byte SetProfileColorCommand = 0x0C;
    private const byte GetProfileColorCommand = 0x8C;
    private const byte ProfileIdSize = 1;
    private const byte ProfileColorSize = 4;

    public static byte[] CreateGetMaxProfilesSupportedRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            CommandClass,
            GetMaxProfilesSupportedCommand,
            []);

    public static byte[] CreateGetNumberOfProfilesRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            ProfileIdSize,
            CommandClass,
            GetNumberOfProfilesCommand,
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

    public static byte ParseNumberOfProfiles(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request) =>
        Arguments(response, request, ProfileIdSize, "Number of profiles")[0];

    public static byte ParseMaxProfilesSupported(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request) =>
        Arguments(response, request, ProfileIdSize, "Maximum profile count")[0];

    public static byte ParseProfileId(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request) =>
        Arguments(response, request, ProfileIdSize, "Profile id")[0];

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
