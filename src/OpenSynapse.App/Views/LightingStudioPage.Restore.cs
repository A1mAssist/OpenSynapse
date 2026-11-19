using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage
{
    /// <summary>
    /// Replays the effect that was live when the app last exited. The page stays
    /// hidden; the hardware pump does not need the window.
    /// </summary>
    internal async Task<bool> RestoreAppliedEffectAsync()
    {
        if (_viewModel is null || !LightingStudioViewModel.HasAppliedProject)
        {
            return false;
        }

        _savedProjectLoaded = true;
        if (!_viewModel.LoadAppliedProject())
        {
            _savedProjectLoaded = false;
            return false;
        }

        BindInspector();
        RenderPreview();
        await _viewModel.ApplyAsync();
        return _viewModel.IsApplied;
    }
}
