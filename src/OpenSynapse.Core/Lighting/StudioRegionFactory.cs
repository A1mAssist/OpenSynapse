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
        var seen = new HashSet<(int Row, int Col)>();
        foreach (var led in leds)
        {
            if (seen.Add((led.Row, led.Col))) device.Leds.Add(led);
        }

        if (device.Leds.Count > 0) region.Devices.Add(device);
        return region;
    }
}
