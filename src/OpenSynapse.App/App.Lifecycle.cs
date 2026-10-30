using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App;

public partial class App
{
    private async Task ObserveActivationRequestsAsync(CancellationToken cancellationToken)
    {
        var guard = _singleInstanceGuard;
        if (guard is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var signal = await Task.Run(
                () => guard.WaitForSignal(cancellationToken),
                CancellationToken.None);
            switch (signal)
            {
                case SingleInstanceSignal.Activate:
                    _window?.RequestActivation();
                    break;
                case SingleInstanceSignal.Shutdown:
                    _window?.RequestExit();
                    return;
                default:
                    return;
            }
        }
    }
}
