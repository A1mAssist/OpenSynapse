using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    internal async Task RestoreSoftwareMacroBindingAsync(
        MacroSoftwareBinding binding,
        CancellationToken cancellationToken)
    {
        if (_deviceService is null)
        {
            _diagnosticLog.TryWrite(
                "mapping",
                $"software binding restore skipped: no device service (pid=0x{binding.ProductId:X4})");
            return;
        }

        var connection = CurrentConnections.FirstOrDefault(candidate =>
            candidate.Definition.ProductId == binding.ProductId);
        if (connection is null)
        {
            _diagnosticLog.TryWrite(
                "mapping",
                $"software binding restore skipped: pid=0x{binding.ProductId:X4} is offline");
            return;
        }

        try
        {
            await _deviceService.SetButtonAssignmentAsync(
                connection,
                binding.ButtonId,
                binding.Layer,
                binding.OriginalFunctionId,
                binding.OriginalData,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnosticLog.TryWrite(
                "mapping",
                $"software binding restore failed: {exception.Message}");
        }
    }
}
