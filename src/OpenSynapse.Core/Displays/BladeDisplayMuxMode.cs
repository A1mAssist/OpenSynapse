namespace OpenSynapse.Core.Displays;

public enum BladeDisplayMuxMode : byte
{
    Hybrid = 0x00,
    Discrete = 0x01,
    Dds = 0x02,
}

[Flags]
public enum BladeDisplayMuxCapabilities : byte
{
    None = 0x00,
    Hybrid = 0x01,
    Discrete = 0x02,
    DynamicDisplaySwitch = 0x04,
    IntegratedOnly = 0x08,
}

public enum BladeDisplayMuxBlockReason
{
    None,
    NoDevice,
    MissingCapability,
    ReadFailed,
}

public readonly record struct BladeDisplayMuxStatus(
    bool IsSupported,
    bool CanChange,
    BladeDisplayMuxMode Mode,
    BladeDisplayMuxBlockReason BlockReason,
    BladeDisplayMuxCapabilities Capabilities);

public enum BladeDisplayMuxSwitchOutcome
{
    Success,
    RestartRequired,
    Unsupported,
    NoDevice,
    Failed,
    Restored,
    RestoreFailed,
}

public readonly record struct BladeDisplayMuxSwitchResult(
    BladeDisplayMuxSwitchOutcome Outcome,
    int ErrorCode = 0);

public static class BladeDisplayMuxModes
{
    /// <summary>
    /// Masks the modes a model may expose. The 2026 bundle only defines the
    /// Microsoft Hybrid (0) and Discrete MUX (1) mode values, so callers drop the
    /// hardware DDS capability bit before building the switcher.
    /// </summary>
    public static BladeDisplayMuxCapabilities AdmittedCapabilities(
        BladeDisplayMuxCapabilities capabilities,
        bool allowDds) =>
        allowDds
            ? capabilities
            : capabilities & (BladeDisplayMuxCapabilities.Hybrid | BladeDisplayMuxCapabilities.Discrete);

    public static bool Supports(
        BladeDisplayMuxCapabilities capabilities,
        BladeDisplayMuxMode mode,
        bool allowDds = true) =>
        mode switch
        {
            BladeDisplayMuxMode.Hybrid =>
                capabilities.HasFlag(BladeDisplayMuxCapabilities.Hybrid),
            BladeDisplayMuxMode.Discrete =>
                capabilities.HasFlag(BladeDisplayMuxCapabilities.Discrete),
            BladeDisplayMuxMode.Dds =>
                allowDds && capabilities.HasFlag(BladeDisplayMuxCapabilities.DynamicDisplaySwitch),
            _ => false,
        };

    public static bool CanSwitch(
        BladeDisplayMuxCapabilities capabilities,
        bool allowDds = true) =>
        Supports(capabilities, BladeDisplayMuxMode.Discrete) &&
        (Supports(capabilities, BladeDisplayMuxMode.Hybrid) ||
         Supports(capabilities, BladeDisplayMuxMode.Dds, allowDds));

    public static IReadOnlyList<BladeDisplayMuxMode> GetSupportedModes(
        BladeDisplayMuxCapabilities capabilities,
        bool allowDds = true)
    {
        var modes = new List<BladeDisplayMuxMode>(3);
        if (Supports(capabilities, BladeDisplayMuxMode.Hybrid))
        {
            modes.Add(BladeDisplayMuxMode.Hybrid);
        }
        if (allowDds && Supports(capabilities, BladeDisplayMuxMode.Dds))
        {
            modes.Add(BladeDisplayMuxMode.Dds);
        }
        if (Supports(capabilities, BladeDisplayMuxMode.Discrete))
        {
            modes.Add(BladeDisplayMuxMode.Discrete);
        }
        return modes;
    }
}
