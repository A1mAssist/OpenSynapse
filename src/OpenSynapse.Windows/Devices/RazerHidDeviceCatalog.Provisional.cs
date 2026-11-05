using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceCatalog
{
    /// <summary>
    /// A catalog-less 91-byte device gets only the identity GET and the runtime mapping
    /// probe. It never inherits another product's lighting, DPI or macro commands.
    /// </summary>
    internal static RazerHidDeviceDefinition CreateProvisional(
        ushort productId,
        string displayName,
        DeviceCategory category) =>
        new(
            productId,
            displayName,
            category,
            new[] { "get_firmware" }.ToFrozenSet(StringComparer.Ordinal),
            maximumDpi: null,
            Array.AsReadOnly(Array.Empty<int>()),
            matrixDimensions: null,
            TimeSpan.FromMilliseconds(31),
            new Dictionary<string, byte>(StringComparer.Ordinal)
            {
                ["razer_chroma_standard_get_firmware_version"] = 0x1F,
            }.ToFrozenDictionary(StringComparer.Ordinal),
            FrozenDictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>.Empty,
            RazerHidStorage.Persistent,
            category == DeviceCategory.Keyboard ? RazerHidLedZone.Backlight : RazerHidLedZone.All,
            FrozenDictionary<string, IReadOnlyList<RazerHidCommandStep>>.Empty);
}