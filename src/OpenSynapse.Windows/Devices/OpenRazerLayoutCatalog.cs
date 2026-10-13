namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Describes how much physical keyboard layout information is known for an
/// OpenRazer device. Matrix dimensions alone do not identify named keys.
/// </summary>
public enum OpenRazerLayoutSupport
{
    None,
    MatrixOnly,
    Verified,
}

internal static class OpenRazerLayoutCatalog
{
    public static OpenRazerLayoutSupport GetSupport(
        OpenRazerMatrixDimensions? dimensions,
        bool hasKeyRowCommand) =>
        dimensions is null || !hasKeyRowCommand
            ? OpenRazerLayoutSupport.None
            : OpenRazerLayoutSupport.MatrixOnly;
}
