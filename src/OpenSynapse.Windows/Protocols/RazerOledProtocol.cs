namespace OpenSynapse.Windows.Protocols;

public readonly record struct RazerOledScreensaverOption(byte Mode, byte Item);

public readonly record struct RazerChromaBrightnessStatus(byte ProfileId, byte RegionId, byte Status);

/// <summary>
/// Protocol 25 class 0x17 OLED commands from the 028D, 02A6 and 02B3 keyboard
/// middleware bundles. Each tuple below is [dataSize, 0x17, commandId]; the
/// argument builders reproduce the bytes the middleware writes before sendCommand.
/// </summary>
public static class RazerOledProtocol
{
    public const byte CommandClass = 0x17;
    public const int AnimationStateCount = 6;
    public const int ImageStateCount = 10;

    private const byte GetBrightnessCommandId = 0x83;
    private const byte SetBrightnessCommandId = 0x03;
    private const byte GetLanguageCommandId = 0x85;
    private const byte SetLanguageCommandId = 0x05;
    private const byte SetHomeScreenActiveItemCommandId = 0x0D;
    private const byte GetScreensaverOptionCommandId = 0x96;
    private const byte SetHomeScreenImageStateCommandId = 0x15;
    private const byte GetHomeScreenAnimationStateCommandId = 0x94;
    private const byte SetTimeToDimCommandId = 0x06;
    private const byte SetLowBatteryWarningPercentageCommandId = 0x12;
    private const byte SetUpdateVolumeCommandId = 0x0B;
    private const byte GetChromaBrightnessStatusCommandId = 0x98;
    private const byte SetChromaBrightnessStatusCommandId = 0x18;

    public static byte[] CreateGetBrightnessRequest(byte transactionId) =>
        Create(transactionId, 1, GetBrightnessCommandId);

    // The middleware caps the brightness percentage at 100 before writing the byte.
    public static byte[] CreateSetBrightnessRequest(byte transactionId, byte brightness) =>
        Create(
            transactionId,
            1,
            SetBrightnessCommandId,
            brightness >= 100 ? (byte)100 : brightness);

    public static byte[] CreateGetLanguageRequest(byte transactionId) =>
        Create(transactionId, 1, GetLanguageCommandId);

    public static byte[] CreateSetLanguageRequest(byte transactionId, byte language) =>
        Create(transactionId, 1, SetLanguageCommandId, language);

    // The middleware copies the selected item into both payload bytes.
    public static byte[] CreateSetHomeScreenActiveItemRequest(byte transactionId, byte activeItem) =>
        Create(transactionId, 2, SetHomeScreenActiveItemCommandId, activeItem, activeItem);

    public static byte[] CreateGetScreensaverOptionRequest(byte transactionId) =>
        Create(transactionId, 2, GetScreensaverOptionCommandId);

    public static byte[] CreateSetHomeScreenImageStateRequest(
        byte transactionId,
        ReadOnlySpan<byte> imageStates)
    {
        if (imageStates.Length != ImageStateCount)
        {
            throw new ArgumentException(
                $"OLED home screen image state requires {ImageStateCount} bytes.",
                nameof(imageStates));
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            ImageStateCount,
            CommandClass,
            SetHomeScreenImageStateCommandId,
            imageStates);
    }

    public static byte[] CreateGetHomeScreenAnimationStateRequest(byte transactionId) =>
        Create(transactionId, AnimationStateCount, GetHomeScreenAnimationStateCommandId);

    public static byte[] CreateSetTimeToDimRequest(byte transactionId, byte minutes) =>
        Create(transactionId, 1, SetTimeToDimCommandId, minutes);

    public static byte[] CreateSetLowBatteryWarningPercentageRequest(
        byte transactionId,
        byte percentage) =>
        Create(transactionId, 1, SetLowBatteryWarningPercentageCommandId, percentage);

    // The caller passes [volume, isMuted ? 1 : 0] to the middleware.
    public static byte[] CreateSetUpdateVolumeRequest(
        byte transactionId,
        byte volume,
        bool muted) =>
        Create(transactionId, 2, SetUpdateVolumeCommandId, volume, muted ? (byte)1 : (byte)0);

    public static byte[] CreateGetChromaBrightnessStatusRequest(
        byte transactionId,
        byte profileId) =>
        Create(transactionId, 3, GetChromaBrightnessStatusCommandId, profileId);

    public static byte[] CreateSetChromaBrightnessStatusRequest(
        byte transactionId,
        byte profileId,
        byte regionId,
        byte status) =>
        Create(
            transactionId,
            3,
            SetChromaBrightnessStatusCommandId,
            profileId,
            regionId,
            status);

    public static byte ReadBrightness(ReadOnlySpan<byte> response) =>
        Data(response, 1)[0];

    public static byte ReadLanguage(ReadOnlySpan<byte> response) =>
        Data(response, 1)[0];

    // The active item is in the second response byte, not the first.
    public static byte ReadHomeScreenActiveItem(ReadOnlySpan<byte> response) =>
        Data(response, 2)[1];

    public static RazerOledScreensaverOption ReadScreensaverOption(ReadOnlySpan<byte> response)
    {
        var data = Data(response, 2);
        return new RazerOledScreensaverOption(data[0], data[1]);
    }

    public static byte[] ReadHomeScreenAnimationState(ReadOnlySpan<byte> response) =>
        Data(response, AnimationStateCount).ToArray();

    public static byte[] ReadHomeScreenImageState(ReadOnlySpan<byte> response) =>
        Data(response, ImageStateCount).ToArray();

    public static byte ReadTimeToDim(ReadOnlySpan<byte> response) =>
        Data(response, 1)[0];

    public static byte ReadLowBatteryWarningPercentage(ReadOnlySpan<byte> response) =>
        Data(response, 1)[0];

    public static byte ReadUpdateVolume(ReadOnlySpan<byte> response) =>
        Data(response, 1)[0];

    public static RazerChromaBrightnessStatus ReadChromaBrightnessStatus(
        ReadOnlySpan<byte> response)
    {
        var data = Data(response, 3);
        return new RazerChromaBrightnessStatus(data[0], data[1], data[2]);
    }

    private static byte[] Create(
        byte transactionId,
        byte dataSize,
        byte commandId,
        params byte[] arguments) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            dataSize,
            CommandClass,
            commandId,
            arguments);

    private static ReadOnlySpan<byte> Data(ReadOnlySpan<byte> response, int length)
    {
        if (response.Length != RazerFeatureReport.Length)
        {
            throw new InvalidDataException(
                $"Razer feature reports are {RazerFeatureReport.Length} bytes; got {response.Length}.");
        }

        if (response[RazerFeatureReport.DataSizeOffset] < length)
        {
            throw new InvalidDataException(
                $"Razer OLED response declares {response[RazerFeatureReport.DataSizeOffset]} data bytes; {length} required.");
        }

        return response.Slice(RazerFeatureReport.ArgumentsOffset, length);
    }
}
