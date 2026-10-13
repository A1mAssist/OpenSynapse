using OpenSynapse.Core.Devices;

namespace OpenSynapse.App;

public partial class App
{
    private void OnPeripheralBatteryObserved(DeviceBatteryReading reading)
    {
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (Volatile.Read(ref _closing) != 0 || _lowBatteryNotifications is null) return;
            var levels = _behaviorSettings.LowBatteryNotifiedLevels;
            var previous = levels.GetValueOrDefault(reading.DeviceId);
            var threshold = _lowBatteryNotifications.Observe(reading,
                _behaviorSettings.LowBatteryNotificationsEnabled && _appNotificationsRegistered, out var changed);
            if (!changed) return;
            if (threshold is not null && !ShowNotification(
                AppStrings.Text("PeripheralLowBatteryTitle"),
                AppStrings.FormatText("PeripheralLowBatteryBody", reading.DeviceName, reading.Percent),
                "battery-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(reading.DeviceId)))[..16]))
            {
                if (previous == 0) levels.Remove(reading.DeviceId);
                else levels[reading.DeviceId] = previous;
                return;
            }
            try
            {
                _behaviorSettings.Save();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or System.Security.SecurityException)
            {
                _diagnosticLog.TryWrite("notifications", $"battery notification state save failed: {exception}");
            }
        });
    }

}
