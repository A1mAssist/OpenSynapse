namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private PhilipsHueViewModel? _hue;

    public PhilipsHueViewModel Hue =>
        _hue ??= new PhilipsHueViewModel(message => _diagnosticLog.TryWrite("hue", message));
}
