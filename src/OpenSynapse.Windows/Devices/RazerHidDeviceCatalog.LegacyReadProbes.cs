using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Synapse 2 P1 devices use the same 91-byte command envelope with a raw
/// command id that is OR-ed with 0x80 for reads. The command ids below come
/// from the P1 table in artifacts/synapse2-2.25.1028.6/aio/HANDOFF-MISSING-DEVICES.md.
/// This admission is read-only: every entry has a get_* builder only, so no
/// write path can be reached before the exact product id has on-device
/// evidence.
///
/// Mouse first-round order is Info firmware 00/87 (four bytes; 00/81 is the
/// two-byte fallback), serial 00/82, P1 DPI 04/81, polling 00/85 and standard LED
/// brightness 03/83; the three dockType-0 mice (0021/0031/0033) use 00/93 with
/// 00/91 fallback and 00/92 serial. Keyboards use the same Info pair, serial and
/// brightness. All frames use transaction 0xFF. The 12 mice carry the
/// byte DPI builder and P1 scaling; the 10 keyboards carry Info plus brightness.
/// Blade 0116/011D are admitted for firmware Info only; their serial comes from
/// the system and their keyboard/display paths are separate transports. Turret
/// 0206 and Serval 0900 stay out, as do the audio-only product ids, because no
/// 91-byte command table exists for them.
///
/// The remaining P1 reads are deliberately absent: 04/85 (DPI stages) has no
/// byte-exact builder (the existing 04/85 builder is the modern DPI read, and
/// GetDpiStages sends 04/86); 05/80/05/81 only exist in the fixed-0x1F OBM
/// service, whose 05/81 frame declares 0x50 bytes instead of P1's 11; 0E/84 and
/// 0F/84 either lose to the standard 03/83 builder or make lighting selection
/// ambiguous. Add them only with a byte-exact builder and a frame test.
/// </summary>
public sealed partial class RazerHidDeviceCatalog
{
    private static readonly FrozenSet<string> LegacyMouseReadCapabilities =
        new[] { "get_firmware", "get_dpi_xy_byte", "get_poll_rate", "get_brightness" }
            .ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> LegacyKeyboardReadCapabilities =
        new[] { "get_firmware", "get_brightness" }
            .ToFrozenSet(StringComparer.Ordinal);

    // Synapse 2 marks these three product ids as dockType 0. Their Info reads use
    // 00/91 (firmware fallback) and 00/92 (serial); every other P1 probe uses
    // 00/81 and 00/82.
    internal static bool UsesLegacyDockTypeZero(ushort productId) =>
        productId is 0x0021 or 0x0031 or 0x0033;

    private static void AddLegacyReadProbes(IDictionary<ushort, RazerHidDeviceDefinition> devices)
    {
        // Sibling rows are only used for the page shape (name, category, max DPI);
        // their capabilities and writes are deliberately not inherited.
        (ushort Product, string Name, int? MaxDpi)[] mice =
        [
            (0x0016, "Razer DeathAdder 3500", 3500),
            (0x0014, "Razer Orochi", 4000),
            (0x0021, "Razer Naga Epic", 5600),
            (0x0033, "Razer Ouroboros", 8200),
            (0x003C, "Razer Abyssus 1800", 1800),
            (0x0007, "Razer DeathAdder", null),
            (0x0029, "Razer DeathAdder Black", null),
            (0x002A, "Razer SWTOR Gaming Mouse", null),
            (0x0031, "Razer SWTOR Gaming Mouse", null),
            (0x0035, "Razer Krait 2013", null),
            (0x0049, "Razer Turret Mouse", null),
            (0x0056, "Razer Orochi", null),
        ];
        foreach (var (product, name, maxDpi) in mice)
        {
            AddProbe(devices, product, name, DeviceCategory.Mouse, maxDpi,
                LegacyMouseReadCapabilities, RazerHidLedZone.All, [0x01], p1Dpi: true);
        }

        (ushort Product, string Name)[] keyboards =
        [
            (0x0119, "Razer DeathStalker Essential"),
            (0x011F, "Razer DeathStalker Essential"),
            (0x002B, "Razer SWTOR Gaming Keyboard"),
            (0x0109, "Razer Lycosa"),
            (0x010B, "Razer Arctosa"),
            (0x0110, "Razer Cyclosa"),
            (0x0114, "Razer Deathstalker Ultimate"),
            (0x011E, "Razer Edge"),
            (0x020D, "Razer Cynosa Pro"),
            (0x020E, "Razer Cynosa"),
        ];
        foreach (var (product, name) in keyboards)
        {
            AddProbe(devices, product, name, DeviceCategory.Keyboard, null,
                LegacyKeyboardReadCapabilities, RazerHidLedZone.Backlight, [0x01, 0x05], p1Dpi: false);
        }

        // The two Blade ids expose the Info channel but their keyboard, display and
        // touch paths are separate interfaces. Blade serial numbers come from the
        // system SMBIOS, not HID, so only the firmware read is admitted.
        (ushort Product, string Name, DeviceCategory Category)[] bladeInfo =
        [
            (0x0116, "Razer Blade Keyboard", DeviceCategory.Keyboard),
            (0x011D, "Razer Blade", DeviceCategory.Laptop),
        ];
        foreach (var (product, name, category) in bladeInfo)
        {
            AddFirmwareProbe(devices, product, name, category);
        }
    }

