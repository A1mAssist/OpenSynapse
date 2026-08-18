using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

internal static class KeyboardInputValidation
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = Options.Parse(args);
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
        var transport = new RazerFeatureTransport();
        string? controlDevicePath = null;
        var softwareModeApplied = false;

        try
        {
            if (options.UseSoftwareMode)
            {
                var devices = await WindowsHidDiscovery.DiscoverAllAsync();
                controlDevicePath = devices.Devices.SingleOrDefault(device =>
                    device.ProductId == 0x02C6 &&
                    device.Access == DeviceAccessState.Available &&
                    device.UsagePage == 0x0001 &&
                    device.Usage == 0x0002 &&
                    device.FeatureReportByteLength == RazerFeatureReport.Length)?.Id ??
                    throw new InvalidOperationException("找不到唯一可用的 Blade 02C6 控制 collection。");
                await SendModeAsync(transport, controlDevicePath, softwareMode: true);
                softwareModeApplied = true;
            }
            await adapter.StartAsync(interrupted.Token);
            Console.WriteLine($"Raw Input 已启动；请依次按 F1、F2、F3。观察 {options.HoldSeconds} 秒。");
            await Task.Delay(TimeSpan.FromSeconds(options.HoldSeconds), interrupted.Token);
        }
        catch (OperationCanceledException) when (interrupted.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            await adapter.StopAsync();
            if (softwareModeApplied)
            {
                await SendModeAsync(transport, controlDevicePath!, softwareMode: false);
                Console.WriteLine("Blade 已恢复 Normal 模式。");
            }
        }

        Observation[] snapshot;
        lock (gate)
        {
            snapshot = observations.ToArray();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(options.Output)!);
        await File.WriteAllTextAsync(
            options.Output,
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"记录 {snapshot.Length} 个 Blade key-down 事件：{options.Output}");
        return 0;
    }

    private static Task<byte[]> SendModeAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        bool softwareMode)
    {
        var request = softwareMode
            ? BladeDeviceModeProtocol.CreateSetSoftwareRequest()
            : BladeDeviceModeProtocol.CreateSetNormalRequest();
        return transport.QueryAsync(
            devicePath,
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            TimeSpan.FromMilliseconds(2),
            CancellationToken.None);
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

    internal sealed record Options(int HoldSeconds, string Output, bool UseSoftwareMode)
    {
        internal static Options Parse(string[] args) => new(
            ReadHoldSeconds(args),
            ReadOutput(args),
            args.Contains("--software-mode", StringComparer.Ordinal));
    }

    private sealed record Observation(
        DateTimeOffset At,
        string Kind,
        string DeviceName,
        string Detail);
}
