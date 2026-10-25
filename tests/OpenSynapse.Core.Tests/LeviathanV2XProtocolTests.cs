using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LeviathanV2XProtocolTests
{
    [Fact]
    public void EqBandRequest_UsesTheNoaT3Envelope()
    {
        var report = LeviathanV2XProtocol.CreateGetEqBands();

        Assert.Equal(RazerFeatureReport.Length, report.Length);
        Assert.Equal(0x00, report[0]);
        Assert.Equal(0x00, report[2]);
        Assert.Equal(0x0B, report[6]);
        Assert.Equal(0x08, report[7]);
        Assert.Equal(0x84, report[8]);
        Assert.Equal(LeviathanV2XProtocol.ReadProfile, report[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
        Assert.Equal(7, LeviathanV2XProtocol.ReportId);
    }

    [Fact]
    public void SetEqBands_EncodesTenLevels()
    {
        byte[] bands = [0, 10, 20, 30, 40, 50, 60, 70, 80, 100];

        var report = LeviathanV2XProtocol.CreateSetEqBands(bands);

        Assert.Equal(0x0B, report[6]);
        Assert.Equal(0x04, report[8]);
        Assert.Equal(LeviathanV2XProtocol.WriteProfile, report[9]);
        Assert.Equal(bands, report[10..20]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Fact]
    public void PresetAndBassWritesUseTheMiddlewareProfiles()
    {
        var preset = LeviathanV2XProtocol.CreateSetEqPreset(LeviathanV2XEqPreset.Custom);
        var bass = LeviathanV2XProtocol.CreateSetBass(4);

        Assert.Equal(LeviathanV2XProtocol.ReadProfile, preset[9]);
        Assert.Equal((byte)LeviathanV2XEqPreset.Custom, preset[10]);
        Assert.Equal(LeviathanV2XProtocol.WriteProfile, bass[9]);
        Assert.Equal(4, bass[10]);
    }

    [Fact]
    public void RegionListRequest_ParsesTheFirstRegion()
    {
        var report = LeviathanV2XProtocol.CreateGetRegionIds();
        var response = Response(0x05, 0x0F, 0x80, [0x04, 0x01, 0x03, 0x01, 0x00]);

        Assert.Equal(0x50, report[6]);
        Assert.Equal(0x0F, report[7]);
        Assert.Equal(0x80, report[8]);
        Assert.Equal(0x04, LeviathanV2XProtocol.ReadFirstRegion(response));
    }

    [Theory]
    [InlineData(0, "Razer Leviathan V2 X")]
    [InlineData(128, "Razer | Pokémon Pikachu Edition Soundbar")]
    [InlineData(129, "Razer Leviathan V2 X Pokémon AP Edition")]
    [InlineData(131, "Razer | Kuromi Limited Edition Soundbar")]
    [InlineData(132, "Razer Leviathan V2 X Kuromi Edition")]
    public void EditionNamesMatchTheMiddleware(int edition, string expected)
    {
        Assert.Equal(expected, LeviathanV2XProtocol.GetEditionDisplayName((byte)edition));
    }

    [Fact]
    public void TransactionIdsCycleFromZeroThroughThirtyOne()
    {
        var sequence = -1;
        var actual = Enumerable.Range(0, 34)
            .Select(_ => RazerFeatureTransport.NextLeviathanTransactionId(ref sequence))
            .ToArray();

        Assert.Equal(Enumerable.Range(0, 32).Concat([0, 1]).Select(value => (byte)value), actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void SetBass_RejectsOutOfRangeLevels(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LeviathanV2XProtocol.CreateSetBass((byte)level));
    }

    [Fact]
    public void ReadEqBands_ParsesProfileAndTenLevels()
    {
        byte[] levels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var response = Response(0x0B, 0x08, 0x84, [LeviathanV2XProtocol.ReadProfile, .. levels]);

        var bands = LeviathanV2XProtocol.ReadEqBands(response, out var profile);

        Assert.Equal(LeviathanV2XProtocol.ReadProfile, profile);
        Assert.Equal(levels, bands);
    }

    [Fact]
    public void ReadAudioIoStatus_HandlesTheFiveByteAndLegacyForms()
    {
        var fiveByte = LeviathanV2XProtocol.ReadAudioIoStatus(
            Response(0x05, 0x08, 0x8B, [1, 1, 0, 1, 1]));
        var threeByte = LeviathanV2XProtocol.ReadAudioIoStatus(
            Response(0x03, 0x08, 0x8B, [0, 1, 1]));

        Assert.Equal(new LeviathanV2XAudioIoStatus(true, true, false, true, true), fiveByte);
        Assert.Equal(new LeviathanV2XAudioIoStatus(false, true, true, false, false), threeByte);
    }

    [Fact]
    public void ReadEqPreset_AndEdition_UseTheExtractedResponseOffsets()
    {
        var presetResponse = Response(0x02, 0x08, 0x82, [0x00, (byte)LeviathanV2XEqPreset.Custom]);
        var editionResponse = Response(0x03, 0x00, 0x86, [0x01, 0x83, 0x07]);

        Assert.Equal((byte)LeviathanV2XEqPreset.Custom,
            LeviathanV2XProtocol.ReadEqPreset(presetResponse, out var profile));
        Assert.Equal(0, profile);
        Assert.Equal((byte)1, LeviathanV2XProtocol.ReadEditionInfo(editionResponse).KeyboardLayout);
        Assert.Equal((byte)0x83, LeviathanV2XProtocol.ReadEditionInfo(editionResponse).Edition);
        Assert.Equal((byte)7, LeviathanV2XProtocol.ReadEditionInfo(editionResponse).FirmwareId);
    }

    private static byte[] Response(byte dataSize, byte commandClass, byte commandId, byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[0] = LeviathanV2XProtocol.ReportId;
        response[1] = 0x02;
        response[2] = 0x00;
        response[6] = dataSize;
        response[7] = commandClass;
        response[8] = commandId;
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
