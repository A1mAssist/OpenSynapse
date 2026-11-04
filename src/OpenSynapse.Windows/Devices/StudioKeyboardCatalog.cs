using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Keyboards that exist only in Razer's engine manifests. Each manifest names the
/// rzDevice25 protocol, whose 0x0F:0x03 frame payload matches the OpenRazer keyboards,
/// so these borrow a same-family sibling's transaction set and only override identity
/// and device matrix size. They stay outside <see cref="RazerHidDeviceCatalog"/> so the
/// keyboard page keeps owning game mode, polling and battery.
/// </summary>
public static class StudioKeyboardCatalog
{
    private static readonly IReadOnlyDictionary<ushort, RazerHidDeviceDefinition> Definitions = Build();
    private static readonly ConcurrentDictionary<(ushort ProductId, string Path), RazerHidDeviceConnection> Connections = new();

    public static bool TryGet(ushort productId, out RazerHidDeviceDefinition definition)
    {
        if (Definitions.TryGetValue(productId, out var found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    /// <summary>
    /// Publishes a keyboard-service connection to the lighting canvas. Only the frame
    /// capability is advertised; the keyboard page remains the settings owner.
    /// </summary>
    public static RazerHidDeviceConnection? CreateConnection(RazerKeyboardConnection keyboard)
    {
        if (!Definitions.TryGetValue(keyboard.ProductId, out var definition))
        {
            return null;
        }

        // The router compares connection identity; returning a fresh object per refresh
        // would rebuild the frame source and layout every time.
        return Connections.GetOrAdd((keyboard.ProductId, keyboard.DevicePath), _ =>
            new RazerHidDeviceConnection(
                definition,
                keyboard.DevicePath,
                physicalDeviceKey: null,
                RazerHidEndpointState.Resolved,
                new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.MatrixFrameWrite },
                new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(),
                error: null));
    }

    private static IReadOnlyDictionary<ushort, RazerHidDeviceDefinition> Build()
    {
        // Rows and columns are the manifest's DeviceMax grid, not its visual MatrixMax grid.
        (ushort Sibling, ushort Product, string Name, byte Rows, byte Columns)[] entries =
        [
            (0x02D7, 0x023E, "Razer Turret Keyboard Xbox One Edition", 6, 18),
            (0x02D7, 0x0A23, "Razer Turret Keyboard Gears of War 5 Edition", 6, 18),
            (0x02D7, 0x02B1, "Razer Huntsman V3 X Tenkeyless", 6, 18),
            (0x02D7, 0x02B3, "Razer BlackWidow V4 Pro 75%", 6, 18),
            (0x02A7, 0x02C4, "Razer Pro Type Ergo", 1, 19),
            (0x02CF, 0x02CC, "Razer BlackWidow V4 Low-profile HyperSpeed", 7, 22),
            (0x02CF, 0x02D0, "Razer Huntsman V3 Pro Tenkeyless 8KHz", 6, 22),
            (0x02B0, 0x02D1, "Razer Huntsman V3 Pro Mini 8KHz", 5, 15),
            (0x02D7, 0x02D4, "Razer BlackWidow V4 Low-Profile Tenkeyless HyperSpeed", 6, 18),
            (0x02CF, 0x02D8, "Razer Huntsman Signature Edition", 6, 22),
            (0x02A5, 0x02E3, "Razer Reclusa X Mini 65%", 5, 16),
            (0x02A5, 0x02E4, "Razer Huntsman V3 HE Magnetic Mini 65% 8KHz", 5, 16),
            (0x02CF, 0x02E5, "Razer Huntsman V3 Tenkeyless 8KHz", 6, 22),
            (0x02CF, 0x02E6, "Razer Huntsman V3 Pro Low-Profile Tenkeyless 8KHz", 6, 22),
            (0x02D7, 0x02EA, "Razer Huntsman V3 HE Magnetic Tenkeyless 8KHz", 6, 18),
            (0x02D7, 0x02F0, "Razer BlackWidow V4 75% XBOX 25 Anniversary Edition", 7, 18),
            (0x02A5, 0x02F4, "Razer Reclusa Mini 65%", 5, 16),
        ];

        var catalog = RazerHidDeviceCatalog.BuiltIn;
        var definitions = new Dictionary<ushort, RazerHidDeviceDefinition>();
        foreach (var entry in entries)
        {
            var sibling = catalog.Find(RazerHidDeviceDefinition.VendorId, entry.Sibling);
            if (sibling is null)
            {
                continue;
            }

            definitions[entry.Product] = new RazerHidDeviceDefinition(
                entry.Product,
                entry.Name,
                DeviceCategory.Keyboard,
                sibling.SourceCapabilities,
                sibling.MaximumDpi,
                sibling.PollingRates,
                new RazerHidMatrixDimensions(entry.Rows, entry.Columns),
                sibling.DeviceWait,
                sibling.Transactions,
                sibling.BuilderArgumentPrefixes,
                sibling.DefaultStorage,
                sibling.DefaultLedZone,
                sibling.CommandSequences,
                sibling.CommandSizes);
        }

        return definitions;
    }
}
