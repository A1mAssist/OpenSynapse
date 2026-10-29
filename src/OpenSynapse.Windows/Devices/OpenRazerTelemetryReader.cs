using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class OpenRazerTelemetryReader
{
    private readonly RazerDeviceResolver _resolver;
    private readonly RazerCapabilityClient _client;
    private string? _validatedViperPollingPath;
    private string? _validatedViperDpiPath;
    private string? _validatedViperDpiStagesPath;
    private string? _validatedViperIdlePath;
    private string? _validatedViperLowBatteryThresholdPath;

    public OpenRazerTelemetryReader(RazerDeviceResolver resolver, RazerCapabilityClient client)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async ValueTask<OpenRazerTelemetryReadResult> ReadAsync(
        ResolvedRazerDevice viper,
        CancellationToken cancellationToken = default)
    {
        ResetValidation();
        int? batteryPercent = null;
        int? pollingRate = null;
        int? dpiX = null;
        int? dpiY = null;
        int? idleSeconds = null;
        ViperDpiStagesTelemetry? dpiStages = null;
        byte? lowBatteryThresholdRaw = null;
        var errors = new List<string>();
            try
            {
                var response = await _client.QueryAsync(viper, CapabilityIds.BatteryGet, cancellationToken);
                batteryPercent = ViperProduct184Protocol.ParseBatteryPercent(
                    response, RazerCapabilityClient.CreateRequest(viper, CapabilityIds.BatteryGet));
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse battery: {exception.Message}");
            }

            try
            {
                var response = await _client.QueryAsync(viper, CapabilityIds.PollingRateGet, cancellationToken);
                pollingRate = ViperProduct184Protocol.ParsePollingRateHertz(
                    response, RazerCapabilityClient.CreateRequest(viper, CapabilityIds.PollingRateGet));
                _validatedViperPollingPath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse polling rate: {exception.Message}");
            }

            try
            {
                var response = await _client.QueryAsync(viper, CapabilityIds.CurrentDpiGet, cancellationToken);
                (dpiX, dpiY) = ViperProduct184Protocol.ParseDpi(
                    response, RazerCapabilityClient.CreateRequest(viper, CapabilityIds.CurrentDpiGet));
                _validatedViperDpiPath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse DPI: {exception.Message}");
            }

            try
            {
                var response = await _client.QueryAsync(viper, CapabilityIds.IdleTimeoutGet, cancellationToken);
                idleSeconds = ViperProduct184Protocol.ParseIdleSeconds(
                    response, RazerCapabilityClient.CreateRequest(viper, CapabilityIds.IdleTimeoutGet));
                _validatedViperIdlePath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse idle timeout: {exception.Message}");
            }

            try
            {
                var response = await _client.QueryAsync(viper, CapabilityIds.DpiStagesGet, cancellationToken);
                var parsed = ViperDpiStagesProtocol.Parse(
                    response, RazerCapabilityClient.CreateRequest(viper, CapabilityIds.DpiStagesGet));
                dpiStages = new ViperDpiStagesTelemetry(
                    parsed.ActiveStage,
                    parsed.Stages.Select(stage => new ViperDpiStageTelemetry(stage.Number, stage.X, stage.Y)).ToArray());
                _validatedViperDpiStagesPath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse DPI stages: {exception.Message}");
            }

            try
            {
                var response = await _client.QueryAsync(
                    viper, CapabilityIds.LowBatteryThresholdGet, cancellationToken);
                lowBatteryThresholdRaw = ViperLowBatteryThresholdProtocol.ParseRaw(
                    response, RazerCapabilityClient.CreateRequest(viper, CapabilityIds.LowBatteryThresholdGet));
                _validatedViperLowBatteryThresholdPath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse low-battery threshold: {exception.Message}");
            }


        return new OpenRazerTelemetryReadResult(
            batteryPercent,
            pollingRate,
            dpiX,
            dpiY,
            idleSeconds,
            dpiStages,
            lowBatteryThresholdRaw,
            errors);
    }
    public async ValueTask<int> SetViperPollingRateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int hertz,
        CancellationToken cancellationToken = default)
    {
        var viper = _resolver.FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(_validatedViperPollingPath, viper.Descriptor.Id, "Read the mouse polling rate successfully first.");

        var original = await ReadViperPollingRateAsync(viper, cancellationToken);
        if (original == hertz)
        {
            return original;
        }

        try
        {
            await WriteViperPollingRateAsync(viper, hertz, cancellationToken);
            var actual = await ReadViperPollingRateAsync(viper, cancellationToken);
            if (actual != hertz)
            {
                throw new InvalidOperationException($"Polling-rate readback mismatch: wrote {hertz} Hz, read {actual} Hz.");
            }
            return actual;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreViperPollingRateAsync(viper, original);
            var message = "Polling-rate update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the polling rate immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<(int X, int Y)> SetViperDpiAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        if (x is < 100 or > 30000 || x % 50 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "DPI must be between 100 and 30000 in increments of 50.");
        }
        if (y is < 100 or > 30000 || y % 50 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "DPI must be between 100 and 30000 in increments of 50.");
        }

        var viper = _resolver.FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(_validatedViperDpiPath, viper.Descriptor.Id, "Read the mouse DPI successfully first.");
        var original = await ReadViperDpiAsync(viper, cancellationToken);
        if (original == (x, y))
        {
            return original;
        }

        try
        {
            await WriteViperDpiAsync(viper, x, y, cancellationToken);
            var actual = await ReadViperDpiAsync(viper, cancellationToken);
            if (actual != (x, y))
            {
                throw new InvalidOperationException(
                    $"DPI readback mismatch: wrote {x} x {y}, read {actual.X} x {actual.Y}.");
            }
            return actual;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreViperDpiAsync(viper, original);
            var message = "DPI update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check DPI immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<ViperDpiStagesTelemetry> SetViperDpiStagesAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ViperDpiStagesTelemetry stages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var requested = ToProtocolState(stages);
        var viper = _resolver.FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(_validatedViperDpiStagesPath, viper.Descriptor.Id, "Read the mouse DPI stages successfully first.");

        var original = await ReadViperDpiStagesAsync(viper, cancellationToken);
        if (AreDpiStagesEqual(original, requested))
        {
            return ToTelemetry(original);
        }

        try
        {
            await _client.QueryBuiltRequestAsync(
                viper,
                CapabilityIds.DpiStagesSet,
                ViperProduct184Protocol.CreateSetDpiStagesRequest(requested),
                cancellationToken);
            var actual = await ReadViperDpiStagesAsync(viper, cancellationToken);
            EnsureDpiStagesEqual(requested, actual, "mouse DPI stages");
            return ToTelemetry(actual);
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreViperDpiStagesAsync(viper, original);
            var message = "DPI-stage update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the DPI stages immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<int> SetViperIdleSecondsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int seconds,
        CancellationToken cancellationToken = default)
    {
        if (seconds is < 60 or > 900 || seconds % 60 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Idle timeout must be a whole number of minutes between 1 and 15.");
        }

        var viper = _resolver.FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(_validatedViperIdlePath, viper.Descriptor.Id, "Read the mouse idle timeout successfully first.");
        var original = await ReadViperIdleSecondsAsync(viper, cancellationToken);
        if (original == seconds)
        {
            return original;
        }

        try
        {
            await WriteViperIdleSecondsAsync(viper, seconds, cancellationToken);
            var actual = await ReadViperIdleSecondsAsync(viper, cancellationToken);
            if (actual != seconds)
            {
                throw new InvalidOperationException($"Idle-timeout readback mismatch: wrote {seconds} seconds, read {actual} seconds.");
            }
            return actual;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreViperIdleSecondsAsync(viper, original);
            var message = "Idle-timeout update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the idle timeout immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }
    public async ValueTask<int> SetViperLowBatteryThresholdAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int percent,
        CancellationToken cancellationToken = default)
    {
        _ = ViperLowBatteryThresholdProtocol.ToRaw(percent);
        var viper = _resolver.FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(
            _validatedViperLowBatteryThresholdPath,
            viper.Descriptor.Id,
            "Read the mouse low-battery threshold successfully first.");

        var originalRaw = await ReadViperLowBatteryThresholdAsync(viper, cancellationToken);
        var originalPercent = ViperLowBatteryThresholdProtocol.ToPercent(originalRaw);
        if (originalPercent == percent)
        {
            return originalPercent;
        }

        try
        {
            await WriteViperLowBatteryThresholdAsync(viper, percent, cancellationToken);
            var actualRaw = await ReadViperLowBatteryThresholdAsync(viper, cancellationToken);
            var actualPercent = ViperLowBatteryThresholdProtocol.ToPercent(actualRaw);
            if (actualPercent != percent)
            {
                throw new InvalidOperationException(
                    $"Low-battery threshold readback mismatch: wrote {percent}%, read {actualPercent}%.");
            }
            return actualPercent;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreViperLowBatteryThresholdAsync(viper, originalPercent);
            var message = "Low-battery threshold update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." :
                    "Original value restoration failed; check the threshold immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }
    public void ResetValidation()
    {
        _validatedViperPollingPath = null;
        _validatedViperDpiPath = null;
        _validatedViperDpiStagesPath = null;
        _validatedViperIdlePath = null;
        _validatedViperLowBatteryThresholdPath = null;
    }

    private async Task<int> ReadViperPollingRateAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var response = await _client.QueryAsync(device, CapabilityIds.PollingRateGet, cancellationToken);
        return ViperProduct184Protocol.ParsePollingRateHertz(
            response, RazerCapabilityClient.CreateRequest(device, CapabilityIds.PollingRateGet));
    }

    private Task WriteViperPollingRateAsync(
        ResolvedRazerDevice device,
        int hertz,
        CancellationToken cancellationToken) =>
        _client.QueryBuiltRequestAsync(
            device,
            CapabilityIds.PollingRateSet,
            ViperProduct184Protocol.CreateSetPollingRateRequest(hertz),
            cancellationToken);

    private async Task<bool> TryRestoreViperPollingRateAsync(ResolvedRazerDevice device, int original)
    {
        try
        {
            await WriteViperPollingRateAsync(device, original, CancellationToken.None);
            return await ReadViperPollingRateAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<(int X, int Y)> ReadViperDpiAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var response = await _client.QueryAsync(device, CapabilityIds.CurrentDpiGet, cancellationToken);
        return ViperProduct184Protocol.ParseDpi(
            response, RazerCapabilityClient.CreateRequest(device, CapabilityIds.CurrentDpiGet));
    }

    private Task WriteViperDpiAsync(
        ResolvedRazerDevice device,
        int x,
        int y,
        CancellationToken cancellationToken) =>
        _client.QueryBuiltRequestAsync(
            device,
            CapabilityIds.CurrentDpiSet,

            ViperProduct184Protocol.CreateSetDpiRequest(x, y),
            cancellationToken);

    private async Task<bool> TryRestoreViperDpiAsync(
        ResolvedRazerDevice device,
        (int X, int Y) original)
    {
        try
        {
            await WriteViperDpiAsync(device, original.X, original.Y, CancellationToken.None);
            return await ReadViperDpiAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<int> ReadViperIdleSecondsAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var response = await _client.QueryAsync(device, CapabilityIds.IdleTimeoutGet, cancellationToken);
        return ViperProduct184Protocol.ParseIdleSeconds(
            response, RazerCapabilityClient.CreateRequest(device, CapabilityIds.IdleTimeoutGet));
    }

    private async Task<byte> ReadViperLowBatteryThresholdAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var response = await _client.QueryAsync(
            device, CapabilityIds.LowBatteryThresholdGet, cancellationToken);
        return ViperLowBatteryThresholdProtocol.ParseRaw(
            response, RazerCapabilityClient.CreateRequest(device, CapabilityIds.LowBatteryThresholdGet));
    }

    private Task WriteViperLowBatteryThresholdAsync(
        ResolvedRazerDevice device,
        int percent,
        CancellationToken cancellationToken) =>
        _client.QueryBuiltRequestAsync(
            device,
            CapabilityIds.LowBatteryThresholdSet,
            ViperProduct184Protocol.CreateSetLowBatteryThresholdRequest(percent),
            cancellationToken);

    private async Task<bool> TryRestoreViperLowBatteryThresholdAsync(
        ResolvedRazerDevice device,
        int originalPercent)
    {
        try
        {
            await WriteViperLowBatteryThresholdAsync(device, originalPercent, CancellationToken.None);
            var restoredRaw = await ReadViperLowBatteryThresholdAsync(device, CancellationToken.None);
            return ViperLowBatteryThresholdProtocol.ToPercent(restoredRaw) == originalPercent;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private Task WriteViperIdleSecondsAsync(
        ResolvedRazerDevice device,
        int seconds,
        CancellationToken cancellationToken) =>
        _client.QueryBuiltRequestAsync(
            device,
            CapabilityIds.IdleTimeoutSet,
            ViperProduct184Protocol.CreateSetIdleTimeoutRequest(seconds),
            cancellationToken);

    private async Task<bool> TryRestoreViperIdleSecondsAsync(ResolvedRazerDevice device, int original)
    {
        try
        {
            await WriteViperIdleSecondsAsync(device, original, CancellationToken.None);
            return await ReadViperIdleSecondsAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<ViperDpiStagesState> ReadViperDpiStagesAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var response = await _client.QueryAsync(device, CapabilityIds.DpiStagesGet, cancellationToken);
        return ViperDpiStagesProtocol.Parse(
            response, RazerCapabilityClient.CreateRequest(device, CapabilityIds.DpiStagesGet));
    }

    private async Task<bool> TryRestoreViperDpiStagesAsync(
        ResolvedRazerDevice device,
        ViperDpiStagesState original)
    {
        try
        {
            await _client.QueryBuiltRequestAsync(
                device,
                CapabilityIds.DpiStagesSet,
                ViperProduct184Protocol.CreateSetDpiStagesRequest(original),
                CancellationToken.None);
            var restored = await ReadViperDpiStagesAsync(device, CancellationToken.None);
            EnsureDpiStagesEqual(original, restored, "DPI-stage restore");
            return true;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private static ViperDpiStagesState ToProtocolState(ViperDpiStagesTelemetry telemetry) =>
        new(
            telemetry.ActiveStage,
            telemetry.Stages.Select(stage => new ViperDpiStage(stage.Number, stage.X, stage.Y)).ToArray());

    private static ViperDpiStagesTelemetry ToTelemetry(ViperDpiStagesState state) =>
        new(
            state.ActiveStage,
            state.Stages.Select(stage => new ViperDpiStageTelemetry(stage.Number, stage.X, stage.Y)).ToArray());

    private static void EnsureDpiStagesEqual(
        ViperDpiStagesState expected,
        ViperDpiStagesState actual,
        string operation)
    {
        if (expected.ActiveStage != actual.ActiveStage ||
            expected.Stages.Count != actual.Stages.Count ||
            !expected.Stages.SequenceEqual(actual.Stages))
        {
            throw new InvalidOperationException(
                $"{operation} readback mismatch: wrote {ViperDpiStagesProtocol.Format(expected)}, " +
                $"read {ViperDpiStagesProtocol.Format(actual)}.");
        }
    }

    private static bool AreDpiStagesEqual(
        ViperDpiStagesState left,
        ViperDpiStagesState right) =>
        left.ActiveStage == right.ActiveStage &&
        left.Stages.Count == right.Stages.Count &&
        left.Stages.SequenceEqual(right.Stages);


}

internal sealed record OpenRazerTelemetryReadResult(
    int? BatteryPercent,
    int? PollingRateHertz,
    int? DpiX,
    int? DpiY,
    int? IdleSeconds,
    ViperDpiStagesTelemetry? DpiStages,
    byte? LowBatteryThresholdRaw,
    IReadOnlyList<string> Errors);

