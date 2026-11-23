namespace OpenSynapse.App.ViewModels;

public sealed partial class LightingStudioViewModel
{
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_hidRouter is not null)
        {
            _hidRouter.WriteFailed -= OnOutputWriteFailed;
            _hidRouter.RestoreFailed -= OnHidRestoreFailed;
        }
        if (_argbRouter is not null) _argbRouter.WriteFailed -= OnOutputWriteFailed;
        if (_headsetRouter is not null) _headsetRouter.WriteFailed -= OnOutputWriteFailed;
        if (_chromaRouter is not null)
        {
            _chromaRouter.WriteFailed -= OnOutputWriteFailed;
            _chromaRouter.RestoreFailed -= OnHidRestoreFailed;
        }
        if (_lighting is not null) _lighting.RestoreFailed -= OnHidRestoreFailed;
        DetachProfile(_project.SelectedProfile);
        CancelMediaLoads();
        _visualCancellation?.Cancel();
        _visualCancellation?.Dispose();
        _visualsCancellation?.Cancel();
        _visualsCancellation?.Dispose();
        _visualLoader.Dispose();
        _outputCancellation?.Cancel();
        _outputCancellation?.Dispose();
        if (_inputSampler is { } sampler) _ = sampler.DisposeAsync().AsTask();
        if (_hidRouter is not null) _ = _hidRouter.DisposeAsync().AsTask();
        if (_argbRouter is not null) _ = _argbRouter.DisposeAsync().AsTask();
        if (_headsetRouter is not null) _ = _headsetRouter.DisposeAsync().AsTask();
        if (_chromaRouter is not null) _ = _chromaRouter.DisposeAsync().AsTask();
        _layerCache.Clear();
        _loadedMedia.Clear();
        _loadedChroma.Clear();
        _source = null;
        _appliedBlade = null;
    }
}
