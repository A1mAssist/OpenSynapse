using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

public static class BladeAdapterPowerProtocol
{
    public const byte CommandClass = 0x07;
    public const byte CommandId = 0x8C;
    public const int DataSize = 2;

    public static byte[] CreateGetRequest() =>
        RazerFeatureReport.CreateRequest(
            transactionId: 0x00,
            dataSize: DataSize,
            commandClass: CommandClass,
            commandId: CommandId,
            arguments: new byte[DataSize]);

    public static BladeAdapterPowerSnapshot Parse(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(
                request, response, DataSize, allowRemainingPacketsMismatch: true))
        {
            throw new InvalidOperationException(
                "Blade adapter power returned an invalid or out-of-order feature report.");
        }

        var offset = RazerFeatureReport.ArgumentsOffset;
        var actualLevel = response[offset];
        var recommendedLevel = response[offset + 1];
        return new BladeAdapterPowerSnapshot(
            actualLevel,
            recommendedLevel,
            GetWatts(actualLevel),
            GetWatts(recommendedLevel));
    }

    public static int? GetWatts(byte level) => level switch
    {
        0x00 => 0,
        0x01 => 40,
        0x02 => 45,
        0x03 => 60,
        0x04 => 65,
        0x05 => 80,
        0x06 => 90,
        0x07 => 100,
        0x08 => 120,
        0x09 => 130,
        0x0A => 150,
        0x0B => 180,
        0x0C => 200,
        0x0D => 230,
        0x0E => 250,
        0x0F => 310,
        0x11 => 280,
        0x12 => 330,
        0x13 => 400,
        _ => null,
    };
}
