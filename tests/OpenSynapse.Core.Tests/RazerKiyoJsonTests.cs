using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerKiyoJsonTests
{
    [Theory]
    [InlineData(0x0E03, "RzNative_0E03.dll", "Razer Kiyo")]
    [InlineData(0x0E05, "RzNative_0E05.dll", "Razer Kiyo Pro")]
    [InlineData(0x0E06, "RzNative_0E06.dll", "Razer Kiyo X")]
    [InlineData(0x0E08, "RzNative_0E08.dll", "Razer Kiyo Pro Ultra")]
    [InlineData(0x0E0A, "RzNative_GenericCamera.dll", "Razer Kiyo V2 Pro")]
    [InlineData(0x0E0B, "RzNative_GenericCamera.dll", "Razer Kiyo V2")]
    [InlineData(0x0E0C, "RzNative_GenericCamera.dll", "Razer Kiyo V2 X")]
    public void Find_ReturnsBundleProfile(ushort productId, string dllName, string productName)
    {
        var profile = RazerKiyoDevices.Find(productId);

        Assert.NotNull(profile);
        Assert.Equal(productId, profile.ProductId);
        Assert.Equal(dllName, profile.DllName);
        Assert.Equal(productName, profile.ProductName);
        Assert.Equal($"VID_1532 / PID_{productId:X4}", profile.Identity);
    }

    [Fact]
    public void SupportedProductIds_ContainsTheSevenBundlePids()
    {
        Assert.Equal(7, RazerKiyoDevices.All.Count);
        Assert.Equal(
            new ushort[] { 0x0E03, 0x0E05, 0x0E06, 0x0E08, 0x0E0A, 0x0E0B, 0x0E0C },
            RazerKiyoDevices.All.Select(device => device.ProductId));
        Assert.All(
            RazerKiyoDevices.All,
            device => Assert.Contains(device.ProductId, RazerKiyoDevices.SupportedProductIds));
    }

    [Fact]
    public void Find_ReturnsNullForAnUnsupportedPid()
    {
        Assert.Null(RazerKiyoDevices.Find(0x0E04));
    }

    [Fact]
    public void ParseValue_ReadsQuotedValueAndAutoFlag()
    {
        var value = RazerKiyoJson.ParseValue("{\"value\":\"50\", \"isAuto\":\"true\"} \r\n");

        Assert.Equal(new RazerKiyoValue(50, true), value);
    }

    [Fact]
    public void ParseValue_ReadsQuotedFalse()
    {
        var value = RazerKiyoJson.ParseValue("{\"value\":\"4000\", \"isAuto\":\"false\"} \r\n");

        Assert.Equal(new RazerKiyoValue(4000, false), value);
    }

    [Fact]
    public void ParseValue_AcceptsPlainNumbersAndBooleans()
    {
        var value = RazerKiyoJson.ParseValue("{\"value\":121,\"isAuto\":true}");

        Assert.Equal(new RazerKiyoValue(121, true), value);
    }

    [Fact]
    public void ParseValue_DefaultsMissingAutoFlagToFalse()
    {
        var value = RazerKiyoJson.ParseValue("{\"value\":\"128\"}");

        Assert.Equal(new RazerKiyoValue(128, false), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    public void ParseValue_ReturnsNullWhenTheDllHasNoValue(string json)
    {
        Assert.Null(RazerKiyoJson.ParseValue(json));
    }

    [Fact]
    public void ParseFirmwareVersion_JoinsTheFourBytes()
    {
        Assert.Equal(
            "1.0.16.0",
            RazerKiyoJson.ParseFirmwareVersion("{\"major\":1,\"minor\":0,\"internal\":16,\"reserved\":0}"));
    }

    [Fact]
    public void ParseFirmwareVersion_ReturnsEmptyWhenAFieldIsMissing()
    {
        Assert.Equal(string.Empty, RazerKiyoJson.ParseFirmwareVersion("{\"major\":1}"));
    }

    [Fact]
    public void FindContainerId_DoesNotThrowWhenNoCameraIsPresent()
    {
        var containerId = RazerKiyoDiscovery.FindContainerId(RazerKiyoDevices.DefaultProductId);

        Assert.True(containerId is null || containerId.Value != Guid.Empty);
    }

    [Fact]
    public void FindDevices_OnlyReturnsSupportedPids()
    {
        var devices = RazerKiyoDiscovery.FindDevices();

        Assert.All(
            devices,
            device => Assert.Contains(device.ProductId, RazerKiyoDevices.SupportedProductIds));
        Assert.All(devices, device => Assert.NotEqual(Guid.Empty, device.ContainerId));
    }

    [Fact]
    public void ParseSerialNumber_ReadsAsciiField()
    {
        Assert.Equal(
            "AB12C",
            RazerKiyoJson.ParseSerialNumber("{\"serialNumberAscii\":\"AB12C\"}"));
        Assert.Equal(string.Empty, RazerKiyoJson.ParseSerialNumber("{}"));
    }
}
