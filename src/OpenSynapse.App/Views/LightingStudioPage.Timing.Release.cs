#if !DEBUG
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

/// <summary>Release no-ops for the DEBUG-only load timing hooks.</summary>
public sealed partial class LightingStudioPage
{
    private void StartStudioTimingIfRequested()
    {
    }

    private void StudioTimingAttachViewModel()
    {
    }

    private void RefreshDevicesWithTiming(string phase) => _viewModel?.RefreshDevices();

    private void StudioTimingOnPropertyChanged(string? propertyName)
    {
    }

    private void StudioTimingMark(string phase, string details = "")
    {
    }

    private void StudioTimingMarkRefresh(
        long started, IReadOnlyList<StudioDeviceVisual> visuals, string reason)
    {
    }

    private void StudioTimingMarkFrameApplied()
    {
    }
}
#endif
