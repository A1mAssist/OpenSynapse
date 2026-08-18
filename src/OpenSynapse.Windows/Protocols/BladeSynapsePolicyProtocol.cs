using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Product 710 policy calls recovered from the locally loaded Synapse modules.
/// Production callers must provide their own readback and recovery gate.
/// </summary>
public static class BladeSynapsePolicyProtocol
{
    public const byte SynapseTransactionId = 0x00;
    public const byte GameModeCommandClass = 0x00;
    public const byte GameModeGetCommandId = 0x88;
    public const byte GameModeSetCommandId = 0x08;
    public const byte FnCommandClass = 0x02;
    public const byte FnSetCommandId = 0x06;
    public const byte LedCommandClass = 0x03;
    public const byte LedEffectSetCommandId = 0x02;
    public const byte LedStateSetCommandId = 0x00;
    public const byte LogoLedId = 0x04;
    public const byte GameModeLedId = 0x08;
    public const byte StartupAnimationCommandClass = 0x0F;
    public const byte StartupAnimationGetCommandId = 0x98;
    public const byte StartupAnimationSetCommandId = 0x18;
    public const byte AudioMuteCommandClass = 0x18;
    public const byte AudioMuteSetCommandId = 0x04;

    public static byte[] CreateGetGameModeRequest() =>
        RazerFeatureReport.CreateRequest(
            SynapseTransactionId,
            0x04,
            GameModeCommandClass,
            GameModeGetCommandId,
            ReadOnlySpan<byte>.Empty);

    public static byte[] CreateSetGameModeRequest(byte state) =>
        RazerFeatureReport.CreateRequest(
            SynapseTransactionId,
            0x04,
            GameModeCommandClass,
            GameModeSetCommandId,
            new[] { state });

    public static BladeGameModeState ParseGameMode(ReadOnlySpan<byte> response) =>
        ParseGameMode(response, CreateGetGameModeRequest());

