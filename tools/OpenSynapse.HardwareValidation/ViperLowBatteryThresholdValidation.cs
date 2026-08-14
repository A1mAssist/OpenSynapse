using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

internal static class ViperLowBatteryThresholdValidation
{
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(60);

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
        var transport = new RazerFeatureTransport();
        OperationResult? result = null;
        string? discoveryError = null;

        try
        {
            var snapshot = await WindowsHidDiscovery.DiscoverAllAsync();
            if (snapshot.ErrorMessage is not null)
            {
                throw new InvalidOperationException(snapshot.ErrorMessage);
            }

            var devices = snapshot.Devices.Where(device =>
                device.ProductId == ViperProduct184Protocol.ProductId &&
                device.Access == DeviceAccessState.Available &&
                device.UsagePage == 0x0001 &&
                device.Usage == 0x0002 &&
                device.FeatureReportByteLength == RazerFeatureReport.Length).ToArray();
            if (devices.Length != 1)
            {
                throw new InvalidOperationException(
                    $"需要且只能有一个可用的 Viper 00B8 控制 collection，当前为 {devices.Length}。请确保鼠标已唤醒并关闭 Synapse UI。");
            }

            var originalRaw = await ReadRawAsync(transport, devices[0].Id);
            Console.WriteLine($"原阈值：{ViperLowBatteryThresholdProtocol.Format(originalRaw)}。");
            result = await ExecuteAsync(transport, devices[0].Id, originalRaw, options.TargetPercent);
        }
        catch (Exception exception)
        {
            discoveryError = exception.Message;
        }

        var artifact = new ValidationArtifact(
            startedAt,
            DateTimeOffset.UtcNow,
            "1532:00B8",
            "0001:0002",
            RazerFeatureReport.Length,
            options.TargetPercent,
            result?.OriginalRaw,
            result?.OriginalPercent,
            result?.SameValueReadbackRaw,
            result?.SameValueReadbackPercent,
            result?.TargetRaw,
            result?.TargetReadbackRaw,
            result?.TargetReadbackPercent,
            result?.RestorationReadbackRaw,
            result?.RestorationReadbackPercent,
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

        Console.WriteLine(
            $"目标 {options.TargetPercent}% 写入并读回成功；原阈值 {result!.OriginalPercent}% (raw 0x{result.OriginalRaw:X2}) 已恢复并读回。");
        Console.WriteLine($"证据已写入 {output}");
        return 0;
    }

