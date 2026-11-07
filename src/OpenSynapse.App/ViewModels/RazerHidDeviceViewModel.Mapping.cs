using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private RazerHidMappingViewModel? _mapping;

    public Func<string, MacroInput, MacroTriggerMode, CancellationToken, Task<string?>>?
        SoftwareMacroBinder { get; set; }

    public Func<byte, MacroInput?>? SoftwareMacroTriggerResolver { get; set; }

    private bool IsNagaMouse =>
        Connection.Definition.Category == DeviceCategory.Mouse &&
        RazerNagaSideButtons.IsNaga(
            0x1532, Connection.Definition.ProductId, Connection.Definition.DisplayName);

    public RazerHidMappingViewModel Mapping =>
        _mapping ??= new RazerHidMappingViewModel(
            new RazerHidMappingTransport(_service, Connection),
            Has(RazerHidBackendCapability.MappingWrite),
            CreateService,
            _localMacros,
            productId: Connection.Definition.ProductId,
            deviceKind: Connection.Definition.Category == DeviceCategory.Keyboard
                ? RazerMappingDeviceKind.Keyboard
                : RazerMappingDeviceKind.Mouse,
            actionCatalog: IsNagaMouse ? RazerNagaMappingActionCatalog.Instance : null,
            canWriteOnboardMacros:
                Has(RazerHidBackendCapability.MacroStorageDataWrite) &&
                Has(RazerHidBackendCapability.MacroStorageNameWrite),
            softwareTriggerResolver:
                SoftwareMacroTriggerResolver ?? ResolveDefaultSoftwareMacroTrigger,
            softwareTriggerBinder: SoftwareMacroBinder);

    private MacroInput? ResolveDefaultSoftwareMacroTrigger(byte buttonId)
    {
        if (Connection.Definition.Category == DeviceCategory.Keyboard)
        {
            return RazerKeyboardMacroTriggerResolver.Resolve(
                buttonId, Connection.Definition.ProductId);
        }

        if (Connection.Definition.Category != DeviceCategory.Mouse)
        {
            return null;
        }

        if (buttonId is 4 or 5)
        {
            return new MacroInput
            {
                Kind = MacroInputKind.MouseButton,
                MouseButton = buttonId,
            };
        }

        return IsNagaMouse ? RazerNagaSideButtonResolver.Resolve(buttonId) : null;
    }

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
