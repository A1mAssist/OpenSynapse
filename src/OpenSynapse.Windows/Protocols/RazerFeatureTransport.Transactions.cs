using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

public sealed partial class RazerFeatureTransport
{
    internal static bool UsesMouseDockLinker(string devicePath) =>
        DeviceIdParser.TryParse(devicePath, out var vendorId, out var productId) &&
        vendorId == 0x1532 && productId == 0x00A4;

    internal static bool UsesPinnedStandardTransaction(string devicePath) =>
        DeviceIdParser.TryParse(devicePath, out var vendorId, out var productId) &&
        vendorId == 0x1532 && (productId == 0x025C || productId is 0x0086 or 0x0088);

    internal static byte PrepareTransactionId(
        string devicePath, byte requested, byte commandClass, byte commandId, ref int sequence)
    {
        if (UsesMouseDockLinker(devicePath))
        {
            // 00A4 rzDevice25Linker uses E0 | 0..30 for linker and child battery/charging
            // commands; Scan(46)/Pair(41) clear the high bits. OpenRazer's
            // razeraccessory_driver sends FF for the 00A4 firmware, serial and brightness
            // commands, and FF is the wildcard that accepts any echoed transaction id.
            if (commandClass == 0x07 && commandId is 0x80 or 0x84)
                return (byte)(0xE0 | NextDockTransactionId(ref sequence));
            if (commandClass != 0x00) return 0xFF;
            if (commandId is 0x46 or 0x41) return NextDockTransactionId(ref sequence);
            if (commandId is 0xBF or 0xC7 or 0x47)
                return (byte)(0xE0 | NextDockTransactionId(ref sequence));
            return 0xFF;
        }
        if (requested == 0xFF) return requested;
        if (DeviceIdParser.TryParse(devicePath, out _, out var productId) &&
            RazerKeyboardProtocol.UsesDualLinkTransaction(productId))
        {
            // DualLinkKeyboard clears the high bit for Set Device Pairing Mode (0x41)
            // and Set Device Scan (0x46); every other command keeps 0x80 | sequence.
            if (commandClass == 0x00 && commandId is 0x41 or 0x46)
                return NextTransactionId(ref sequence);
            return (byte)(0x80 | NextTransactionId(ref sequence));
        }
        if (TryGetKnownWirelessTransaction(devicePath, commandClass, commandId, out var known))
        {
            // OpenRazer overrides the catalog's generic battery transaction for these
            // wireless families: razerkbd_driver.c:739-745 sends 025C charge reads with
            // 0x9F, while razermouse_driver.c:1615-1665 sends the 0088 receiver's charge
            // read with 0x1F. The catalog's 1F/3F values are its generic family defaults.
            return known;
        }
        if (UsesPinnedStandardTransaction(devicePath) && requested is 0x00 or 0x1F or 0x3F or 0x9F)
        {
            // OpenRazer pins these for the wireless BlackWidow V3 Pro / Basilisk Ultimate
            // families; 0x9F/0x3F need high bits. shortcut: 0086/0088 catalog 00 -> 1F per
            // OpenRazer; revisit if a real capture shows 00.
            return requested == 0x00 ? (byte)0x1F : requested;
        }
        return NextTransactionId(ref sequence);
    }

    private static bool TryGetKnownWirelessTransaction(
        string devicePath, byte commandClass, byte commandId, out byte transactionId)
    {
        transactionId = 0;
        if (!DeviceIdParser.TryParse(devicePath, out var vendorId, out var productId) ||
            vendorId != 0x1532 || commandClass != 0x07)
        {
            return false;
        }

        switch (productId)
        {
            case 0x025C when commandId is 0x80 or 0x84:
                transactionId = 0x9F;
                return true;
            case 0x0088 when commandId == 0x80:
                transactionId = 0x1F;
                return true;
            default:
                return false;
        }
    }

    private static byte NextDockTransactionId(ref int sequence) =>
        (byte)(unchecked((uint)Interlocked.Increment(ref sequence)) % 31);

    private static void PrepareStandardTransaction(string devicePath, byte[] request)
    {
        var sequence = _transactionSequences.GetOrAdd(devicePath, static _ => [-1]);
        request[2] = PrepareTransactionId(devicePath, request[2], request[7], request[8], ref sequence[0]);
    }
}
