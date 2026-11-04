using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>Generate media loading and live screen/audio input.</summary>
public sealed partial class LightingStudioViewModel
{
    private const int MediaLoadCacheLimit = 128;
    private readonly Dictionary<StudioEffectParams, MediaLoad> _mediaLoads =
        new(ReferenceEqualityComparer.Instance);

    private sealed class MediaLoad(string? source, CancellationTokenSource cancellation)
    {
        public string? Source { get; } = source;
        public CancellationTokenSource Cancellation { get; } = cancellation;
    }

    /// <summary>
    /// Starts every Generate layer/region load independently. The UI keeps its
    /// procedural fallback until the first frame arrives; the remaining video
    /// frames are filled by <see cref="StudioVideoPlayback"/>'s worker.
    /// </summary>
    private Task EnsureMediaSamplerAsync()
    {
        if (_disposed != 0) return Task.CompletedTask;

        var current = StudioMediaPreload
            .EnumerateGenerateParams(_project.SelectedProfile)
            .ToArray();
        var currentSet = new HashSet<StudioEffectParams>(current, ReferenceEqualityComparer.Instance);

        foreach (var pair in _mediaLoads.ToArray())
        {
            if (currentSet.Contains(pair.Key) &&
                string.Equals(pair.Key.MediaSrc, pair.Value.Source, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CancelMediaLoad(pair.Key, pair.Value);
        }

        foreach (var parameters in current)
        {
            var source = parameters.MediaSrc;
            if (_loadedMedia.TryGetValue(parameters, out var loaded) &&
                string.Equals(source, loaded, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_loadedMedia.Count >= MediaLoadCacheLimit) CancelMediaLoads();
            _loadedMedia[parameters] = source;
            var load = new MediaLoad(source, new CancellationTokenSource());
            _mediaLoads[parameters] = load;
            _ = LoadMediaAsync(parameters, load);
        }

        return Task.CompletedTask;
    }

    private async Task LoadMediaAsync(StudioEffectParams parameters, MediaLoad load)
    {
        var cancellationToken = load.Cancellation.Token;
        try
        {
            var sampler = await StudioMediaSource.TryLoadProgressiveAsync(load.Source, cancellationToken);
            if (cancellationToken.IsCancellationRequested || _disposed != 0) return;
            if (!_mediaLoads.TryGetValue(parameters, out var current) ||
                !ReferenceEquals(current, load))
            {
                return;
            }

            parameters.MediaSampler = sampler;
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            if (string.IsNullOrWhiteSpace(load.Source)) return;

            if (sampler is null)
            {
                ForgetMediaLoad(parameters, load);
                ReportMediaFallback(load.Source, unavailable: true, detail: null);
                return;
            }

            var warning = sampler.DecodeWarning;
            if (!string.IsNullOrWhiteSpace(warning))
            {
                ReportMediaFallback(load.Source, unavailable: false, warning);
            }

            // Keep the load record alive until the background cache stops so a
            // later source change can still cancel it after the first frame.
            await sampler.BackgroundLoad;
            if (!cancellationToken.IsCancellationRequested && _disposed == 0 &&
                !string.Equals(sampler.DecodeWarning, warning, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(sampler.DecodeWarning))
            {
                ReportMediaFallback(load.Source, unavailable: false, sampler.DecodeWarning);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A new project or source replaced this load.
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested && _disposed == 0)
            {
                if (!string.IsNullOrWhiteSpace(load.Source)) ForgetMediaLoad(parameters, load);
                ReportMediaFallback(load.Source, unavailable: true, exception.Message);
            }
        }
        finally
        {
            if (_mediaLoads.TryGetValue(parameters, out var current) &&
                ReferenceEquals(current, load))
            {
                _mediaLoads.Remove(parameters);
            }

            load.Cancellation.Dispose();
        }
    }

    /// <summary>Puts a failed or degraded media load on the page status line.</summary>
    private void ReportMediaFallback(string? source, bool unavailable, string? detail)
    {
        if (string.IsNullOrWhiteSpace(source)) return;

        var file = Path.GetFileName(source);
        var reason = string.IsNullOrWhiteSpace(detail)
            ? AppStrings.Text("LightingStudioMediaUnavailableReason")
            : detail.Trim();
        var message = AppStrings.FormatText(
            unavailable ? "LightingStudioStatusMediaUnavailable" : "LightingStudioStatusMediaDecodeFallback",
            file,
            reason);

        void Report() => StatusText = message;
        // Queue even on the UI thread so the project-load status message that
        // follows ApplyProject cannot overwrite the media warning.
        if (_dispatcher is { } dispatcher) dispatcher.TryEnqueue(Report);
        else Report();
    }

    /// <summary>Clears the cache marker so a failed source can be retried later.</summary>
    private void ForgetMediaLoad(StudioEffectParams parameters, MediaLoad load)
    {
        if (_mediaLoads.TryGetValue(parameters, out var current) &&
            ReferenceEquals(current, load))
        {
            _loadedMedia.Remove(parameters);
        }
    }

    private void CancelMediaLoad(StudioEffectParams parameters, MediaLoad load)
    {
        if (_mediaLoads.TryGetValue(parameters, out var current) &&
            ReferenceEquals(current, load))
        {
            _mediaLoads.Remove(parameters);
        }

        _loadedMedia.Remove(parameters);
        try
        {
            load.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The worker already completed.
        }
    }

    /// <summary>Stops queued/active media loads when the project or view model closes.</summary>
    private void CancelMediaLoads()
    {
        foreach (var pair in _mediaLoads.ToArray()) CancelMediaLoad(pair.Key, pair.Value);
        _loadedMedia.Clear();
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