    internal static async Task<OperationResult> ExecuteAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        byte originalRaw,
        int targetPercent)
    {
        var originalPercent = ViperLowBatteryThresholdProtocol.ToPercent(originalRaw);
        byte targetRaw;
        try
        {
            targetRaw = ViperLowBatteryThresholdProtocol.ToRaw(targetPercent);
            var restorationRaw = ViperLowBatteryThresholdProtocol.ToRaw(originalPercent);
            if (restorationRaw != originalRaw)
            {
                return new OperationResult(
                    originalRaw, originalPercent, targetRaw, null, null, null, null, null, null,
                    $"原值 {ViperLowBatteryThresholdProtocol.Format(originalRaw)} 无法通过官方百分比 SET 精确恢复，已在写入前停止。",
                    null);
            }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return new OperationResult(
                originalRaw, originalPercent, null, null, null, null, null, null, null,
                $"原值或目标值不在官方可写集合内，已在写入前停止：{exception.Message}",
                null);
        }

        byte? targetReadbackRaw = null;
        int? targetReadbackPercent = null;
        byte? sameValueReadbackRaw = null;
        int? sameValueReadbackPercent = null;
        byte? restorationReadbackRaw = null;
        int? restorationReadbackPercent = null;
        string? operationError = null;
        string? restorationError = null;
        var writeAttempted = false;

        try
        {
            writeAttempted = true;
            await SendAsync(transport, devicePath,
                ViperProduct184Protocol.CreateSetLowBatteryThresholdRequest(originalPercent));
            sameValueReadbackRaw = await ReadRawAsync(transport, devicePath);
            sameValueReadbackPercent = ViperLowBatteryThresholdProtocol.ToPercent(sameValueReadbackRaw.Value);
            if (sameValueReadbackRaw != originalRaw)
            {
                throw new InvalidOperationException(
                    $"同值阈值读回不一致：写入 {ViperLowBatteryThresholdProtocol.Format(originalRaw)}，读回 {ViperLowBatteryThresholdProtocol.Format(sameValueReadbackRaw.Value)}。");
            }

            await SendAsync(transport, devicePath,
                ViperProduct184Protocol.CreateSetLowBatteryThresholdRequest(targetPercent));
            targetReadbackRaw = await ReadRawAsync(transport, devicePath);
            targetReadbackPercent = ViperLowBatteryThresholdProtocol.ToPercent(targetReadbackRaw.Value);
            if (targetReadbackPercent != targetPercent)
            {
                throw new InvalidOperationException(
                    $"目标阈值读回不一致：写入 {targetPercent}% (raw 0x{targetRaw:X2})，读回 {ViperLowBatteryThresholdProtocol.Format(targetReadbackRaw.Value)}。");
            }
        }
        catch (Exception exception)
        {
            operationError = exception.Message;
        }
        finally
        {
            if (writeAttempted)
            {
                var restoration = await RestoreAsync(transport, devicePath, originalRaw, originalPercent);
                restorationReadbackRaw = restoration.Raw;
                restorationReadbackPercent = restoration.Percent;
                restorationError = restoration.Error;
            }
        }

        return new OperationResult(
            originalRaw,
            originalPercent,
            targetRaw,
            sameValueReadbackRaw,
            sameValueReadbackPercent,
            targetReadbackRaw,
            targetReadbackPercent,
            restorationReadbackRaw,
            restorationReadbackPercent,
            operationError,
            restorationError);
    }

    private static async Task<RestoreResult> RestoreAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        byte originalRaw,
        int originalPercent)
    {
        var errors = new List<string>();
        try
        {
            await SendAsync(transport, devicePath,
                ViperProduct184Protocol.CreateSetLowBatteryThresholdRequest(originalPercent));
        }
        catch (Exception exception)
        {
            errors.Add($"恢复写入：{exception.Message}");
        }

        byte? readbackRaw = null;
        int? readbackPercent = null;
        try
        {
            readbackRaw = await ReadRawAsync(transport, devicePath);
            readbackPercent = ViperLowBatteryThresholdProtocol.ToPercent(readbackRaw.Value);
            if (readbackRaw != originalRaw)
            {
                errors.Add(
                    $"恢复读回不一致：原值 {ViperLowBatteryThresholdProtocol.Format(originalRaw)}，读回 {ViperLowBatteryThresholdProtocol.Format(readbackRaw.Value)}。");
            }
        }
        catch (Exception exception)
        {
            errors.Add($"恢复读回：{exception.Message}");
        }

        return new RestoreResult(readbackRaw, readbackPercent, errors.Count == 0 ? null : string.Join(" ", errors));
    }

    private static async Task<byte> ReadRawAsync(IRazerFeatureTransport transport, string devicePath)
    {
        var response = await SendAsync(
            transport, devicePath, ViperProduct184Protocol.CreateGetLowBatteryThresholdRequest());
        return ViperLowBatteryThresholdProtocol.ParseRaw(response);
    }

    private static Task<byte[]> SendAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        byte[] request) =>
        transport.QueryAsync(
            devicePath,
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            DeviceWait,
            CancellationToken.None);

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

    internal sealed record OperationResult(
        byte OriginalRaw,
        int OriginalPercent,
        byte? TargetRaw,
        byte? SameValueReadbackRaw,
        int? SameValueReadbackPercent,
        byte? TargetReadbackRaw,
        int? TargetReadbackPercent,
        byte? RestorationReadbackRaw,
        int? RestorationReadbackPercent,
        string? OperationError,
        string? RestorationError);

    private sealed record RestoreResult(byte? Raw, int? Percent, string? Error);

    private sealed record ValidationArtifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Device,
        string Collection,
        int FeatureReportByteLength,
        int TargetPercent,
        byte? OriginalRaw,
        int? OriginalPercent,
        byte? SameValueReadbackRaw,
        int? SameValueReadbackPercent,
        byte? TargetRaw,
        byte? TargetReadbackRaw,
        int? TargetReadbackPercent,
        byte? RestorationReadbackRaw,
        int? RestorationReadbackPercent,
        string? OperationError,
        string? RestorationError);

    internal sealed record Options(int TargetPercent, string OutputPath)
    {
        public static Options Parse(string[] args)
        {
            int? targetPercent = null;
            string? outputPath = null;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--viper-low-battery-threshold":
                        break;
                    case "--target" when index + 1 < args.Length &&
                                         int.TryParse(args[index + 1], out var parsedTarget):
                        targetPercent = parsedTarget;
                        index++;
                        break;
                    case "--output" when index + 1 < args.Length &&
                                         !args[index + 1].StartsWith("--", StringComparison.Ordinal):
                        outputPath = args[++index];
                        break;
                    default:
                        throw new ArgumentException($"不支持或不完整的参数：{args[index]}");
                }
            }

            if (targetPercent is null || string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException(
                    "必须提供 --viper-low-battery-threshold --target <5..100, step 5> --output <json>。");
            }
            _ = ViperLowBatteryThresholdProtocol.ToRaw(targetPercent.Value);
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputPath), ".json"))
            {
                throw new ArgumentException("--output 必须是 .json 文件。");
            }
            if (File.Exists(Path.GetFullPath(outputPath)))
            {
                throw new ArgumentException("--output 已存在，拒绝覆盖。");
            }

            return new Options(targetPercent.Value, outputPath);
        }
    }
}
