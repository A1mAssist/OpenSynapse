using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

internal static class BladePolicyValidation
{
    public static async Task<int> RunAsync(string[] args)
    {
        var output = ParseOutput(args);
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<Result>();
        string? error = null;

        try
        {
            var discovery = await WindowsHidDiscovery.DiscoverAllAsync();
            var devices = discovery.Devices.Where(device =>
                device.ProductId == 0x02C6 &&
                device.Access == DeviceAccessState.Available &&
                device.UsagePage == 0x0001 &&
                device.Usage == 0x0002 &&
                device.FeatureReportByteLength == RazerFeatureReport.Length).ToArray();
            if (devices.Length != 1)
            {
                throw new InvalidOperationException($"需要一个可用的 Blade 02C6 控制 collection，当前为 {devices.Length}。");
            }

            var reader = new RazerDeviceTelemetryReader(new RazerFeatureTransport());
            var telemetry = await reader.ReadAsync(devices);

            if (telemetry.BladeStartupAnimationEnabled is bool startup)
            {
                var target = !startup;
                var targetReadback = await reader.SetBladeStartupAnimationAsync(devices, target);
                var restored = await reader.SetBladeStartupAnimationAsync(devices, startup);
                results.Add(new("startup-animation", startup, target, targetReadback, restored));
            }

            if (telemetry.BladeNativeDisplayMode is BladeNativeDisplayMode display)
            {
                var target = display == BladeNativeDisplayMode.Uhd
                    ? BladeNativeDisplayMode.Fhd
                    : BladeNativeDisplayMode.Uhd;
                var targetReadback = await reader.SetBladeNativeDisplayModeAsync(devices, target);
                var restored = await reader.SetBladeNativeDisplayModeAsync(devices, display);
                results.Add(new("native-display-mode", display, target, targetReadback, restored));
            }

            if (telemetry.BladeOneTimeFullChargeEnabled is bool fullCharge)
            {
                var target = !fullCharge;
                var targetReadback = await reader.SetBladeOneTimeFullChargeAsync(devices, target);
                var restored = await reader.SetBladeOneTimeFullChargeAsync(devices, fullCharge);
                results.Add(new("one-time-full-charge", fullCharge, target, targetReadback, restored));
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        var artifact = new Artifact(startedAt, DateTimeOffset.UtcNow, results, error);
        var fullPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Blade policy GET/SET/恢复验证完成；证据已写入 {fullPath}");
        return error is null && results.Count > 0 ? 0 : 1;
    }

    private static string ParseOutput(string[] args)
    {
        var index = Array.IndexOf(args, "--output");
        if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("--"))
        {
            throw new ArgumentException("必须提供 --blade-policy-writes --output <json>。");
        }
        return args[index + 1];
    }

    private sealed record Result(
        string Capability,
        object Original,
        object Target,
        object TargetReadback,
        object RestorationReadback);

    private sealed record Artifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        IReadOnlyList<Result> Results,
        string? Error);
}
