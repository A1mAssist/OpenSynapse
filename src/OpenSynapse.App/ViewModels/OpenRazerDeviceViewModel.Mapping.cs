using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class OpenRazerDeviceViewModel
{
    private OpenRazerMappingViewModel? _mapping;

    public OpenRazerMappingViewModel Mapping =>
        _mapping ??= new OpenRazerMappingViewModel(_service, Connection);

    public Visibility MappingVisibility => VisibleWhen(Has(OpenRazerBackendCapability.MappingRead));

    public Task LoadMappingAsync(CancellationToken cancellationToken = default) =>
        Mapping.ReadAsync(cancellationToken);

    public Task ApplyMappingAsync(CancellationToken cancellationToken = default) =>
        Mapping.ApplyAsync(cancellationToken);
}
