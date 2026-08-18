using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

internal static class ViperObmWriteValidation
{
    private const byte ProfileId = 1;
    private const byte ButtonId = 5;
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(60);

    public static Task<int> RunKeyboardAsync(string[] args) =>
        RunFunctionAsync(
            args,
            "--viper-obm-keyboard",
            new(ProfileId, ButtonId, ViperObmMappingMode.Normal,
                ViperObmFunctionId.KeyCode, new byte[] { 0x00, 0x04 }),
            "Keyboard A");

    public static Task<int> RunDoubleClickAsync(string[] args) =>
        RunFunctionAsync(
            args,
            "--viper-obm-double-click",
            new(ProfileId, ButtonId, ViperObmMappingMode.Normal,
                ViperObmFunctionId.DoubleClick, new byte[] { 0x01 }),
            "DoubleClick");

    public static Task<int> RunDpiAsync(string[] args) =>
        RunFunctionAsync(args, "--viper-obm-dpi",
            new(ProfileId, ButtonId, ViperObmMappingMode.Normal,
                ViperObmFunctionId.Dpi, new byte[] { 6 }), "DPI CycleUp");

    public static Task<int> RunMediaAsync(string[] args) =>
        RunFunctionAsync(args, "--viper-obm-media",
            new(ProfileId, ButtonId, ViperObmMappingMode.Normal,
                ViperObmFunctionId.MediaKeys, new byte[] { 0xCD, 0x00 }), "Media PlayPause");

    public static Task<int> RunHyperShiftAsync(string[] args) =>
        RunFunctionAsync(args, "--viper-obm-hypershift",
            new(ProfileId, ButtonId, ViperObmMappingMode.Normal,
                ViperObmFunctionId.ModeButtonKey, new byte[] { 1 }), "HyperShift");

    public static Task<int> RunKeyboardTurboAsync(string[] args) =>
        RunFunctionAsync(args, "--viper-obm-keyboard-turbo",
            new(ProfileId, ButtonId, ViperObmMappingMode.Normal,
                ViperObmFunctionId.TurboModeKey, new byte[] { 0, 4, 100, 0 }), "KeyboardTurbo A/100ms");

    public static Task<int> RunMouseTurboAsync(string[] args) =>
        RunFunctionAsync(args, "--viper-obm-mouse-turbo",
            new(ProfileId, ButtonId, ViperObmMappingMode.Normal,
                ViperObmFunctionId.TurboModeButton, new byte[] { 1, 100, 0 }), "MouseTurbo Button1/100ms");

