using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

internal static class BladeFanValidation
{
    public static async Task<int> RunAsync(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 64;
        }

        var startedAt = DateTimeOffset.UtcNow;
        OperationResult? result = null;
        string? discoveryError = null;
        using var interrupted = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            interrupted.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var snapshot = await WindowsHidDiscovery.DiscoverAllAsync();
            if (snapshot.ErrorMessage is not null)
            {
                throw new InvalidOperationException(snapshot.ErrorMessage);
            }

            var devices = snapshot.Devices.Where(device =>
                device.ProductId == 0x02C6 &&
                device.Access == DeviceAccessState.Available &&
                device.UsagePage == 0x0001 &&
                device.Usage == 0x0002 &&
                device.FeatureReportByteLength == RazerFeatureReport.Length).ToArray();
            if (devices.Length != 1)
            {
                throw new InvalidOperationException(
                    $"需要且只能有一个可用的 Blade 02C6 控制 collection，当前为 {devices.Length}。请关闭 Synapse UI 后重试。");
            }

            var reader = new RazerDeviceTelemetryReader(new RazerFeatureTransport());
            var telemetry = await reader.ReadAsync(devices, interrupted.Token);
            if (telemetry.BladeFanMode is not BladeFanMode originalMode)
            {
                throw new InvalidOperationException("无法读取 Blade 两个风扇分区的统一模式。");
            }
            if (originalMode == BladeFanMode.Manual && telemetry.BladeFanTargetRpm is null)
            {
                throw new InvalidOperationException("无法读取 Blade 两个风扇分区的统一固定转速。");
            }

