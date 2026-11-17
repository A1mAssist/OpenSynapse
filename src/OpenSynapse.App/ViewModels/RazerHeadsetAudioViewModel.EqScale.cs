using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private byte ToFirmwareBand(double level) => IsProtocol40
        ? (byte)(level < 0
            ? 0x80 | (byte)Math.Abs(Math.Round(level))
            : (byte)Math.Round(level))
        : _connection.Transport switch
    {
        RazerHeadsetAudioTransport.Mxic64 => RazerHeadsetEqScale.ToStep(level),
        RazerHeadsetAudioTransport.ValkyrieT3Feature64 => RazerValkyrieHeadsetProtocol.EncodeEqBandLevel((sbyte)Math.Round(level)),
        RazerHeadsetAudioTransport.Jolie64 => (byte)(level < 0
            ? 0x80 | (byte)Math.Abs(Math.Round(level))
            : (byte)Math.Round(level)),
        RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64 =>
            RazerValkyrieHeadsetProtocol.EncodeEqBandLevel((sbyte)Math.Round(level)),
        RazerHeadsetAudioTransport.Protocol25Feature65 => unchecked((byte)(sbyte)Math.Round(level)),
        _ => (byte)Math.Round(level),
    };

    private double FromFirmwareBand(byte level) => IsProtocol40
        ? RazerProtocol40HeadsetProtocol.DecodeEqBandLevel(level)
        : _connection.Transport switch
    {
        RazerHeadsetAudioTransport.Mxic64 => RazerHeadsetEqScale.ToDecibels(level),
        RazerHeadsetAudioTransport.ValkyrieT3Feature64 => RazerValkyrieHeadsetProtocol.DecodeEqBandLevel(level),
        RazerHeadsetAudioTransport.Jolie64 => (level & 0x80) != 0 ? -(level & 0x7F) : level,
        RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64 =>
            RazerValkyrieHeadsetProtocol.DecodeEqBandLevel(level),
        RazerHeadsetAudioTransport.Protocol25Feature65 => unchecked((sbyte)level),
        _ => level,
    };
}
