using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Pins the wire format of the mouse builders to the Razer middleware bundles.
/// The middleware sends <c>sendCommand([dataSize, commandClass, commandId], name, data)</c>,
/// and a fourth argument overrides the declared data size, so both the header
/// triple and the declared size are observable on the wire.
/// </summary>
public sealed class OpenRazerMiddlewareAlignmentTests
{
    private static OpenRazerDeviceDefinition Device(ushort productId) =>
        OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

    [Fact]
    public void SmartReelUsesTheEightyByteScrollModeAutoSwitchFrame()
    {
        var device = Device(0x00CB);

        var get = OpenRazerMouseProtocol.GetSmartReel(device).Report;
        Assert.Equal(0x50, get[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x02, get[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x97, get[RazerFeatureReport.CommandIdOffset]);

        var set = OpenRazerMouseProtocol.SetSmartReel(device, true).Report;
        Assert.Equal(0x50, set[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x02, set[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x17, set[RazerFeatureReport.CommandIdOffset]);
        Assert.Equal(0x01, set[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Fact]
    public void DpiStageFramesUseTheEightyByteProfileDpiClassDataHeader()
    {
        var device = Device(0x00CB);

        var get = OpenRazerMouseProtocol.GetDpiStages(device).Report;
        Assert.Equal(0x50, get[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x04, get[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x86, get[RazerFeatureReport.CommandIdOffset]);

        var set = OpenRazerMouseProtocol.SetDpiStages(device, new OpenRazerDpiStages(
            1, [new OpenRazerDpiStage(1, 800, 1600, 400)])).Report;
        Assert.Equal(0x50, set[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x04, set[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x06, set[RazerFeatureReport.CommandIdOffset]);
    }

    [Fact]
    public void DpiStagePayloadWritesSevenBytesPerStageInBigEndianOrder()
    {
        var device = Device(0x00CB);

        var set = OpenRazerMouseProtocol.SetDpiStages(device, new OpenRazerDpiStages(
            2,
            [
                new OpenRazerDpiStage(0, 800, 1600, 400),
                new OpenRazerDpiStage(1, 3200, 6400, 800),
            ])).Report;

        var arguments = set.AsSpan(RazerFeatureReport.ArgumentsOffset);
        Assert.Equal(0x01, arguments[0]);
        Assert.Equal(0x02, arguments[1]);
        Assert.Equal(0x02, arguments[2]);

        Assert.Equal(0x00, arguments[3]);
        Assert.Equal(0x03, arguments[4]);
        Assert.Equal(0x20, arguments[5]);
        Assert.Equal(0x06, arguments[6]);
        Assert.Equal(0x40, arguments[7]);
        Assert.Equal(0x01, arguments[8]);
        Assert.Equal(0x90, arguments[9]);
        Assert.Equal(0x01, arguments[10]);
        Assert.Equal(0x0C, arguments[11]);
        Assert.Equal(0x80, arguments[12]);
        Assert.Equal(0x19, arguments[13]);
        Assert.Equal(0x00, arguments[14]);
        Assert.Equal(0x03, arguments[15]);
        Assert.Equal(0x20, arguments[16]);
    }

    [Fact]
    public void BatteryChargeLimiterPacksTheEnabledFlagIntoTheHighBit()
    {
        var device = Device(0x00CB);

        var set = OpenRazerMouseProtocol.SetBatteryChargeLimiter(device, 80, true).Report;
        Assert.Equal(0x01, set[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x07, set[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x12, set[RazerFeatureReport.CommandIdOffset]);
        // The middleware writes `payload[0] = isEnabled << 7 | value`, so the
        // flag lives in the high bit and the level in the low seven.
        Assert.Equal(0xD0, set[RazerFeatureReport.ArgumentsOffset]);

        var off = OpenRazerMouseProtocol.SetBatteryChargeLimiter(device, 80, false).Report;
        Assert.Equal(0x50, off[RazerFeatureReport.ArgumentsOffset]);
    }

    [Fact]
    public void BatteryChargeLimiterReadsTheLevelAndTheFlagBackOut()
    {
        var response = OpenRazerMouseProtocol.GetBatteryChargeLimiter(Device(0x00CB)).Report;
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset] = 0xD0;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        var (level, enabled) = OpenRazerMouseProtocol.ParseBatteryChargeLimiter(response);
        Assert.Equal(80, level);
        Assert.True(enabled);
    }

    [Fact]
    public void AutoSleepUsesTheClassSevenSleepTimeCommands()
    {
        var device = Device(0x00CB);

        var get = OpenRazerMouseProtocol.GetAutoSleep(device).Report;
        Assert.Equal(0x02, get[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x07, get[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x88, get[RazerFeatureReport.CommandIdOffset]);

        var set = OpenRazerMouseProtocol.SetAutoSleep(device, true).Report;
        Assert.Equal(0x02, set[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x07, set[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x08, set[RazerFeatureReport.CommandIdOffset]);
        Assert.Equal(0x01, set[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Fact]
    public void TimeToDimWritesTheBigEndianDelayAndThePercentage()
    {
        var device = Device(0x00CB);

        var set = OpenRazerMouseProtocol.SetTimeToDim(device, 0, true, 300, 60).Report;
        Assert.Equal(0x05, set[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x07, set[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x0B, set[RazerFeatureReport.CommandIdOffset]);

        var arguments = set.AsSpan(RazerFeatureReport.ArgumentsOffset);
        Assert.Equal(0x00, arguments[0]);
        Assert.Equal(0x01, arguments[1]);
        Assert.Equal(0x01, arguments[2]);
        Assert.Equal(0x2C, arguments[3]);
        Assert.Equal(0x3C, arguments[4]);
    }

    [Fact]
    public void ProfileDebounceWritesTheFourTimingBytes()
    {
        var device = Device(0x00CB);

        var set = OpenRazerMouseProtocol.SetProfileDebounceTime(device, 0, 4, 5, 6, 7).Report;
        Assert.Equal(0x05, set[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x02, set[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x15, set[RazerFeatureReport.CommandIdOffset]);

        var arguments = set.AsSpan(RazerFeatureReport.ArgumentsOffset);
        Assert.Equal(0x00, arguments[0]);
        Assert.Equal(0x04, arguments[1]);
        Assert.Equal(0x05, arguments[2]);
        Assert.Equal(0x06, arguments[3]);
        Assert.Equal(0x07, arguments[4]);
    }

    [Fact]
    public void MacroStorageInfoReadsTheFourBigEndianPairs()
    {
        var response = OpenRazerMouseProtocol.GetMacroStorageInfo(Device(0x00CB)).Report;
        response[1] = 0x02;
        var arguments = response.AsSpan(RazerFeatureReport.ArgumentsOffset);
        arguments[0] = 0x00;
        arguments[1] = 0x0A;
        arguments[2] = 0x01;
        arguments[3] = 0x00;
        arguments[4] = 0x02;
        arguments[5] = 0x00;
        arguments[6] = 0x00;
        arguments[7] = 0x80;
        arguments[8] = 0x00;
        arguments[9] = 0x40;
        arguments[10] = 0x00;
        arguments[11] = 0x20;
        arguments[12] = 0x00;
        arguments[13] = 0x10;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        var info = OpenRazerMouseProtocol.ParseMacroStorageInfo(response);
        Assert.Equal(10, info.MaxId);
        Assert.Equal(256, info.MaxMemoryLow);
        Assert.Equal(512, info.MaxMemoryHigh);
        Assert.Equal(128, info.AvailableMemoryLow);
        Assert.Equal(64, info.AvailableMemoryHigh);
        Assert.Equal(32, info.RecyclableMemoryLow);
        Assert.Equal(16, info.RecyclableMemoryHigh);
    }

    [Theory]
    [InlineData(0x00C0)]
    [InlineData(0x00C1)]
    public void ViperV3ProDpiStagesUseTheMiddlewareProfileClass(ushort productId)
    {
        var device = Device(productId);

        var get = OpenRazerMouseProtocol.GetDpiStages(device).Report;
        Assert.Equal(0x50, get[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x04, get[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x86, get[RazerFeatureReport.CommandIdOffset]);
        Assert.Equal(0x01, get[RazerFeatureReport.ArgumentsOffset]);

        var set = OpenRazerMouseProtocol.SetDpiStages(device, new OpenRazerDpiStages(
            1, [new OpenRazerDpiStage(1, 800, 1600, 400)])).Report;
        Assert.Equal(0x50, set[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x04, set[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x06, set[RazerFeatureReport.CommandIdOffset]);
        Assert.Equal(0x01, set[RazerFeatureReport.ArgumentsOffset]);
    }

    [Fact]
    public void ViperV3ProWirelessPollingUsesTheTwoByteProfileFrame()
    {
        var device = Device(0x00C1);

        var get = OpenRazerMouseProtocol.GetPollingRate(device).Report;
        Assert.Equal(0x02, get[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x00, get[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0xC0, get[RazerFeatureReport.CommandIdOffset]);
        Assert.Equal(0x01, get[RazerFeatureReport.ArgumentsOffset]);

        var set = Assert.Single(OpenRazerMouseProtocol.SetPollingRate(device, 8_000)).Report;
        Assert.Equal(0x02, set[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x00, set[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x40, set[RazerFeatureReport.CommandIdOffset]);
        Assert.Equal(0x01, set[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, set[RazerFeatureReport.ArgumentsOffset + 1]);
    }
}
