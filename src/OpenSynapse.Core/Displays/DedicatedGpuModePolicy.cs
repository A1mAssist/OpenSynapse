namespace OpenSynapse.Core.Displays;

public enum DedicatedGpuModeBlockReason
{
    None,
    NoDiscreteGraphics,
    NoIntegratedGraphics,
    DiscreteGpuFaulted,
    DisplayTopologyUnavailable,
    InternalDisplayUnavailable,
    InternalDisplayRouteUnavailable,
    InternalDisplayUsesDiscreteGpu,
    ExternalDisplayConnected,
}

public readonly record struct DedicatedGpuModeSnapshot(
    bool HasIntegratedGraphics,
    bool HasDiscreteGraphics,
    DedicatedGpuDeviceState DiscreteGpuState,
    bool DisplayTopologyKnown,
    bool HasActiveInternalDisplay,
    bool InternalDisplayRouteKnown,
    bool InternalDisplayUsesDiscreteGpu,
    bool HasExternalDisplay);

public static class DedicatedGpuModePolicy
{
    /// <summary>
    /// True when the adapter already sits in the requested state, so the change can
    /// be skipped. A faulted adapter is neither enabled nor disabled and never counts
    /// as already in a state.
    /// </summary>
    public static bool IsAlreadyInState(DedicatedGpuDeviceState state, bool enabled) =>
        !state.IsFaulted() && state.IsEnabled() == enabled;

    public static bool IsSupported(in DedicatedGpuModeSnapshot snapshot) =>
        snapshot.HasDiscreteGraphics &&
        snapshot.HasIntegratedGraphics &&
        (!snapshot.DiscreteGpuState.IsEnabled() || !snapshot.InternalDisplayUsesDiscreteGpu);

    /// <summary>
    /// A machine with both adapters keeps the control visible even when the current
    /// MUX route or an external display blocks the switch; the status line explains
    /// what must change first. Only a machine missing one of the adapters hides it.
    /// </summary>
    public static bool ShouldShow(DedicatedGpuModeBlockReason reason) =>
        reason is not (DedicatedGpuModeBlockReason.NoDiscreteGraphics or
            DedicatedGpuModeBlockReason.NoIntegratedGraphics);

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

        if (snapshot.DiscreteGpuState.IsFaulted())
        {
            // The adapter already failed. Disabling it would not help, and it would
            // take away the restart that recovers it.
            return DedicatedGpuModeBlockReason.DiscreteGpuFaulted;
        }

        if (!snapshot.DiscreteGpuState.IsEnabled())
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
