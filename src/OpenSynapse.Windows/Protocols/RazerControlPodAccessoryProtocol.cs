namespace OpenSynapse.Windows.Protocols;

internal sealed record RazerControlPodState(
    int? Brightness,
    int? RollerDirection,
    int? LowBatteryPercent,
    string? Error);

/// <summary>
/// Safe OLED controls for the Wireless Control Pod (0566). These are one-byte class 0x17
/// commands; the time/language/screensaver enums are left to the native bundle.
/// </summary>
internal static class RazerControlPodAccessoryProtocol
{
    internal const byte CommandClass = 0x17;
    internal const byte GetBrightness = 0x83;
    internal const byte SetBrightness = 0x03;
    internal const byte GetRollerDirection = 0x8C;
    internal const byte SetRollerDirection = 0x0C;
    internal const byte GetLowBattery = 0x92;
    internal const byte SetLowBattery = 0x12;

    internal static int ReadByte(byte[] response)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        return data.Length == 0
            ? throw new InvalidDataException("Control Pod response has no data byte.")
            : data[0];
    }
}
