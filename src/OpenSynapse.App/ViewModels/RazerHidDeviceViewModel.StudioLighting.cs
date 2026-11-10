using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private static readonly RazerHidLightingEffect[] SoftwareLightingEffects =
    [
        RazerHidLightingEffect.SoftwareAudioMeter,
        RazerHidLightingEffect.SoftwareSpectrum,
        RazerHidLightingEffect.SoftwareWave,
        RazerHidLightingEffect.SoftwareFire,
        RazerHidLightingEffect.SoftwareWheel,
        RazerHidLightingEffect.SoftwareGradient,
        RazerHidLightingEffect.SoftwareRipple,
        RazerHidLightingEffect.SoftwareTidal,
        RazerHidLightingEffect.SoftwareRadiate,
    ];

    private IReadOnlyList<RazerRgb> RenderStudioSoftwareFrame(
        StudioEffectKind effect,
        LightingPalette palette,
        int rows,
        int columns)
    {
        var parameters = StudioEffectCatalog.CreateDefault(effect);
        // The catalog defaults use Rainbow ColorStops; replace them so the card's
        // colour controls drive the rendered frame.
        parameters.ColorStops.Clear();
        var count = palette.Colors.Count;
        for (var index = 0; index < count; index++)
        {
            var stop = count == 1
                ? 0
                : (int)Math.Round(index * 100d / (count - 1), MidpointRounding.AwayFromZero);
            var color = palette.Colors[index];
            parameters.ColorStops.Add(new StudioColorStop(
                stop, new StudioRgb(color.Red, color.Green, color.Blue)));
        }

        var first = palette.Colors[0];
        var last = palette.Colors[^1];
        parameters.Primary = new StudioRgb(first.Red, first.Green, first.Blue);
        parameters.Secondary = new StudioRgb(last.Red, last.Green, last.Blue);
        parameters.Speed = LightingSpeed * 5d;

        // Ripple renders one finite ring. Wrap its own cycle so the card keeps
        // animating instead of turning black after the first pass.
        var cycle = StudioEffectRenderer.CycleSeconds(parameters, rows, columns);
        var elapsed = _softwareLightingElapsed.TotalSeconds;
        if (cycle > 0)
        {
            elapsed %= cycle;
        }

        var frame = StudioEffectRenderer.Render(parameters, rows, columns, elapsed);
        var colors = new RazerRgb[frame.Length];
        for (var index = 0; index < frame.Length; index++)
        {
            colors[index] = new RazerRgb(frame[index].R, frame[index].G, frame[index].B);
        }

        return colors;
    }
}
