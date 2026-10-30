namespace OpenSynapse.Core.Devices;

/// <summary>
/// Keyboard-lighting report family declared by one Blade manifest. The value is
/// evidence-backed metadata; the controller must not infer it from a product id.
/// </summary>
public enum BladeLightingDialect
{
    Unknown,
    FirmwareEffect,
    LedMatrix710,
    LedMatrixBlade,
    LedMatrixBlade2,
    LedMatrix2026,
}
