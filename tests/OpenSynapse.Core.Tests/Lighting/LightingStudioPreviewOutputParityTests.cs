using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Preview and device output both feed the same timed layer snapshot into
/// <see cref="StudioCompositor"/>. These tests pin that contract for a
/// whole-device layer and for a masked region.
/// </summary>
public sealed class LightingStudioPreviewOutputParityTests
{
    private const ushort ProductId = 0x0256;
    private const string InstanceId = "kbd-1";
    private const int Rows = 2;
    private const int Columns = 4;

    [Fact]
    public void WholeDeviceLayerRendersIdenticallyForPreviewAndDeviceOutput()
    {
        var project = new StudioProject { Name = "Parity" };
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        layer.Params.Speed = 2;
        profile.Items.Add(layer);
        project.Profiles.Add(profile);
        project.SelectedProfileId = profile.Id;

        var layers = BuildLayers(project, ProductId, InstanceId, Rows, Columns);
        var clock = new StudioPlaybackClock();
        var preview = Render(layers, clock, 2.75);
        var device = Render(layers, clock, 2.75);

        Assert.Equal(preview, device);
        Assert.Contains(preview, color => color != StudioRgb.Black);
    }

    [Fact]
    public void MaskedRegionDiffersOnlyOnItsLedsForBothPaths()
    {
        var project = new StudioProject { Name = "Parity" };
        var profile = new StudioProfile("Default");

        var regionLayer = StudioLayerItem.CreateLayer("Region", StudioEffectKind.Solid);
        regionLayer.Params.Primary = new StudioRgb(255, 0, 0);
        var region = new StudioRegion { Params = regionLayer.Params };
        var device = new StudioRegionDevice { ProductId = ProductId, InstanceId = InstanceId };
        device.Leds.Add(new StudioLedRef(0, 1));
        device.Leds.Add(new StudioLedRef(1, 2));
        region.Devices.Add(device);
        regionLayer.Regions.Add(region);

        var background = StudioLayerItem.CreateLayer("Background", StudioEffectKind.Solid);
        background.Params.Primary = new StudioRgb(0, 0, 255);
        profile.Items.Add(regionLayer);
        profile.Items.Add(background);
        project.Profiles.Add(profile);
        project.SelectedProfileId = profile.Id;

        var mask = StudioMaskBuilder.BuildRegion(region, ProductId, InstanceId, Rows, Columns);
        Assert.Equal(StudioMaskBuilder.Full, mask[0 * Columns + 1]);
        Assert.Equal(StudioMaskBuilder.Full, mask[1 * Columns + 2]);
        Assert.Equal(0, mask[0]);

        var layers = BuildLayers(project, ProductId, InstanceId, Rows, Columns);
        var clock = new StudioPlaybackClock();
        var preview = Render(layers, clock, 1.5);
        var deviceFrame = Render(layers, clock, 1.5);
        Assert.Equal(preview, deviceFrame);

        var backgroundOnly = Render(
            [new StudioLayerRender(background.Params, null, background.Opacity, background.Blend, background.Visible, background.Id)],
            new StudioPlaybackClock(),
            1.5);
        for (var index = 0; index < preview.Length; index++)
        {
            if (mask[index] == StudioMaskBuilder.Full)
                Assert.Equal(new StudioRgb(255, 0, 0), preview[index]);
            else
                Assert.Equal(backgroundOnly[index], preview[index]);
        }

        Assert.Equal(new StudioRgb(0, 0, 255), preview[0]);
    }

    /// <summary>Mirrors the view model's per-device layer snapshot.</summary>
    private static StudioLayerRender[] BuildLayers(
        StudioProject project,
        ushort productId,
        string instanceId,
        int rows,
        int columns)
    {
        var profile = project.SelectedProfile;
        Assert.NotNull(profile);
        var layers = new List<StudioLayerRender>();
        foreach (var layer in profile.EnumerateLayers())
        {
            if (layer.IsGroup) continue;
            if (layer.Regions.Count == 0)
            {
                layers.Add(new StudioLayerRender(
                    layer.Params, null, layer.Opacity, layer.Blend, layer.Visible, layer.Id));
                continue;
            }

            foreach (var region in layer.Regions)
            {
                var mask = StudioMaskBuilder.BuildRegion(region, productId, instanceId, rows, columns);
                if (mask.Any(value => value != 0))
                    layers.Add(new StudioLayerRender(
                        region.Params, mask, layer.Opacity, layer.Blend, layer.Visible, layer.Id));
            }
        }

        return layers.ToArray();
    }

    private static StudioRgb[] Render(
        IReadOnlyList<StudioLayerRender> layers,
        StudioPlaybackClock clock,
        double seconds)
    {
        var timed = new (StudioLayerRender Layer, double Seconds)[layers.Count];
        for (var index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            var state = clock.Sample(seconds, layer.Params);
            timed[index] = (layer with { Opacity = state.Running ? layer.Opacity : 0 }, state.LocalSeconds);
        }

        return StudioCompositor.RenderTimedLayers(timed, Rows, Columns);
    }
}
