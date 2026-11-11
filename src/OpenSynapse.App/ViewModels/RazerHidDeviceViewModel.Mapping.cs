using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private RazerHidMappingViewModel? _mapping;

    public Func<string, MacroInput, MacroTriggerMode, bool, CancellationToken, Task<string?>>?
        SoftwareMacroBinder { get; set; }

    public Func<string, CancellationToken, Task<string?>>? SoftwareMacroClearer { get; set; }

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
            mappingProfileSaver: PersistMappingProfileAsync,
            canWriteOnboardMacros:
                Has(RazerHidBackendCapability.MacroStorageDataWrite) &&
                Has(RazerHidBackendCapability.MacroStorageNameWrite),
            softwareTriggerResolver:
                SoftwareMacroTriggerResolver ?? ResolveDefaultSoftwareMacroTrigger,
            softwareTriggerBinder: SoftwareMacroBinder,
            softwareTriggerClearer: SoftwareMacroClearer);

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

        return RazerMouseMacroTriggerResolver.Resolve(
            Connection.Definition.ProductId,
            buttonId,
            IsNagaMouse);
    }

    private async Task PersistMappingProfileAsync(
        IReadOnlyList<ButtonMappingProfile> mappings,
        CancellationToken cancellationToken)
    {
        if (mappings.Count == 0)
        {
            return;
        }
        if (_getActiveProfile is null || _saveProfileAsync is null)
        {
            return;
        }

        var settings = ResolveDeviceProfileSettings()!;
        var mode = mappings[0].Mode ?? 0;
        if (IsKeyboard)
        {
            var previous = settings.Keyboard.ButtonMappings;
            settings.Keyboard.ButtonMappings = RazerHidMappingActions.MergeButtonMappings(
                previous, mappings, mode);
            if (await _saveProfileAsync(cancellationToken).ConfigureAwait(true))
            {
                return;
            }

            settings.Keyboard.ButtonMappings = previous;
        }
        else
        {
            var previous = settings.Viper.ButtonMappings;
            settings.Viper.ButtonMappings = RazerHidMappingActions.MergeButtonMappings(
                previous, mappings, mode);
            if (await _saveProfileAsync(cancellationToken).ConfigureAwait(true))
            {
                return;
            }

            settings.Viper.ButtonMappings = previous;
        }

        throw new IOException("RazerHid mapping could not be saved to the profile.");
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
