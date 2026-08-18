using System.Text.Json;
using System.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

var validationCommands = new[]
{
    "--viper-dpi-stages",
    "--viper-obm-read",
    "--viper-obm-write",
    "--viper-obm-keyboard",
    "--viper-obm-double-click",
    "--viper-obm-dpi",
    "--viper-obm-media",
    "--viper-obm-hypershift",
    "--viper-obm-keyboard-turbo",
    "--viper-obm-mouse-turbo",
    "--viper-low-battery-threshold",
    "--keyboard-lighting",
    "--software-lighting",
    "--keyboard-input-log",
    "--blade-col04-input",
    "--blade-col05-input",
    "--blade-fan-fixed",
    "--blade-fan-curve",
    "--blade-policy-writes",
    "--blade-audio-mute-led",
    "--logo",
};
if (args.Any(argument => validationCommands.Contains(argument, StringComparer.Ordinal)))
{
    var runningApp = Process.GetProcessesByName("OpenSynapse.App")
        .FirstOrDefault(process => process.Id != Environment.ProcessId);
    if (runningApp is not null)
    {
        using (runningApp)
        {
            Console.Error.WriteLine(
                $"检测到 OpenSynapse.App 仍在运行（PID {runningApp.Id}）。请从托盘完全退出后再执行验证；验证工具不会自动终止进程。\n" +
                "设备被占用时继续执行可能得到错序报告，并且不能作为协议证据。");
        }
        return 2;
    }
}

return args.Contains("--viper-dpi-stages", StringComparer.Ordinal)
    ? await ViperDpiStagesValidation.RunAsync(args)
    : args.Contains("--viper-obm-dpi", StringComparer.Ordinal)
        ? await ViperObmWriteValidation.RunDpiAsync(args)
    : args.Contains("--viper-obm-media", StringComparer.Ordinal)
        ? await ViperObmWriteValidation.RunMediaAsync(args)
    : args.Contains("--viper-obm-hypershift", StringComparer.Ordinal)
        ? await ViperObmWriteValidation.RunHyperShiftAsync(args)
    : args.Contains("--viper-obm-keyboard-turbo", StringComparer.Ordinal)
        ? await ViperObmWriteValidation.RunKeyboardTurboAsync(args)
    : args.Contains("--viper-obm-mouse-turbo", StringComparer.Ordinal)
        ? await ViperObmWriteValidation.RunMouseTurboAsync(args)
    : args.Contains("--viper-obm-double-click", StringComparer.Ordinal)
        ? await ViperObmWriteValidation.RunDoubleClickAsync(args)
    : args.Contains("--viper-obm-keyboard", StringComparer.Ordinal)
        ? await ViperObmWriteValidation.RunKeyboardAsync(args)
    : args.Contains("--viper-obm-write", StringComparer.Ordinal)
        ? await ViperObmWriteValidation.RunAsync(args)
    : args.Contains("--viper-obm-read", StringComparer.Ordinal)
        ? await ViperObmReadValidation.RunAsync(args)
    : args.Contains("--viper-low-battery-threshold", StringComparer.Ordinal)
        ? await ViperLowBatteryThresholdValidation.RunAsync(args)
    : args.Contains("--keyboard-lighting", StringComparer.Ordinal)
        ? await KeyboardLightingValidation.RunAsync(args)
    : args.Contains("--software-lighting", StringComparer.Ordinal)
        ? await SoftwareLightingValidation.RunAsync(args)
    : args.Contains("--keyboard-input-log", StringComparer.Ordinal)
        ? await KeyboardInputValidation.RunAsync(args)
    : args.Contains("--blade-col04-input", StringComparer.Ordinal)
        ? await BladeCol04InputValidation.RunAsync(args)
    : args.Contains("--blade-col05-input", StringComparer.Ordinal)
        ? await BladeCol04InputValidation.RunAsync(args)
    : args.Contains("--blade-fan-fixed", StringComparer.Ordinal)
        ? await BladeFanValidation.RunAsync(args)
    : args.Contains("--blade-fan-curve", StringComparer.Ordinal)
        ? await BladeFanCurveValidation.RunAsync(args)
    : args.Contains("--blade-policy-writes", StringComparer.Ordinal)
        ? await BladePolicyValidation.RunAsync(args)
    : args.Contains("--blade-audio-mute-led", StringComparer.Ordinal)
        ? await BladeAudioMuteLedValidation.RunAsync(args)
    : args.Contains("--core-audio-mute-read", StringComparer.Ordinal)
        ? await CoreAudioMuteValidation.RunAsync()
        : await LogoValidation.RunAsync(args);

