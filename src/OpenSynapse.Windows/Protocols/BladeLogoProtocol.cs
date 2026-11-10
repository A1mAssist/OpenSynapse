using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Source-backed Blade lid-logo GET/SET reports and strict response parsing.
/// The profile id comes from the device manifest and is not implied by the
/// protocol family. The mode read returns a Chroma effect id; the writable
/// Logo subset uses 0x00 for static, 0x02 for breathing, with power held by
/// the companion LED-state command.
/// </summary>
public static class BladeLogoProtocol
{
    private const byte DataSize = 0x03;
    private const byte CommandClass = 0x03;
    private const byte LogoLedId = 0x04;
    private const byte TransactionId = 0xFF;

    public static byte[] CreateGetPowerRequest(byte profileId) => CreateRequest(0x80, 0x00, profileId);

    public static byte[] CreateGetModeRequest(byte profileId) => CreateRequest(0x82, 0x00, profileId);

    public static byte[] CreateSetPowerRequest(bool powered, byte profileId) =>
        CreateRequest(0x00, powered ? (byte)0x01 : (byte)0x00, profileId);

    public static byte[] CreateSetModeRequest(BladeLogoMode mode, byte profileId) =>
        CreateRequest(
            0x02,
            mode switch
            {
                BladeLogoMode.Static => 0x00,
                BladeLogoMode.Breathing => 0x02,
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            }, profileId);

    public static bool ParsePower(ReadOnlySpan<byte> response, byte profileId)
        => ParsePower(response, CreateGetPowerRequest(profileId), profileId);

    internal static bool ParsePower(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte profileId)
    {
        var arguments = ValidateResponse(response, request, profileId);
        return arguments[2] switch
        {
            0x00 => false,
            0x01 => true,
            var value => throw new InvalidOperationException(
                $"Blade Logo returned an unknown power state: 0x{value:X2}."),
        };
    }

    public static BladeLogoMode ParseMode(ReadOnlySpan<byte> response, byte profileId)
        => ParseMode(response, CreateGetModeRequest(profileId), profileId);

    internal static BladeLogoMode ParseMode(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte profileId)
    {
        var raw = ParseModeRaw(response, request, profileId);
        return MapMode(raw) ?? throw new InvalidOperationException(
            $"Blade Logo returned an unknown lighting mode: 0x{raw:X2}.");
    }

    public static byte ParseModeRaw(ReadOnlySpan<byte> response, byte profileId)
        => ParseModeRaw(response, CreateGetModeRequest(profileId), profileId);

    internal static byte ParseModeRaw(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte profileId)
    {
        var arguments = ValidateResponse(response, request, profileId);
        return arguments[2];
    }

    public static BladeLogoMode? MapMode(byte value) => value switch
    {
        0x00 or 0x01 => BladeLogoMode.Static,
        0x02 => BladeLogoMode.Breathing,
        _ => null,
    };

    public static BladeLogoMode? Combine(bool power, BladeLogoMode? poweredMode)
    {
        if (!power)
        {
            return BladeLogoMode.Off;
        }

        return poweredMode;
    }

    private static ReadOnlySpan<byte> ValidateResponse(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte profileId)
    {
        if (profileId > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(profileId));
        }
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, 3))
        {
            throw new InvalidOperationException("Blade Logo returned an invalid or out-of-order feature report.");
        }

        if (response[6] < 3)
        {
            throw new InvalidOperationException($"Blade Logo response is too short: {response[6]} < 3.");
        }

        var arguments = response[
            RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 3)];
        if (arguments[0] != profileId || arguments[1] != LogoLedId)
        {
            throw new InvalidOperationException("Blade Logo returned an incorrect object identifier.");
        }

        return arguments;
    }

    private static byte[] CreateRequest(byte commandId, byte value, byte profileId)
    {
        if (profileId > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(profileId));
        }

        return RazerFeatureReport.CreateRequest(
            TransactionId,
            DataSize,
            CommandClass,
            commandId,
            new byte[] { profileId, LogoLedId, value });
    }
}
