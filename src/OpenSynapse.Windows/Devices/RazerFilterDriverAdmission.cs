namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The 0F1F per-LED path needs Razer's RZCONTROL filter driver before the
/// 320-byte SET_FEATURE frame can be sent. When the endpoint is missing, the
/// lighting page offers Razer's signed driver package instead of hiding silently.
/// </summary>
internal static class RazerFilterDriverAdmission
{
    internal const ushort ArgbProductId = 0x0F1F;
    internal const string ArgbInstallerUri =
        "https://app-assets.razer.com/files/synapse/devices/3871/1.0.0.0/PID0f1fDrv_v0.1.0.0.exe";

    internal static bool NeedsDriver(ushort productId, bool endpointReady) =>
        productId == ArgbProductId && !endpointReady;
}
