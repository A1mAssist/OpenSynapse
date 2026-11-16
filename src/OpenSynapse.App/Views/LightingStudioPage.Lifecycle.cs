namespace OpenSynapse.App.Views;

public sealed partial class LightingStudioPage
{
    internal void Shutdown()
    {
        _timer.Stop();
        _historyTimer.Stop();
        _captureSaveTimer.Stop();
        if (_viewModel is { } viewModel)
        {
            viewModel.CommitHistory();
            viewModel.Dispose();
        }
        _viewModel = null;
        _inputSampler = null;
    }
}
