using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task PrimeBleBatteriesAsync(CancellationToken cancellationToken)
    {
        if (SelectedBle?.IsBusy == true)
        {
            return;
        }

        foreach (var row in BleDevices.Where(row => row.BatteryPercent is null).ToArray())
        {
            if (SelectedBle is { } selected &&
                StringComparer.OrdinalIgnoreCase.Equals(row.Device.Id, selected.DeviceId))
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                using var service = await RazerBleDeviceService.OpenAsync(row.Device, null, timeout.Token);
                row.SetBatteryPercent(await service.TryReadBatteryPercentAsync(timeout.Token));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The card stays batteryless until the next page visit or discovery change.
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception) || exception is TimeoutException)
            {
                _diagnosticLog.TryWrite("ble", $"battery prime failed for {row.Device.Id}: {exception.Message}");
            }
        }
    }

    private async Task PrimeAwBleBatteriesAsync(CancellationToken cancellationToken)
    {
        if (SelectedAwBleHeadset?.IsBusy == true)
        {
            return;
        }

        foreach (var row in AwBleHeadsetDevices.Where(row => row.BatteryPercent is null).ToArray())
        {
            if (SelectedAwBleHeadset is { } selected &&
                StringComparer.OrdinalIgnoreCase.Equals(row.Device.Id, selected.Device.Id))
            {
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                using var service = await RazerAwBleHeadsetService.ConnectAsync(
                    row.Device.Id, null, timeout.Token);
                row.SetBatteryPercent((await service.ReadBatteryAsync(timeout.Token)).HeadsetPercent);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception) || exception is TimeoutException)
            {
                _diagnosticLog.TryWrite("aw-ble", $"battery prime failed for {row.Device.Id}: {exception.Message}");
            }
        }
    }

    private async Task PrimeAccessoryBatteriesAsync(CancellationToken cancellationToken)
    {
        if (Accessories.SelectedHeadsetAudio?.IsBusy != true)
        {
            foreach (var row in Accessories.HeadsetAudioDevices
                         .Where(row => row.BatteryPercent is null).ToArray())
            {
                if (Accessories.SelectedHeadsetAudio is { } selected &&
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.DevicePath, selected.DevicePath))
                {
                    continue;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    var percent = await RazerHeadsetAudioService.TryReadBatteryPercentAsync(
                        row.Connection, null, timeout.Token);
                    if (percent is int value)
                    {
                        row.SetProbeResult(row.ProbeState, row.CapabilityText, row.Error, value);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception exception) when (IsExpectedRuntimeException(exception) || exception is TimeoutException)
                {
                    _diagnosticLog.TryWrite("headset-audio", $"battery prime failed: {exception.Message}");
                }
            }
        }

        if (SelectedBlackShark?.IsBusy != true)
        {
            foreach (var row in BlackSharkDevices.Where(row => row.BatteryPercent is null).ToArray())
            {
                if (SelectedBlackShark is { } selected &&
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.DevicePath, selected.DevicePath))
                {
                    continue;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await using var service = await BlackSharkHeadsetService.OpenAsync(
                        row.Connection, null, timeout.Token);
                    if (await service.TryReadBatteryPercentAsync(timeout.Token) is int percent)
                    {
                        row.SetCapabilityText(row.CapabilityText, percent);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception exception) when (IsExpectedRuntimeException(exception) || exception is TimeoutException)
                {
                    _diagnosticLog.TryWrite("blackshark", $"battery prime failed: {exception.Message}");
                }
            }
        }

        foreach (var row in Accessories.KeyboardDevices
                     .Where(row => row.BatteryPercent is null && !row.Detail.IsBusy).ToArray())
        {
            if (Accessories.SelectedKeyboard is { } selected &&
                StringComparer.OrdinalIgnoreCase.Equals(row.Connection.DevicePath, selected.DevicePath))
            {
                continue;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                if (await RazerKeyboardService.TryReadBatteryPercentAsync(
                        row.Connection, null, timeout.Token) is int percent)
                {
                    row.SetBatteryPercent(percent);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception) || exception is TimeoutException)
            {
                _diagnosticLog.TryWrite("keyboard", $"battery prime failed: {exception.Message}");
            }
        }
    }
}
