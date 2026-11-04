namespace OpenSynapse.Core.Lighting;

/// <summary>
/// One ARGB controller as a studio device: six addressable channels stacked as
/// rows, each row holding the configured 1 x N strip.
/// </summary>
public sealed record StudioArgbDeviceInfo(
    string InstanceId,
    ushort ProductId,
    string Name,
    int Rows,
    int Columns,
    IReadOnlyList<int> LedCounts,
    StudioLedLayout Layout);

/// <summary>Layout and import rules for Razer's six-channel ARGB controller.</summary>
public static class StudioArgbDevice
{
    public const ushort ProductId = 0x0F1F;
    public const int ChannelCount = 6;
    public const int MaxLedsPerChannel = 80;
    private const int LegacyRegionIdBase = 26;

    public static StudioArgbDeviceInfo Create(
        string instanceId,
        string name,
        IReadOnlyList<int> ledCounts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var counts = new int[ChannelCount];
        for (var channel = 0; channel < counts.Length; channel++)
        {
            var count = channel < ledCounts.Count ? ledCounts[channel] : MaxLedsPerChannel;
            counts[channel] = Math.Clamp(count, 1, MaxLedsPerChannel);
        }

        var columns = counts.Max();
        var leds = new List<StudioLedMap>(counts.Sum());
        for (var channel = 0; channel < counts.Length; channel++)
        {
            for (var column = 0; column < counts[channel]; column++)
            {
                leds.Add(new StudioLedMap(channel, column, channel, column));
            }
        }

        var layout = StudioLedLayout.Create(ChannelCount, columns, ChannelCount, columns, leds);
        return new StudioArgbDeviceInfo(
            instanceId,
            ProductId,
            string.IsNullOrWhiteSpace(name) ? "Razer Chroma Addressable RGB Controller" : name,
            ChannelCount,
            columns,
            counts,
            layout);
    }

    /// <summary>
    /// Chroma's ARGB manifest uses region ids 26..31 for the six strips. Some
    /// exports carry that value as the device instance instead of a global row.
    /// </summary>
    public static int? ChannelFromLegacyInstance(string? instanceId)
    {
        if (!int.TryParse(instanceId, out var value)) return null;
        return value >= LegacyRegionIdBase && value < LegacyRegionIdBase + ChannelCount
            ? value - LegacyRegionIdBase
            : null;
    }

    /// <summary>
    /// Rewrites a single-strip import so its LED row identifies the physical
    /// channel. A whole-controller import already has rows 0..5 and is kept.
    /// </summary>
    public static void NormalizeImportedRows(StudioRegionDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.ProductId != ProductId || device.Leds.Count == 0) return;

        var distinctRows = device.Leds.Select(led => led.Row).Distinct().ToArray();
        if (distinctRows.Length > 1 && distinctRows.Max() < ChannelCount) return;

        var instanceChannel = ChannelFromLegacyInstance(device.InstanceId);
        for (var index = 0; index < device.Leds.Count; index++)
        {
            var led = device.Leds[index];
            var channel = instanceChannel ?? LegacyChannel(led.RegionId);
            if (channel is { } value && led.Row != value)
            {
                device.Leds[index] = led with { Row = value };
            }
        }
    }

    /// <summary>Slices a whole-controller layer stack down to one channel strip.</summary>
    public static StudioLayerRender[] SliceChannel(
        IReadOnlyList<StudioLayerRender> layers,
        int rows,
        int columns,
        int channel,
        int ledCount)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (rows <= 1) return layers.ToArray();

        var result = new StudioLayerRender[layers.Count];
        for (var index = 0; index < layers.Count; index++)
        {
            var layer = layers[index];
            result[index] = layer.Mask is null
                ? layer
                : layer with
                {
                    Mask = StudioMaskBuilder.SliceRow(layer.Mask, rows, columns, channel, ledCount),
                };
        }

        return result;
    }

    private static int? LegacyChannel(int? regionId) =>
        regionId is >= LegacyRegionIdBase and < LegacyRegionIdBase + ChannelCount
            ? regionId - LegacyRegionIdBase
            : null;
}
