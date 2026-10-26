using OpenSynapse.Core.Audio;
using OpenSynapse.Windows.Audio;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ThxAudioProtocolTests
{
    private static readonly ThxAudioProfile Profile = new(
        "RZ09-0528",
        "Blade 16 - RZ09-0528",
        "rznb11",
        "02C6",
        "thx_rznb11_service",
        7,
        new Dictionary<string, double[]>(),
        new Dictionary<string, double[]>());

    [Fact]
    public void Rznb11SpeakerBandsExposeSevenBandsStartingAt250Hz()
    {
        var bands = ThxAudioAdmission.ToBands(
            Profile,
            ThxAudioEndpoint.Speakers,
            [99, 99, 99, 0, 1, 2, 3, 4, 5, 6]);

        Assert.Equal([250, 500, 1000, 2000, 4000, 8000, 16000], bands.Select(band => band.FrequencyHertz));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], bands.Select(band => band.GainDb));
    }

    [Fact]
    public void Rznb11SpeakerGainsPadThreeHiddenBands()
    {
        Assert.Equal(
            [0, 0, 0, -1, 1, 3, 4, 0, -3, -6],
            ThxAudioAdmission.ToNativeGains(
                Profile,
                ThxAudioEndpoint.Speakers,
                [-1, 1, 3, 4, 0, -3, -6]));
    }

    [Fact]
    public void VendorJsonAcceptsOnlyTheReviewedShapes()
    {
        Assert.True(ThxVendorJson.ParseBoolean("true"));
        Assert.Equal(["game", "custom"], ThxVendorJson.ParsePresetIds("[\"game\",\"custom\"]"));
        Assert.Equal("podcast", ThxVendorJson.ParseSelectedPreset("\"podcast\""));
        Assert.Throws<InvalidDataException>(() => ThxVendorJson.ParsePresetIds("[\"game\",7]"));
        Assert.Throws<InvalidDataException>(() => ThxVendorJson.ParseFloat("13", -12, 12));
    }

    [Fact]
    public void VendorJsonAcceptsObjectPresetAndGainShapes()
    {
        var presets = ThxVendorJson.ParsePresetEntries(
            "{\"presets\":[{\"id\":\"game\",\"name\":\"Game\"},\"movie\"]}");
        Assert.Equal(["game", "movie"], presets.Select(preset => preset.Id));
        Assert.Equal(["Game", "movie"], presets.Select(preset => preset.Name));

        Assert.Equal([1, -2, 3], ThxVendorJson.ParseGains("[1,-2,3]"));
        Assert.Equal([1, -2], ThxVendorJson.ParseGains(
            "{\"bands\":[{\"gain\":1},{\"value\":-2}]}"));
        Assert.Equal("movie", ThxVendorJson.ParseSelectedPresetValue(
            "{\"id\":\"movie\",\"name\":\"Movie\"}"));

        var v4Presets = ThxVendorJson.ParsePresetEntries(
            "{\"presets\":[{\"name\":\"game\",\"current\":[0,0,0,0,0,0,0,0,0,0]}]}");
        Assert.Equal("game", Assert.Single(v4Presets).Id);
        Assert.Equal("game", ThxVendorJson.ParseSelectedPresetValue("{\"name\":\"game\"}"));
    }
}