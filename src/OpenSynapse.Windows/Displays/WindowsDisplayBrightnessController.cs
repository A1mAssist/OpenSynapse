using System.Management;

namespace OpenSynapse.Windows.Displays;

public sealed class WindowsDisplayBrightnessController
{
    private const string WmiScope = @"root\WMI";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public double? TryGetLevel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                WmiScope,
                "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE");
            using var results = searcher.Get();
            foreach (ManagementObject monitor in results)
            {
                using (monitor)
                {
                    if (monitor["CurrentBrightness"] is not { } value)
                    {
                        continue;
                    }

                    return Math.Clamp(Convert.ToInt32(value) / 100d, 0, 1);
                }
            }
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // Desktops and some OLED panels do not expose WMI backlight control.
        }

        return null;
    }

    public async Task<double> SetLevelAsync(
        double level,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        var target = Math.Clamp(level, 0, 1);
        var percent = (byte)Math.Round(target * 100, MidpointRounding.AwayFromZero);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await Task.Run(() => Apply(percent), cancellationToken);
            }
            catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    "Windows could not write the internal display brightness.",
                    exception);
            }

            return target;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void Apply(byte percent)
    {
        using var methods = new ManagementClass(WmiScope, "WmiMonitorBrightnessMethods", null);
        using var instances = methods.GetInstances();
        foreach (ManagementObject monitor in instances)
        {
            using (monitor)
            {
                if (monitor["Active"] is false)
                {
                    continue;
                }

                using var input = monitor.GetMethodParameters("WmiSetBrightness");
                input["Timeout"] = 1u;
                input["Brightness"] = percent;
                monitor.InvokeMethod("WmiSetBrightness", input, null);
                return;
            }
        }

        throw new InvalidOperationException("Windows did not report a writable internal display brightness method.");
    }

    private static bool IsUnavailable(Exception exception) =>
        exception is ManagementException or UnauthorizedAccessException or InvalidOperationException;
}
