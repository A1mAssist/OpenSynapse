using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The scan output is the only evidence an unlisted device is mapped from, so the header,
/// the status legend, and the row layout are part of the contract.
/// </summary>
public sealed class HidCommandScanTests
{
    [Fact]
    public void HeaderNamesTheDeviceAndExplainsTheStatusBytes()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0245);
        Assert.NotNull(definition);

        var header = HidCommandScan.FormatHeader(definition.DisplayName, definition.ProductId);

        Assert.Contains("Razer Blade 15 (Mid 2019) Mercury", header, StringComparison.Ordinal);
        Assert.Contains("1532:0245", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x05 not supported", header, StringComparison.Ordinal);
    }

    [Fact]
    public void RowCarriesClassCommandStatusAndArguments()
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset] = 0x01;
        response[RazerFeatureReport.ArgumentsOffset + 1] = 0x8E;

        var row = HidCommandScan.FormatRow(0x0D, 0x8F, response);

        Assert.StartsWith("0d 8f 02 018e", row, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanClassListCoversTheVendorBaseBlockAndTheThermalBlock()
    {
        Assert.Contains((byte)0x00, HidCommandScan.ScanClasses);
        Assert.Contains((byte)0x0D, HidCommandScan.ScanClasses);
    }
}
