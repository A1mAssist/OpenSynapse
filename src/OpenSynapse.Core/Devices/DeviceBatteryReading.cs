namespace OpenSynapse.Core.Devices;

public sealed record DeviceBatteryReading(string DeviceId, string DeviceName, int? Percent, bool? IsCharging);
