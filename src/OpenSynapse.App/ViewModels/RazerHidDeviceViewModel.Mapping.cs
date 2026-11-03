using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private RazerHidMappingViewModel? _mapping;

    public RazerHidMappingViewModel Mapping =>
        _mapping ??= new RazerHidMappingViewModel(_service, Connection,
            Has(RazerHidBackendCapability.MappingWrite));

    public Visibility MappingVisibility => VisibleWhen(Has(RazerHidBackendCapability.MappingRead));

    public async Task LoadMappingAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RazerHidMacroOption> macros = [];
        if (RazerOnboardMacroService.IsSupportedProduct(Connection.Definition.ProductId))
        {
            try
            {
                var ids = await CreateService().ReadIdsAsync(cancellationToken).ConfigureAwait(true);
                macros = ids
                    .Select(id => new RazerHidMacroOption(
                        id, AppStrings.FormatText("RazerHidMappingMacroName", id)))
                    .ToArray();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                InvalidOperationException or NotSupportedException or UnauthorizedAccessException or
                TimeoutException or System.ComponentModel.Win32Exception)
            {
                // Macro slots are optional; a firmware that does not expose them must not
                // hide the button mapping that does work.
            }
        }

        await Mapping.ReadAsync(macros, cancellationToken);
    }

    private async Task LoadMappingIfNeededAsync(CancellationToken cancellationToken)
    {
        if (MappingVisibility != Visibility.Visible || Mapping.IsLoaded)
        {
            return;
        }

        try
        {
            await LoadMappingAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The mapping card is optional; its failure must not mark the whole device page broken.
            Mapping.SetLoadError(exception.Message);
        }
    }

    public Task ApplyMappingAsync(CancellationToken cancellationToken = default) =>
        Mapping.ApplyAsync(cancellationToken);
}
