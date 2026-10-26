namespace OpenSynapse.Windows.Macros;

/// <summary>
/// Products whose middleware model declares OBMSpecs.supportedFeature.macros. The
/// keyboard PIDs are the ones this app already admits; the mouse PIDs are the Naga and
/// Turret models that advertise onboard macro slots.
/// </summary>
public static class RazerOnboardMacroDevices
{
    public static readonly IReadOnlySet<ushort> Keyboards = new HashSet<ushort>
    {
        0x02D0, 0x02D1, 0x02D8, 0x02E3, 0x02E4, 0x02E5, 0x02E6, 0x02EA, 0x02EB, 0x02F4, 0x02CC,
    };

    public static readonly IReadOnlySet<ushort> Mice = new HashSet<ushort>
    {
        0x0067, 0x008D, 0x0093, 0x0075, 0x0A22,
    };

    public static readonly IReadOnlySet<ushort> Products =
        new HashSet<ushort>(Keyboards.Concat(Mice));
}
