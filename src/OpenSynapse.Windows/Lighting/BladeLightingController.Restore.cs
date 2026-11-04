using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Lighting;

public sealed partial class BladeLightingController
{
    private async Task RestoreAsync(string devicePath, string deviceName, bool reportFallback, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _turnOffOnStop) != 0)
        {
            return;
        }

        // The Blade 710 manifest documents quickEffectOnExit as hardware effect 3
        // (Spectrum) and exposes no readable original effect. Use that documented
        // exit state instead of inventing a keyboard frame; fall back to the
        // configured frame only when the firmware rejects the effect.
        var wroteExitEffect = false;
        if (TryCreateNativeEffectRequest(BladeLightingEffect.Spectrum, out var exitRequest))
        {
            try
            {
                await SendAsync(devicePath, exitRequest, cancellationToken).ConfigureAwait(false);
                wroteExitEffect = true;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The configured frame below is the last deterministic fallback.
            }
        }

        if (!wroteExitEffect)
        {
            try
            {
                await SendFrameAsync(
                    devicePath,
                    BladeMatrixFramePump.ScaleFrame(_restoreFrame, _matrixRows, _matrixColumns),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception frameFailure)
            {
                try
                {
                    await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception modeFailure)
                {
                    throw new AggregateException(frameFailure, modeFailure);
                }
                throw;
            }
        }

        await ReleaseModeLeaseAsync(CancellationToken.None).ConfigureAwait(false);
        if (reportFallback)
        {
            RestoreFailed?.Invoke(deviceName, new NotSupportedException(wroteExitEffect
                ? "The Blade firmware has no readable original effect; the documented Studio exit effect (Spectrum) was applied."
                : "The Blade firmware has no readable original effect; the documented Studio exit effect was rejected and the default restore frame was written."));
        }
    }
}
