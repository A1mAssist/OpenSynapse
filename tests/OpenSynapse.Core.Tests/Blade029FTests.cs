using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Blade 16 (2023) RZ09-0483. Its middleware bundle is the source for every contract
/// pinned here: the fixed fan speed it drives from each performance mode, the charge
/// limiter report layout, and the display MUX reports.
/// </summary>
public sealed class Blade029FTests
{
    [Fact]
    public void TheBlade16_2023ManifestCarriesTheDisplayMuxReports()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x029F);

        Assert.NotNull(manifest);
        Assert.Equal("blade-671", manifest!.ProtocolFamily);

        var getter = manifest.GetRequiredCapability("display-mux.get");
        Assert.Equal((byte)0x1F, getter.TransactionId);
        Assert.Equal((byte)0x02, getter.MaximumDataSize);
        Assert.Equal((byte)0x0D, getter.CommandClass);
        Assert.Equal((byte)0x89, getter.CommandId);
        Assert.Equal(new byte[] { 0x00, 0x00 }, getter.Arguments.ToArray());

        Assert.Equal((byte)0x09, manifest.GetRequiredCapability("display-mux.set").CommandId);
        Assert.Equal(
            (byte)0x8A,
            manifest.GetRequiredCapability("display-mux-capabilities.get").CommandId);
    }

    /// <summary>
    /// The MUX reports are the same bytes the 710 family uses, but the Blade 14 (2022)
    /// and the Blade 15 share the blade-671 family and their middleware bundles never
    /// answered the VGA reports, so they must not inherit the switch.
    /// </summary>
    [Theory]
    [InlineData(0x029F, true)]
    [InlineData(0x028C, false)]
    [InlineData(0x0245, false)]
    public void OnlyTheBlade16_2023CarriesTheDisplayMuxReports(ushort productId, bool expected)
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, productId);

        Assert.NotNull(manifest);
        Assert.Equal(expected, manifest!.Capabilities.ContainsKey("display-mux.get"));
        Assert.Equal(expected, manifest.Capabilities.ContainsKey("display-mux.set"));
        Assert.Equal(expected, manifest.Capabilities.ContainsKey("display-mux-capabilities.get"));
    }

    /// <summary>
    /// The official middleware reads the charge limiter as a low-seven-bit threshold
    /// with the top bit as the enabled flag, so 0x4B is a disabled 75 percent limiter
    /// and every code OpenSynapse already knew keeps the value it reported.
    /// </summary>
    [Theory]
    [InlineData((byte)0xB2, 50)]
    [InlineData((byte)0xB7, 55)]
    [InlineData((byte)0xBC, 60)]
    [InlineData((byte)0xC1, 65)]
    [InlineData((byte)0xC6, 70)]
    [InlineData((byte)0xCB, 75)]
    [InlineData((byte)0xD0, 80)]
    [InlineData((byte)0x50, 100)]
    [InlineData((byte)0x4B, 100)]
    public void TheChargeLimiterReportDecodesTheThresholdAndTheEnabledFlag(
        byte raw,
        int expected) =>
        Assert.Equal(expected, RazerDeviceTelemetryReader.DecodeBladeChargeLimit(raw));

    [Fact]
    public void AnEnabledThresholdTheFirmwareDoesNotOfferIsRejected()
    {
        Assert.Throws<InvalidOperationException>(
            () => RazerDeviceTelemetryReader.DecodeBladeChargeLimit(0xC8));
    }

    /// <summary>
    /// The Blade 16 (2023) official modes reach 5200 RPM and Custom reaches 5300, but
    /// the shared fan-target report rejects a write past the global ceiling, so the
    /// slider and the write guard stop at 5000 while the firmware keeps a higher
    /// readback ceiling.
    /// </summary>
    [Fact]
    public void TheFixedFanSliderStopsAtTheGlobalCeilingWhileTheReadbackAllowsMore()
    {
        const ushort productId = 0x029F;

        Assert.Equal(5300, BladeModelCapabilities.MaximumFanTargetRpmFor(productId));
        Assert.Equal(5000, BladeModelCapabilities.MaximumWritableFanTargetRpmFor(productId));
    }

    /// <summary>
    /// A target the firmware already stored above the write ceiling must still read
    /// back, so the readback ceiling stays the model maximum.
    /// </summary>
    [Fact]
    public void AStoredTargetAboveTheWriteCeilingStillReadsBack()
    {
        var request = BladeFanProtocol.CreateGetTargetRequest(BladeFanProtocol.ZoneCpu);
        var response = request.ToArray();
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset + 2] = 0x35;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(
            5300,
            BladeFanProtocol.ParseTarget(
                response,
                BladeFanProtocol.ZoneCpu,
                request,
                BladeModelCapabilities.MaximumFanTargetRpmFor(0x029F)));
    }

    /// <summary>
    /// The fixed speed rides the shared fan-target report, so the request keeps the
    /// 100 RPM step the firmware stores.
    /// </summary>
    [Fact]
    public void TheFixedFanTargetUsesTheSharedHundredRpmStep()
    {
        var request = BladeFanProtocol.CreateSetTargetRequest(BladeFanProtocol.ZoneCpu, 4200);

        Assert.Equal((byte)0x1F, request[2]);
        Assert.Equal((byte)0x03, request[6]);
        Assert.Equal((byte)0x0D, request[7]);
        Assert.Equal((byte)0x01, request[8]);
        Assert.Equal((byte)0x00, request[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal((byte)0x01, request[RazerFeatureReport.ArgumentsOffset + 1]);
        Assert.Equal((byte)0x2A, request[RazerFeatureReport.ArgumentsOffset + 2]);
    }
}
