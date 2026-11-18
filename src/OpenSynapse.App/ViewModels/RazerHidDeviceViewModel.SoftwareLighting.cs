using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    // Software frames leave the keyboard in custom-frame mode, where its own profile
    // keys stop working. Keep what the firmware had so every stop can hand it back.
    private RazerHidLightingBaseline? _softwareLightingBaseline;
    private bool _softwareCustomFrameActive;

    private async Task StopSoftwareLightingAsync(
        CancellationToken cancellationToken,
        bool restore = true)
    {
        if (_softwareLightingRuntime is not null)
        {
            await _softwareLightingRuntime.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        if (_softwareAudioInput is not null)
        {
            await _softwareAudioInput.DisposeAsync().ConfigureAwait(false);
            _softwareAudioInput = null;
        }
        if (restore)
        {
            await RestoreCustomFrameAsync().ConfigureAwait(false);
        }
    }

    internal Task StopLightingRuntimeAsync(
        CancellationToken cancellationToken = default,
        bool restore = true) =>
        StopSoftwareLightingAsync(cancellationToken, restore);

    private async Task RestoreCustomFrameAsync()
    {
        if (!_softwareCustomFrameActive)
        {
            return;
        }

        _softwareCustomFrameActive = false;
        var baseline = _softwareLightingBaseline;
        _softwareLightingBaseline = null;
        try
        {
            if (baseline is null)
            {
                if (!await RazerHidLightingBaseline.ApplyExitEffectAsync(
                        _service, Connection, CancellationToken.None).ConfigureAwait(false))
                {
                    _log?.Invoke($"{Name}: no lighting effect can replace the last software frame.");
                }
                return;
            }

            foreach (var failure in await baseline.RestoreAsync(
                         _service, Connection, CancellationToken.None).ConfigureAwait(false))
            {
                _log?.Invoke($"{Name}: lighting restore failed: {failure.Message}");
            }
        }
        catch (Exception exception) when (RazerHidLightingBaseline.IsDeviceFailure(exception))
        {
            _log?.Invoke($"{Name}: lighting restore failed: {exception.Message}");
        }
    }
}
