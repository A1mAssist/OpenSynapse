using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private RazerHidMappingViewModel? _mapping;

    public RazerHidMappingViewModel Mapping =>
        _mapping ??= new RazerHidMappingViewModel(
            new RazerHidMappingTransport(_service, Connection),
            Has(RazerHidBackendCapability.MappingWrite),
            CreateService,
            _localMacros,
            productId: Connection.Definition.ProductId,
            deviceKind: Connection.Definition.Category == DeviceCategory.Keyboard
                ? RazerMappingDeviceKind.Keyboard
                : RazerMappingDeviceKind.Mouse);

    public Visibility MappingVisibility => VisibleWhen(Has(RazerHidBackendCapability.MappingRead));

    public Task LoadMappingAsync(CancellationToken cancellationToken = default) =>
        Mapping.ReadAsync(cancellationToken);

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
