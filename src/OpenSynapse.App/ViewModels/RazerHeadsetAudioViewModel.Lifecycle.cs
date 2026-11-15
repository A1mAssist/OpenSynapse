namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopSoftwareEffectCoreAsync();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"mako-x effect dispose failed: {exception.Message}");
        }

        var controller = _thxController;
        _thxController = null;
        if (controller is not null)
        {
            await controller.DisposeAsync();
        }

        if (Mixer053E is { } mixer053E)
        {
            await mixer053E.DisposeAsync();
        }
    }
}
