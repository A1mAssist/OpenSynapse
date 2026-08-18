using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

internal static class ViperObmReadValidation
{
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(60);

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
        ViperObmSnapshot? data = null;
        string? error = null;
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

            data = await ReadAsync(new RazerFeatureTransport(), devices[0].Id);
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        var artifact = new ValidationArtifact(
            startedAt,
            DateTimeOffset.UtcNow,
            "1532:00B8",
            "0001:0002",
            RazerFeatureReport.Length,
            data,
            error);
        await WriteArtifactAsync(outputPath, artifact);

        if (error is not null)
        {
            Console.Error.WriteLine($"只读 OBM 验证失败：{error}");
            Console.Error.WriteLine($"证据已写入 {outputPath}");
            return 1;
        }

        Console.WriteLine(
            $"只读完成：{data!.ProfileIds.Count}/{data.MaximumProfiles} 个 Profile，" +
            $"{data.ButtonIds.Count} 个 Button，{data.Assignments.Count} 条 Normal/HyperShift 映射。");
        Console.WriteLine($"证据已写入 {outputPath}");
        return 0;
    }

    internal static async Task<ViperObmSnapshot> ReadAsync(
        IRazerFeatureTransport transport,
        string devicePath)
    {
        async Task<byte[]> Send(byte[] request, string operation)
        {
            var response = await transport.QueryAsync(
                devicePath,
                request[2],
                request[6],
                request[7],
                request[8],
                request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
                DeviceWait,
                CancellationToken.None);
            Console.WriteLine(
                $"{operation}: request={Convert.ToHexString(request)}, response={Convert.ToHexString(response)}");
            return response;
        }

        var maximum = ViperObmProtocol.ParseMaximumProfiles(
            await Send(ViperObmProtocol.CreateGetMaximumProfilesRequest(), "maximum profiles"));
        var count = ViperObmProtocol.ParseProfileCount(
            await Send(ViperObmProtocol.CreateGetProfileCountRequest(), "profile count"));
        var profiles = ViperObmProtocol.ParseProfileIds(
            await Send(ViperObmProtocol.CreateGetProfileIdsRequest(), "profile ids"));
        var buttons = ViperObmProtocol.ParseButtonIds(
            await Send(ViperObmProtocol.CreateGetButtonIdsRequest(), "button ids"));

        if (profiles.Length != count || profiles.Length > maximum)
        {
            throw new InvalidOperationException(
                $"Viper OBM Profile 元数据不一致：max={maximum}, count={count}, ids=[{string.Join(',', profiles)}]。");
        }

        var assignments = new List<ViperObmAssignment>();
        foreach (var profile in profiles)
        {
            foreach (var button in buttons)
            {
                foreach (var mode in Enum.GetValues<ViperObmMappingMode>())
                {
                    var response = await Send(
                        ViperObmProtocol.CreateGetAssignmentRequest(profile, button, mode),
                        $"assignment profile={profile} button={button} mode={mode}");
                    assignments.Add(ViperObmProtocol.ParseAssignment(response, profile, button, mode));
                }
            }
        }

        return new ViperObmSnapshot(maximum, profiles, buttons, assignments);
    }

    private static string ParseOutputPath(string[] args)
    {
        string? outputPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--viper-obm-read":
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
            throw new ArgumentException("必须提供 --viper-obm-read --output <json>。");
        }
        if (File.Exists(outputPath))
        {
            throw new ArgumentException("--output 已存在，拒绝覆盖。");
        }

        return outputPath;
    }

    private static async Task WriteArtifactAsync(string outputPath, ValidationArtifact artifact)
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

    internal sealed record ViperObmSnapshot(
        byte MaximumProfiles,
        IReadOnlyList<byte> ProfileIds,
        IReadOnlyList<byte> ButtonIds,
        IReadOnlyList<ViperObmAssignment> Assignments);

    private sealed record ValidationArtifact(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        string Device,
        string Collection,
        int FeatureReportByteLength,
        ViperObmSnapshot? Snapshot,
        string? Error);
}
