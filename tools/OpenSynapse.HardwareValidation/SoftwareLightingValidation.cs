using System.Text.Json;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;

internal static class SoftwareLightingValidation
{
    public static async Task<int> RunAsync(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 64;
        }

        var startedAt = DateTimeOffset.UtcNow;
        string? operationError = null;
        string? restorationError = null;
        var firstFrameApplied = false;
        using var interrupted = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            interrupted.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        await using var controller = new BladeLightingController();
        try
        {
            var snapshot = await WindowsHidDiscovery.DiscoverAllAsync();
            if (snapshot.ErrorMessage is not null)
            {
                throw new InvalidOperationException(snapshot.ErrorMessage);
            }

            await controller.ApplyAsync(
                snapshot.Devices,
                CreateEffect(options.Mode),
                interrupted.Token);
            firstFrameApplied = true;
            Console.WriteLine($"{options.Mode} 首帧已确认写入；保持 {options.HoldSeconds} 秒供目视验证。");

            var hold = Task.Delay(TimeSpan.FromSeconds(options.HoldSeconds), interrupted.Token);
            var completed = await Task.WhenAny(hold, controller.RuntimeCompletion);
            if (completed != hold)
            {
                await controller.RuntimeCompletion;
                throw new InvalidOperationException("灯效 runtime 在观察期内提前停止。");
            }
            await hold;
        }
        catch (Exception exception)
        {
            var failure = controller.RuntimeCompletion.Exception?.GetBaseException() ?? exception;
            operationError = $"{failure.GetType().FullName}: {failure.Message}";
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            try
            {
                await controller.StopAsync();
            }
            catch (Exception exception)
            {
                restorationError = $"{exception.GetType().FullName}: {exception.Message}";
            }
        }

        var artifact = new Artifact(
            startedAt,
            DateTimeOffset.UtcNow,
            "1532:02C6",
            options.Mode.ToString(),
            options.HoldSeconds,
            firstFrameApplied,
            operationError,
            restorationError,
            null);
        await WriteArtifactAsync(options.OutputPath, artifact);

        if (operationError is not null || restorationError is not null)
        {
            Console.Error.WriteLine($"操作错误：{operationError ?? "无"}");
            Console.Error.WriteLine($"恢复错误：{restorationError ?? "无"}");
            return 1;
        }

        Console.WriteLine($"观察期结束并已恢复 Normal 模式。证据：{Path.GetFullPath(options.OutputPath)}");
        return 0;
    }

    private static BladeLightingEffect CreateEffect(BladeLightingMode mode) => mode switch
    {
        BladeLightingMode.AudioMeter or BladeLightingMode.Ambient => new(mode),
        BladeLightingMode.Reactive or BladeLightingMode.Ripple or BladeLightingMode.Starlight =>
            new(mode, new(0x99, 0xDD, 0x72)),
        BladeLightingMode.Tidal => new(
            mode, new(0x99, 0xDD, 0x72), SecondColor: new(0x00, 0x78, 0xD4)),
        BladeLightingMode.Wave or BladeLightingMode.Fire => new(mode),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static async Task WriteArtifactAsync(string path, Artifact artifact)
    {
        var output = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(
            output,
            JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal sealed record Options(BladeLightingMode Mode, int HoldSeconds, string OutputPath)
    {
        public static Options Parse(string[] args)
        {
            BladeLightingMode? mode = null;
            var holdSeconds = 30;
            string? output = null;
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--software-lighting" when index + 1 < args.Length:
                        mode = args[++index].ToLowerInvariant() switch
                        {
                            "audio" => BladeLightingMode.AudioMeter,
                            "ambient" => BladeLightingMode.Ambient,
                            "reactive" => BladeLightingMode.Reactive,
                            "ripple" => BladeLightingMode.Ripple,
                            "wave" => BladeLightingMode.Wave,
                            "fire" => BladeLightingMode.Fire,
                            "starlight" => BladeLightingMode.Starlight,
                            "tidal" => BladeLightingMode.Tidal,
                            _ => throw new ArgumentException(
                                "--software-lighting 只接受 audio、ambient、reactive、ripple、wave、fire、starlight 或 tidal。"),
                        };
                        break;
                    case "--hold-seconds" when index + 1 < args.Length &&
                                               int.TryParse(args[index + 1], out var parsedHold):
                        holdSeconds = parsedHold;
                        index++;
                        break;
                    case "--output" when index + 1 < args.Length:
                        output = args[++index];
                        break;
                    default:
                        throw new ArgumentException($"不支持或不完整的参数：{args[index]}");
                }
            }

            if (mode is null || string.IsNullOrWhiteSpace(output))
            {
                throw new ArgumentException(
                    "必须提供 --software-lighting <audio|ambient|reactive|ripple|wave|fire|starlight|tidal> 和 --output <json>。");
            }
            if (holdSeconds is < 5 or > 120)
            {
                throw new ArgumentOutOfRangeException(nameof(holdSeconds), "--hold-seconds 必须为 5..120。");
            }
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(output), ".json"))
            {
                throw new ArgumentException("--output 必须是 .json 文件。", nameof(output));
            }
            if (File.Exists(Path.GetFullPath(output)))
            {
                throw new IOException($"证据文件已存在，拒绝覆盖：{Path.GetFullPath(output)}");
            }
            return new Options(mode.Value, holdSeconds, output);
        }
    }

    private sealed record Artifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Device,
        string Mode,
        int HoldSeconds,
        bool FirstFrameApplied,
        string? OperationError,
        string? RestorationError,
        bool? VisualConfirmed);
}
