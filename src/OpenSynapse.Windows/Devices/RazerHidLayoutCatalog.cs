namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Describes how much physical keyboard layout information is known for an
/// RazerHid device. Matrix dimensions alone do not identify named keys.
/// </summary>
public enum RazerHidLayoutSupport
{
    None,
    MatrixOnly,
    Verified,
}

internal static class RazerHidLayoutCatalog
{
    public static RazerHidLayoutSupport GetSupport(
        RazerHidMatrixDimensions? dimensions,
        bool hasKeyRowCommand) =>
        dimensions is null || !hasKeyRowCommand
            ? RazerHidLayoutSupport.None
            : RazerHidLayoutSupport.MatrixOnly;
}
