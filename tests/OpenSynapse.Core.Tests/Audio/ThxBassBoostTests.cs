using OpenSynapse.Core.Audio;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ThxBassBoostTests
{
    [Fact]
    public void BassBoostIsOptionalSoOlderStatePayloadsRemainValid()
    {
        var state = new ThxAudioState(
            ThxAudioEndpoint.Headphones,
            SpatialEnabled: false,
            EqualizerEnabled: false,
            SelectedPresetId: null,
            Presets: [],
            EqualizerBands: [],
            FactoryCurves: new Dictionary<string, IReadOnlyList<double>>(),
            SoundNormalization: null,
            VoiceClarity: new ThxAudioEnhancement(false, 0),
            CanEdit: true);

        Assert.Null(state.BassBoost);
        Assert.Equal(65, (state with { BassBoost = new ThxAudioEnhancement(true, 65) }).BassBoost?.LevelPercent);
    }

    [Fact]
    public void BassBoostIsWiredToTheV3ExportsAndBothUiPaths()
    {
        var native = Source("src", "OpenSynapse.Windows", "Audio", "ThxV3NativeClient.cs");
        foreach (var export in new[]
        {
            "GetRenderBassBoost", "SetRenderBassBoost",
            "GetRenderBassBoostLevel", "SetRenderBassBoostLevel",
        })
        {
            Assert.Contains(export, native);
        }

        var helper = Source("src", "OpenSynapse.Windows", "Audio", "ThxHeadsetHelperSession.cs");
        Assert.Contains("bassboost_enabled", helper);
        Assert.Contains("bassboost_level", helper);
        Assert.Contains("\"bassBoost\"", helper);
        Assert.Contains("BassBoost: new ThxAudioEnhancement", helper);

        var audioViewModel = Source("src", "OpenSynapse.App", "ViewModels", "ThxAudioViewModel.cs");
        Assert.Contains("ThxAudioEnhancementKind.BassBoost", audioViewModel);
        Assert.Contains("ApplyBassBoostAsync", audioViewModel);

        var headsetViewModel = Source("src", "OpenSynapse.App", "ViewModels", "RazerHeadsetAudioViewModel.ThxEnhancements.cs");
        Assert.Contains("ThxBassBoostVisibility", headsetViewModel);
        Assert.Contains("ApplyThxBassBoostAsync", headsetViewModel);
    }

    private static string Source(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepoLayout.Root, .. segments]));
}