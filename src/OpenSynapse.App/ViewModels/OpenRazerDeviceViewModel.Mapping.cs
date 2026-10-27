using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class OpenRazerDeviceViewModel
{
    private OpenRazerMappingViewModel? _mapping;

    public OpenRazerMappingViewModel Mapping =>
        _mapping ??= new OpenRazerMappingViewModel(_service, Connection);

    public Visibility MappingVisibility => VisibleWhen(Has(OpenRazerBackendCapability.MappingRead));

    public async Task LoadMappingAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OpenRazerMacroOption> macros = [];
        if (RazerOnboardMacroService.IsSupportedProduct(Connection.Definition.ProductId))
        {
            try
            {
                var ids = await CreateService().ReadIdsAsync(cancellationToken).ConfigureAwait(true);
                macros = ids
                    .Select(id => new OpenRazerMacroOption(id, $"Macro {id}"))
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

    public Task ApplyMappingAsync(CancellationToken cancellationToken = default) =>
        Mapping.ApplyAsync(cancellationToken);
}
