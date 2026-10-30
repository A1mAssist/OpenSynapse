using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerAwBleHeadsetViewModel
{
    private async Task LoadInfoAsync(CancellationToken cancellationToken)
    {
        if (_service is null)
        {
            return;
        }

        var firmware = await TryReadAsync(() => _service.ReadFirmwareAsync(cancellationToken))
            .ConfigureAwait(false);
        FirmwareVisibility = firmware is null ? Visibility.Collapsed : Visibility.Visible;
        if (firmware is { } version)
        {
            FirmwareText = version.ToString();
        }

        var serial = await TryReadReferenceAsync(
            () => _service.ReadSerialAsync(cancellationToken)).ConfigureAwait(false);
        SerialVisibility = string.IsNullOrWhiteSpace(serial)
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (serial is not null)
        {
            SerialText = serial;
        }

        var connection = await TryReadAsync(
            () => _service.ReadConnectionAsync(cancellationToken)).ConfigureAwait(false);
        ConnectionVisibility = connection is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (connection is { } state)
        {
            ConnectionText = FormatConnection(state);
        }

        var charging = await TryReadAsync(
            () => _service.ReadChargingAsync(cancellationToken)).ConfigureAwait(false);
        ChargingVisibility = charging is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (charging is { } status)
        {
            ChargingText = FormatCharging(status);
        }
    }

    private static string FormatConnection(RazerAwConnectionState state)
    {
        if (state.Headset is { } headset)
        {
            return headset switch
            {
                RazerAwHeadsetConnectionStatus.NoConnection =>
                    AppStrings.Text("AwBleHeadsetConnectionNone"),
                RazerAwHeadsetConnectionStatus.BleConnected =>
                    AppStrings.Text("AwBleHeadsetConnectionBle"),
                RazerAwHeadsetConnectionStatus.DongleConnected =>
                    AppStrings.Text("AwBleHeadsetConnectionDongle"),
                _ => "—",
            };
        }

        if (state.Right is { } right && state.Left is { } left)
        {
            return AppStrings.FormatText(
                "AwBleHeadsetEarbudConnection",
                FormatEarbudConnection(right),
                FormatEarbudConnection(left));
        }

        return "—";
    }

    private static string FormatEarbudConnection(RazerAwEarbudConnectionStatus status) =>
        status switch
        {
            RazerAwEarbudConnectionStatus.ConnectedAsMaster =>
                AppStrings.Text("AwBleHeadsetConnectionMaster"),
            RazerAwEarbudConnectionStatus.ConnectedAsSlave =>
                AppStrings.Text("AwBleHeadsetConnectionSlave"),
            RazerAwEarbudConnectionStatus.NoConnection =>
                AppStrings.Text("AwBleHeadsetConnectionNone"),
            _ => "—",
        };

    private static string FormatCharging(RazerAwChargingStatus status) =>
        status switch
        {
            RazerAwChargingStatus.NotCharging =>
                AppStrings.Text("AwBleHeadsetChargingNone"),
            RazerAwChargingStatus.Charging =>
                AppStrings.Text("AwBleHeadsetChargingActive"),
            RazerAwChargingStatus.ChargingFailed =>
                AppStrings.Text("AwBleHeadsetChargingFailed"),
            _ => "—",
        };
}
