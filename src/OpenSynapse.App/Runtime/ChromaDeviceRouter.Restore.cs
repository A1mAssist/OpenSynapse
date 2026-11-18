using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.Runtime;

internal sealed partial class ChromaDeviceRouter
{
    /// <summary>Restores the effect each whole-device target had before the session.</summary>
    internal async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RestoreStandardAsync(cancellationToken).ConfigureAwait(false);
            await RestoreJolieAsync(cancellationToken).ConfigureAwait(false);
            await RestoreHidAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var instanceId in _jolieBaselines.Keys)
            {
                _lastFrames.Remove(instanceId);
            }
            foreach (var instanceId in _hidBaselines.Keys)
            {
                _lastFrames.Remove(instanceId);
            }
            _standardBaselines.Clear();
            _baselineAttempted.Clear();
            _hidBaselines.Clear();
            _hidBaselineAttempted.Clear();
            _customEffectPrepared.Clear();
            _jolieBaselines.Clear();
            _jolieBaselineAttempted.Clear();
            _joliePrepared.Clear();
            if (_makoService is not null)
            {
                await _makoService.DisposeAsync().ConfigureAwait(false);
                _makoService = null;
                _makoPath = null;
            }
            _gate.Release();
        }
    }

    private async Task RestoreStandardAsync(CancellationToken cancellationToken)
    {
        foreach (var (instanceId, state) in _standardBaselines.ToArray())
        {
            if (state.Effect is not { } effect ||
                !_standardServices.TryGetValue(instanceId, out var service))
            {
                continue;
            }

            try
            {
                await service.SetEffectAsync(effect, state.Colors, cancellationToken).ConfigureAwait(false);
                if (state.BrightnessPercent is byte brightness)
                {
                    await service.SetBrightnessAsync(brightness, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (IsDeviceFailure(exception))
            {
                _diagnostic?.Invoke($"Chroma restore for {instanceId} failed: {exception.Message}");
            }
        }
    }

    private async Task RestoreJolieAsync(CancellationToken cancellationToken)
    {
        foreach (var (instanceId, state) in _jolieBaselines.ToArray())
        {
            if (state.Effect is not { } effect || _makoService is null ||
                !StringComparer.OrdinalIgnoreCase.Equals(_makoPath, instanceId))
            {
                continue;
            }

            try
            {
                await _makoService.SetEffectAsync(effect, state.Colors, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsDeviceFailure(exception))
            {
                _diagnostic?.Invoke($"Chroma restore for {instanceId} failed: {exception.Message}");
            }
        }
    }

    private async Task RestoreHidAsync(CancellationToken cancellationToken)
    {
        if (_hid is null)
        {
            return;
        }

        foreach (var (instanceId, baseline) in _hidBaselines.ToArray())
        {
            var connection = FindHid(instanceId);
            if (connection is null) continue;
            try
            {
                if (baseline is null)
                {
                    if (!await RazerHidLightingBaseline.ApplyExitEffectAsync(
                            _hid, connection, cancellationToken).ConfigureAwait(false))
                    {
                        _diagnostic?.Invoke(
                            $"Chroma restore for {instanceId} failed: no exit effect is available.");
                    }
                    continue;
                }

                foreach (var failure in await baseline.RestoreAsync(
                             _hid, connection, cancellationToken).ConfigureAwait(false))
                {
                    _diagnostic?.Invoke($"Chroma restore for {instanceId} failed: {failure.Message}");
                }
            }
            catch (Exception exception) when (RazerHidLightingBaseline.IsDeviceFailure(exception))
            {
                _diagnostic?.Invoke($"Chroma restore for {instanceId} failed: {exception.Message}");
            }
        }
    }
}
