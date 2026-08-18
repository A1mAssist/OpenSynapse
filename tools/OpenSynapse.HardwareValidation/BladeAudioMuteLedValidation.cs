using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

internal static class BladeAudioMuteLedValidation
{
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(5);

    internal static async Task<int> RunAsync(string[] args)
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
        IRazerFeatureSession? session = null;
        string? onRequest = null;
        string? onResponse = null;
        string? offRequest = null;
        string? offResponse = null;
        string? error = null;
        var offAcknowledged = false;
        var driverModeApplied = false;
        var indicator = options.Target == BladeAudioMuteTarget.Speaker ? "F1" : "M5";

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
                    $"需要且只能有一个可用的 Blade 02C6 控制 collection，当前为 {devices.Length}。请完全退出 OpenSynapse 和 Synapse 后重试。");
            }

            session = await transport.OpenSessionAsync(devices[0].Id, CancellationToken.None);
            await BladeAudioMuteRuntime.InitializeSessionAsync(session, CancellationToken.None);
            if (!options.KeepNormalMode)
            {
                await BladeAudioMuteRuntime.SetDeviceModeAsync(
                    session,
                    softwareMode: true,
                    CancellationToken.None);
                driverModeApplied = true;
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            var on = BladeSynapsePolicyProtocol.CreateSetAudioMuteStatusRequest(
                options.Target,
                muted: true,
                session.NextTransactionId());
            onRequest = Convert.ToHexString(on);
            var onResult = await SendAsync(session, on);
            onResponse = Convert.ToHexString(onResult.Response);
            _ = onResult.State;

            if (options.TransientDriverMode)
            {
                await BladeAudioMuteRuntime.SetDeviceModeAsync(
                    session,
                    softwareMode: false,
                    CancellationToken.None);
                driverModeApplied = false;
            }

            Console.WriteLine(
                $"{indicator} 静音指示灯 On 命令已收到响应；保持 {options.HoldSeconds} 秒供目视确认。响应不代表物理灯已点亮。");
            await Task.Delay(TimeSpan.FromSeconds(options.HoldSeconds));

            if (options.TransientDriverMode)
            {
                await BladeAudioMuteRuntime.SetDeviceModeAsync(
                    session,
                    softwareMode: true,
                    CancellationToken.None);
                driverModeApplied = true;
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            var off = BladeSynapsePolicyProtocol.CreateSetAudioMuteStatusRequest(
                options.Target,
                muted: false,
                session.NextTransactionId());
            offRequest = Convert.ToHexString(off);
            var offResult = await SendAsync(session, off);
            offResponse = Convert.ToHexString(offResult.Response);
            offAcknowledged = !offResult.State.Muted;
        }
        catch (Exception exception)
        {
            error = exception.ToString();
        }
        finally
        {
            if (!offAcknowledged && onRequest is not null && session is not null)
            {
                try
                {
                    var off = BladeSynapsePolicyProtocol.CreateSetAudioMuteStatusRequest(
                        options.Target,
                        muted: false,
                        session.NextTransactionId());
                    offRequest ??= Convert.ToHexString(off);
                    var offResult = await SendAsync(session, off);
                    offResponse = Convert.ToHexString(offResult.Response);
                    offAcknowledged = !offResult.State.Muted;
                }
                catch (Exception restoreException)
                {
                    error = string.Join(
                        Environment.NewLine,
                        new[] { error, $"Restore failed: {restoreException}" }
                            .Where(value => !string.IsNullOrWhiteSpace(value)));
                }
            }

            if (driverModeApplied && session is not null)
            {
                try
                {
                    await BladeAudioMuteRuntime.SetDeviceModeAsync(
                        session,
                        softwareMode: false,
                        CancellationToken.None);
                }
                catch (Exception modeException)
                {
                    error = string.Join(
                        Environment.NewLine,
                        new[] { error, $"Device mode restore failed: {modeException}" }
                            .Where(value => !string.IsNullOrWhiteSpace(value)));
                }
            }

            if (session is not null)
            {
                await session.DisposeAsync();
            }
        }

        var artifact = new Artifact(
            startedAt,
            DateTimeOffset.UtcNow,
            options.Target.ToString(),
            options.HoldSeconds,
            options.KeepNormalMode ? "Normal" : options.TransientDriverMode ? "Transient" : "Software",
            onRequest,
            onResponse,
            offRequest,
            offResponse,
            offAcknowledged,
            error);
        var output = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(
            output,
            JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"证据已写入 {output}");

        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        Console.WriteLine(offAcknowledged
            ? $"{indicator} 指示灯 Off 命令已收到响应；物理熄灭状态仍须目视确认。"
            : $"{indicator} 指示灯 Off 命令未确认。");
        return offAcknowledged ? 0 : 1;
    }

    private static async Task<(BladeAudioMuteState State, byte[] Response)> SendAsync(
        IRazerFeatureSession session,
        byte[] request)
    {
        var response = await session.QueryAsync(
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            DeviceWait,
            responseReportId: 0x02,
            CancellationToken.None);
        return (BladeSynapsePolicyProtocol.ParseAudioMuteCommandResult(response, request), response);
    }

    internal sealed record Options(
        BladeAudioMuteTarget Target,
        int HoldSeconds,
        string OutputPath,
        bool KeepNormalMode,
        bool TransientDriverMode)
    {
        internal static Options Parse(string[] args)
        {
            BladeAudioMuteTarget? target = null;
            var holdSeconds = 15;
            string? output = null;
            var keepNormalMode = false;
            var transientDriverMode = false;
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--blade-audio-mute-led":
                        break;
                    case "--target" when index + 1 < args.Length:
                        target = args[++index].ToLowerInvariant() switch
                        {
                            "speaker" => BladeAudioMuteTarget.Speaker,
                            "microphone" => BladeAudioMuteTarget.Microphone,
                            _ => throw new ArgumentException("--target 只接受 speaker 或 microphone。"),
                        };
                        break;
                    case "--hold-seconds" when index + 1 < args.Length &&
                        int.TryParse(args[++index], out var parsedHold):
                        holdSeconds = parsedHold;
                        break;
                    case "--output" when index + 1 < args.Length:
                        output = args[++index];
                        break;
                    case "--normal-mode":
                        keepNormalMode = true;
                        break;
                    case "--transient-driver-mode":
                        transientDriverMode = true;
                        break;
                    default:
                        throw new ArgumentException($"未知参数：{args[index]}");
                }
            }

            if (target is null || output is null || holdSeconds is < 1 or > 120)
            {
                throw new ArgumentException(
                    "用法：--blade-audio-mute-led --target <speaker|microphone> --hold-seconds 1..120 --output <json>。");
            }
            if (keepNormalMode && transientDriverMode)
            {
                throw new ArgumentException("--normal-mode 和 --transient-driver-mode 不能同时使用。");
            }
            return new(target.Value, holdSeconds, output, keepNormalMode, transientDriverMode);
        }
    }

    private sealed record Artifact(
        DateTimeOffset StartedAt,
        DateTimeOffset FinishedAt,
        string Target,
        int HoldSeconds,
        string DeviceMode,
        string? OnRequestHex,
        string? OnResponseHex,
        string? OffRequestHex,
        string? OffResponseHex,
        bool OffAcknowledged,
        string? Error);
}
