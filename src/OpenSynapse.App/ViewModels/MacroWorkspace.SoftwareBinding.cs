using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    private async Task<bool> RestoreSoftwareBindingAsync(
        MacroSoftwareBinding binding,
        CancellationToken cancellationToken)
    {
        if (SoftwareBindingRestorer is null)
        {
            return false;
        }

        try
        {
            await SoftwareBindingRestorer(binding, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite(
                "macro",
                $"software binding restore failed: {exception.Message}");
            return false;
        }
    }
}