    internal static BladeGameModeState ParseGameMode(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, minimumArguments: 3))
        {
            throw new InvalidOperationException("Blade Gaming Mode 返回了无效或错序的 feature report。");
        }

        var offset = RazerFeatureReport.ArgumentsOffset;
        return new BladeGameModeState(response[offset], response[offset + 1], response[offset + 2]);
    }

    public static byte[] CreateSetFnKeyStateRequest(
        bool multiFunctionPrimary,
        byte classId = 0x00) =>
        RazerFeatureReport.CreateRequest(
            SynapseTransactionId,
            0x02,
            FnCommandClass,
            FnSetCommandId,
            new[] { classId, multiFunctionPrimary ? (byte)0x01 : (byte)0x00 });

    public static BladeFnKeyState ParseFnKeyState(ReadOnlySpan<byte> response) =>
        ParseFnKeyState(response, CreateSetFnKeyStateRequest(multiFunctionPrimary: false));

    internal static BladeFnKeyState ParseFnKeyState(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, minimumArguments: 2))
        {
            throw new InvalidOperationException("Blade Fn 主功能返回了无效或错序的 feature report。");
        }

        var offset = RazerFeatureReport.ArgumentsOffset;
        if (response[offset] != request[offset])
        {
            throw new InvalidOperationException("Blade Fn 主功能返回了错误的 classId。");
        }

        var state = response[offset + 1];
        return state switch
        {
            0x00 => new BladeFnKeyState(response[offset], false),
            0x01 => new BladeFnKeyState(response[offset], true),
            _ => throw new InvalidOperationException(
                $"Blade Fn 主功能返回了未知 alternateState 0x{state:X2}。"),
        };
    }

    /// <summary>
    /// Synapse's Product 710 Logo path uses effect 0 for Off/Static and effect
    /// 2 for Breathing. Power is a separate state SET and must follow it.
    /// </summary>
    public static byte[] CreateSetLogoEffectRequest(BladeLogoMode mode) =>
        RazerFeatureReport.CreateRequest(
            SynapseTransactionId,
            0x03,
            LedCommandClass,
            LedEffectSetCommandId,
            new byte[]
            {
                0x00,
                LogoLedId,
                mode switch
                {
                    BladeLogoMode.Off or BladeLogoMode.Static => 0x00,
                    BladeLogoMode.Breathing => 0x02,
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                },
            });

    public static byte[] CreateSetLogoStateRequest(BladeLogoMode mode) =>
        RazerFeatureReport.CreateRequest(
            SynapseTransactionId,
            0x03,
            LedCommandClass,
            LedStateSetCommandId,
            new byte[]
            {
                0x00,
                LogoLedId,
                mode switch
                {
                    BladeLogoMode.Off => 0x00,
                    BladeLogoMode.Static or BladeLogoMode.Breathing => 0x01,
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                },
            });

    public static byte[] CreateSetGameModeIndicatorRequest(bool enabled) =>
        RazerFeatureReport.CreateRequest(
            SynapseTransactionId,
            0x03,
            LedCommandClass,
            LedStateSetCommandId,
            new byte[] { 0x00, GameModeLedId, enabled ? (byte)0x01 : (byte)0x00 });

    public static BladeLedCommandResult ParseLedCommandResult(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, minimumArguments: 3))
        {
            throw new InvalidOperationException("Blade Logo LED 返回了无效或错序的 feature report。");
        }

        var offset = RazerFeatureReport.ArgumentsOffset;
        if (response[offset] != request[offset] ||
            response[offset + 1] != request[offset + 1] ||
            response[offset + 2] != request[offset + 2])
        {
            throw new InvalidOperationException("Blade 指示灯返回了错误的对象或状态。");
        }

        return new BladeLedCommandResult(
            response[offset],
            response[offset + 1],
            response[offset + 2]);
    }

    public static byte[] CreateGetStartupAnimationRequest() =>
        RazerFeatureReport.CreateRequest(
            BladeProduct710Protocol.TransactionId,
            0x01,
            StartupAnimationCommandClass,
            StartupAnimationGetCommandId,
            new byte[] { 0x00 });

    public static byte[] CreateSetStartupAnimationRequest(bool enabled) =>
        RazerFeatureReport.CreateRequest(
            BladeProduct710Protocol.TransactionId,
            0x02,
            StartupAnimationCommandClass,
            StartupAnimationSetCommandId,
            new byte[] { 0x00, enabled ? (byte)0x00 : (byte)0x01 });

    public static BladeStartupAnimationState ParseStartupAnimation(
        ReadOnlySpan<byte> response)
        => ParseStartupAnimation(response, CreateGetStartupAnimationRequest());

    internal static BladeStartupAnimationState ParseStartupAnimation(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        if (response.Length != RazerFeatureReport.Length ||
            response[1] != 0x02 ||
            response[6] is not (0x01 or 0x02) ||
            !RazerFeatureReport.Matches(request, response))
        {
            throw new InvalidOperationException(
                "Blade 启动动画返回了无效或错序的 feature report。");
        }

        var offset = RazerFeatureReport.ArgumentsOffset;
        var hasProfileId = response[6] == 0x02;
        var disabled = response[offset + (hasProfileId ? 1 : 0)];
        if (disabled is not (0x00 or 0x01))
        {
            throw new InvalidOperationException(
                $"Blade 启动动画返回了未知 disableAnimation 0x{disabled:X2}。");
        }

        return new BladeStartupAnimationState(
            hasProfileId ? response[offset] : null,
            disabled == 0x00);
    }

    public static byte[] CreateSetAudioMuteStatusRequest(
        BladeAudioMuteTarget target,
        bool muted,
        byte transactionId = SynapseTransactionId)
    {
        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        return RazerFeatureReport.CreateRequest(
            transactionId,
            0x03,
            AudioMuteCommandClass,
            AudioMuteSetCommandId,
            new byte[] { 0x00, (byte)target, muted ? (byte)0x01 : (byte)0x00 });
    }

    public static BladeAudioMuteState ParseAudioMuteCommandResult(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, minimumArguments: 3))
        {
            throw new InvalidOperationException(
                "Blade 音频静音指示灯返回了无效或错序的 feature report。");
        }

        var offset = RazerFeatureReport.ArgumentsOffset;
        var target = (BladeAudioMuteTarget)response[offset + 1];
        var muted = response[offset + 2];
        if (response[offset] != request[offset] ||
            response[offset + 1] != request[offset + 1] ||
            response[offset + 2] != request[offset + 2] ||
            !Enum.IsDefined(target) ||
            muted is not (0x00 or 0x01))
        {
            throw new InvalidOperationException("Blade 音频静音指示灯返回了无效状态。");
        }

        return new BladeAudioMuteState(target, muted == 0x01);
    }
}

public readonly record struct BladeGameModeState(
    byte GameMode,
    byte KeyCover,
    byte Lifted);

public readonly record struct BladeFnKeyState(
    byte ClassId,
    bool MultiFunctionPrimary);

public readonly record struct BladeLedCommandResult(
    byte ClassId,
    byte LedId,
    byte Value);

public readonly record struct BladeStartupAnimationState(
    byte? ProfileId,
    bool Enabled);

public enum BladeAudioMuteTarget : byte
{
    Speaker = 0x01,
    Microphone = 0x02,
}

public readonly record struct BladeAudioMuteState(
    BladeAudioMuteTarget Target,
    bool Muted);