    private static void AddFirmwareProbe(
        IDictionary<ushort, RazerHidDeviceDefinition> devices,
        ushort productId,
        string displayName,
        DeviceCategory category)
    {
        if (devices.ContainsKey(productId))
        {
            return;
        }

        devices.Add(productId, new RazerHidDeviceDefinition(
            productId,
            displayName,
            category,
            new[] { "get_firmware" }.ToFrozenSet(StringComparer.Ordinal),
            null,
            Array.AsReadOnly(Array.Empty<int>()),
            matrixDimensions: null,
            TimeSpan.FromMilliseconds(5),
            new Dictionary<string, byte>(StringComparer.Ordinal)
            {
                ["razer_chroma_standard_get_firmware_version"] = 0xFF,
            }.ToFrozenDictionary(StringComparer.Ordinal),
            FrozenDictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>.Empty,
            RazerHidStorage.Persistent,
            category == DeviceCategory.Keyboard ? RazerHidLedZone.Backlight : RazerHidLedZone.All,
            FrozenDictionary<string, IReadOnlyList<RazerHidCommandStep>>.Empty,
            usesLegacyInfo: true));
    }

    private static void AddProbe(
        IDictionary<ushort, RazerHidDeviceDefinition> devices,
        ushort productId,
        string displayName,
        DeviceCategory category,
        int? maximumDpi,
        FrozenSet<string> capabilities,
        RazerHidLedZone ledZone,
        byte[] brightnessPrefix,
        bool p1Dpi)
    {
        if (devices.ContainsKey(productId))
        {
            return;
        }

        var transactions = new Dictionary<string, byte>(StringComparer.Ordinal)
        {
            ["razer_chroma_standard_get_firmware_version"] = 0xFF,
            ["razer_chroma_standard_get_serial"] = 0xFF,
            ["razer_chroma_standard_get_led_brightness"] = 0xFF,
        };
        if (category == DeviceCategory.Mouse)
        {
            transactions["razer_chroma_misc_get_dpi_xy_byte"] = 0xFF;
            transactions["razer_chroma_misc_get_polling_rate"] = 0xFF;
            // P1 current profile read 05/84. GetPollingRateAsync uses it only to
            // resolve the profile id, so it stays out of the published capabilities.
            transactions["razer_chroma_misc_get_active_profile"] = 0xFF;
        }

        devices.Add(productId, new RazerHidDeviceDefinition(
            productId,
            displayName,
            category,
            capabilities,
            maximumDpi,
            Array.AsReadOnly(Array.Empty<int>()),
            matrixDimensions: null,
            TimeSpan.FromMilliseconds(5),
            transactions.ToFrozenDictionary(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(StringComparer.Ordinal)
            {
                ["razer_chroma_standard_get_led_brightness"] =
                    Array.AsReadOnly(new[] { new ReadOnlyMemory<byte>(brightnessPrefix) }),
            }.ToFrozenDictionary(StringComparer.Ordinal),
            RazerHidStorage.Persistent,
            ledZone,
            FrozenDictionary<string, IReadOnlyList<RazerHidCommandStep>>.Empty,
            usesP1DpiScale: p1Dpi,
            usesLegacyInfo: true));
    }
}

