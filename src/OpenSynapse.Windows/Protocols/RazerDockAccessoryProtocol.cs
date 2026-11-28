namespace OpenSynapse.Windows.Protocols;

internal sealed record RazerDockState(
    bool DockSync,
    bool ChargingEffect,
    bool HasDockSync,
    bool HasChargingEffect,
    string? Error);

/// <summary>
/// Mouse Dock V2 Pro's switches; dock sync carries profile/LED selectors in the payload.
/// </summary>
internal static class RazerDockAccessoryProtocol
{
    internal const ushort ProductId = 0x00F1;
    internal const byte CommandClass = 0x03;
    internal const byte GetDockSync = 0x8F;
    internal const byte SetDockSync = 0x0F;
    internal const byte GetChargingEffect = 0x90;
    internal const byte SetChargingEffect = 0x10;

    internal static byte[] CreateGetDockSync(byte profileId, byte ledId, byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId, 3, CommandClass, GetDockSync, [profileId, ledId, 0]);

    internal static byte[] CreateSetDockSync(
        byte profileId,
        byte ledId,
        bool enabled,
        byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId, 3, CommandClass, SetDockSync,
            [profileId, ledId, enabled ? (byte)1 : (byte)0]);

    internal static byte[] CreateGetChargingEffect(byte profileId, byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId, 1, CommandClass, GetChargingEffect, [0]);

    internal static byte[] CreateSetChargingEffect(
        byte profileId,
        bool enabled,
        byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId, 1, CommandClass, SetChargingEffect,
            [enabled ? (byte)1 : (byte)0]);

    internal static bool ReadDockSync(byte[] response, byte profileId = 0, byte ledId = 0)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        if (data.Length < 3 || data[0] != profileId || data[1] != ledId)
            throw new InvalidDataException("Dock sync response has invalid selectors or length.");
        return data[2] != 0;
    }

    internal static bool ReadEnabled(byte[] response)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        if (data.Length < 1) throw new InvalidDataException("Dock charging-effect response is empty.");
        return data[0] != 0;
    }
}
