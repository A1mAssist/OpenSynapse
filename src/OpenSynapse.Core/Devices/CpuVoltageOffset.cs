namespace OpenSynapse.Core.Devices;

/// <summary>
/// The Intel per-core voltage offset that the OC mailbox (<c>MSR 0x150</c>) carries.
/// The value is a signed 21-bit count of 1/1024 V, so 1 unit is about 0.977 mV and the
/// register can express roughly -1 V to +1 V.
///
/// Only the encoding lives here. Reading and writing the mailbox is a separate, ordered
/// command sequence and stays out of the model: a wrong command word is a voltage write,
/// not a read.
/// </summary>
public static class CpuVoltageOffset
{
    public const uint MailboxMsr = 0x150;

    /// <summary>Below this the offset is outside anything a Blade can run stably.</summary>
    public const int MinimumMillivolts = -250;

    public const int MaximumMillivolts = 250;

    /// <summary>Offsets beyond this are a decode failure, not a real setting.</summary>
    public const int PlausibleMagnitudeMillivolts = 1000;

    private const ulong DataMask = 0x1F_FFFF;
    private const ulong SignBit = 0x10_0000;
    private const double MillivoltsPerUnit = 1000d / 1024d;

    /// <summary>
    /// Decodes the low dword of the mailbox reply. The 21-bit field is two's complement,
    /// so it must be sign-extended before scaling; treating it as unsigned turns a small
    /// undervolt into a large overvolt.
    /// </summary>
    public static bool TryDecode(ulong mailboxReply, out int millivolts)
    {
        var raw = mailboxReply & DataMask;
        if ((raw & SignBit) != 0)
        {
            raw |= ~DataMask;
        }

        var value = (long)raw * MillivoltsPerUnit;
        if (value is > PlausibleMagnitudeMillivolts or < -PlausibleMagnitudeMillivolts)
        {
            millivolts = 0;
            return false;
        }

        millivolts = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Encodes one clamped offset into the 21-bit data field.</summary>
    public static ulong Encode(int millivolts)
    {
        var units = (int)Math.Round(millivolts / MillivoltsPerUnit, MidpointRounding.AwayFromZero);
        var clamped = Math.Clamp(units, -(int)SignBit, (int)DataMask - (int)SignBit);
        return (ulong)clamped & DataMask;
    }

    public static bool IsWithinUserRange(int millivolts) =>
        millivolts is >= MinimumMillivolts and <= MaximumMillivolts;
}