            var original = telemetry.BladeFanTargetRpm is int target
                ? new BladeFanControlState(originalMode, target)
                : await reader.SetBladeFanAsync(devices, BladeFanMode.Automatic, null, interrupted.Token);
            result = await ExecuteAsync(
                reader,
                devices,
                original,
                options.TargetRpm,
                options.HoldSeconds,
                interrupted.Token);
        }
        catch (Exception exception)
        {
            discoveryError = exception.Message;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        var artifact = new ValidationArtifact(
            startedAt,
            DateTimeOffset.UtcNow,
            "1532:02C6",
            "0001:0002",
            RazerFeatureReport.Length,
            options.TargetRpm,
            options.HoldSeconds,
            result?.Original,
            result?.SameValueReadback,
            result?.TargetReadback,
            result?.RestorationReadback,
            discoveryError ?? result?.OperationError,
            result?.RestorationError);
        var output = Path.GetFullPath(options.OutputPath);
        await WriteArtifactAsync(output, artifact);

        var operationError = discoveryError ?? result?.OperationError;
        if (operationError is not null || result?.RestorationError is not null)
        {
            if (operationError is not null)
            {
                Console.Error.WriteLine($"操作失败：{operationError}");
            }
            if (result?.RestorationError is not null)
            {
                Console.Error.WriteLine($"恢复失败：{result.RestorationError}");
            }
            Console.Error.WriteLine($"证据已写入 {output}");
            return 1;
        }

        Console.WriteLine($"固定风扇目标 {options.TargetRpm} RPM 已读回；原状态已恢复并读回。证据已写入 {output}");
        return 0;
    }

    internal static async Task<OperationResult> ExecuteAsync(
        IRazerDeviceTelemetryReader reader,
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanControlState original,
        int targetRpm,
        int holdSeconds,
        CancellationToken cancellationToken,
        Func<int, CancellationToken, Task>? holdAsync = null)
    {
        var result = new OperationResult(original);
        var sameValueApplied = false;
        try
        {
            var same = await reader.SetBladeFanAsync(
                devices,
                original.Mode,
                original.Mode == BladeFanMode.Manual ? original.TargetRpm : null,
                cancellationToken);
            result.SameValueReadback = same;
            sameValueApplied = true;
            if (same.TargetRpm != original.TargetRpm)
            {
                throw new InvalidOperationException(
                    $"同值固定风扇读回不一致：原值 {original.TargetRpm} RPM，读回 {same.TargetRpm} RPM。");
            }

            if (Math.Abs(targetRpm - original.TargetRpm) != BladeFanProtocol.StepRpm)
            {
                throw new ArgumentException(
                    $"验证目标必须与原值相差 {BladeFanProtocol.StepRpm} RPM。", nameof(targetRpm));
            }

            result.TargetReadback = await reader.SetBladeFanAsync(
                devices, BladeFanMode.Manual, targetRpm, cancellationToken);
            if (result.TargetReadback != new BladeFanControlState(BladeFanMode.Manual, targetRpm))
            {
                throw new InvalidOperationException(
                    $"目标固定风扇读回不一致：写入 Manual / {targetRpm} RPM，" +
                    $"读回 {result.TargetReadback.Mode} / {result.TargetReadback.TargetRpm} RPM。");
            }
            if (holdAsync is not null)
            {
                await holdAsync(holdSeconds, cancellationToken);
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(holdSeconds), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            result.OperationError = exception.Message;
        }
        finally
        {
            if (sameValueApplied)
            {
                var restoration = await RestoreAsync(reader, devices, original);
                result.RestorationReadback = restoration.Readback;
                result.RestorationError = restoration.Error;
            }
        }

        return result;
    }

    private static async Task<RestoreResult> RestoreAsync(
        IRazerDeviceTelemetryReader reader,
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanControlState original)
    {
        var errors = new List<string>();
        BladeFanControlState? readback = null;

        async Task AttemptAsync(Func<Task<BladeFanControlState>> action)
        {
            try
            {
                readback = await action();
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }
        }

        if (original.Mode == BladeFanMode.Manual)
        {
            await AttemptAsync(() => reader.SetBladeFanAsync(
                devices, BladeFanMode.Manual, original.TargetRpm, CancellationToken.None).AsTask());
        }
        else
        {
            await AttemptAsync(() => reader.SetBladeFanAsync(
                devices, BladeFanMode.Manual, original.TargetRpm, CancellationToken.None).AsTask());
            await AttemptAsync(() => reader.SetBladeFanAsync(
                devices, BladeFanMode.Automatic, null, CancellationToken.None).AsTask());
        }

        if (readback is not null && readback != original)
        {
            errors.Add($"恢复读回不一致：期望 {original.Mode} / {original.TargetRpm} RPM，读回 {readback.Mode} / {readback.TargetRpm} RPM。");
        }

        return new RestoreResult(readback, errors.Count == 0 ? null : string.Join(" ", errors));
    }

    private static async Task WriteArtifactAsync(string output, ValidationArtifact artifact)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(output), ".json"))
        {
            throw new ArgumentException("--output 必须是 .json 文件。", nameof(output));
        }
        if (File.Exists(output))
        {
            throw new IOException($"证据文件已存在，拒绝覆盖：{output}");
        }

        var directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, output, overwrite: false);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal sealed record OperationResult(BladeFanControlState Original)
    {
        public BladeFanControlState? SameValueReadback { get; set; }
        public BladeFanControlState? TargetReadback { get; set; }
        public BladeFanControlState? RestorationReadback { get; set; }
        public string? OperationError { get; set; }
        public string? RestorationError { get; set; }
    }

    private sealed record RestoreResult(BladeFanControlState? Readback, string? Error);

    private sealed record ValidationArtifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Device,
        string Collection,
        int FeatureReportByteLength,
        int TargetRpm,
        int HoldSeconds,
        BladeFanControlState? Original,
        BladeFanControlState? SameValueReadback,
        BladeFanControlState? TargetReadback,
        BladeFanControlState? RestorationReadback,
        string? OperationError,
        string? RestorationError);

    internal sealed record Options(int TargetRpm, int HoldSeconds, string OutputPath)
    {
        public static Options Parse(string[] args)
        {
            int? targetRpm = null;
            int? holdSeconds = null;
            string? outputPath = null;
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--blade-fan-fixed":
                        break;
                    case "--target-rpm" when index + 1 < args.Length && int.TryParse(args[++index], out var parsedTarget):
                        BladeFanProtocol.ValidateTargetRpm(parsedTarget);
                        targetRpm = parsedTarget;
                        break;
                    case "--hold-seconds" when index + 1 < args.Length && int.TryParse(args[++index], out var parsedHold)
                        && parsedHold is >= 5 and <= 60:
                        holdSeconds = parsedHold;
                        break;
                    case "--output" when index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal):
                        outputPath = args[++index];
                        break;
                    default:
                        throw new ArgumentException($"不支持或不完整的参数：{args[index]}");
                }
            }

            if (targetRpm is null || holdSeconds is null || string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("必须提供 --blade-fan-fixed --target-rpm <2000..5000> --hold-seconds <5..60> --output <json>。");
            }
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputPath), ".json"))
            {
                throw new ArgumentException("--output 必须是 .json 文件。");
            }
            if (File.Exists(Path.GetFullPath(outputPath)))
            {
                throw new ArgumentException("--output 已存在，拒绝覆盖。");
            }
            return new Options(targetRpm.Value, holdSeconds.Value, outputPath);
        }
    }
}
