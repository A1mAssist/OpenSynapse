using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    private async Task<bool> RestoreSoftwareBindingsAsync(
        MacroDefinition macro,
        CancellationToken cancellationToken,
        bool includeKeypad = true)
    {
        foreach (var trigger in macro.AllTriggers)
        {
            var binding = trigger.SoftwareBinding ??
                (ReferenceEquals(trigger, macro.Trigger) ? macro.SoftwareBinding : null);
            if (binding is not null && (includeKeypad || !RazerKeypadInputCatalog.UsesHostMapping(binding.ProductId)) &&
                !await RestoreSoftwareBindingAsync(binding, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static MacroSoftwareBinding[] KeypadSoftwareBindings(
        MacroDefinition macro, IReadOnlyList<MacroTrigger> triggers) =>
        triggers.Select(trigger => trigger.SoftwareBinding ??
                (ReferenceEquals(trigger, macro.Trigger) ? macro.SoftwareBinding : null))
            .OfType<MacroSoftwareBinding>()
            .Where(binding => RazerKeypadInputCatalog.UsesHostMapping(binding.ProductId))
            .Select(binding => binding.Clone()).ToArray();

    private async Task<SoftwareBindingRestoreTransaction?> BeginKeypadBindingRestoreAsync(
        IReadOnlyList<MacroSoftwareBinding> bindings, CancellationToken cancellationToken)
    {
        if (bindings.Count == 0 || SoftwareBindingRestoreTransactionFactory is null) return null;
        try
        {
            return await SoftwareBindingRestoreTransactionFactory(bindings, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnosticLog.TryWrite("macro", $"keypad binding restore failed: {exception.Message}");
            return null;
        }
    }

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
            return await SoftwareBindingRestorer(binding, cancellationToken).ConfigureAwait(true);
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
