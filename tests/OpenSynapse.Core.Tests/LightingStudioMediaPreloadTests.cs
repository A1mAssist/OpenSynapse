using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioMediaPreloadTests
{
    [Fact]
    public void GroupedGenerateLayersAndTheirRegionsArePreloaded()
    {
        var profile = new StudioProfile("Profile");
        var group = StudioLayerItem.CreateGroup("Group");
        var layer = StudioLayerItem.CreateLayer("Generate", StudioEffectKind.ChromaGenerate);
        layer.Params.MediaSrc = @"C:\media\layer.mp4";
        var region = new StudioRegion
        {
            Params = new StudioEffectParams
            {
                Effect = StudioEffectKind.ChromaGenerate,
                MediaSrc = @"C:\media\region.mp4",
            },
        };
        layer.Regions.Add(region);
        group.Children.Add(layer);
        profile.Items.Add(group);

        var preloaded = StudioMediaPreload.EnumerateGenerateParams(profile).ToArray();

        Assert.Equal(2, preloaded.Length);
        Assert.Same(layer.Params, preloaded[0]);
        Assert.Same(region.Params, preloaded[1]);
    }

    [Fact]
    public void NonGenerateLayersAreIgnored()
    {
        var profile = new StudioProfile("Profile");
        profile.Items.Add(StudioLayerItem.CreateLayer("Solid", StudioEffectKind.Solid));
        var generate = StudioLayerItem.CreateLayer("Generate", StudioEffectKind.ChromaGenerate);
        profile.Items.Add(generate);

        var preloaded = StudioMediaPreload.EnumerateGenerateParams(profile).ToArray();

        Assert.Same(generate.Params, Assert.Single(preloaded));
    }
}