    private static async Task<int> RunFunctionAsync(
        string[] args,
        string command,
        ViperObmAssignment target,
        string targetLabel)
    {
        KeyboardOptions options;
        try
        {
            options = ParseKeyboardOptions(args, command);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 64;
        }

        var startedAt = DateTimeOffset.UtcNow;
        KeyboardOperationResult? result = null;
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
                    $"需要且只能有一个可用的 Viper 00B8 控制 collection，当前为 {devices.Length}。请唤醒鼠标并完全关闭 Synapse UI。");
            }

            result = await ExecuteFunctionAsync(
                new RazerFeatureTransport(),
                devices[0].Id,
                target,
                targetLabel,
                (normal, hyperShift) => WriteArtifactAsync(
                    options.BaselinePath,
                    new KeyboardBaselineArtifact(startedAt, "1532:00B8", ProfileId, ButtonId, normal, hyperShift)),
                async () =>
                {
                    Console.WriteLine(
                        $"Viper button 5 Normal 已读回为 {targetLabel}；保持 {options.HoldSeconds} 秒供物理验证，然后自动恢复。");
                    await Task.Delay(TimeSpan.FromSeconds(options.HoldSeconds));
                });
        }
        catch (Exception exception)
        {
            discoveryError = exception.Message;
        }

        var artifact = new KeyboardValidationArtifact(
            startedAt,
            DateTimeOffset.UtcNow,
            "1532:00B8",
            "0001:0002",
            ProfileId,
            ButtonId,
            result?.OriginalNormal,
            result?.OriginalHyperShift,
            result?.TargetReadback,
            result?.HyperShiftAfterTarget,
            result?.RestorationNormalReadback,
            result?.RestorationHyperShiftReadback,
            discoveryError ?? result?.OperationError,
            result?.RestorationError);
        await WriteArtifactAsync(options.OutputPath, artifact);

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
            Console.Error.WriteLine($"证据已写入 {options.OutputPath}");
            return 1;
        }

        Console.WriteLine($"Viper button 5 Normal 的 {targetLabel} 验证已恢复原鼠标按键并完成两层读回。");
        Console.WriteLine($"证据已写入 {options.OutputPath}");
        return 0;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        string outputPath;
        try
        {
            outputPath = ParseOutputPath(args);
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
                    $"需要且只能有一个可用的 Viper 00B8 控制 collection，当前为 {devices.Length}。请唤醒鼠标并完全关闭 Synapse UI。");
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
            ProfileId,
            ButtonId,
            result?.OriginalNormal,
            result?.OriginalHyperShift,
            result?.SameValueReadback,
            result?.TargetReadback,
            result?.NormalAfterTarget,
            result?.RestorationHyperShiftReadback,
            result?.RestorationNormalReadback,
            discoveryError ?? result?.OperationError,
            result?.RestorationError);
        await WriteArtifactAsync(outputPath, artifact);

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
            Console.Error.WriteLine($"证据已写入 {outputPath}");
            return 1;
        }

        Console.WriteLine("Viper button 5 HyperShift 已完成同值写入、临时 Off、层隔离读回和原值恢复。");
        Console.WriteLine($"证据已写入 {outputPath}");
        return 0;
    }

    internal static async Task<OperationResult> ExecuteAsync(
        IRazerFeatureTransport transport,
        string devicePath)
    {
        ViperObmAssignment? originalNormal = null;
        ViperObmAssignment? originalHyperShift = null;
        ViperObmAssignment? sameValueReadback = null;
        ViperObmAssignment? targetReadback = null;
        ViperObmAssignment? normalAfterTarget = null;
        ViperObmAssignment? restorationHyperShiftReadback = null;
        ViperObmAssignment? restorationNormalReadback = null;
        string? operationError = null;
        string? restorationError = null;
        var writeAttempted = false;

        try
        {
            originalNormal = await ReadAsync(transport, devicePath, ViperObmMappingMode.Normal);
            originalHyperShift = await ReadAsync(transport, devicePath, ViperObmMappingMode.HyperShift);
            EnsureExpectedBaseline(originalNormal, originalHyperShift);

            writeAttempted = true;
            await WriteAsync(transport, devicePath, originalHyperShift);
            sameValueReadback = await ReadAsync(transport, devicePath, ViperObmMappingMode.HyperShift);
            EnsureEqual(originalHyperShift, sameValueReadback, "同值");

            var target = originalHyperShift with
            {
                Function = ViperObmFunctionId.Off,
                FunctionData = Array.Empty<byte>(),
            };
            await WriteAsync(transport, devicePath, target);
            targetReadback = await ReadAsync(transport, devicePath, ViperObmMappingMode.HyperShift);
            EnsureEqual(target, targetReadback, "目标");
            normalAfterTarget = await ReadAsync(transport, devicePath, ViperObmMappingMode.Normal);
            EnsureEqual(originalNormal, normalAfterTarget, "Normal 层隔离");
        }
        catch (Exception exception)
        {
            operationError = exception.Message;
        }
        finally
        {
            if (writeAttempted && originalHyperShift is not null)
            {
                var restoration = await RestoreAsync(
                    transport, devicePath, originalNormal, originalHyperShift);
                restorationHyperShiftReadback = restoration.HyperShiftReadback;
                restorationNormalReadback = restoration.NormalReadback;
                restorationError = restoration.Error;
            }
        }

        return new OperationResult(
            originalNormal,
            originalHyperShift,
            sameValueReadback,
            targetReadback,
            normalAfterTarget,
            restorationHyperShiftReadback,
            restorationNormalReadback,
            operationError,
            restorationError);
    }

    internal static async Task<KeyboardOperationResult> ExecuteKeyboardAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        Func<ViperObmAssignment, ViperObmAssignment, Task> checkpointAsync,
        Func<Task> holdAsync) =>
        await ExecuteFunctionAsync(
            transport,
            devicePath,
            new(ProfileId, ButtonId, ViperObmMappingMode.Normal,
                ViperObmFunctionId.KeyCode, new byte[] { 0x00, 0x04 }),
            "Keyboard A",
            checkpointAsync,
            holdAsync);

    internal static async Task<KeyboardOperationResult> ExecuteFunctionAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        ViperObmAssignment target,
        string targetLabel,
        Func<ViperObmAssignment, ViperObmAssignment, Task> checkpointAsync,
        Func<Task> holdAsync)
    {
        ViperObmAssignment? originalNormal = null;
        ViperObmAssignment? originalHyperShift = null;
        ViperObmAssignment? targetReadback = null;
        ViperObmAssignment? hyperShiftAfterTarget = null;
        ViperObmAssignment? restorationNormalReadback = null;
        ViperObmAssignment? restorationHyperShiftReadback = null;
        string? operationError = null;
        string? restorationError = null;
        var writeAttempted = false;

        try
        {
            originalNormal = await ReadAsync(transport, devicePath, ViperObmMappingMode.Normal);
            originalHyperShift = await ReadAsync(transport, devicePath, ViperObmMappingMode.HyperShift);
            EnsureExpectedBaseline(originalNormal, originalHyperShift);
            await checkpointAsync(originalNormal, originalHyperShift);

            if (target.ProfileId != ProfileId || target.ButtonId != ButtonId ||
                target.Mode != ViperObmMappingMode.Normal)
            {
                throw new ArgumentException("验证目标必须是 Product 184 button 5 Normal 映射。", nameof(target));
            }
            writeAttempted = true;
            await WriteAsync(transport, devicePath, target);
            targetReadback = await ReadAsync(transport, devicePath, ViperObmMappingMode.Normal);
            EnsureEqual(target, targetReadback, $"{targetLabel} 目标");
            hyperShiftAfterTarget = await ReadAsync(transport, devicePath, ViperObmMappingMode.HyperShift);
            EnsureEqual(originalHyperShift, hyperShiftAfterTarget, $"{targetLabel} 层隔离");
            await holdAsync();
        }
        catch (Exception exception)
        {
            operationError = exception.Message;
        }
        finally
        {
            if (writeAttempted && originalNormal is not null && originalHyperShift is not null)
            {
                var errors = new List<string>();
                try
                {
                    await WriteAsync(transport, devicePath, originalNormal);
                }
                catch (Exception exception)
                {
                    errors.Add($"恢复写入：{exception.Message}");
                }
                try
                {
                    restorationNormalReadback = await ReadAsync(
                        transport, devicePath, ViperObmMappingMode.Normal);
                    EnsureEqual(originalNormal, restorationNormalReadback, "恢复 Normal");
                }
                catch (Exception exception)
                {
                    errors.Add($"恢复 Normal 读回：{exception.Message}");
                }
                try
                {
                    restorationHyperShiftReadback = await ReadAsync(
                        transport, devicePath, ViperObmMappingMode.HyperShift);
                    EnsureEqual(originalHyperShift, restorationHyperShiftReadback, "恢复 HyperShift 层隔离");
                }
                catch (Exception exception)
                {
                    errors.Add($"恢复 HyperShift 读回：{exception.Message}");
                }
                restorationError = errors.Count == 0 ? null : string.Join(" ", errors);
            }
        }

        return new KeyboardOperationResult(
            originalNormal,
            originalHyperShift,
            targetReadback,
            hyperShiftAfterTarget,
            restorationNormalReadback,
            restorationHyperShiftReadback,
            operationError,
            restorationError);
    }

    private static void EnsureExpectedBaseline(
        ViperObmAssignment normal,
        ViperObmAssignment hyperShift)
    {
        EnsureEqual(
            new(ProfileId, ButtonId, ViperObmMappingMode.Normal,
                ViperObmFunctionId.ButtonCode, new byte[] { ButtonId }),
            normal,
            "Normal 基线");
        EnsureEqual(
            new(ProfileId, ButtonId, ViperObmMappingMode.HyperShift,
                ViperObmFunctionId.ButtonCode, new byte[] { ButtonId }),
            hyperShift,
            "HyperShift 基线");
    }

    private static async Task<RestoreResult> RestoreAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        ViperObmAssignment? originalNormal,
        ViperObmAssignment originalHyperShift)
    {
        var errors = new List<string>();
        ViperObmAssignment? hyperShiftReadback = null;
        ViperObmAssignment? normalReadback = null;
        try
        {
            await WriteAsync(transport, devicePath, originalHyperShift);
        }
        catch (Exception exception)
        {
            errors.Add($"恢复写入：{exception.Message}");
        }
        try
        {
            hyperShiftReadback = await ReadAsync(transport, devicePath, ViperObmMappingMode.HyperShift);
            EnsureEqual(originalHyperShift, hyperShiftReadback, "恢复 HyperShift");
        }
        catch (Exception exception)
        {
            errors.Add($"恢复 HyperShift 读回：{exception.Message}");
        }
        if (originalNormal is not null)
        {
            try
            {
                normalReadback = await ReadAsync(transport, devicePath, ViperObmMappingMode.Normal);
                EnsureEqual(originalNormal, normalReadback, "恢复 Normal 层隔离");
            }
            catch (Exception exception)
            {
                errors.Add($"恢复 Normal 读回：{exception.Message}");
            }
        }

        return new(hyperShiftReadback, normalReadback,
            errors.Count == 0 ? null : string.Join(" ", errors));
    }

    private static async Task<ViperObmAssignment> ReadAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        ViperObmMappingMode mode)
    {
        var request = ViperObmProtocol.CreateGetAssignmentRequest(ProfileId, ButtonId, mode);
        var response = await SendAsync(transport, devicePath, request);
        return ViperObmProtocol.ParseAssignment(response, ProfileId, ButtonId, mode);
    }

    private static Task<byte[]> WriteAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        ViperObmAssignment assignment) =>
        SendAsync(transport, devicePath, ViperObmProtocol.CreateSetAssignmentRequest(assignment));

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

    private static void EnsureEqual(
        ViperObmAssignment expected,
        ViperObmAssignment actual,
        string phase)
    {
        if (expected.ProfileId != actual.ProfileId ||
            expected.ButtonId != actual.ButtonId ||
            expected.Mode != actual.Mode ||
            expected.Function != actual.Function ||
            !expected.FunctionData.SequenceEqual(actual.FunctionData))
        {
            throw new InvalidOperationException($"{phase}映射读回不一致：写入 {Format(expected)}，读回 {Format(actual)}。");
        }
    }

    private static string Format(ViperObmAssignment assignment) =>
        $"profile={assignment.ProfileId},button={assignment.ButtonId},mode={assignment.Mode}," +
        $"function={assignment.Function},data={Convert.ToHexString(assignment.FunctionData.ToArray())}";

    private static string ParseOutputPath(string[] args)
    {
        string? outputPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--viper-obm-write":
                    break;
                case "--output" when index + 1 < args.Length &&
                                     !args[index + 1].StartsWith("--", StringComparison.Ordinal):
                    outputPath = Path.GetFullPath(args[++index]);
                    break;
                default:
                    throw new ArgumentException($"不支持或不完整的参数：{args[index]}");
            }
        }

        if (outputPath is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputPath), ".json"))
        {
            throw new ArgumentException("必须提供 --viper-obm-write --output <json>。");
        }
        if (File.Exists(outputPath))
        {
            throw new ArgumentException("--output 已存在，拒绝覆盖。");
        }
        return outputPath;
    }

    private static KeyboardOptions ParseKeyboardOptions(string[] args, string command)
    {
        string? outputPath = null;
        var holdSeconds = 30;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case var value when StringComparer.Ordinal.Equals(value, command):
                    break;
                case "--hold-seconds" when index + 1 < args.Length &&
                                           int.TryParse(args[index + 1], out var parsedHold):
                    holdSeconds = parsedHold;
                    index++;
                    break;
                case "--output" when index + 1 < args.Length &&
                                     !args[index + 1].StartsWith("--", StringComparison.Ordinal):
                    outputPath = Path.GetFullPath(args[++index]);
                    break;
                default:
                    throw new ArgumentException($"不支持或不完整的参数：{args[index]}");
            }
        }

        if (outputPath is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputPath), ".json"))
        {
            throw new ArgumentException($"必须提供 {command} --output <json>。");
        }
        if (holdSeconds is < 5 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "--hold-seconds 必须为 5..60。");
        }
        var baselinePath = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            Path.GetFileNameWithoutExtension(outputPath) + ".baseline.json");
        if (File.Exists(outputPath) || File.Exists(baselinePath))
        {
            throw new ArgumentException("--output 或对应 baseline 证据已存在，拒绝覆盖。");
        }
        return new KeyboardOptions(outputPath, baselinePath, holdSeconds);
    }

    private static async Task WriteArtifactAsync<T>(string outputPath, T artifact)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporary = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, outputPath, overwrite: false);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal sealed record OperationResult(
        ViperObmAssignment? OriginalNormal,
        ViperObmAssignment? OriginalHyperShift,
        ViperObmAssignment? SameValueReadback,
        ViperObmAssignment? TargetReadback,
        ViperObmAssignment? NormalAfterTarget,
        ViperObmAssignment? RestorationHyperShiftReadback,
        ViperObmAssignment? RestorationNormalReadback,
        string? OperationError,
        string? RestorationError);

    internal sealed record KeyboardOperationResult(
        ViperObmAssignment? OriginalNormal,
        ViperObmAssignment? OriginalHyperShift,
        ViperObmAssignment? TargetReadback,
        ViperObmAssignment? HyperShiftAfterTarget,
        ViperObmAssignment? RestorationNormalReadback,
        ViperObmAssignment? RestorationHyperShiftReadback,
        string? OperationError,
        string? RestorationError);

    private sealed record RestoreResult(
        ViperObmAssignment? HyperShiftReadback,
        ViperObmAssignment? NormalReadback,
        string? Error);

    private sealed record ValidationArtifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Device,
        string Collection,
        byte ProfileId,
        byte ButtonId,
        ViperObmAssignment? OriginalNormal,
        ViperObmAssignment? OriginalHyperShift,
        ViperObmAssignment? SameValueReadback,
        ViperObmAssignment? TargetReadback,
        ViperObmAssignment? NormalAfterTarget,
        ViperObmAssignment? RestorationHyperShiftReadback,
        ViperObmAssignment? RestorationNormalReadback,
        string? OperationError,
        string? RestorationError);

    private sealed record KeyboardOptions(string OutputPath, string BaselinePath, int HoldSeconds);

    private sealed record KeyboardBaselineArtifact(
        DateTimeOffset StartedAt,
        string Device,
        byte ProfileId,
        byte ButtonId,
        ViperObmAssignment OriginalNormal,
        ViperObmAssignment OriginalHyperShift);

    private sealed record KeyboardValidationArtifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Device,
        string Collection,
        byte ProfileId,
        byte ButtonId,
        ViperObmAssignment? OriginalNormal,
        ViperObmAssignment? OriginalHyperShift,
        ViperObmAssignment? TargetReadback,
        ViperObmAssignment? HyperShiftAfterTarget,
        ViperObmAssignment? RestorationNormalReadback,
        ViperObmAssignment? RestorationHyperShiftReadback,
        string? OperationError,
        string? RestorationError);
}
