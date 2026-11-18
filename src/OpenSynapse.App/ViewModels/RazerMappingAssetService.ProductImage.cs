using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerMappingAssetService
{
    /// <summary>
    /// One product page can carry several SKU renders (the Viper V2 Pro page also ships a
    /// PUBG edition). The device never reports which one it is, so take the plainest render:
    /// fewest saturated pixels, then the darkest.
    /// </summary>
    private async Task<string?> SelectProductImageAsync(
        ushort productId,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count <= 1)
        {
            return candidates.Count == 1 ? candidates[0] : null;
        }

        string? chosen = null;
        var chosenSaturated = double.MaxValue;
        var chosenBrightness = double.MaxValue;
        foreach (var candidate in candidates)
        {
            var path = await CacheRemoteFileAsync(
                productId, candidate, MaxImageBytes, "image", cancellationToken).ConfigureAwait(false);
            if (path is null ||
                !RazerAvifDecoder.TryDecode(
                    await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false),
                    out var pixels,
                    out _,
                    out _))
            {
                // Non-AVIF or undecodable art keeps the old first-image behaviour.
                return candidates[0];
            }

            var (saturated, brightness) = ScoreProductImage(pixels);
            if (saturated < chosenSaturated - 0.005 ||
                (Math.Abs(saturated - chosenSaturated) <= 0.005 && brightness < chosenBrightness))
            {
                chosen = candidate;
                chosenSaturated = saturated;
                chosenBrightness = brightness;
            }
        }

        Log.TryWrite(
            "mapping-catalog",
            $"pid={productId:X4} art candidates={candidates.Count} chosen={Path.GetFileName(chosen)}");

        return chosen;
    }

    private static (double Saturated, double Brightness) ScoreProductImage(byte[] bgra)
    {
        long saturated = 0;
        long opaque = 0;
        long brightness = 0;
        for (var offset = 0; offset + 3 < bgra.Length; offset += 4)
        {
            if (bgra[offset + 3] < 16)
            {
                continue;
            }

            var blue = bgra[offset];
            var green = bgra[offset + 1];
            var red = bgra[offset + 2];
            var max = Math.Max(red, Math.Max(green, blue));
            var min = Math.Min(red, Math.Min(green, blue));
            if (max - min > 60)
            {
                saturated++;
            }

            brightness += max;
            opaque++;
        }

        return opaque == 0 ? (1, 255) : ((double)saturated / opaque, (double)brightness / opaque);
    }
}
