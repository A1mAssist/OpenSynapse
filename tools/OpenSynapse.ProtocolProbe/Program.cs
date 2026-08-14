using System.ComponentModel;
using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.ProtocolProbe;

public sealed record ProbeOptions(bool IncludeSourceBacked, string? OutputPath)
{
    public static ProbeOptions Parse(string[] args)
    {
        var includeSourceBacked = false;
        string? outputPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--include-source-backed":
                    includeSourceBacked = true;
                    break;
                case "--output" when index + 1 < args.Length &&
                                            !args[index + 1].StartsWith("--", StringComparison.Ordinal):
                    outputPath = args[++index];
                    break;
                default:
                    throw new ArgumentException($"Unsupported or incomplete option: {args[index]}");
            }
        }

        return new ProbeOptions(includeSourceBacked, outputPath);
    }
}

public sealed record ProbeResult(
    ushort ProductId,
    string Name,
    ProbeEvidenceLevel Evidence,
    string RequestHex,
    string? ResponseHex,
    byte? ResponseStatus,
    string? Error,
    string? DecodedValue = null);

public sealed record ProbeDocument(
    DateTimeOffset CapturedAt,
    ushort UsagePage,
    ushort Usage,
    ushort FeatureReportByteLength,
    IReadOnlyList<ProbeResult> Results);

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        ProbeOptions options;
        try
        {
            options = ProbeOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 64;
        }

        var snapshot = await new WindowsHidDiscovery().DiscoverAsync();
        if (snapshot.ErrorMessage is not null)
        {
            Console.Error.WriteLine(snapshot.ErrorMessage);
            return 1;
        }

        var devices = snapshot.Devices
            .Where(device =>
                device.Access == DeviceAccessState.Available &&
                device.FeatureReportByteLength == RazerFeatureReport.Length &&
                device.UsagePage == 0x0001 &&
                device.Usage == 0x0002)
            .ToDictionary(device => device.ProductId);

        var transport = new RazerFeatureTransport();
        var results = new List<ProbeResult>();
        foreach (var command in ProbeCatalog.Get(options.IncludeSourceBacked))
        {
            if (!devices.TryGetValue(command.ProductId, out var device))
            {
                continue;
            }

            var request = RazerFeatureReport.CreateRequest(
                command.TransactionId,
                command.DataSize,
                command.CommandClass,
                command.CommandId,
                command.Arguments.Span);

            try
            {
                var response = await transport.QueryAsync(
                    device.Id,
                    command.TransactionId,
                    command.DataSize,
                    command.CommandClass,
                    command.CommandId,
                    command.Arguments,
                    TimeSpan.FromMilliseconds(command.WaitMilliseconds),
                    CancellationToken.None,
                    command.AllowRemainingPacketsMismatch);
                var decodedValue = command.Name switch
                {
                    "blade.cpu-boost" => BladeBoostProtocol.ParseCpu(response).ToString(),
                    "blade.gpu-boost" => BladeBoostProtocol.ParseGpu(response).ToString(),
                    "blade.logo-power" => BladeLogoProtocol.ParsePower(response) ? "On" : "Off",
                    "blade.logo-mode" => BladeLogoProtocol.ParseMode(response).ToString(),
                    "blade.fan-id-list" => string.Join(",", BladeThermalProtocol.ParseFanIdList(response)),
                    "blade.current-fan-speed-cpu" => BladeThermalProtocol.ParseCurrentSpeedRpm(response, BladeThermalProtocol.CpuFanId).ToString(),
                    "blade.current-fan-speed-gpu" => BladeThermalProtocol.ParseCurrentSpeedRpm(response, BladeThermalProtocol.GpuFanId).ToString(),
                    "blade.advanced-fan-cpu" => BladeThermalProtocol.ParseAdvancedFanMode(response, BladeThermalProtocol.CpuFanId).ToString(),
                    "blade.advanced-fan-gpu" => BladeThermalProtocol.ParseAdvancedFanMode(response, BladeThermalProtocol.GpuFanId).ToString(),
                    "blade.battery-level" => BladeProduct710Protocol.ParseBatteryPercent(response).ToString(),
                    "viper.low-battery-threshold" => ViperLowBatteryThresholdProtocol.Format(
                        ViperLowBatteryThresholdProtocol.ParseRaw(response)),
                    "viper.dpi-stages" => ViperDpiStagesProtocol.Format(
                        ViperDpiStagesProtocol.Parse(response)),
                    _ => null,
                };
                results.Add(new ProbeResult(
                    command.ProductId,
                    command.Name,
                    command.Evidence,
                    Convert.ToHexString(request),
                    Convert.ToHexString(response),
                    response[1],
                    null,
                    decodedValue));
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                results.Add(new ProbeResult(
                    command.ProductId,
                    command.Name,
                    command.Evidence,
                    Convert.ToHexString(request),
                    null,
                    null,
                    exception.Message));
            }
        }

        var document = new ProbeDocument(
            DateTimeOffset.UtcNow,
            0x0001,
            0x0002,
            RazerFeatureReport.Length,
            results);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);

        if (options.OutputPath is not null)
        {
            var fullPath = Path.GetFullPath(options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return results.Count == 0 ? 2 : results.Any(result => result.Error is not null) ? 1 : 0;
    }
}
