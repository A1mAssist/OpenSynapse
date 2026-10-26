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
    public static bool Supports(
        BladeDisplayMuxCapabilities capabilities,
        BladeDisplayMuxMode mode) =>
        mode switch
        {
            BladeDisplayMuxMode.Hybrid =>
                capabilities.HasFlag(BladeDisplayMuxCapabilities.Hybrid),
            BladeDisplayMuxMode.Discrete =>
                capabilities.HasFlag(BladeDisplayMuxCapabilities.Discrete),
            BladeDisplayMuxMode.Dds =>
                capabilities.HasFlag(BladeDisplayMuxCapabilities.DynamicDisplaySwitch),
            _ => false,
        };

    public static bool CanSwitch(BladeDisplayMuxCapabilities capabilities) =>
        Supports(capabilities, BladeDisplayMuxMode.Discrete) &&
        (Supports(capabilities, BladeDisplayMuxMode.Hybrid) ||
         Supports(capabilities, BladeDisplayMuxMode.Dds));

    public static IReadOnlyList<BladeDisplayMuxMode> GetSupportedModes(
        BladeDisplayMuxCapabilities capabilities)
    {
        var modes = new List<BladeDisplayMuxMode>(3);
        if (Supports(capabilities, BladeDisplayMuxMode.Hybrid))
        {
            modes.Add(BladeDisplayMuxMode.Hybrid);
        }
        if (Supports(capabilities, BladeDisplayMuxMode.Dds))
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
