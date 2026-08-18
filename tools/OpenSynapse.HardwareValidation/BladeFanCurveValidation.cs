using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Sensors;

internal static class BladeFanCurveValidation
{
    public static async Task<int> RunAsync(string[] args)
    {
        var output = GetOutput(args);
        var startedAt = DateTimeOffset.UtcNow;
        var samples = new List<Sample>();
        BladeFanControlSnapshot? original = null;
        BladeFanControlSnapshot? restored = null;
        string? error = null;
        BladeFanCurveRuntime? runtime = null;

        try
        {
            var snapshot = await WindowsHidDiscovery.DiscoverAllAsync();
            var devices = snapshot.Devices.Where(device =>
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
            using var monitor = new WindowsPerformanceMonitor();
            original = await reader.ReadBladeFanControlStateAsync(devices);
            runtime = new BladeFanCurveRuntime(reader, monitor);
            var curve = new BladeFanCurve(
                BladeFanCurveTemperatureMode.Cpu,
                [new BladeFanCurvePoint(41, 5000, 5000)],
                [new BladeFanCurvePoint(41, 5000, 5000)]);
            await runtime.StartAsync(devices, curve);

            for (var index = 0; index < 5; index++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                var telemetry = await reader.ReadAsync(devices);
                samples.Add(new Sample(
                    DateTimeOffset.UtcNow,
                    telemetry.BladeCurrentFanCpuRpm,
                    telemetry.BladeCurrentFanGpuRpm,
                    telemetry.BladeFanMode,
                    telemetry.BladeFanTargetRpm));
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            if (runtime is not null)
            {
                try
                {
                    await runtime.DisposeAsync();
                }
                catch (Exception exception)
                {
                    error = error is null ? exception.Message : $"{error} {exception.Message}";
                }
            }
        }

        try
        {
            if (original is not null)
            {
                var snapshot = await WindowsHidDiscovery.DiscoverAllAsync();
                var devices = snapshot.Devices.Where(device =>
                    device.ProductId == 0x02C6 &&
                    device.Access == DeviceAccessState.Available &&
                    device.UsagePage == 0x0001 &&
                    device.Usage == 0x0002 &&
                    device.FeatureReportByteLength == RazerFeatureReport.Length).ToArray();
                if (devices.Length == 1)
                {
                    var reader = new RazerDeviceTelemetryReader(new RazerFeatureTransport());
                    restored = await reader.ReadBladeFanControlStateAsync(devices);
                }
            }
        }
        catch (Exception exception)
        {
            error = error is null ? exception.Message : $"{error} {exception.Message}";
        }

        var document = new Artifact(startedAt, DateTimeOffset.UtcNow, original, samples, restored, error);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"智能风扇曲线已运行并恢复；证据已写入 {Path.GetFullPath(output)}");
        return error is null && restored is not null ? 0 : 1;
    }

    private static string GetOutput(string[] args)
    {
        var index = Array.IndexOf(args, "--output");
        if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("--"))
        {
            throw new ArgumentException("必须提供 --output <json>。");
        }
        return args[index + 1];
    }

    private sealed record Sample(
        DateTimeOffset At,
        int? CpuRpm,
        int? GpuRpm,
        BladeFanMode? Mode,
        int? TargetRpm);

    private sealed record Artifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        BladeFanControlSnapshot? Original,
        IReadOnlyList<Sample> Samples,
        BladeFanControlSnapshot? Restored,
        string? Error);
}
