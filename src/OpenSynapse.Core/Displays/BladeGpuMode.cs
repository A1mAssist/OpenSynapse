namespace OpenSynapse.Core.Displays;

using OpenSynapse.Core.Sensors;

/// <summary>
/// The four user-facing graphics modes. <see cref="Auto"/> is a policy over the other
/// three, not a hardware route of its own.
/// </summary>
public enum BladeGpuMode
{
    Integrated,
    Standard,
    Discrete,
    Auto,
}

/// <summary>
/// What the machine can currently do, assembled from the PnP adapter state and the
/// firmware MUX report. A model without MUX leaves <see cref="Mux"/> unsupported and
/// only exposes the adapter switch.
/// </summary>
public readonly record struct BladeGpuModeSnapshot(
    bool DedicatedGpuSupported,
    bool DedicatedGpuEnabled,
    BladeDisplayMuxStatus Mux)
{
    public bool MuxPresent =>
        Mux.IsSupported || Mux.Capabilities != BladeDisplayMuxCapabilities.None;
}

public static class BladeGpuModes
{
    public static IReadOnlyList<BladeGpuMode> All { get; } =
        [BladeGpuMode.Integrated, BladeGpuMode.Standard, BladeGpuMode.Discrete, BladeGpuMode.Auto];

    /// <summary>
    /// A mode is offered only when the hardware path behind it exists on this model.
    /// "Standard" without a MUX is just the discrete GPU switched back on.
    /// </summary>
    public static bool IsAvailable(in BladeGpuModeSnapshot snapshot, BladeGpuMode mode) =>
        mode switch
        {
            BladeGpuMode.Integrated => snapshot.DedicatedGpuSupported,
            // Already being on the iGPU route counts even when the capability bits do not
            // offer a Hybrid write: no write is needed to stay there.
            BladeGpuMode.Standard =>
                snapshot.MuxPresent
                    ? BladeDisplayMuxModes.Supports(snapshot.Mux.Capabilities, BladeDisplayMuxMode.Hybrid) ||
                      snapshot.Mux.Mode is BladeDisplayMuxMode.Hybrid or BladeDisplayMuxMode.Dds
                    : snapshot.DedicatedGpuSupported,
            // A panel already on the discrete route blocks the PnP switch but not the
            // firmware MUX, so the MUX modes stay available without it.
            BladeGpuMode.Discrete =>
                snapshot.MuxPresent &&
                ((snapshot.Mux.IsSupported &&
                  BladeDisplayMuxModes.Supports(snapshot.Mux.Capabilities, BladeDisplayMuxMode.Discrete)) ||
                 snapshot.Mux.Mode == BladeDisplayMuxMode.Discrete),
            BladeGpuMode.Auto =>
                IsAvailable(snapshot, BladeGpuMode.Integrated) &&
                IsAvailable(snapshot, BladeGpuMode.Standard),
            _ => false,
        };

    public static IReadOnlyList<BladeGpuMode> GetAvailableModes(in BladeGpuModeSnapshot snapshot)
    {
        var modes = new List<BladeGpuMode>(All.Count);
        foreach (var mode in All)
        {
            if (IsAvailable(snapshot, mode))
            {
                modes.Add(mode);
            }
        }
        return modes;
    }

    /// <summary>
    /// The mode the hardware is actually in. Auto cannot be derived here; callers keep
    /// the stored preference separately and fall back to this when it is not Auto.
    /// </summary>
    public static BladeGpuMode GetCurrentMode(in BladeGpuModeSnapshot snapshot)
    {
        if (!snapshot.DedicatedGpuEnabled)
        {
            return BladeGpuMode.Integrated;
        }

        return snapshot.MuxPresent && snapshot.Mux.Mode == BladeDisplayMuxMode.Discrete
            ? BladeGpuMode.Discrete
            : BladeGpuMode.Standard;
    }

    /// <summary>
    /// Auto follows the power source through the PnP switch only. Never writing the MUX
    /// here is what keeps a power-source change from turning into a restart loop.
    /// </summary>
    public static BladeGpuMode ResolveAutoTarget(PowerSourceKind powerSource) =>
        powerSource == PowerSourceKind.Battery
            ? BladeGpuMode.Integrated
            : BladeGpuMode.Standard;
}
