namespace OpenSynapse.Windows.Devices;

public static class RazerCapabilityExposure
{
    /// <summary>
    /// A declared capability stays exposed when its GET probe fails; an unknown
    /// device still needs a successful probe. Only audited bundles are listed.
    /// </summary>
    public static bool ShouldExpose(
        ushort productId,
        RazerHidBackendCapability capability,
        bool probeSucceeded)
    {
        if (probeSucceeded) return true;
        return capability is RazerHidBackendCapability.ProximitySensorRead
                or RazerHidBackendCapability.ProximitySensorWrite
            && RazerProximitySensorProfiles.TryGet(productId, out _);
    }
}
