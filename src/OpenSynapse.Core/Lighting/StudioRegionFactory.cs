namespace OpenSynapse.Core.Lighting;

/// <summary>Builds one region that targets a specific set of device LEDs.</summary>
public static class StudioRegionFactory
{
    public static StudioRegion FromLeds(
        StudioEffectParams parameters,
        ushort productId,
        string? instanceId,
        IEnumerable<StudioLedRef> leds)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(leds);

        var region = new StudioRegion { Params = parameters.Clone() };
        var device = new StudioRegionDevice { ProductId = productId, InstanceId = instanceId };
        foreach (var led in leds)
        {
            if (!StudioLedSelection.Contains(device.Leds, led)) device.Leds.Add(led);
        }

        if (device.Leds.Count > 0) region.Devices.Add(device);
        return region;
    }

    /// <summary>
    /// Builds one region with one device entry per instance. LEDs without an
    /// instance inherit <paramref name="fallbackInstanceId"/>; instances whose
    /// product id cannot be resolved are skipped.
    /// </summary>
    public static StudioRegion FromLedsByInstance(
        StudioEffectParams parameters,
        IEnumerable<StudioLedRef> leds,
        Func<string, ushort?> productIdForInstance,
        string? fallbackInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(leds);
        ArgumentNullException.ThrowIfNull(productIdForInstance);

        var region = new StudioRegion { Params = parameters.Clone() };
        var devices = new List<StudioRegionDevice>();
        foreach (var led in leds)
        {
            var instanceId = string.IsNullOrWhiteSpace(led.InstanceId) ? fallbackInstanceId : led.InstanceId;
            if (string.IsNullOrWhiteSpace(instanceId)) continue;
            var device = devices.FirstOrDefault(candidate =>
                StudioLedSelection.SameInstance(candidate.InstanceId, instanceId));
            if (device is null)
            {
                if (productIdForInstance(instanceId) is not { } productId || productId == 0) continue;
                device = new StudioRegionDevice { ProductId = productId, InstanceId = instanceId };
                devices.Add(device);
            }

            var cell = led with { InstanceId = instanceId };
            if (!StudioLedSelection.Contains(device.Leds, cell)) device.Leds.Add(cell);
        }

        foreach (var device in devices)
            if (device.Leds.Count > 0) region.Devices.Add(device);
        return region;
    }
}
