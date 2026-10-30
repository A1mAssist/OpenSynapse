using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Session-local dialect override keyed by Blade product id. A firmware that explicitly
/// rejects the declared effect report keeps that dialect off until the app restarts;
/// keyboard brightness uses a separate report and stays available.
/// </summary>
internal static class BladeLightingRuntimeDialect
{
    private static readonly ConcurrentDictionary<ushort, string> Downgraded = new();

    public static BladeLightingDialect Get(ushort productId) =>
        Get(
            productId,
            RazerDeviceRegistry.BuiltIn.Find(0x1532, productId)?.LightingDialect
                ?? BladeLightingDialect.Unknown);

    public static BladeLightingDialect Get(ushort productId, BladeLightingDialect declared) =>
        Downgraded.ContainsKey(productId) ? BladeLightingDialect.Unknown : declared;

    public static bool IsDowngraded(ushort productId) => Downgraded.ContainsKey(productId);

    public static void Downgrade(ushort productId, string reason) =>
        Downgraded.TryAdd(productId, reason ?? string.Empty);

    /// <summary>Only a command the firmware answered with 0x03/0x05 is a rejection.</summary>
    public static bool IsExplicitRejection(Exception exception) =>
        exception is RazerCommandRejectedException or NotSupportedException;

    internal static void Reset()
    {
        foreach (var productId in Downgraded.Keys)
        {
            Downgraded.TryRemove(productId, out _);
        }
    }
}
