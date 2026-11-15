using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    internal async Task<string?> BindSoftwareMacroTriggerAsync(
        string macroId,
        MacroInput input,
        MacroTriggerMode triggerMode,
        MacroPlaybackMode playbackMode,
        int repeatCount,
        bool hyperShiftLayer,
        MacroSoftwareBinding? existingBinding,
        CancellationToken cancellationToken)
    {
        var device = FindDeviceForTrigger(input);
        if (device is null)
        {
            return AppStrings.Text("MacroTriggerDeviceMissing");
        }
        if (device.MappingVisibility != Visibility.Visible)
        {
            return AppStrings.Text("RazerMappingMacroSoftwareUnavailable");
        }

        return await device.Mapping.BindSoftwareMacroAsync(
                macroId,
                input,
                triggerMode,
                playbackMode,
                repeatCount,
                hyperShiftLayer,
                existingBinding,
                cancellationToken)
            .ConfigureAwait(true);
    }

    internal void InvalidateMappings()
    {
        foreach (var row in Devices)
        {
            row.Detail?.InvalidateMapping();
        }
    }

    private RazerHidDeviceViewModel? FindDeviceForTrigger(MacroInput input)
    {
        if (input.Kind == MacroInputKind.NagaSideButton)
        {
            if (SelectedDevice is { } selected && IsNaga(selected.Connection))
            {
                return selected;
            }

            var naga = Devices.FirstOrDefault(row =>
                row.Connection.IsReady && IsNaga(row.Connection));
            if (naga is not null)
            {
                var device = naga.Detail ?? CreateDeviceViewModel(naga.Connection);
                naga.AttachDetail(device);
                return device;
            }
        }

        if (input.DeviceId is > 0 and <= ushort.MaxValue)
        {
            var productId = (ushort)input.DeviceId;
            if (SelectedDevice is { } selected &&
                selected.Connection.Definition.ProductId == productId &&
                selected.Connection.IsReady)
            {
                return selected;
            }

            var row = Devices.FirstOrDefault(candidate =>
                candidate.Connection.Definition.ProductId == productId &&
                candidate.Connection.IsReady);
            if (row is not null)
            {
                var device = row.Detail ?? CreateDeviceViewModel(row.Connection);
                row.AttachDetail(device);
                return device;
            }
        }

        return null;
    }

    private static bool IsNaga(RazerHidDeviceConnection connection) =>
        connection.Definition.Category == DeviceCategory.Mouse &&
        RazerNagaSideButtons.IsNaga(
            0x1532,
            connection.Definition.ProductId,
            connection.Definition.DisplayName);
}
