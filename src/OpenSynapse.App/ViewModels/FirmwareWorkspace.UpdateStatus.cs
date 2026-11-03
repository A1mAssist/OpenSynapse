using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class FirmwareWorkspace
{
    private static string FormatUpdateStatus(FirmwareUpdateCheckResult result)
    {
        var available = string.Join(", ", result.NewerComponents);
        if (result.DeviceName is { Length: > 0 } name) available = $"{name}: {available}";
        var uncheckedComponents = string.Join(", ", result.UncheckedComponents ?? ["BIOS", "EC", "MCU", "vBIOS"]);
        return result.HasUpdate
            ? result.IsComplete
                ? AppStrings.FormatText("FirmwareUpdateAvailable", available)
                : AppStrings.FormatText("FirmwareUpdateAvailablePartial", available, uncheckedComponents)
            : result.IsComplete
                ? AppStrings.Text("FirmwareUpdateComparedCurrent")
                : AppStrings.FormatText("FirmwareUpdatePartial", uncheckedComponents);
    }
}
