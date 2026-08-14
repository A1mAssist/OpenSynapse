using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

internal static class BladeBatterySleepValidation
{
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(2);

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
        PowerSnapshot? snapshot = null;
        string? error = null;
        try
        {
            var discovery = await WindowsHidDiscovery.DiscoverAllAsync();
            if (discovery.ErrorMessage is not null)
            {
                throw new InvalidOperationException(discovery.ErrorMessage);
            }

            var devices = discovery.Devices.Where(device =>
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

            snapshot = await ReadAsync(new RazerFeatureTransport(), devices[0].Id);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = exception.Message;
        }

        var artifact = new ValidationArtifact(
            startedAt,
            DateTimeOffset.UtcNow,
            "1532:02C6",
            "0001:0002",
            RazerFeatureReport.Length,
            snapshot,
            error);
        var output = Path.GetFullPath(options.OutputPath);
        await WriteArtifactAsync(output, artifact);
        if (error is not null)
        {
            Console.Error.WriteLine($"读取失败：{error}");
            Console.Error.WriteLine($"证据已写入 {output}");
            return 1;
        }

        Console.WriteLine(
            $"电池 {snapshot!.BatteryPercent}%，charging raw 0x{snapshot.ChargingStatusRaw:X2}，" +
            $"auto-sleep raw 0x{snapshot.AutoSleepRaw:X2}，休眠 {snapshot.TimeToSleepSeconds} 秒。");
        Console.WriteLine($"证据已写入 {output}");
        return 0;
    }

    internal static async Task<PowerSnapshot> ReadAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);

        var battery = await QueryAsync(
            transport, devicePath, BladeProduct710Protocol.CreateGetBatteryLevelRequest(), cancellationToken);
        var charging = await QueryAsync(
            transport, devicePath, BladeProduct710Protocol.CreateGetChargingStatusRequest(), cancellationToken);
        var autoSleep = await QueryAsync(
            transport, devicePath, BladeProduct710Protocol.CreateGetAutoSleepRequest(), cancellationToken);
        var timeToSleep = await QueryAsync(
            transport, devicePath, BladeProduct710Protocol.CreateGetTimeToSleepRequest(), cancellationToken);

        var result = new PowerSnapshot(
            BladeProduct710Protocol.ParseBatteryPercent(battery),
            BladeProduct710Protocol.ParseChargingStatusRaw(charging),
            BladeProduct710Protocol.ParseAutoSleepRaw(autoSleep),
            BladeProduct710Protocol.ParseTimeToSleepSeconds(timeToSleep),
            [Envelope.Create(battery), Envelope.Create(charging), Envelope.Create(autoSleep), Envelope.Create(timeToSleep)]);
        if (result.BatteryPercent is < 0 or > 100 || result.TimeToSleepSeconds is < 0 or > 86_400)
        {
            throw new InvalidOperationException("Blade 电池或休眠值超出可信范围。");
        }

        return result;
    }

    private static Task<byte[]> QueryAsync(
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

    internal sealed record PowerSnapshot(
        int BatteryPercent,
        byte ChargingStatusRaw,
        byte AutoSleepRaw,
        int TimeToSleepSeconds,
        IReadOnlyList<Envelope> Envelopes);

    internal sealed record Envelope(
        byte Status,
        byte TransactionId,
        byte DataSize,
        byte CommandClass,
        byte CommandId,
        string Arguments,
        byte Crc)
    {
        internal static Envelope Create(byte[] response)
        {
            var size = response[6];
            return new Envelope(
                response[1],
                response[2],
                size,
                response[7],
                response[8],
                Convert.ToHexString(response.AsSpan(RazerFeatureReport.ArgumentsOffset, size)),
                response[89]);
        }
    }

    private sealed record ValidationArtifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Device,
        string Collection,
        int FeatureReportByteLength,
        PowerSnapshot? Snapshot,
        string? Error);

    internal sealed record Options(string OutputPath)
    {
        internal static Options Parse(string[] args)
        {
            string? output = null;
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--blade-battery-sleep":
                        break;
                    case "--output" when index + 1 < args.Length &&
                        !args[index + 1].StartsWith("--", StringComparison.Ordinal):
                        output = args[++index];
                        break;
                    default:
                        throw new ArgumentException($"不支持或不完整的参数：{args[index]}");
                }
            }

            if (string.IsNullOrWhiteSpace(output) ||
                !StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(output), ".json"))
            {
                throw new ArgumentException("必须提供 --blade-battery-sleep --output <json>。");
            }
            if (File.Exists(Path.GetFullPath(output)))
            {
                throw new ArgumentException("--output 已存在，拒绝覆盖。");
            }
            return new Options(output);
        }
    }
}
