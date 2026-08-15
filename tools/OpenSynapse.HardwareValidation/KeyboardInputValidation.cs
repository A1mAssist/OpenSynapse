using System.Text.Json;
using OpenSynapse.Windows.Lighting;

internal static class KeyboardInputValidation
{
    public static async Task<int> RunAsync(string[] args)
    {
        var holdSeconds = ReadHoldSeconds(args);
        var output = ReadOutput(args);
        var observations = new List<Observation>();
        var gate = new object();
        using var interrupted = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            interrupted.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        void Record(Observation observation)
        {
            lock (gate)
            {
                observations.Add(observation);
            }
            Console.WriteLine(observation.Detail);
        }

        await using var adapter = new WindowsKeyboardLightingAdapter(
            item =>
            {
                if ((item.Flags & WindowsKeyboardLightingAdapter.KeyUpFlag) == 0)
                {
                    Record(new Observation(
                        DateTimeOffset.UtcNow,
                        "Keyboard",
                        item.DeviceName,
                        $"scan=0x{item.ScanCode:X2}, flags=0x{item.Flags:X2}, vk=0x{item.VirtualKey:X2}"));
                }
            },
            item => Record(new Observation(
                DateTimeOffset.UtcNow,
                "HID",
                item.DeviceName,
                Convert.ToHexString(item.Report))));

        try
        {
            await adapter.StartAsync(interrupted.Token);
            Console.WriteLine($"Raw Input 已启动；请依次按 M1..M5，再使用外接键盘输入。观察 {holdSeconds} 秒。");
            await Task.Delay(TimeSpan.FromSeconds(holdSeconds), interrupted.Token);
        }
        catch (OperationCanceledException) when (interrupted.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            await adapter.StopAsync();
        }

        Observation[] snapshot;
        lock (gate)
        {
            snapshot = observations.ToArray();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(
            output,
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"记录 {snapshot.Length} 个 Blade key-down 事件：{output}");
        return 0;
    }

    private static int ReadHoldSeconds(string[] args)
    {
        var index = Array.IndexOf(args, "--hold-seconds");
        if (index < 0 || index + 1 >= args.Length ||
            !int.TryParse(args[index + 1], out var seconds) || seconds is < 5 or > 120)
        {
            throw new ArgumentException("--hold-seconds 必须为 5..120。");
        }
        return seconds;
    }

    private static string ReadOutput(string[] args)
    {
        var index = Array.IndexOf(args, "--output");
        if (index < 0 || index + 1 >= args.Length)
        {
            throw new ArgumentException("必须提供 --output <json>。");
        }
        var output = Path.GetFullPath(args[index + 1]);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(output), ".json") ||
            File.Exists(output))
        {
            throw new ArgumentException("--output 必须是尚不存在的 .json 文件。");
        }
        return output;
    }

    private sealed record Observation(
        DateTimeOffset At,
        string Kind,
        string DeviceName,
        string Detail);
}
