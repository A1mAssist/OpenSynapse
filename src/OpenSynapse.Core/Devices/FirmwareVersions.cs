namespace OpenSynapse.Core.Devices;

public sealed record FirmwareVersions(
    string Bios,
    string Ec,
    string Mcu,
    string Vbios,
    string? VbiosModel = null)
{
    public static FirmwareVersions Empty { get; } = new("--", "--", "--", "--");
}
