namespace OpenSynapse.Windows.Protocols;

/// <summary>Haptics control state from the class 0x04 control command.</summary>
public readonly record struct RazerHapticsControlState(bool Enabled, ushort Strength);

/// <summary>
/// Haptics control from the AudioAvnera device class used by the 0528, 0576 and
/// 056B middleware bundles. The JS command tuples are:
/// get [0xFD, 0x04, 0x02, 0xF1, 0x03, 0x20, 0x00] and
/// set [0xFF, 0x04, 0x02, 0xF1, 0x06, 0x20].
/// The first byte is the 64-byte transport direction marker (0xFD read, 0xFF
/// write); 0x04 is the command class and 0x02 the command id. The set payload is
/// [enabled, strength low, strength high], and the get response returns the same
/// three fields. Strength is a raw little-endian 16-bit value: the middleware
/// writes a.l7(value) then a.E4(value), and parses with a.Jz(high, low). Its unit
/// is not defined by the JS, so this class keeps it raw.
/// The 91-byte feature-report envelope has no direction byte, so the getter uses
/// Razer's standard read bit (0x02 | 0x80 = 0x82); the setter keeps the literal
/// 0x02 from the JS tuple.
/// </summary>
public static class RazerHapticsProtocol
{
    public const byte CommandClass = 0x04;
    public const byte SetCommandId = 0x02;
    public const byte GetCommandId = SetCommandId | 0x80;
    public const int PayloadSize = 3;
    public const int MaxStrength = ushort.MaxValue;

    /// <summary>
    /// The JS get tuple carries no payload; the trailing zero belongs to the
    /// 64-byte transport header. The 91-byte envelope therefore declares zero
    /// data bytes and leaves the argument area empty.
    /// </summary>
    public static byte[] CreateGetHapticsControlRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            0,
            CommandClass,
            GetCommandId,
            ReadOnlySpan<byte>.Empty);

    public static byte[] CreateSetHapticsControlRequest(
        byte transactionId,
        bool enabled,
        int strength)
    {
        if (strength is < 0 or > MaxStrength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(strength),
                strength,
                $"Haptics strength is a raw 16-bit value from 0 to {MaxStrength}.");
        }

        Span<byte> arguments = stackalloc byte[PayloadSize];
        arguments[0] = enabled ? (byte)0x01 : (byte)0x00;
        arguments[1] = (byte)strength;
        arguments[2] = (byte)(strength >> 8);
        return RazerFeatureReport.CreateRequest(
            transactionId,
            PayloadSize,
            CommandClass,
            SetCommandId,
            arguments);
    }

    /// <summary>
    /// Parses the get echo: enabled at argument 0, raw strength low byte at
    /// argument 1 and high byte at argument 2. The JS response declares four
    /// bytes, so three arguments are the minimum this parser requires.
    /// </summary>
    public static RazerHapticsControlState ParseHapticsControl(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.Matches(request, response) ||
            !RazerFeatureReport.IsAcceptedStatus(response[1]) ||
            response[6] < PayloadSize ||
            response[6] > 80)
        {
            throw new InvalidOperationException(
                "Razer haptics control returned an invalid or unsuccessful feature report.");
        }

        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        return new RazerHapticsControlState(
            arguments[0] != 0,
            (ushort)(arguments[1] | (arguments[2] << 8)));
    }
}