internal static class CoreAudioMuteValidation
{
    public static async Task<int> RunAsync()
    {
        using var observed = new CancellationTokenSource();
        var received = 0;
        using var source = new WindowsCoreAudioMuteEventSource(state =>
        {
            Interlocked.Increment(ref received);
            Console.WriteLine($"{state.Target}: muted={state.Muted}");
        });

        try
        {
            source.Start();
            await Task.Delay(TimeSpan.FromSeconds(1), observed.Token);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Core Audio 静音读取失败：{exception.Message}");
            return 1;
        }

        if (source.LastError is string error)
        {
            Console.Error.WriteLine($"Core Audio 静音读取失败：{error}");
            return 1;
        }

        if (received < 1)
        {
            Console.Error.WriteLine("Core Audio 未返回默认端点的初始静音状态。");
            return 1;
        }

        Console.WriteLine("Core Audio 静音读取完成；未发送任何 Razer 报文。");
        return 0;
    }
}

internal static class LogoValidation
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
        LogoState? original = null;
        LogoState? targetReadback = null;
        LogoState? restorationReadback = null;
        string? targetCommandAcknowledgement = null;
        var targetApplied = false;
        string? operationError = null;
        string? restorationError = null;
        var transport = new RazerFeatureTransport();
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

            var blade = snapshot.Devices.Where(device =>
                device.ProductId == 0x02C6 &&
                device.Access == DeviceAccessState.Available &&
                device.UsagePage == 0x0001 &&
                device.Usage == 0x0002 &&
                device.FeatureReportByteLength == RazerFeatureReport.Length).ToArray();
            if (blade.Length != 1)
            {
                throw new InvalidOperationException($"需要且只能有一个可用的 Blade 02C6 控制 collection，当前为 {blade.Length}。请关闭 Synapse UI 后重试。");
            }

            if (options.EffectOnly)
            {
                var request = options.StateOnly
                    ? BladeLogoProtocol.CreateSetPowerRequest(true, options.ProfileId)
                    : BladeLogoProtocol.CreateSetModeRequest(options.State, options.ProfileId);
                await SendAsync(transport, blade[0].Id, request);
                targetApplied = true;
                var isolatedAction = options.StateOnly ? "State=On" : $"effect={options.State}";
                Console.WriteLine($"Logo {isolatedAction} 单报文已发送（profile {options.ProfileId}），接下来保持 {options.HoldSeconds} 秒；期间不会发送其它 Logo 报文/GET。"
                    );
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.HoldSeconds), interrupted.Token);
                }
                finally
                {
                    // The single-report check starts from the restored Off state. Keep
                    // the cleanup to one final state write so it cannot mask the target.
                    await SendAsync(
                        transport,
                        blade[0].Id,
                        BladeLogoProtocol.CreateSetPowerRequest(false, options.ProfileId));
                }
                var effectArtifact = new LogoValidationArtifact(
                    startedAt,
                    DateTimeOffset.UtcNow,
                    "1532:02C6",
                    "0001:0002",
                    RazerFeatureReport.Length,
                    options.State.ToString(),
                    options.ProfileId,
                    options.HoldSeconds,
                    "effect-only",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                var effectOutput = Path.GetFullPath(options.OutputPath);
                await WriteArtifactAsync(effectOutput, effectArtifact);
                return 0;
            }

            // Product 710 restores Normal device mode around its regular lighting tasks.
            await SendAsync(transport, blade[0].Id, BladeDeviceModeProtocol.CreateSetNormalRequest());

            original = await ReadAsync(transport, blade[0].Id, options.ProfileId);
            if (options.Product710Sequence)
            {
                var product710Operation = await ExecuteProduct710Async(
                    transport,
                    blade[0].Id,
                    original,
                    options.State,
                    async () =>
                    {
                        Console.WriteLine($"Logo Product 710 {options.State} 两报文已确认；保持 {options.HoldSeconds} 秒供目视确认。");
                        await Task.Delay(TimeSpan.FromSeconds(options.HoldSeconds), interrupted.Token);
                    },
                    options.ProfileId);
                targetCommandAcknowledgement = product710Operation.TargetAcknowledgement;
                restorationReadback = product710Operation.RestorationReadback;
                targetApplied = product710Operation.TargetApplied;
                operationError = product710Operation.OperationError;
                restorationError = product710Operation.RestorationError;
            }
            else
            {
            var target = options.State switch
            {
                BladeLogoMode.Off => new LogoState(false, original.Mode),
                BladeLogoMode.Static => new LogoState(true, BladeLogoMode.Static),
                BladeLogoMode.Breathing => new LogoState(true, BladeLogoMode.Breathing),
                _ => throw new ArgumentOutOfRangeException(nameof(options.State)),
            };

            var operation = await ExecuteAsync(
                transport,
                blade[0].Id,
                original,
                target,
                options.LeaveTarget,
                async () =>
                {
                    Console.WriteLine($"Logo {options.State} 已写入并读回；保持 {options.HoldSeconds} 秒供目视确认。");
                    await Task.Delay(TimeSpan.FromSeconds(options.HoldSeconds), interrupted.Token);
                },
                options.ProfileId);
            targetReadback = operation.TargetReadback;
            restorationReadback = operation.RestorationReadback;
            targetApplied = operation.TargetApplied;
            operationError = operation.OperationError;
            restorationError = operation.RestorationError;
            }

            if (options.LeaveTarget && targetApplied)
            {
                Console.WriteLine($"Logo {options.State} 已写入并读回；按要求留置目标状态，不执行恢复。");
            }
        }
        catch (Exception exception)
        {
            operationError ??= exception.Message;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        var artifact = new LogoValidationArtifact(
            startedAt,
            DateTimeOffset.UtcNow,
            "1532:02C6",
            "0001:0002",
            RazerFeatureReport.Length,
            options.State.ToString(),
            options.ProfileId,
            options.HoldSeconds,
            options.LeaveTarget ? options.State.ToString() : null,
            original?.ToString(),
            targetReadback?.ToString(),
            targetCommandAcknowledgement,
            restorationReadback?.ToString(),
            null,
            operationError,
            restorationError);
        var output = Path.GetFullPath(options.OutputPath);
        await WriteArtifactAsync(output, artifact);

        if (operationError is not null || restorationError is not null)
        {
            if (operationError is not null)
            {
                Console.Error.WriteLine($"操作失败：{operationError}");
            }
            if (restorationError is not null)
            {
                Console.Error.WriteLine($"恢复失败：{restorationError}");
            }
            return 1;
        }

        Console.WriteLine(options.LeaveTarget
            ? $"Logo 已留置为 {options.State}。证据已写入 {output}"
            : $"Logo 原状态已恢复并读回。证据已写入 {output}");
        return 0;
    }

    internal static async Task<OperationResult> ExecuteAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        LogoState original,
        LogoState target,
        bool leaveTarget,
        Func<Task> holdAsync,
        byte profileId = 1)
    {
        LogoState? targetReadback = null;
        LogoState? restorationReadback = null;
        var targetApplied = false;
        string? operationError = null;
        string? restorationError = null;

        try
        {
            await WriteAsync(transport, devicePath, target, profileId);
            targetReadback = await ReadAsync(transport, devicePath, profileId);
            if (targetReadback != target)
            {
                throw new InvalidOperationException($"Logo 目标状态读回不一致：写入 {target}，读回 {targetReadback}。");
            }

            if (leaveTarget)
            {
                // A readback cannot be the final HID action: mode GET can affect the
                // physical logo. Replay the restricted target as mode then power.
                await WriteAsync(transport, devicePath, target, profileId);
            }

            targetApplied = true;
            if (!leaveTarget)
            {
                await holdAsync();
            }
        }
        catch (Exception exception)
        {
            operationError = exception.Message;
        }
        finally
        {
            if (!leaveTarget || !targetApplied)
            {
                var restoration = await RestoreAsync(transport, devicePath, original, profileId);
                restorationReadback = restoration.Readback;
                restorationError = restoration.Error;
            }
        }

        return new OperationResult(
            targetReadback,
            restorationReadback,
            targetApplied,
            operationError,
            restorationError);
    }

    internal static async Task<Product710OperationResult> ExecuteProduct710Async(
        IRazerFeatureTransport transport,
        string devicePath,
        LogoState original,
        BladeLogoMode target,
        Func<Task> holdAsync,
        byte restoreProfileId = 1)
    {
        var targetApplied = false;
        string? operationError = null;
        LogoState? restorationReadback = null;
        string? restorationError = null;
        try
        {
            await SendProduct710Async(
                transport, devicePath, BladeSynapsePolicyProtocol.CreateSetLogoEffectRequest(target));
            await SendProduct710Async(
                transport, devicePath, BladeSynapsePolicyProtocol.CreateSetLogoStateRequest(target));
            targetApplied = true;
            await holdAsync();
        }
        catch (Exception exception)
        {
            operationError = exception.Message;
        }
        finally
        {
            var restoration = await RestoreAsync(transport, devicePath, original, restoreProfileId);
            restorationReadback = restoration.Readback;
            restorationError = restoration.Error;
        }

        return new Product710OperationResult(
            targetApplied ? "effect+state ACK" : null,
            restorationReadback,
            targetApplied,
            operationError,
            restorationError);
    }

    private static async Task<LogoState> ReadAsync(IRazerFeatureTransport transport, string devicePath, byte profileId)
    {
        var power = await SendAsync(transport, devicePath, BladeLogoProtocol.CreateGetPowerRequest(profileId));
        var mode = await SendAsync(transport, devicePath, BladeLogoProtocol.CreateGetModeRequest(profileId));
        return new LogoState(BladeLogoProtocol.ParsePower(power, profileId), BladeLogoProtocol.ParseMode(mode, profileId));
    }

    internal static async Task WriteAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        LogoState state,
        byte profileId = 1)
    {
        await SendAsync(transport, devicePath, BladeLogoProtocol.CreateSetModeRequest(state.Mode, profileId));
        await SendAsync(transport, devicePath, BladeLogoProtocol.CreateSetPowerRequest(state.Powered, profileId));
    }

    internal static async Task<RestoreResult> RestoreAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        LogoState original,
        byte profileId = 1)
    {
        var errors = new List<string>();

        async Task TryWrite(byte[] request, string label)
        {
            try
            {
                await SendAsync(transport, devicePath, request);
            }
            catch (Exception exception)
            {
                errors.Add($"{label}：{exception.Message}");
            }
        }

        if (!original.Powered)
        {
            await TryWrite(BladeLogoProtocol.CreateSetModeRequest(original.Mode, profileId), "恢复 Logo 底层模式");
            // 02C6 physically lights the logo when mode is written even while the
            // power GET reports Off, so power-off must be the final restore command.
            await TryWrite(BladeLogoProtocol.CreateSetPowerRequest(false, profileId), "恢复 Logo 关闭状态");
        }
        else
        {
            await TryWrite(BladeLogoProtocol.CreateSetModeRequest(original.Mode, profileId), "恢复 Logo 模式");
            await TryWrite(BladeLogoProtocol.CreateSetPowerRequest(true, profileId), "恢复 Logo 开启状态");
        }

        bool? restoredPower = null;
        BladeLogoMode? restoredMode = null;
        try
        {
            var power = await SendAsync(transport, devicePath, BladeLogoProtocol.CreateGetPowerRequest(profileId));
            restoredPower = BladeLogoProtocol.ParsePower(power, profileId);
        }
        catch (Exception exception)
        {
            errors.Add($"恢复电源读回：{exception.Message}");
        }
        try
        {
            var mode = await SendAsync(transport, devicePath, BladeLogoProtocol.CreateGetModeRequest(profileId));
            restoredMode = BladeLogoProtocol.ParseMode(mode, profileId);
        }
        catch (Exception exception)
        {
            errors.Add($"恢复模式读回：{exception.Message}");
        }

        LogoState? readback = null;
        if (restoredPower is bool powerValue && restoredMode is BladeLogoMode modeValue)
        {
            readback = new LogoState(powerValue, modeValue);
            if (readback != original)
            {
                errors.Add($"恢复读回不一致：原值 {original}，读回 {readback}。");
            }
        }

        if (!original.Powered)
        {
            // The physical logo may illuminate after the subsequent mode GET even
            // though power still reads Off. Leave power-off as the final HID action.
            await TryWrite(BladeLogoProtocol.CreateSetPowerRequest(false, profileId), "最终 Logo 关灯");
        }

        return new RestoreResult(readback, errors.Count == 0 ? null : string.Join(" ", errors));
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

    private static async Task SendProduct710Async(
        IRazerFeatureTransport transport,
        string devicePath,
        byte[] request)
    {
        var response = await SendAsync(transport, devicePath, request);
        _ = BladeSynapsePolicyProtocol.ParseLedCommandResult(response, request);
    }

    private static async Task WriteArtifactAsync(string output, LogoValidationArtifact artifact)
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

    internal sealed record LogoState(bool Powered, BladeLogoMode Mode);
    internal sealed record RestoreResult(LogoState? Readback, string? Error);
    internal sealed record OperationResult(
        LogoState? TargetReadback,
        LogoState? RestorationReadback,
        bool TargetApplied,
        string? OperationError,
        string? RestorationError);

    private sealed record LogoValidationArtifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Device,
        string Collection,
        int FeatureReportByteLength,
        string TargetState,
        byte ProfileId,
        int HoldSeconds,
        string? LeaveTarget,
        string? OriginalState,
        string? TargetElectronicReadback,
        string? TargetCommandAcknowledgement,
        string? RestorationElectronicReadback,
        bool? VisualConfirmed,
        string? OperationError,
        string? RestorationError);

    internal sealed record Options(BladeLogoMode State, int HoldSeconds, string OutputPath, bool LeaveTarget, byte ProfileId, bool EffectOnly, bool StateOnly, bool Product710Sequence)
    {
        public static Options Parse(string[] args)
        {
            BladeLogoMode? state = null;
            var holdSeconds = 30;
            string? outputPath = null;
            var leaveTarget = false;
            byte profileId = 1;
            var effectOnly = false;
            var stateOnly = false;
            var product710Sequence = false;
            var legacyLeaveOff = false;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--logo" when index + 1 < args.Length:
                        state = args[++index].ToLowerInvariant() switch
                        {
                            "off" => BladeLogoMode.Off,
                            "static" => BladeLogoMode.Static,
                            "breathing" => BladeLogoMode.Breathing,
                            _ => throw new ArgumentException("--logo 只接受 off、static 或 breathing。"),
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
                    case "--leave-off":
                        leaveTarget = true;
                        legacyLeaveOff = true;
                        break;
                    case "--leave-target":
                        leaveTarget = true;
                        break;
                    case "--profile" when index + 1 < args.Length && byte.TryParse(args[index + 1], out var parsedProfile):
                        profileId = parsedProfile;
                        index++;
                        break;
                    case "--effect-only":
                        effectOnly = true;
                        break;
                    case "--state-only":
                        effectOnly = true;
                        stateOnly = true;
                        break;
                    case "--product710-sequence":
                        product710Sequence = true;
                        break;
                    default:
                        throw new ArgumentException($"不支持或不完整的参数：{args[index]}");
                }
            }

            if (state is null || string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("必须提供 --logo <off|static|breathing> 和 --output <json>。");
            }
            if (holdSeconds is < 5 or > 60)
            {
                throw new ArgumentOutOfRangeException(nameof(holdSeconds), "--hold-seconds 必须为 5..60。" );
            }
            if (profileId > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(profileId), "--profile 只接受 0 或 1。");
            }
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputPath), ".json"))
            {
                throw new ArgumentException("--output 必须是 .json 文件。" );
            }
            if (File.Exists(Path.GetFullPath(outputPath)))
            {
                throw new ArgumentException("--output 已存在，拒绝覆盖。" );
            }
            if (legacyLeaveOff && state != BladeLogoMode.Off)
            {
                throw new ArgumentException("--leave-off 只能和 --logo off 一起使用。" );
            }

            if (effectOnly && state == BladeLogoMode.Off)
            {
                throw new ArgumentException("--effect-only 只接受 static 或 breathing。");
            }

            if (product710Sequence &&
                (state != BladeLogoMode.Breathing || effectOnly || leaveTarget || profileId != 1))
            {
                throw new ArgumentException(
                    "--product710-sequence 只接受可恢复的 --logo breathing，且不能组合 --effect-only、--leave-target 或非默认 profile。");
            }

            return new Options(state.Value, holdSeconds, outputPath, leaveTarget, profileId, effectOnly, stateOnly, product710Sequence);
        }
    }

    internal sealed record Product710OperationResult(
        string? TargetAcknowledgement,
        LogoState? RestorationReadback,
        bool TargetApplied,
        string? OperationError,
        string? RestorationError);
}
