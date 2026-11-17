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

        if (input.Kind == MacroInputKind.MouseButton && input.MouseButton > 5)
        {
            if (SelectedDevice is { } selected && IsMouse(selected.Connection))
            {
                return selected;
            }

            var mouse = Devices.FirstOrDefault(row =>
                row.Connection.IsReady && IsMouse(row.Connection));
            if (mouse is not null)
            {
                var device = mouse.Detail ?? CreateDeviceViewModel(mouse.Connection);
                mouse.AttachDetail(device);
                return device;
            }
        }

        if (input.DeviceId is > 0 and <= ushort.MaxValue)
        {
            var productId = (ushort)input.DeviceId;
            bool Fits(RazerHidDeviceConnection connection) =>
                connection.Definition.ProductId == productId &&
                connection.IsReady &&
                (string.IsNullOrWhiteSpace(input.InstanceId) ||
                 StringComparer.OrdinalIgnoreCase.Equals(
                     connection.InstanceId, input.InstanceId));
            if (SelectedDevice is { } selected && Fits(selected.Connection))
            {
                return selected;
            }

            var row = Devices.FirstOrDefault(candidate => Fits(candidate.Connection));
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

    private static bool IsMouse(RazerHidDeviceConnection connection) =>
        connection.Definition.Category == DeviceCategory.Mouse;
}
