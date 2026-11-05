using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHapticoreScrollProtocolTests
{
    [Fact]
    public void ProfileCommandsUseTheMiddlewareTuplesAndPayloadLayout()
    {
        var getActive = RazerHapticoreScrollProtocol.CreateGetActiveStageRequest(0x11, 0x05);
        var setActive = RazerHapticoreScrollProtocol.CreateSetActiveStageRequest(0x12, 0x05, 0x06);
        var getMax = RazerHapticoreScrollProtocol.CreateGetMaxStageRequest(0x13, 0x04);
        var setMax = RazerHapticoreScrollProtocol.CreateSetMaxStageRequest(0x14, 0x04, 0x03);
        var getTick = RazerHapticoreScrollProtocol.CreateGetTickSettingRequest(0x15, 0x05, 0x02);
        var setTick = RazerHapticoreScrollProtocol.CreateSetTickSettingRequest(
            0x16,
            0x05,
            0x02,
            true,
            RazerHapticoreScrollTickMode.Mode4,
            1.5f,
            -2.25f,
            0.5f,
            45f,
            90f);
        var getStages = RazerHapticoreScrollProtocol.CreateGetStageInformationRequest(0x17, 0x05);
        var setStages = RazerHapticoreScrollProtocol.CreateSetStageInformationRequest(
            0x18,
            0x05,
            0x02,
            0x03,
            [
                RazerHapticoreScrollStageType.Standard,
                RazerHapticoreScrollStageType.Distinct,
                RazerHapticoreScrollStageType.UltraFine,
            ]);
        var getBrowser = RazerHapticoreScrollProtocol.CreateGetBrowserModeRequest(0x19);
        var setBrowser = RazerHapticoreScrollProtocol.CreateSetBrowserModeRequest(0x1A, true);
        var reset = RazerHapticoreScrollProtocol.CreateSetStageResetRequest(0x1B, 0x05, 0x06);

        AssertFrame(getActive, 0x11, 0x02, 0x80, 0x05);
        AssertFrame(setActive, 0x12, 0x02, 0x00, 0x05, 0x06);
        AssertFrame(getMax, 0x13, 0x02, 0x81, 0x04);
        AssertFrame(setMax, 0x14, 0x02, 0x01, 0x04, 0x03);
        AssertFrame(getTick, 0x15, 0x50, 0x82, 0x05, 0x02);
        AssertFrame(
            setTick,
            0x16,
            0x50,
            0x02,
            0x05, 0x02, 0x01, 0x03,
            0x00, 0x00, 0xC0, 0x3F,
            0x00, 0x00, 0x10, 0xC0,
            0x00, 0x00, 0x00, 0x3F,
            0x00, 0x00, 0x34, 0x42,
            0x00, 0x00, 0xB4, 0x42);
        AssertFrame(getStages, 0x17, 0x09, 0x87, 0x05);
        AssertFrame(
            setStages,
            0x18,
            0x09,
            0x07,
            0x05, 0x02, 0x03, 0x81, 0x82, 0x83, 0x00, 0x00, 0x00);
        AssertFrame(getBrowser, 0x19, 0x01, 0x88, 0x00);
        AssertFrame(setBrowser, 0x1A, 0x01, 0x08, 0x01);
        AssertFrame(reset, 0x1B, 0x02, 0x0A, 0x05, 0x06);
    }

    [Fact]
    public void GeneratorCommandsUseTheMiddlewareTuplesAndPayloadLayout()
    {
        var getSetting = RazerHapticoreScrollProtocol.CreateGetGeneratorSettingRequest(0x21, 0x05, 0x03);
        var setSetting = RazerHapticoreScrollProtocol.CreateSetGeneratorSettingRequest(
            0x22,
            0x05,
            0x03,
            true,
            0x07,
            1.5f,
            -2.25f,
            0.5f,
            45f,
            0x1234,
            0x5678,
            0x9ABC);
        var getTable = RazerHapticoreScrollProtocol.CreateGetGeneratorTableRequest(
            0x23,
            0x05,
            0x03,
            RazerHapticoreScrollTableClass.CcwTable,
            0x0102,
            0x0002);
        var setTable = RazerHapticoreScrollProtocol.CreateSetGeneratorTableRequest(
            0x24,
            0x05,
            0x03,
            RazerHapticoreScrollTableClass.CcwTable,
            0x0102,
            [
                new RazerHapticoreScrollGeneratorPoint(1.5f, -2.25f),
                new RazerHapticoreScrollGeneratorPoint(0.5f, 45f),
            ]);
        var maxPointCount = RazerHapticoreScrollProtocol.CreateGetGeneratorTableMaxPointCountRequest(0x25);
        var version = RazerHapticoreScrollProtocol.CreateGetLibraryVersionRequest(0x26);
        var getMeta = RazerHapticoreScrollProtocol.CreateGetGeneratorCustomMetaDataRequest(0x27, 0x05);

        AssertFrame(getSetting, 0x21, 0x50, 0x83, 0x05, 0x03);
        AssertFrame(
            setSetting,
            0x22,
            0x50,
            0x03,
            0x05, 0x03, 0x01, 0x07,
            0x00, 0x00, 0xC0, 0x3F,
            0x00, 0x00, 0x10, 0xC0,
            0x00, 0x00, 0x00, 0x3F,
            0x00, 0x00, 0x34, 0x42,
            0x12, 0x34,
            0x56, 0x78,
            0x9A, 0xBC);
        AssertFrame(getTable, 0x23, 0x50, 0x84, 0x05, 0x03, 0x02, 0x01, 0x02, 0x00, 0x02);
        AssertFrame(
            setTable,
            0x24,
            0x50,
            0x04,
            0x05, 0x03, 0x02, 0x01, 0x02, 0x00, 0x02,
            0x00, 0x00, 0xC0, 0x3F,
            0x00, 0x00, 0x10, 0xC0,
            0x00, 0x00, 0x00, 0x3F,
            0x00, 0x00, 0x34, 0x42);
        AssertFrame(maxPointCount, 0x25, 0x06, 0x85);
        AssertFrame(version, 0x26, 0x02, 0x86);
        AssertFrame(getMeta, 0x27, 0x50, 0x89, 0x05);
    }

    [Fact]
    public void CustomMetaDataBuilderWritesTheFortyBytePayloadAndDirectionMarkers()
    {
        var up = new RazerHapticoreScrollDirection(
            [
                new(0x11, 0x12),
                new(0x13, 0x14),
                new(0x15, 0x16),
                new(0x17, 0x18),
                new(0x19, 0x1A),
            ]);
        var down = new RazerHapticoreScrollDirection(
            [
                new(0x21, 0x22),
                new(0x23, 0x24),
                new(0x25, 0x26),
                new(0x27, 0x28),
                new(0x29, 0x2A),
            ]);

        var request = RazerHapticoreScrollProtocol.CreateSetGeneratorCustomMetaDataRequest(
            0x31,
            0x05,
            0x09,
            true,
            false,
            0x31,
            0x32,
            0x33,
            0x34,
            up,
            down);

        var expected = new byte[40];
        expected[0] = 0x05;
        expected[1] = 0x28;
        expected[2] = 0x09;
        expected[3] = 0x01;
        expected[4] = 0x00;
        expected[5] = 0x31;
        expected[6] = 0x32;
        expected[7] = 0x33;
        expected[8] = 0x34;
        new byte[]
        {
            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A,
        }.CopyTo(expected, 9);
        expected[19] = 0xFF;
        expected[20] = 0xFF;
        new byte[]
        {
            0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A,
        }.CopyTo(expected, 21);
        expected[31] = 0xFF;
        expected[32] = 0xFF;

        AssertFrame(request, 0x31, 0x50, 0x09, expected);
    }

    [Fact]
    public void ActiveMaxBrowserAndResetParsersReadTheirResponses()
    {
        var activeRequest = RazerHapticoreScrollProtocol.CreateGetActiveStageRequest(0x41, 0x05);
        var active = RazerHapticoreScrollProtocol.ParseActiveStage(
            Response(0x41, 0x02, 0x80, 0x05, 0x02),
            activeRequest);

        Assert.Equal(0x05, active.ProfileId);
        Assert.Equal(0x02, active.Stage);

        var maxRequest = RazerHapticoreScrollProtocol.CreateGetMaxStageRequest(0x42, 0x04);
        var max = RazerHapticoreScrollProtocol.ParseMaxStage(
            Response(0x42, 0x02, 0x81, 0x04, 0x03),
            maxRequest);

        Assert.Equal(0x04, max.ProfileId);
        Assert.Equal(0x03, max.MaxStage);

        var browserRequest = RazerHapticoreScrollProtocol.CreateGetBrowserModeRequest(0x43);
        var browser = RazerHapticoreScrollProtocol.ParseBrowserMode(
            Response(0x43, 0x01, 0x88, 0x01),
            browserRequest);

        Assert.True(browser.Enabled);

        var resetRequest = RazerHapticoreScrollProtocol.CreateSetStageResetRequest(0x44, 0x05, 0x06);
        var reset = RazerHapticoreScrollProtocol.ParseStageReset(
            Response(0x44, 0x02, 0x0A),
            resetRequest);

        Assert.Equal(0x05, reset.ProfileId);
        Assert.Equal(0x06, reset.Stage);
    }

    [Fact]
    public void TickAndGeneratorSettingParsersReadFloatsAndBigEndianTableSizes()
    {
        var tickRequest = RazerHapticoreScrollProtocol.CreateGetTickSettingRequest(0x51, 0x05, 0x02);
        var tick = RazerHapticoreScrollProtocol.ParseTickSetting(
            Response(
                0x51,
                0x50,
                0x82,
                0x05, 0x02, 0x01, 0x03,
                0x00, 0x00, 0xC0, 0x3F,
                0x00, 0x00, 0x10, 0xC0,
                0x00, 0x00, 0x00, 0x3F,
                0x00, 0x00, 0x34, 0x42,
                0x00, 0x00, 0xB4, 0x42),
            tickRequest);

        Assert.Equal(0x05, tick.ProfileId);
        Assert.Equal(0x02, tick.Stage);
        Assert.True(tick.Enabled);
        Assert.Equal(RazerHapticoreScrollTickMode.Mode4, tick.Mode);
        Assert.Equal(1.5f, tick.TickAngle);
        Assert.Equal(-2.25f, tick.TickCurrent);
        Assert.Equal(0.5f, tick.VelocityFactor);
        Assert.Equal(45f, tick.IndexWindowAngle);
        Assert.Equal(90f, tick.WindowAngle);

        var generatorRequest = RazerHapticoreScrollProtocol.CreateGetGeneratorSettingRequest(0x52, 0x05, 0x03);
        var generator = RazerHapticoreScrollProtocol.ParseGeneratorSetting(
            Response(
                0x52,
                0x50,
                0x83,
                0x05, 0x03, 0x01, 0x07,
                0x00, 0x00, 0xC0, 0x3F,
                0x00, 0x00, 0x10, 0xC0,
                0x00, 0x00, 0x00, 0x3F,
                0x00, 0x00, 0x34, 0x42,
                0x12, 0x34,
                0x56, 0x78,
                0x9A, 0xBC),
            generatorRequest);

        Assert.Equal(0x05, generator.ProfileId);
        Assert.Equal(0x03, generator.Stage);
        Assert.True(generator.Enabled);
        Assert.Equal(0x07, generator.AlternateCurrent);
        Assert.Equal(1.5f, generator.ReductionAngleCw);
        Assert.Equal(-2.25f, generator.IndexCountAngleCw);
        Assert.Equal(0.5f, generator.ReductionAngleCcw);
        Assert.Equal(45f, generator.IndexCountAngleCcw);
        Assert.Equal((ushort)0x1234, generator.CurrentTableSizeCw);
        Assert.Equal((ushort)0x5678, generator.CurrentTableSizeCcw);
        Assert.Equal((ushort)0x9ABC, generator.VelocityFactorTableSize);
    }

    [Fact]
    public void GeneratorTableParsesPointPairsAndTheRemainingClass15Reads()
    {
        var tableRequest = RazerHapticoreScrollProtocol.CreateGetGeneratorTableRequest(
            0x61,
            0x05,
            0x03,
            RazerHapticoreScrollTableClass.CcwTable,
            0x0102,
            0x0002);
        var table = RazerHapticoreScrollProtocol.ParseGeneratorTable(
            Response(
                0x61,
                0x50,
                0x84,
                0x05, 0x03, 0x02, 0x01, 0x02, 0x00, 0x02,
                0x00, 0x00, 0xC0, 0x3F,
                0x00, 0x00, 0x10, 0xC0,
                0x00, 0x00, 0x00, 0x3F,
                0x00, 0x00, 0x34, 0x42),
            tableRequest);

        Assert.Equal(0x05, table.ProfileId);
        Assert.Equal(0x03, table.Stage);
        Assert.Equal(RazerHapticoreScrollTableClass.CcwTable, table.TableClass);
        Assert.Equal((ushort)0x0102, table.PointOffset);
        Assert.Equal((ushort)2, table.PointSize);
        Assert.Equal(
            [new RazerHapticoreScrollGeneratorPoint(1.5f, -2.25f), new RazerHapticoreScrollGeneratorPoint(0.5f, 45f)],
            table.Points);

        var countRequest = RazerHapticoreScrollProtocol.CreateGetGeneratorTableMaxPointCountRequest(0x62);
        var count = RazerHapticoreScrollProtocol.ParseGeneratorTableMaxPointCount(
            Response(0x62, 0x06, 0x85, 0x01, 0x00, 0x00, 0x09, 0x00, 0x06),
            countRequest);

        Assert.Equal((ushort)0x0100, count.CwTableMaxPoint);
        Assert.Equal((ushort)0x0009, count.CcwTableMaxPoint);
        Assert.Equal((ushort)0x0006, count.VelocityFactorTableMaxPoint);

        var versionRequest = RazerHapticoreScrollProtocol.CreateGetLibraryVersionRequest(0x63);
        var version = RazerHapticoreScrollProtocol.ParseLibraryVersion(
            Response(0x63, 0x02, 0x86, 0x01, 0x02),
            versionRequest);

        Assert.Equal(0x01, version.Major);
        Assert.Equal(0x02, version.Minor);
    }

    [Fact]
    public void StageInformationParsesAllSixSlots()
    {
        var request = RazerHapticoreScrollProtocol.CreateGetStageInformationRequest(0x71, 0x05);
        var stages = RazerHapticoreScrollProtocol.ParseStageInformation(
            Response(0x71, 0x09, 0x87, 0x05, 0x02, 0x03, 0x81, 0x82, 0x83, 0x00, 0x00, 0x00),
            request);

        Assert.Equal(0x05, stages.ProfileId);
        Assert.Equal(0x02, stages.ActiveStage);
        Assert.Equal(0x03, stages.MaxStage);
        Assert.Equal(RazerHapticoreScrollProtocol.MaxStage, stages.StageTypes.Count);
        Assert.Equal(RazerHapticoreScrollStageType.Standard, stages.StageTypes[0]);
        Assert.Equal(RazerHapticoreScrollStageType.Distinct, stages.StageTypes[1]);
        Assert.Equal(RazerHapticoreScrollStageType.UltraFine, stages.StageTypes[2]);
        Assert.Equal((RazerHapticoreScrollStageType)0, stages.StageTypes[5]);
    }

    [Fact]
    public void CustomMetaDataParserReadsBothDirectionControlPointSets()
    {
        var request = RazerHapticoreScrollProtocol.CreateGetGeneratorCustomMetaDataRequest(0x81, 0x05);
        var responseArguments = new byte[31];
        responseArguments[0] = 0x05;
        responseArguments[1] = 0x28;
        responseArguments[2] = 0x09;
        responseArguments[3] = 0x01;
        responseArguments[4] = 0x00;
        responseArguments[5] = 0x31;
        responseArguments[6] = 0x32;
        responseArguments[7] = 0x33;
        responseArguments[8] = 0x34;
        new byte[]
        {
            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A,
        }.CopyTo(responseArguments, 9);
        new byte[]
        {
            0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A,
        }.CopyTo(responseArguments, 21);

        var meta = RazerHapticoreScrollProtocol.ParseGeneratorCustomMetaData(
            Response(0x81, 0x50, 0x89, responseArguments),
            request);

        Assert.Equal(0x05, meta.ProfileId);
        Assert.Equal(0x28, meta.Size);
        Assert.Equal(0x09, meta.Order);
        Assert.True(meta.IsActive);
        Assert.False(meta.IsLink);
        Assert.Equal(0x31, meta.UpScrollSteps);
        Assert.Equal(0x32, meta.UpScrollTension);
        Assert.Equal(0x33, meta.DownScrollSteps);
        Assert.Equal(0x34, meta.DownScrollTension);
        Assert.Equal(5, meta.UpDirection.ControlPoints.Count);
        Assert.Equal(new RazerHapticoreScrollControlPoint(0x11, 0x12), meta.UpDirection.ControlPoints[0]);
        Assert.Equal(new RazerHapticoreScrollControlPoint(0x19, 0x1A), meta.UpDirection.ControlPoints[4]);
        Assert.Equal(new RazerHapticoreScrollControlPoint(0x21, 0x22), meta.DownDirection.ControlPoints[0]);
        Assert.Equal(new RazerHapticoreScrollControlPoint(0x29, 0x2A), meta.DownDirection.ControlPoints[4]);
    }

    [Fact]
    public void InvalidOrTruncatedReportsAreRejected()
    {
        var tickRequest = RazerHapticoreScrollProtocol.CreateGetTickSettingRequest(0x91, 0x05, 0x02);
        var tableRequest = RazerHapticoreScrollProtocol.CreateGetGeneratorTableRequest(
            0x92,
            0x05,
            0x03,
            RazerHapticoreScrollTableClass.CwTable,
            0,
            10);
        var activeRequest = RazerHapticoreScrollProtocol.CreateGetActiveStageRequest(0x93, 0x05);

        Assert.Throws<InvalidOperationException>(
            () => RazerHapticoreScrollProtocol.ParseTickSetting(new byte[20], tickRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerHapticoreScrollProtocol.ParseGeneratorTable(
                Response(0x92, 0x50, 0x84, 0x05, 0x03, 0x01, 0x00, 0x00, 0x00, 0x0A),
                tableRequest));
        Assert.Throws<InvalidOperationException>(
            () => RazerHapticoreScrollProtocol.ParseActiveStage(
                Response(0x93, 0x02, 0x81, 0x05, 0x02),
                activeRequest));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHapticoreScrollProtocol.CreateGetActiveStageRequest(0x94, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHapticoreScrollProtocol.CreateSetActiveStageRequest(0x95, 0x05, 7));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerHapticoreScrollProtocol.CreateSetGeneratorTableRequest(
                0x96,
                0x05,
                0x03,
                RazerHapticoreScrollTableClass.CwTable,
                0,
                new RazerHapticoreScrollGeneratorPoint[RazerHapticoreScrollProtocol.MaxGeneratorTablePoints + 1]));
    }

    private static void AssertFrame(
        byte[] frame,
        byte transaction,
        byte dataSize,
        byte commandId,
        params byte[] arguments)
    {
        Assert.Equal(RazerFeatureReport.Length, frame.Length);
        Assert.Equal(0x00, frame[0]);
        Assert.Equal(0x00, frame[1]);
        Assert.Equal(transaction, frame[2]);
        Assert.Equal(0x00, frame[3]);
        Assert.Equal(0x00, frame[4]);
        Assert.Equal(0x00, frame[5]);
        Assert.Equal(dataSize, frame[6]);
        Assert.Equal(0x15, frame[7]);
        Assert.Equal(commandId, frame[8]);
        Assert.Equal(arguments, frame.AsSpan(RazerFeatureReport.ArgumentsOffset, arguments.Length).ToArray());
        Assert.All(
            frame[(RazerFeatureReport.ArgumentsOffset + arguments.Length)..89],
            value => Assert.Equal(0, value));
        Assert.Equal(RazerFeatureReport.CalculateCrc(frame), frame[89]);
        Assert.Equal(0x00, frame[90]);
    }

    private static byte[] Response(byte transaction, byte dataSize, byte commandId, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transaction;
        response[6] = dataSize;
        response[7] = 0x15;
        response[8] = commandId;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
