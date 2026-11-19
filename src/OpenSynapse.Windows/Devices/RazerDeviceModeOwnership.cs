using System.Collections.Concurrent;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Driver mode is shared by the mapping page and the private-key runtime. The page
/// must not restore normal mode while a macro runtime still depends on driver mode.
/// </summary>
internal static class RazerDeviceModeOwnership
{
    private static readonly ConcurrentDictionary<ushort, byte> DriverModeHolders = new();

    internal static void Acquire(ushort productId) => DriverModeHolders.TryAdd(productId, 0);

    internal static void Release(ushort productId) => DriverModeHolders.TryRemove(productId, out _);

    internal static bool IsDriverModeHeld(ushort productId) =>
        DriverModeHolders.ContainsKey(productId);
}
