namespace OpenSynapse.Core.Displays;

public enum DedicatedGpuModeBlockReason
{
    None,
    NoDiscreteGraphics,
    NoIntegratedGraphics,
    DisplayTopologyUnavailable,
    InternalDisplayUnavailable,
    InternalDisplayRouteUnavailable,
    InternalDisplayUsesDiscreteGpu,
    ExternalDisplayConnected,
}

public readonly record struct DedicatedGpuModeSnapshot(
    bool HasIntegratedGraphics,
    bool HasDiscreteGraphics,
    bool DiscreteGpuEnabled,
    bool DisplayTopologyKnown,
    bool HasActiveInternalDisplay,
    bool InternalDisplayRouteKnown,
    bool InternalDisplayUsesDiscreteGpu,
    bool HasExternalDisplay);

public static class DedicatedGpuModePolicy
{
    public static bool IsSupported(in DedicatedGpuModeSnapshot snapshot) =>
        snapshot.HasDiscreteGraphics &&
        snapshot.HasIntegratedGraphics &&
        (!snapshot.DiscreteGpuEnabled || !snapshot.InternalDisplayUsesDiscreteGpu);

    public static DedicatedGpuModeBlockReason GetBlockReason(in DedicatedGpuModeSnapshot snapshot)
    {
        if (!snapshot.HasDiscreteGraphics)
        {
            return DedicatedGpuModeBlockReason.NoDiscreteGraphics;
        }

        if (!snapshot.HasIntegratedGraphics)
        {
            return DedicatedGpuModeBlockReason.NoIntegratedGraphics;
        }

        if (!snapshot.DiscreteGpuEnabled)
        {
            // Re-enabling the discrete GPU is always safe and is the recovery path.
            return DedicatedGpuModeBlockReason.None;
        }

        if (snapshot.InternalDisplayUsesDiscreteGpu)
        {
            return DedicatedGpuModeBlockReason.InternalDisplayUsesDiscreteGpu;
        }

        if (!snapshot.DisplayTopologyKnown)
        {
            return DedicatedGpuModeBlockReason.DisplayTopologyUnavailable;
        }

        if (!snapshot.HasActiveInternalDisplay)
        {
            return DedicatedGpuModeBlockReason.InternalDisplayUnavailable;
        }

        if (!snapshot.InternalDisplayRouteKnown)
        {
            return DedicatedGpuModeBlockReason.InternalDisplayRouteUnavailable;
        }

        return snapshot.HasExternalDisplay
            ? DedicatedGpuModeBlockReason.ExternalDisplayConnected
            : DedicatedGpuModeBlockReason.None;
    }
}