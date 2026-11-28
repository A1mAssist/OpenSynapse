using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private string? _mappingMacroSignature;

    private RazerHidMappingViewModel? _mapping;

    public Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
        MacroSoftwareBinding?, CancellationToken, Task<string?>>? SoftwareMacroBinder { get; set; }

    public Func<string, MacroInput, bool, CancellationToken, Task<string?>>? SoftwareMacroClearer
    {
        get;
        set;
    }

    public Func<byte, MacroInput?>? SoftwareMacroTriggerResolver { get; set; }

    public Func<CancellationToken, Task<KeypadMacroBindingTransaction>>? KeypadMacroTransactionFactory { get; set; }

    private bool IsNagaMouse =>
        Connection.Definition.Category == DeviceCategory.Mouse &&
        RazerNagaSideButtons.IsNaga(
            0x1532, Connection.Definition.ProductId, Connection.Definition.DisplayName);

    private bool IsKeypad =>
        Connection.Definition.ProductId is 0x022B or 0x0244 or 0x02EB;

    public RazerHidMappingViewModel Mapping => _mapping ??= CreateMapping();

    private RazerHidMappingViewModel CreateMapping()
    {
        var mapping = new RazerHidMappingViewModel(
            new RazerHidMappingTransport(_service, Connection),
            Has(RazerHidBackendCapability.MappingWrite),
            RazerOnboardMacroDevices.Products.Contains(Connection.Definition.ProductId)
                ? CreateService
                : null,
            _localMacros,
            productId: Connection.Definition.ProductId,
            deviceKind: Connection.Definition.Category == DeviceCategory.Keyboard
                ? RazerMappingDeviceKind.Keyboard
                : RazerMappingDeviceKind.Mouse,
            actionCatalog: RazerKeypadInputCatalog.UsesHostMapping(Connection.Definition.ProductId)
                ? new RazerTartarusProMappingActionCatalog(Connection.Definition.ProductId)
                : IsNagaMouse
                ? new RazerNagaMappingActionCatalog(Connection.Definition.ProductId)
                : IsKeypad
                    ? new RazerKeypadMappingActionCatalog(Connection.Definition.ProductId)
                    // Every keyboard needs its own names: the shared catalog maps
                    // firmware/HID ids 1..15 and 0x34..0x6A to mouse buttons.
                    : Connection.Definition.Category == DeviceCategory.Keyboard
                        ? new RazerKeyboardMappingActionCatalog(Connection.Definition.ProductId)
                        : null,
            mappingProfileSaver: PersistMappingProfileAsync,
            canWriteOnboardMacros:
                Has(RazerHidBackendCapability.MacroStorageDataWrite) &&
                Has(RazerHidBackendCapability.MacroStorageNameWrite),
            softwareTriggerResolver: ResolveSoftwareMacroTrigger,
            softwareTriggerBinder: SoftwareMacroBinder,
            softwareTriggerClearer: SoftwareMacroClearer,
            keypadMacroTransactionFactory: KeypadMacroTransactionFactory,
            log: _log,
            systemMacros: () => _getActiveProfile is null
                ? []
                : SystemMacroCatalog.Bindings(_getActiveProfile().SystemMacros))
        { DeviceInstanceId = Connection.InstanceId };
        mapping.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RazerHidMappingViewModel.ReadErrorText))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ProtocolAvailableCount));
                OnPropertyChanged(nameof(ProtocolDetails));
            }
        };
        return mapping;
    }

    private IReadOnlyDictionary<string, string>? MappingProtocolErrors
    {
        get
        {
            if (_mapping is not { ReadErrorText.Length: > 0 } mapping)
            {
                return _basicState?.Errors;
            }
            var errors = _basicState?.Errors.ToDictionary(pair => pair.Key, pair => pair.Value)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
            errors[nameof(RazerHidBackendCapability.MappingRead)] = mapping.ReadErrorText;
            errors[nameof(RazerHidBackendCapability.MappingWrite)] = mapping.ReadErrorText;
            return errors;
        }
    }

    private MacroInput? ResolveDefaultSoftwareMacroTrigger(byte buttonId)
    {
        if (RazerKeypadInputCatalog.UsesHostMapping(Connection.Definition.ProductId))
        {
            return new MacroInput
            {
                Kind = MacroInputKind.RazerKey,
                DeviceId = Connection.Definition.ProductId,
                InstanceId = Connection.InstanceId,
                RazerKeyCode = buttonId,
            };
        }

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

    private MacroInput? ResolveSoftwareMacroTrigger(byte buttonId)
    {
        var input = (SoftwareMacroTriggerResolver ?? ResolveDefaultSoftwareMacroTrigger)(buttonId);
        if (input is { Kind: MacroInputKind.Keyboard, DeviceId: 0 })
        {
            // The trigger binder locates the device by id; keyboard inputs carry
            // none, so stamp the page's device before the shortcut is bound.
            input = input.Clone();
            input.DeviceId = Connection.Definition.ProductId;
            input.InstanceId = Connection.InstanceId;
        }

        return input;
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
            if (RazerKeypadInputCatalog.UsesHostMapping(Connection.Definition.ProductId))
                throw new InvalidOperationException(AppStrings.Text("MacroSaveFailed"));
            return;
        }

        var settings = ResolveDeviceProfileSettings()!;
        var mode = mappings[0].Mode ?? 0;
        if (IsKeyboard)
        {
            var previous = settings.Keyboard.ButtonMappings;
            settings.Keyboard.ButtonMappings = RazerHidMappingActions.MergeButtonMappings(
                previous, mappings, mode);
            var saved = false;
            try
            {
                saved = await _saveProfileAsync(cancellationToken).ConfigureAwait(true);
                if (saved) return;
            }
            finally
            {
                if (!saved) settings.Keyboard.ButtonMappings = previous;
            }
        }
        else
        {
            var previous = settings.Viper.ButtonMappings;
            settings.Viper.ButtonMappings = RazerHidMappingActions.MergeButtonMappings(
                previous, mappings, mode);
            var saved = false;
            try
            {
                saved = await _saveProfileAsync(cancellationToken).ConfigureAwait(true);
                if (saved) return;
            }
            finally
            {
                if (!saved) settings.Viper.ButtonMappings = previous;
            }
        }

        throw new IOException("RazerHid mapping could not be saved to the profile.");
    }

    public Visibility MappingVisibility => VisibleWhen(Has(RazerHidBackendCapability.MappingRead));

    public Task LoadMappingAsync(CancellationToken cancellationToken = default) =>
        Mapping.ReadAsync(cancellationToken);

    internal void InvalidateMapping()
    {
        _mappingMacroSignature = null;
        // ReadAsync queues itself when a read is already running, so a profile switch
        // that lands mid-refresh still re-reads the new slot instead of being dropped.
        if (_mapping is { IsLoaded: true } mapping)
        {
            _ = mapping.ReadAsync();
        }
    }

    private async Task LoadMappingIfNeededAsync(CancellationToken cancellationToken)
    {
        if (MappingVisibility != Visibility.Visible)
        {
            return;
        }

        // Rows hold a macro id from the library snapshot; reload when macros changed
        // so a deleted or recreated macro cannot leave a stale binding behind.
        var signature = MacroContextSignature();
        if (Mapping.IsLoaded &&
            string.Equals(signature, _mappingMacroSignature, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await LoadMappingAsync(cancellationToken);
            if (!Mapping.HasError)
            {
                _mappingMacroSignature = signature;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            // A selection can be superseded while the mapping mode session is being
            // restored. That is not a failure of this device's button table.
            _log?.Invoke($"mapping load canceled: {exception.Message}");
        }
        catch (Exception exception)
        {
            // The mapping card is optional; its failure must not mark the whole device page broken.
            _log?.Invoke(
                $"mapping load failed ({exception.GetType().Name}): {exception.Message}");
            Mapping.SetLoadError(exception.Message);
        }
    }

    private string MacroContextSignature()
    {
        // Shortcuts live in the profile's system macros; a new trigger there must
        // reload the rows even when the user macro library did not change.
        var macros = (_localMacros?.Invoke() ?? Array.Empty<MacroDefinition>())
            .Concat(_getActiveProfile?.Invoke().SystemMacros ?? []);
        return string.Join(';', macros.Select(macro => FormattableString.Invariant(
            $"{macro.Id}|{macro.Name}|{TriggersSignature(macro)}")));
    }

    private static string TriggersSignature(MacroDefinition macro) =>
        string.Join('+', macro.AllTriggers.Select(trigger => FormattableString.Invariant(
            $"{trigger.Mode}|{trigger.Input.Kind}|{trigger.Input.VirtualKey}|{trigger.Input.MouseButton}|{trigger.Input.RazerKeyCode}|{trigger.Input.DeviceId}|{trigger.HyperShiftLayer}")));

    public Task ApplyMappingAsync(CancellationToken cancellationToken = default) =>
        Mapping.ApplyAsync(cancellationToken);
}
