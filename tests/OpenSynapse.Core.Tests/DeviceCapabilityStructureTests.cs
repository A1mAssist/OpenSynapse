using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class DeviceCapabilityStructureTests
{
    [Fact]
    public void BuiltInManifestsDeclareEachVendorProductPairOnce()
    {
        var registry = RazerDeviceRegistry.BuiltIn;
        var duplicates = registry.Manifests
            .SelectMany(manifest => manifest.ProductIds.Select(productId => new
            {
                manifest.Id,
                manifest.VendorId,
                ProductId = productId,
            }))
            .GroupBy(entry => (entry.VendorId, entry.ProductId))
            .Where(group => group.Count() > 1)
            .Select(group =>
                $"{group.Key.VendorId:X4}:{group.Key.ProductId:X4} [{string.Join(", ", group.Select(entry => entry.Id))}]")
            .ToArray();

        Assert.True(
            duplicates.Length == 0,
            "Built-in manifests declare duplicate VID/PID pairs:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, duplicates));

        var unresolved = registry.Manifests
            .SelectMany(manifest => manifest.ProductIds.Select(productId => new
            {
                manifest.Id,
                manifest.VendorId,
                ProductId = productId,
                Resolved = registry.Find(manifest.VendorId, productId),
            }))
            .Where(entry => entry.Resolved?.Id != entry.Id)
            .Select(entry =>
                $"{entry.VendorId:X4}:{entry.ProductId:X4} -> {entry.Resolved?.Id ?? "null"} (expected {entry.Id})")
            .ToArray();

        Assert.True(
            unresolved.Length == 0,
            "Built-in manifest product ids do not resolve back to their manifest:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, unresolved));
    }

    [Fact]
    public void EveryFamilyRequirementHasAMatchingProtocolContract()
    {
        var missing = RazerDeviceRegistry.FamilyRequirements
            .SelectMany(pair => pair.Value
                .Where(capability =>
                    !RazerDeviceRegistry.FamilyContractContains(pair.Key, capability))
                .Select(capability => $"{pair.Key}: {capability}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Protocol family requirements without a matching contract:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryBladeModelCapabilityProductHasABladeManifest()
    {
        var missing = BladeModelCapabilities.AdmittedProductIds
            .Order()
            .Select(productId => (ProductId: productId,
                Manifest: RazerDeviceRegistry.BuiltIn.Find(0x1532, productId)))
            .Where(entry =>
                entry.Manifest is null || !DeviceProtocolFamilies.IsBlade(entry.Manifest.ProtocolFamily))
            .Select(entry => entry.Manifest is null
                ? $"1532:{entry.ProductId:X4} (no built-in manifest)"
                : $"1532:{entry.ProductId:X4} (family '{entry.Manifest.ProtocolFamily}')")
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "BladeModelCapabilities admits product ids without a built-in Blade manifest:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryDeclaredOpenRazerPollingRateHasAnEncodableWrite()
    {
        // The built-in catalog includes local aliases such as the Viper V3 Pro SE
        // identities and the middleware-derived mice, so this covers alias entries too.
        var unencodable = OpenRazerDeviceCatalog.BuiltIn.Devices
            .SelectMany(device => device.PollingRates.Select(rate => (Device: device, Rate: rate)))
            .Select(entry =>
            {
                try
                {
                    var requests = OpenRazerMouseProtocol.SetPollingRate(entry.Device, entry.Rate);
                    return requests.Count == 0
                        ? $"1532:{entry.Device.ProductId:X4} {entry.Device.DisplayName}: {entry.Rate} Hz produced no request"
                        : null;
                }
                catch (Exception exception) when (exception is ArgumentOutOfRangeException or NotSupportedException)
                {
                    return $"1532:{entry.Device.ProductId:X4} {entry.Device.DisplayName}: {entry.Rate} Hz: {exception.Message}";
                }
            })
            .Where(failure => failure is not null)
            .ToArray();

        Assert.True(
            unencodable.Length == 0,
            "Declared polling rates without an encodable write:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, unencodable));
    }
}
