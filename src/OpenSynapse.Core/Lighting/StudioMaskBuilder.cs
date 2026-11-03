namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Turns a layer's regions into a per-LED alpha mask. 0 keeps the layer below,
/// 100 applies the effect fully, matching the native engine formula.
/// </summary>
public static class StudioMaskBuilder
{
    public const byte Full = 100;

    public static byte[] Build(
        StudioLayerItem layer,
        ushort productId,
        string? instanceId,
        int rows,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (rows < 1 || columns < 1 || rows * columns > StudioCompositor.MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "The layer canvas is outside the supported range.");
        }

        var mask = new byte[rows * columns];
        if (layer.Regions.Count == 0)
        {
            Array.Fill(mask, Full);
            return mask;
        }

        foreach (var region in layer.Regions)
        {
            var regionMask = BuildRegion(region, productId, instanceId, rows, columns);
            for (var index = 0; index < mask.Length; index++)
            {
                if (regionMask[index] > mask[index]) mask[index] = regionMask[index];
            }
        }

        return mask;
    }

    /// <summary>Mask for one region only; used to render each region with its own params.</summary>
    public static byte[] BuildRegion(
        StudioRegion region,
        ushort productId,
        string? instanceId,
        int rows,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (rows < 1 || columns < 1 || rows * columns > StudioCompositor.MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "The layer canvas is outside the supported range.");
        }

        var mask = new byte[rows * columns];
        if (region.Devices.Count == 0)
        {
            Array.Fill(mask, Full);
            return mask;
        }

        foreach (var device in region.Devices)
        {
            if (device.ProductId != productId) continue;
            if (!string.IsNullOrWhiteSpace(device.InstanceId) &&
                !string.Equals(device.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (device.Leds.Count == 0)
            {
                Array.Fill(mask, Full);
                continue;
            }

            foreach (var led in device.Leds)
            {
                if ((uint)led.Row >= (uint)rows || (uint)led.Col >= (uint)columns) continue;
                mask[led.Row * columns + led.Col] = Full;
            }
        }

        return mask;
    }
}
