using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

internal static class ViperDpiStagesValidation
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
                    $"需要且只能有一个可用的 Viper 00B8 控制 collection，当前为 {devices.Length}。请唤醒鼠标并关闭 Synapse UI。");
            }

            result = await ExecuteAsync(new RazerFeatureTransport(), devices[0].Id);
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
            result?.Original,
            result?.SameValueReadback,
            result?.Target,
            result?.TargetReadback,
            result?.RestorationReadback,
            result?.ChangedStage,
            result?.Delta,
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
            $"DPI 档位 {result!.ChangedStage} 已临时调整 {result.Delta:+#;-#}，目标读回成功；原表已恢复并读回。");
        Console.WriteLine($"证据已写入 {output}");
        return 0;
    }

    internal static async Task<OperationResult> ExecuteAsync(
        IRazerFeatureTransport transport,
        string devicePath)
    {
        ViperDpiStagesState? original = null;
        ViperDpiStagesState? sameValueReadback = null;
        ViperDpiStagesState? target = null;
        ViperDpiStagesState? targetReadback = null;
        ViperDpiStagesState? restorationReadback = null;
        byte? changedStage = null;
        int? delta = null;
        string? operationError = null;
        string? restorationError = null;
        var writeAttempted = false;

        try
        {
            original = await ReadAsync(transport, devicePath);
            target = CreateTarget(original, out var targetStage, out var targetDelta);
            changedStage = targetStage;
            delta = targetDelta;

            writeAttempted = true;
            await SendAsync(transport, devicePath,
                ViperProduct184Protocol.CreateSetDpiStagesRequest(original));
            sameValueReadback = await ReadAsync(transport, devicePath);
            EnsureEqual(original, sameValueReadback, "同值");

            await SendAsync(transport, devicePath,
                ViperProduct184Protocol.CreateSetDpiStagesRequest(target));
            targetReadback = await ReadAsync(transport, devicePath);
            EnsureEqual(target, targetReadback, "目标");
        }
        catch (Exception exception)
        {
            operationError = exception.Message;
        }
        finally
        {
            if (writeAttempted && original is not null)
            {
                var restoration = await RestoreAsync(transport, devicePath, original);
                restorationReadback = restoration.Readback;
                restorationError = restoration.Error;
            }
        }

        return new OperationResult(
            original,
            sameValueReadback,
            target,
            targetReadback,
            restorationReadback,
            changedStage,
            delta,
            operationError,
            restorationError);
    }

    private static ViperDpiStagesState CreateTarget(
        ViperDpiStagesState original,
        out byte changedStage,
        out int delta)
    {
        for (var index = 0; index < original.Stages.Count; index++)
        {
            var stage = original.Stages[index];
            if (stage.Number == original.ActiveStage)
            {
                continue;
            }

            var candidateDelta = stage.X <= 29950 && stage.Y <= 29950 ? 50
                : stage.X >= 150 && stage.Y >= 150 ? -50
                : 0;
            if (candidateDelta == 0)
            {
                continue;
            }

            var stages = original.Stages.ToArray();
            stages[index] = stage with { X = stage.X + candidateDelta, Y = stage.Y + candidateDelta };
            changedStage = stage.Number;
            delta = candidateDelta;
            return new ViperDpiStagesState(original.ActiveStage, stages);
        }

        throw new InvalidOperationException("没有可安全调整 50 DPI 的非当前档位，已在写入前停止。");
    }

    private static async Task<RestoreResult> RestoreAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        ViperDpiStagesState original)
    {
        var errors = new List<string>();
        try
        {
            await SendAsync(transport, devicePath,
                ViperProduct184Protocol.CreateSetDpiStagesRequest(original));
        }
        catch (Exception exception)
        {
            errors.Add($"恢复写入：{exception.Message}");
        }

        ViperDpiStagesState? readback = null;
        try
        {
            readback = await ReadAsync(transport, devicePath);
            EnsureEqual(original, readback, "恢复");
        }
        catch (Exception exception)
        {
            errors.Add($"恢复读回：{exception.Message}");
        }

        return new RestoreResult(readback, errors.Count == 0 ? null : string.Join(" ", errors));
    }

    private static void EnsureEqual(
        ViperDpiStagesState expected,
        ViperDpiStagesState actual,
        string phase)
    {
        if (expected.ActiveStage != actual.ActiveStage ||
            expected.Stages.Count != actual.Stages.Count ||
            !expected.Stages.SequenceEqual(actual.Stages))
        {
            throw new InvalidOperationException(
                $"{phase} DPI 档位读回不一致：写入 {ViperDpiStagesProtocol.Format(expected)}，读回 {ViperDpiStagesProtocol.Format(actual)}。");
        }
    }

    private static async Task<ViperDpiStagesState> ReadAsync(
        IRazerFeatureTransport transport,
        string devicePath)
    {
        var response = await SendAsync(
            transport, devicePath, ViperProduct184Protocol.CreateGetDpiStagesRequest());
        return ViperDpiStagesProtocol.Parse(response);
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
        ViperDpiStagesState? Original,
        ViperDpiStagesState? SameValueReadback,
        ViperDpiStagesState? Target,
        ViperDpiStagesState? TargetReadback,
        ViperDpiStagesState? RestorationReadback,
        byte? ChangedStage,
        int? Delta,
        string? OperationError,
        string? RestorationError);

    private sealed record RestoreResult(ViperDpiStagesState? Readback, string? Error);

    private sealed record ValidationArtifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Device,
        string Collection,
        int FeatureReportByteLength,
        ViperDpiStagesState? Original,
        ViperDpiStagesState? SameValueReadback,
        ViperDpiStagesState? Target,
        ViperDpiStagesState? TargetReadback,
        ViperDpiStagesState? RestorationReadback,
        byte? ChangedStage,
        int? Delta,
        string? OperationError,
        string? RestorationError);

    internal sealed record Options(string OutputPath)
    {
        public static Options Parse(string[] args)
        {
            string? outputPath = null;
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--viper-dpi-stages":
                        break;
                    case "--output" when index + 1 < args.Length &&
                                         !args[index + 1].StartsWith("--", StringComparison.Ordinal):
                        outputPath = args[++index];
                        break;
                    default:
                        throw new ArgumentException($"不支持或不完整的参数：{args[index]}");
                }
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("必须提供 --viper-dpi-stages --output <json>。");
            }
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputPath), ".json"))
            {
                throw new ArgumentException("--output 必须是 .json 文件。");
            }
            if (File.Exists(Path.GetFullPath(outputPath)))
            {
                throw new ArgumentException("--output 已存在，拒绝覆盖。");
            }

            return new Options(outputPath);
        }
    }
}
