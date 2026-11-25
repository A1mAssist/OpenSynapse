namespace OpenSynapse.App.ViewModels;

internal static class DownloadProgressText
{
    public static string Format(long bytesReceived, long totalBytes, double bytesPerSecond)
    {
        var received = FormatBytes(bytesReceived);
        var total = totalBytes > 0 ? FormatBytes(totalBytes) : "?";
        var speed = bytesPerSecond > 0
            ? $"{FormatBytes((long)bytesPerSecond)}/s"
            : "--";
        if (totalBytes > bytesReceived && bytesPerSecond > 0)
        {
            return AppStrings.FormatText(
                "RazerDriverDownloadDetail",
                received,
                total,
                speed,
                FormatEta(TimeSpan.FromSeconds(
                    (totalBytes - bytesReceived) / bytesPerSecond)));
        }

        return AppStrings.FormatText(
            "RazerDriverDownloadDetailNoEta",
            received,
            total,
            speed);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} B",
    };

    private static string FormatEta(TimeSpan eta) =>
        eta.TotalHours >= 1
            ? $"{(int)eta.TotalHours}:{eta.Minutes:00}:{eta.Seconds:00}"
            : $"{eta.Minutes}:{eta.Seconds:00}";
}
