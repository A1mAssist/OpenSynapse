using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Session-local dialect override keyed by Blade product id. A rejected matrix write
/// falls back to the model's firmware effects until the app restarts; keyboard
/// brightness uses a separate report and stays available.
/// </summary>
internal static class BladeLightingRuntimeDialect
{
    private static readonly ConcurrentDictionary<ushort, Fallback> Downgraded = new();

    internal readonly record struct Fallback(BladeLightingDialect Dialect, string Reason);

    public static BladeLightingDialect Get(ushort productId) =>
        Get(
            productId,
            RazerDeviceRegistry.BuiltIn.Find(0x1532, productId)?.LightingDialect
                ?? BladeLightingDialect.Unknown);

    public static BladeLightingDialect Get(ushort productId, BladeLightingDialect declared) =>
        Downgraded.TryGetValue(productId, out var fallback) ? fallback.Dialect : declared;

    public static bool IsDowngraded(ushort productId) => Downgraded.ContainsKey(productId);

    public static void Downgrade(ushort productId, BladeLightingDialect fallback, string reason) =>
        Downgraded.TryAdd(productId, new Fallback(fallback, reason ?? string.Empty));

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
