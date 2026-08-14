using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

internal static class KeyboardLightingValidation
{
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(1);
    private static readonly RazerRgb RestoreColor = new(0x99, 0xDD, 0x72);

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
        OperationResult? operation = null;
        string? setupError = null;
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

            var blades = snapshot.Devices.Where(device =>
                device.ProductId == 0x02C6 &&
                device.Access == DeviceAccessState.Available &&
                device.UsagePage == 0x0001 &&
                device.Usage == 0x0002 &&
                device.FeatureReportByteLength == RazerFeatureReport.Length).ToArray();
            if (blades.Length != 1)
            {
                throw new InvalidOperationException(
                    $"需要且只能有一个可用的 Blade 02C6 控制 collection，当前为 {blades.Length}。请关闭 Synapse UI 后重试。");
            }

            operation = await ExecuteAsync(
                transport,
                blades[0].Id,
                options.Target,
                async () =>
                {
                    Console.WriteLine($"键盘灯效 {options.TargetName} 已写入；保持 {options.HoldSeconds} 秒供目视确认。");
                    await Task.Delay(TimeSpan.FromSeconds(options.HoldSeconds), interrupted.Token);
                });
        }
        catch (Exception exception)
        {
            setupError = exception.Message;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        var artifact = new Artifact(
            startedAt,
            DateTimeOffset.UtcNow,
            "1532:02C6",
            "0001:0002",
            RazerFeatureReport.Length,
            options.TargetName,
            options.HoldSeconds,
            "Static #99DD72",
            operation?.TargetAcknowledged ?? false,
            operation?.MatrixRowsAcknowledged,
            operation?.RestorationAcknowledged ?? false,
            null,
            null,
            setupError ?? operation?.OperationError,
            operation?.RestorationError);
        var output = Path.GetFullPath(options.OutputPath);
        await WriteArtifactAsync(output, artifact);

        var operationError = setupError ?? operation?.OperationError;
        if (operationError is not null || operation?.RestorationError is not null)
        {
            if (operationError is not null)
            {
                Console.Error.WriteLine($"操作失败：{operationError}");
            }
            if (operation?.RestorationError is not null)
            {
                Console.Error.WriteLine($"恢复失败：{operation.RestorationError}");
            }
            return 1;
        }

        Console.WriteLine($"键盘已恢复为 Static #99DD72。证据已写入 {output}");
        return 0;
    }

    internal static async Task<OperationResult> ExecuteAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        Target target,
        Func<Task> holdAsync)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentNullException.ThrowIfNull(holdAsync);

        var targetAcknowledged = false;
        int? matrixRowsAcknowledged = null;
        var restorationAcknowledged = false;
        string? operationError = null;
        string? restorationError = null;
        byte transactionId = 0;

        byte NextTransactionId()
        {
            var current = transactionId;
            transactionId = current == 30 ? (byte)0 : (byte)(current + 1);
            return current;
        }
        async Task RestoreAsync(CancellationToken cancellationToken)
        {
            if (target == Target.MatrixLocator)
            {
                var frame = Enumerable.Repeat(
                    RestoreColor,
                    BladeLightingProtocol.Rows * BladeLightingProtocol.Columns).ToArray();
                var requests = new byte[BladeLightingProtocol.Rows][];
                for (byte row = 0; row < BladeLightingProtocol.Rows; row++)
                {
                    var offset = row * BladeLightingProtocol.Columns;
                    requests[row] = BladeLightingProtocol.CreateMatrixRowRequest(
                        (byte)(row + 1),
                        row,
                        0,
                        frame[offset..(offset + BladeLightingProtocol.Columns)]);
                }
                await transport.SendBatchAsync(devicePath, requests, DeviceWait, cancellationToken);
            }
            else
            {
                await SendAsync(
                    transport,
                    devicePath,
                    BladeLightingProtocol.CreateStaticRequest(RestoreColor),
                    cancellationToken);
            }
            restorationAcknowledged = true;
        }

        if (target == Target.StaticRed)
        {
            try
            {
                await SendAsync(
                    transport,
                    devicePath,
                    BladeLightingProtocol.CreateStaticRequest(new RazerRgb(0xFF, 0x00, 0x00)),
                    CancellationToken.None);
                targetAcknowledged = true;
                await holdAsync();
            }
            catch (Exception exception)
            {
                operationError = exception.Message;
            }
            finally
            {
                try
                {
                    await RestoreAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    restorationError = exception.Message;
                }
            }
        }
        else
        {
            BladeMatrixFramePump? pump = null;
            try
            {
                await SendAsync(
                    transport,
                    devicePath,
                    BladeDeviceModeProtocol.CreateSetSoftwareRequest(NextTransactionId()),
                    CancellationToken.None);
                await SendAsync(
                    transport,
                    devicePath,
                    BladeLightingProtocol.CreateLightingEngineGateRequest(NextTransactionId()),
                    CancellationToken.None);
                pump = new BladeMatrixFramePump(
                    transport,
                    devicePath,
                    RestoreAsync);
                if (!pump.TryPublish(CreateLocatorFrame()))
                {
                    throw new InvalidOperationException("矩阵帧队列拒绝了目标帧。");
                }
                await pump.FirstFrameApplied;
                var hold = holdAsync();
                while (!hold.IsCompleted)
                {
                    await Task.WhenAny(hold, Task.Delay(TimeSpan.FromMilliseconds(40)));
                    if (!hold.IsCompleted && !pump.TryPublish(CreateLocatorFrame()))
                    {
                        await pump.Completion;
                        throw new InvalidOperationException("矩阵帧泵在验证期间停止。");
                    }
                }
                await hold;
            }
            catch (Exception exception)
            {
                operationError = exception.Message;
            }
            finally
            {
                if (pump is not null)
                {
                try
                {
                    await pump.DisposeAsync();
                        targetAcknowledged = true;
                        matrixRowsAcknowledged = BladeLightingProtocol.Rows;
                    }
                    catch (Exception exception)
                    {
                        operationError ??= exception.Message;
                        if (!restorationAcknowledged)
                        {
                            restorationError = "矩阵泵未确认恢复命令。";
                        }
                    }
                }
                try
                {
                    await SendAsync(
                        transport,
                        devicePath,
                        BladeDeviceModeProtocol.CreateSetNormalRequest(NextTransactionId()),
                        CancellationToken.None);
                }
                catch (Exception exception)
                {
                    restorationError ??= $"恢复 Normal 设备模式失败：{exception.Message}";
                }
            }
        }

        return new OperationResult(
            targetAcknowledged,
            matrixRowsAcknowledged,
            restorationAcknowledged,
            operationError,
            restorationError);
    }

    private static RazerRgb[] CreateLocatorFrame()
    {
        var frame = new RazerRgb[BladeLightingProtocol.Rows * BladeLightingProtocol.Columns];
        for (var row = 0; row < BladeLightingProtocol.Rows; row++)
        {
            var color = row switch
            {
                0 => new RazerRgb(0xFF, 0x00, 0x00),
                5 => new RazerRgb(0x00, 0x00, 0xFF),
                _ => new RazerRgb(0x00, 0xFF, 0x00),
            };
            Array.Fill(frame, color, row * BladeLightingProtocol.Columns, BladeLightingProtocol.Columns);
        }

        frame[1] = new RazerRgb(0xFF, 0xFF, 0xFF);
        frame[BladeLightingProtocol.Columns - 1] = new RazerRgb(0xFF, 0xFF, 0xFF);
        frame[^(BladeLightingProtocol.Columns - 1)] = new RazerRgb(0xFF, 0xFF, 0xFF);
        frame[^1] = new RazerRgb(0xFF, 0xFF, 0xFF);
        return frame;
    }

    private static Task<byte[]> SendAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        byte[] request,
        CancellationToken cancellationToken) =>
        transport.QueryAsync(
            devicePath,
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            DeviceWait,
            cancellationToken);

    private static async Task WriteArtifactAsync(string output, Artifact artifact)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(output), ".json"))
        {
            throw new ArgumentException("--output 必须是 .json 文件。", nameof(output));
        }
        if (File.Exists(output))
        {
            throw new IOException($"证据文件已存在，拒绝覆盖：{output}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = Path.Combine(
            Path.GetDirectoryName(output)!,
            $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
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

    internal enum Target
    {
        StaticRed,
        MatrixLocator,
    }

    internal sealed record OperationResult(
        bool TargetAcknowledged,
        int? MatrixRowsAcknowledged,
        bool RestorationAcknowledged,
        string? OperationError,
        string? RestorationError);

    private sealed record Artifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Device,
        string Collection,
        int FeatureReportByteLength,
        string Target,
        int HoldSeconds,
        string RestorationTarget,
        bool TargetAcknowledged,
        int? MatrixRowsAcknowledged,
        bool RestorationAcknowledged,
        bool? TargetVisualConfirmed,
        bool? RestorationVisualConfirmed,
        string? OperationError,
        string? RestorationError);

    internal sealed record Options(Target Target, int HoldSeconds, string OutputPath)
    {
        public string TargetName => Target switch
        {
            Target.StaticRed => "Static red",
            Target.MatrixLocator => "6 x 17 locator matrix",
            _ => throw new ArgumentOutOfRangeException(),
        };

        public static Options Parse(string[] args)
        {
            Target? target = null;
            var holdSeconds = 30;
            string? outputPath = null;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--keyboard-lighting" when index + 1 < args.Length:
                        target = args[++index].ToLowerInvariant() switch
                        {
                            "static-red" => KeyboardLightingValidation.Target.StaticRed,
                            "matrix-locator" => KeyboardLightingValidation.Target.MatrixLocator,
                            _ => throw new ArgumentException(
                                "--keyboard-lighting 只接受 static-red 或 matrix-locator。"),
                        };
                        break;
                    case "--hold-seconds" when index + 1 < args.Length &&
                                                   int.TryParse(args[index + 1], out var parsedHold):
                        holdSeconds = parsedHold;
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

            if (target is null || string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException(
                    "必须提供 --keyboard-lighting <static-red|matrix-locator> 和 --output <json>。");
            }
            if (holdSeconds is < 5 or > 60)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(holdSeconds),
                    "--hold-seconds 必须为 5..60。");
            }
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputPath), ".json"))
            {
                throw new ArgumentException("--output 必须是 .json 文件。");
            }
            if (File.Exists(Path.GetFullPath(outputPath)))
            {
                throw new ArgumentException("--output 已存在，拒绝覆盖。");
            }

            return new Options(target.Value, holdSeconds, outputPath);
        }
    }
}
