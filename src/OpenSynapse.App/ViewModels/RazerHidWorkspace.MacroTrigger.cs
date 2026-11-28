using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    /// <summary>
    /// Keeps driver mode in step with the bindings. A mouse only needs it while a
    /// macro is bound to one of its private keys; otherwise the firmware keeps the
    /// key and nothing changes for the user.
    /// </summary>
    internal async Task SyncPrivateKeyDriverModeAsync(CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<ushort>();
        // Windows shortcuts are profile-scoped system macros; a shortcut bound to a
        // private key needs the same driver mode as a user macro.
        foreach (var macro in _localMacros().Concat(_getActiveProfile().SystemMacros))
        {
            if (!macro.Enabled)
            {
                continue;
            }

            foreach (var trigger in macro.AllTriggers)
            {
                if (trigger.Input.Kind == MacroInputKind.RazerKey &&
                    trigger.Input.DeviceId is > 0 and <= 0xFFFF &&
                    !RazerKeypadInputCatalog.UsesHostMapping(trigger.Input.DeviceId))
                {
                    wanted.Add((ushort)trigger.Input.DeviceId);
                }
            }
        }

        try
        {
            await _privateKeyRuntime
                .SyncAsync(wanted, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is Win32Exception or IOException or InvalidOperationException)
        {
            _diagnosticLog.TryWrite(
                "mouse-private-key",
                $"driver-mode sync failed: {exception.Message}");
        }
    }

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

    /// <summary>
    /// Driver mode makes the firmware stop applying its own mappings, so a
    /// TurboButton row has to repeat on the host while a macro holds the mouse there.
    /// </summary>
    internal RazerTurboBinding? ResolveTurboBinding(MacroInput input, bool hyperShift)
    {
        if (input.Kind != MacroInputKind.RazerKey ||
            input.DeviceId is not (> 0 and <= ushort.MaxValue))
        {
            return null;
        }

        var productId = (ushort)input.DeviceId;
        bool Fits(RazerHidDeviceConnection connection) =>
            connection.Definition.ProductId == productId &&
            connection.IsReady &&
            (string.IsNullOrWhiteSpace(input.InstanceId) ||
             StringComparer.OrdinalIgnoreCase.Equals(
                 connection.InstanceId, input.InstanceId));

        var connection = SelectedDevice is { } selected && Fits(selected.Connection)
            ? selected.Connection
            : CurrentConnections.FirstOrDefault(Fits);
        if (connection is null || connection.Definition.Category != DeviceCategory.Mouse)
        {
            return null;
        }

        var key = ProfileResolver.GetDeviceKey(connection.ToDescriptor());
        return _getActiveProfile().Devices.TryGetValue(key, out var settings) &&
               settings.Viper.ButtonMappings is { } mappings
            ? RazerTurboBindingResolver.Resolve(
                mappings, productId, input.RazerKeyCode, IsNaga(connection), hyperShift)
            : null;
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
