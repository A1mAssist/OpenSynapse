using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<MacroDefinition> _systemMacros = [];

    private async Task RefreshMacroContextAsync(CancellationToken cancellationToken)
    {
        _localMacros = _localMacroProvider?.Invoke() ?? [];
        _systemMacros = _systemMacroProvider?.Invoke() ?? [];
        _localMacroOptions = _localMacros
            .Select((macro, index) => new RazerHidMacroOption(
                (byte)Math.Min(index + 1, byte.MaxValue),
                string.IsNullOrWhiteSpace(macro.Name) ? macro.Id : macro.Name))
            .ToArray();
        _slotChoices = [];
        _freeSlots = [];
        _macrosAvailable = false;
        _onboardWritable = false;
        _slotLocalMacroIds = [];
        if (_macroServiceFactory is null || _localMacros.Count == 0)
        {
            return;
        }

        try
        {
            var service = _macroServiceFactory();
            var storage = await service.ProbeStorageAsync(cancellationToken).ConfigureAwait(true);
            if (storage is null)
            {
                return;
            }

            var slots = await service.ReadSlotInfosAsync(cancellationToken).ConfigureAwait(true);
            _slotChoices = slots
                .Select(slot => new RazerHidMacroOption(slot.Id, DescribeSlot(slot)))
                .ToArray();
            _freeSlots = RazerOnboardMacroSlots.FindFreeSlots(
                storage.MaxId,
                slots.Select(slot => slot.Id).ToArray());
            _macrosAvailable = true;
            _onboardWritable = _canWrite && _canWriteOnboardMacros;
            foreach (var slot in slots)
            {
                if (slot.NameInfo is not { Guid: var guid } || guid == Guid.Empty)
                {
                    continue;
                }

                var local = _localMacros.FirstOrDefault(
                    macro => MacroGuid(macro.Id) == guid);
                if (local is not null)
                {
                    _slotLocalMacroIds[slot.Id] = local.Id;
                }
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _log?.Invoke($"onboard macro slots unavailable: {exception.Message}");
        }
    }

    private string DescribeSlot(RazerOnboardMacroSlotInfo slot)
    {
        var nameInfo = slot.NameInfo;
        var local = nameInfo is { Guid: var guid } && guid != Guid.Empty
            ? _localMacros.FirstOrDefault(macro => MacroGuid(macro.Id) == guid)
            : null;
        if (local is not null)
        {
            return string.IsNullOrWhiteSpace(local.Name) ? local.Id : local.Name;
        }

        return string.IsNullOrWhiteSpace(nameInfo?.Name)
            ? AppStrings.FormatText("RazerMappingMacroSlot", slot.Id)
            : nameInfo.Name;
    }

    private IReadOnlyList<RazerMacroSlotOption> FreeSlotChoices() =>
        _freeSlots
            .Select(id => new RazerMacroSlotOption(id, SlotName(id)))
            .ToArray();

    private string SlotName(byte id) =>
        _slotChoices.FirstOrDefault(slot => slot.Id == id)?.Name ??
        AppStrings.FormatText("RazerMappingMacroSlot", id);

    private static Guid MacroGuid(string id)
    {
        if (Guid.TryParse(id, out var guid) && guid != Guid.Empty)
        {
            return guid;
        }

        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(id)).AsSpan(0, 16));
    }

    private int LocalMacroIndex(string macroId)
    {
        for (var index = 0; index < _localMacros.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(_localMacros[index].Id, macroId))
            {
                return index;
            }
        }

        return -1;
    }

    private async Task WriteMacroToSlotAsync(
        byte slot,
        MacroDefinition macro,
        CancellationToken cancellationToken)
    {
        if (_slotLocalMacroIds.TryGetValue(slot, out var existingMacroId) &&
            StringComparer.Ordinal.Equals(existingMacroId, macro.Id))
        {
            return;
        }

        if (_macroServiceFactory is null)
        {
            throw new InvalidOperationException(AppStrings.Text("RazerMappingMacroUnavailable"));
        }

        var items = OnboardMacroTranslation.FromSteps(macro.Steps);
        if (items.Count == 0)
        {
            throw new InvalidOperationException(AppStrings.Text("RazerMappingMacroEmpty"));
        }

        var guid = MacroGuid(macro.Id);
        var encoded = RazerOnboardMacroCodec.Encode(items);
        var hash = Convert.ToHexString(SHA256.HashData(encoded));
        var name = new RazerOnboardMacroName(
            guid,
            items.Count,
            string.IsNullOrWhiteSpace(macro.Name) ? macro.Id : macro.Name,
            [],
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            hash[..32]);
        await _macroServiceFactory().WriteAsync(slot, items, name, cancellationToken)
            .ConfigureAwait(true);

        var slotName = new RazerHidMacroOption(slot, name.Name);
        _slotChoices = _slotChoices
            .Where(choice => choice.Id != slot)
            .Append(slotName)
            .OrderBy(choice => choice.Id)
            .ToArray();
        _freeSlots = _freeSlots.Where(id => id != slot).ToArray();
        _slotLocalMacroIds[slot] = macro.Id;
        OnPropertyChanged(nameof(SupportsMacros));
    }

    private static byte? MacroSlotOf(byte functionId, IReadOnlyList<byte> data)
    {
        if (functionId is not (RazerHidMappingFunctions.MacroTypeI or
            RazerHidMappingFunctions.MacroTypeII or
            RazerHidMappingFunctions.MacroTypeIII or
            RazerHidMappingFunctions.MacroTypeIV) ||
            data.Count < 2)
        {
            return null;
        }

        var slot = (data[0] << 8) | data[1];
        return slot is > 0 and <= byte.MaxValue ? (byte)slot : null;
    }

    private string DescribeAssignment(byte functionId, IReadOnlyList<byte> data)
    {
        if (functionId is not (RazerHidMappingFunctions.MacroTypeI or
            RazerHidMappingFunctions.MacroTypeII or
            RazerHidMappingFunctions.MacroTypeIII or
            RazerHidMappingFunctions.MacroTypeIV) ||
            data.Count < 2)
        {
            return RazerMappingActionNames.Localize(_catalog.Describe(functionId, data, _slotChoices));
        }

        var slot = (data[0] << 8) | data[1];
        var name = _slotChoices.FirstOrDefault(choice => choice.Id == slot)?.Name ??
            AppStrings.FormatText("RazerMappingMacroSlot", slot);
        return functionId switch
        {
            RazerHidMappingFunctions.MacroTypeI when data.Count >= 3 && data[2] > 1 =>
                AppStrings.FormatText("RazerMappingMacroCurrentRepeat", name, data[2]),
            RazerHidMappingFunctions.MacroTypeI =>
                AppStrings.FormatText("RazerMappingMacroCurrentOnce", name),
            RazerHidMappingFunctions.MacroTypeII =>
                AppStrings.FormatText("RazerMappingMacroCurrentHold", name),
            RazerHidMappingFunctions.MacroTypeIII =>
                AppStrings.FormatText("RazerMappingMacroCurrentToggle", name),
            _ => AppStrings.FormatText("RazerMappingMacroCurrentSequence", name),
        };
    }

    private bool UpdateMacroValidation(RazerHidMappingRowViewModel row)
    {
        if (row.Group != RazerHidMappingGroup.Macro ||
            row.MacroTarget != MacroExecutionTarget.Onboard ||
            !_onboardWritable)
        {
            row.SetMacroValidation(string.Empty);
            return true;
        }

        if (row.OptionIndex < 0 || row.OptionIndex >= _localMacros.Count)
        {
            row.SetMacroValidation(AppStrings.Text("RazerMappingMacroSelectionMissing"));
            return false;
        }

        var macro = _localMacros[row.OptionIndex];
        if (macro.Steps.Count == 0)
        {
            row.SetMacroValidation(AppStrings.Text("RazerMappingMacroEmpty"));
            return false;
        }

        try
        {
            OnboardMacroTranslation.FromSteps(macro.Steps);
            row.SetMacroValidation(string.Empty);
            return true;
        }
        catch (OnboardMacroUnsupportedException exception)
        {
            row.SetMacroValidation(AppStrings.FormatText(
                row.CanUseSoftwareMacro
                    ? "RazerMappingMacroUnsupportedSoftware"
                    : "RazerMappingMacroUnsupported",
                exception.StepIndex,
                exception.Reason));
            return false;
        }
        catch (Exception exception) when (IsExpected(exception) || exception is ArgumentException)
        {
            row.SetMacroValidation(exception.Message);
            return false;
        }
    }

    private void UpdateMacroSlotWarning(RazerHidMappingRowViewModel row)
    {
        if (row.Group == RazerHidMappingGroup.MouseButton &&
            PreviewHostRemap(row) is { Outcome: HostRemapOutcome.HostInject })
        {
            row.SetMacroSlotWarning(AppStrings.Text("RazerMappingHostRemapHint"));
            return;
        }

        if (row.Group != RazerHidMappingGroup.Macro ||
            row.MacroTarget != MacroExecutionTarget.Onboard ||
            row.OptionIndex < 0 || row.OptionIndex >= _localMacros.Count ||
            row.SelectedMacroSlotId == 0 ||
            !_slotLocalMacroIds.TryGetValue(row.SelectedMacroSlotId, out var existingMacroId) ||
            StringComparer.Ordinal.Equals(existingMacroId, _localMacros[row.OptionIndex].Id))
        {
            row.SetMacroSlotWarning(string.Empty);
            return;
        }

        row.SetMacroSlotWarning(AppStrings.Text("RazerMappingMacroSlotOverwrite"));
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RazerHidMappingRowViewModel.GroupIndex) or
            nameof(RazerHidMappingRowViewModel.MacroSlots) or
            nameof(RazerHidMappingRowViewModel.OptionIndex) or
            nameof(RazerHidMappingRowViewModel.MacroTargetIndex) or
            nameof(RazerHidMappingRowViewModel.SelectedMacroSlotIndex))
        {
            if (sender is RazerHidMappingRowViewModel row)
            {
                UpdateMacroValidation(row);
                UpdateMacroSlotWarning(row);
            }

            OnPropertyChanged(nameof(CanApply));
        }
    }
}
