using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Source-backed Blade 02C6 lid-logo GET/SET reports and strict response parsing.
/// Value 0x02 uses the generic LED-effect command; OpenRazer documents it as
/// technically unsupported on Blade laptops, so callers must keep it gated.
/// </summary>
public static class BladeLogoProtocol
{
    private const byte DataSize = 0x03;
    private const byte CommandClass = 0x03;
    // OpenRazer's VARSTORE for Blade logo state is the persistent slot 0x01.
    private const byte RuntimeProfileId = 0x01;
    private const byte LogoLedId = 0x04;
    private const byte TransactionId = 0xFF;

    public static byte[] CreateGetPowerRequest(byte profileId = RuntimeProfileId) => CreateRequest(0x80, 0x00, profileId);

    public static byte[] CreateGetModeRequest(byte profileId = RuntimeProfileId) => CreateRequest(0x82, 0x00, profileId);

    public static byte[] CreateSetPowerRequest(bool powered, byte profileId = RuntimeProfileId) =>
        CreateRequest(0x00, powered ? (byte)0x01 : (byte)0x00, profileId);

    public static byte[] CreateSetModeRequest(BladeLogoMode mode, byte profileId = RuntimeProfileId) =>
        CreateRequest(
            0x02,
            mode switch
            {
                BladeLogoMode.Static => 0x00,
                BladeLogoMode.Breathing => 0x02,
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            }, profileId);

    public static bool ParsePower(ReadOnlySpan<byte> response, byte profileId = RuntimeProfileId)
        => ParsePower(response, CreateGetPowerRequest(profileId), profileId);

    internal static bool ParsePower(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte profileId = RuntimeProfileId)
    {
        var arguments = ValidateResponse(response, request, profileId);
        return arguments[2] switch
        {
            0x00 => false,
            0x01 => true,
            var value => throw new InvalidOperationException(
                $"Blade Logo 返回了未知电源状态 0x{value:X2}。"),
        };
    }

    public static BladeLogoMode ParseMode(ReadOnlySpan<byte> response, byte profileId = RuntimeProfileId)
        => ParseMode(response, CreateGetModeRequest(profileId), profileId);

    internal static BladeLogoMode ParseMode(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte profileId = RuntimeProfileId)
    {
        var arguments = ValidateResponse(response, request, profileId);
        return arguments[2] switch
        {
            0x00 => BladeLogoMode.Static,
            0x02 => BladeLogoMode.Breathing,
            var value => throw new InvalidOperationException(
                $"Blade Logo 返回了未知灯效模式 0x{value:X2}。"),
        };
    }

    public static BladeLogoMode Combine(bool power, BladeLogoMode poweredMode)
    {
        if (!power)
        {
            return BladeLogoMode.Off;
        }

        return poweredMode is BladeLogoMode.Static or BladeLogoMode.Breathing
            ? poweredMode
            : throw new ArgumentOutOfRangeException(nameof(poweredMode));
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
            throw new InvalidOperationException("Blade Logo 返回了无效或错序的 feature report。");
        }

        if (response[6] < 3)
        {
            throw new InvalidOperationException($"Blade Logo 响应长度不足：{response[6]} < 3。");
        }

        var arguments = response[
            RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 3)];
        if (arguments[0] != profileId || arguments[1] != LogoLedId)
        {
            throw new InvalidOperationException("Blade Logo 返回了错误的对象标识。");
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
