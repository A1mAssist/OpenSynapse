using Microsoft.UI.Xaml;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage
{
    private readonly DispatcherTimer _captureSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private StudioScreenRect _pendingCaptureRegion;
    private bool _captureSavePending;

    private void OnCaptureRegionChanged(StudioScreenRect region)
    {
        _inputSampler?.SetCaptureRegion(region);
        _pendingCaptureRegion = region;
        _captureSavePending = true;
        _captureSaveTimer.Stop();
        _captureSaveTimer.Start();
    }

    private void SavePendingCaptureRegion()
    {
        _captureSaveTimer.Stop();
        if (!_captureSavePending) return;
        _captureSavePending = false;
        if (Host is null || Host.BehaviorSettings.AmbientCaptureRegion == _pendingCaptureRegion) return;

        Host.BehaviorSettings.AmbientCaptureRegion = _pendingCaptureRegion;
        try
        {
            Host.BehaviorSettings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The capture still uses the new region for this session.
        }
    }
}
