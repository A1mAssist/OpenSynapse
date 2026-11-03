using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>Generate media loading and live screen/audio input.</summary>
public sealed partial class LightingStudioViewModel
{
    private async Task EnsureMediaSamplerAsync()
    {
        var parameters = SelectedParams;
        if (parameters is null || parameters.Effect != StudioEffectKind.ChromaGenerate) return;
        var source = parameters.MediaSrc;
        if (parameters.MediaSampler is not null &&
            string.Equals(source, _loadedMediaSrc, StringComparison.OrdinalIgnoreCase)) return;

        _loadedMediaSrc = source;
        parameters.MediaSampler = await StudioMediaSource.TryLoadAsync(source);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task EnsureInputAsync()
    {
        if (_inputSampler is null) return;
        if (NeedsLiveInput())
        {
            await _inputSampler.StartAsync(CancellationToken.None);
        }
        else
        {
            await _inputSampler.StopAsync();
        }
    }

    private bool NeedsLiveInput() =>
        _project.SelectedProfile?.EnumerateLayers().Any(layer =>
            !layer.IsGroup &&
            (layer.Params.Effect is StudioEffectKind.Ambient or StudioEffectKind.Audio ||
             layer.Regions.Any(region =>
                 region.Params.Effect is StudioEffectKind.Ambient or StudioEffectKind.Audio))) == true;
}
