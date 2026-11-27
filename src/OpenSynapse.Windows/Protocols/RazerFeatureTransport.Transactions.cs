using OpenSynapse.Core.Devices;
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

    private static bool IsLinkerCommand(byte commandClass, byte commandId) =>
        commandClass == 0x00 && commandId is 0xBF or 0xC5 or 0xC7 or 0x47;

    internal static byte PrepareTransactionId(
        string devicePath, byte requested, byte commandClass, byte commandId, ref int sequence,
        ushort? logicalProductId = null, bool freshDockHandle = false)
    {
        if (UsesMouseDockLinker(devicePath))
        {
            if (logicalProductId is { } childProductId && childProductId != 0x00A4)
            {
                // The paired mouse is an ordinary protocol-25 device reached through the
                // dock's collection: rzDevice25DualLinkMouse rolls 0..30. 0x80 belongs to
                // rzDevice25DualLinkKeyboard, and E0 is the dock's own bank.
                return NextDockTransactionId(ref sequence);
            }
            // The linker restarts its transaction counter at E0 after every HID open.
            // Non-session queries open one handle per command, so reusing the process-wide
            // counter leaves the second command (E1) answered as busy.
            if (freshDockHandle)
                sequence = -1;
            // 00A4 rzDevice25Linker uses E0 | 0..30 for every command. Scan(46)/Pair(41)
            // clear the high bits. The linker firmware leaves later commands busy when
            // the initial firmware/serial handshake uses OpenRazer's FF wildcard.
            if (commandClass == 0x00 && commandId is 0x46 or 0x41)
                return NextDockTransactionId(ref sequence);
            return (byte)(0xE0 | NextDockTransactionId(ref sequence));
        }

        var parsed = DeviceIdParser.TryParse(devicePath, out var vendorId, out var pathProductId);
        var isHost = parsed && vendorId == 0x1532 &&
            RazerDualDongleCatalog.CanHost(pathProductId);
        var manifestKeyboard = parsed && vendorId == 0x1532 &&
            StudioKeyboardCatalog.TryGet(RazerProductAliases.Canonical(logicalProductId ?? pathProductId), out _);

        if (isHost && logicalProductId is { } linkedChildId &&
            RazerProductAliases.Canonical(linkedChildId) != RazerProductAliases.Canonical(pathProductId))
        {
            // rzDevice25DualLinkKeyboard rolls 0x80 | n; the paired mouse uses 0..30.
            var category = RazerDualDongleCatalog.CategoryOf(linkedChildId);
            if (category == DeviceCategory.Unknown)
            {
                category = manifestKeyboard ? DeviceCategory.Keyboard
                    : RazerHidDeviceCatalog.BuiltIn.Find(0x1532, linkedChildId)?.Category ?? DeviceCategory.Unknown;
            }

            return category == DeviceCategory.Keyboard
                ? (byte)(0x80 | NextDockTransactionId(ref sequence))
                : NextDockTransactionId(ref sequence);
        }

        if (DeviceIdParser.TryParse(devicePath, out _, out var productId) &&
            RazerKeyboardProtocol.UsesDualLinkTransaction(productId))
        {
            // DualLinkKeyboard rotates every command from the same high-bank sequence,
            // including the firmware handshake whose catalog transaction is the 0xFF wildcard.
            if (commandClass == 0x00 && commandId is 0x41 or 0x46)
                return manifestKeyboard ? NextDockTransactionId(ref sequence) : NextTransactionId(ref sequence);
            return (byte)(0x80 | (manifestKeyboard ? NextDockTransactionId(ref sequence) : NextTransactionId(ref sequence)));
        }
        if (isHost && RazerDualDongleCatalog.CategoryOf(pathProductId) == DeviceCategory.Keyboard &&
            IsLinkerCommand(commandClass, commandId))
        {
            // Keyboard hosts outside UsesDualLinkTransaction still steer their linker
            // commands through the DualLinkKeyboard bank.
            return (byte)(0x80 | NextTransactionId(ref sequence));
        }
        if (isHost && (IsLinkerCommand(commandClass, commandId) ||
            pathProductId == 0x00B3 && commandClass == 0x07 && commandId is 0x10 or 0x90))
        {
            // 00B3's rzDevice25LinkerUma sends its indicator configuration to the E0 bank.
            // rzDevice25Linker* restarts its E0 counter after every HID open.
            if (freshDockHandle)
                sequence = -1;
            return (byte)(0xE0 | NextDockTransactionId(ref sequence));
        }
        // rzDevice25 wraps before 31; 0x1F is not part of its rolling sequence.
        if (manifestKeyboard)
            return NextDockTransactionId(ref sequence);
        if (requested == 0xFF)
        {
            // Only the devices that opt in rotate the wildcard. Everyone else keeps
            // 0xFF because their firmware uses it as "accept any echoed transaction".
            return DeviceIdParser.TryParse(devicePath, out var wildcardVendorId, out var dynamicProductId) &&
                wildcardVendorId == 0x1532 &&
                RazerHidDeviceCatalog.BuiltIn.Find(wildcardVendorId, dynamicProductId)?.UsesDynamicTransactions == true
                    ? NextTransactionId(ref sequence)
                    : requested;
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

    private static void PrepareStandardTransaction(
        string devicePath, byte[] request, ushort? logicalProductId = null, bool freshDockHandle = false)
    {
        // A linked child keeps its own rolling counter. Sharing the host's key would
        // restart the child on every HID handle and consume the host's linker sequence.
        var sequenceKey = devicePath;
        var isLinkedChild = false;
        if (logicalProductId is { } childProductId &&
            DeviceIdParser.TryParse(devicePath, out _, out var parentProductId) &&
            RazerProductAliases.Canonical(childProductId) != RazerProductAliases.Canonical(parentProductId) &&
            RazerDualDongleCatalog.CanHost(parentProductId))
        {
            isLinkedChild = true;
            sequenceKey = $"{devicePath}#{childProductId:X4}";
        }

        var sequence = _transactionSequences.GetOrAdd(sequenceKey, static _ => [-1]);
        request[2] = PrepareTransactionId(
            devicePath, request[2], request[7], request[8], ref sequence[0], logicalProductId,
            isLinkedChild ? false : freshDockHandle);
    }
}
