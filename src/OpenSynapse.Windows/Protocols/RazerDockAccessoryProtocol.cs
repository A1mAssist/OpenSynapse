namespace OpenSynapse.Windows.Protocols;

internal sealed record RazerDockState(
    bool DockSync,
    bool ChargingEffect,
    bool HasDockSync,
    bool HasChargingEffect,
    string? Error);

/// <summary>
/// Mouse Dock V2 Pro's two body switches. The profile and LED ids live in bytes 3 and 4
/// of the 91-byte frame; the bundle passes profile 0 and the dock LED id through those
/// header fields, not the data payload.
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
        RazerAccessoryProtocol.CreateProfileRequest(
            transactionId, profileId, ledId, 0, CommandClass, GetDockSync, []);

    internal static byte[] CreateSetDockSync(
        byte profileId,
        byte ledId,
        bool enabled,
        byte transactionId) =>
        RazerAccessoryProtocol.CreateProfileRequest(
            transactionId, profileId, ledId, 1, CommandClass, SetDockSync,
            [enabled ? (byte)1 : (byte)0]);

    internal static byte[] CreateGetChargingEffect(byte profileId, byte transactionId) =>
        RazerAccessoryProtocol.CreateProfileRequest(
            transactionId, profileId, 0, 0, CommandClass, GetChargingEffect, []);

    internal static byte[] CreateSetChargingEffect(
        byte profileId,
        bool enabled,
        byte transactionId) =>
        RazerAccessoryProtocol.CreateProfileRequest(
            transactionId, profileId, 0, 1, CommandClass, SetChargingEffect,
            [enabled ? (byte)1 : (byte)0]);

    internal static bool ReadEnabled(byte[] response) =>
        RazerAccessoryProtocol.Arguments(response) is { Length: > 0 } data && data[0] != 0;
}
