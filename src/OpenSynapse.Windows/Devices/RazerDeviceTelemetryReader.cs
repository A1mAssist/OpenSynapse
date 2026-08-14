using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed class RazerDeviceTelemetryReader : IRazerDeviceTelemetryReader
{
    private readonly IRazerFeatureTransport _transport;
    private readonly RazerDeviceRegistry _registry;
    private string? _validatedBladeBrightnessPath;
    private string? _validatedBladePerformancePath;
    private string? _validatedBladeBoostPath;
    private string? _validatedBladeChargeLimitPath;
    private string? _validatedBladeMaxFanPath;
    private string? _validatedBladeLogoPath;
    private string? _validatedViperPollingPath;
    private string? _validatedViperDpiPath;
    private string? _validatedViperDpiStagesPath;
    private string? _validatedViperIdlePath;

    public RazerDeviceTelemetryReader()
        : this(new RazerFeatureTransport(), RazerDeviceRegistry.BuiltIn)
    {
    }

    public RazerDeviceTelemetryReader(IRazerFeatureTransport transport)
        : this(transport, RazerDeviceRegistry.BuiltIn)
    {
    }

    internal RazerDeviceTelemetryReader(
        IRazerFeatureTransport transport,
        RazerDeviceRegistry registry)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async ValueTask<RazerDeviceTelemetry> ReadAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default)
    {
        _validatedBladeBrightnessPath = null;
        _validatedBladePerformancePath = null;
        _validatedBladeBoostPath = null;
        _validatedBladeChargeLimitPath = null;
        _validatedBladeMaxFanPath = null;
        _validatedBladeLogoPath = null;
        _validatedViperPollingPath = null;
        _validatedViperDpiPath = null;
        _validatedViperDpiStagesPath = null;
        _validatedViperIdlePath = null;
        byte? bladeBrightness = null;
        BladePerformanceMode? bladePerformanceMode = null;
        BladeFanMode? bladeFanMode = null;
        int? bladeFanTargetRpm = null;
        int? bladeChargeLimitPercent = null;
        BladeCpuBoostMode? bladeCpuBoostMode = null;
        BladeGpuBoostMode? bladeGpuBoostMode = null;
        BladeMaxFanMode? bladeMaxFanMode = null;
        int? bladeCurrentFanCpuRpm = null;
        int? bladeCurrentFanGpuRpm = null;
        byte? bladeAdvancedFanCpuModeRaw = null;
        byte? bladeAdvancedFanGpuModeRaw = null;
        int? bladeWiredBatteryPercent = null;
        byte? bladeChargingStatusRaw = null;
        byte? bladeAutoSleepRaw = null;
        int? bladeTimeToSleepSeconds = null;
        BladeLogoMode? bladeLogoMode = null;
        int? batteryPercent = null;
        int? pollingRate = null;
        int? dpiX = null;
        int? dpiY = null;
        int? idleSeconds = null;
        ViperDpiStagesTelemetry? dpiStages = null;
        byte? lowBatteryThresholdRaw = null;
        var errors = new List<string>();

        var blade = FindReadyDevice(devices, "blade-710");
        if (blade is not null)
        {
            var bladeThermalReadSucceeded = false;
            try
            {
                var response = await QueryCapabilityAsync(
                    blade, "keyboard-brightness.get", cancellationToken);
                EnsureDataSize(response, 2, "键盘亮度");
                bladeBrightness = response[RazerFeatureReport.ArgumentsOffset + 1];
                _validatedBladeBrightnessPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"键盘亮度：{exception.Message}");
            }

            try
            {
                var thermalState = await ReadBladeThermalStateAsync(blade, cancellationToken);
                bladePerformanceMode = thermalState.PerformanceMode;
                bladeFanMode = thermalState.FanMode;
                if (thermalState.FanMode == BladeFanMode.Manual)
                {
                    bladeFanTargetRpm = await ReadBladeFanTargetRpmAsync(blade, cancellationToken);
                }
                _validatedBladePerformancePath = blade.Descriptor.Id;
                bladeThermalReadSucceeded = true;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"性能与风扇状态：{exception.Message}");
            }

            try
            {
                bladeCurrentFanCpuRpm = await ReadBladeCurrentFanRpmAsync(
                    blade, BladeThermalProtocol.CpuFanId, cancellationToken);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"当前 CPU 风扇转速：{exception.Message}");
            }

            try
            {
                bladeCurrentFanGpuRpm = await ReadBladeCurrentFanRpmAsync(
                    blade, BladeThermalProtocol.GpuFanId, cancellationToken);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"当前 GPU 风扇转速：{exception.Message}");
            }

            try
            {
                bladeAdvancedFanCpuModeRaw = await ReadBladeAdvancedFanModeAsync(
                    blade, BladeThermalProtocol.CpuFanId, cancellationToken);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"CPU 高级风扇模式：{exception.Message}");
            }

            try
            {
                bladeAdvancedFanGpuModeRaw = await ReadBladeAdvancedFanModeAsync(
                    blade, BladeThermalProtocol.GpuFanId, cancellationToken);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"GPU 高级风扇模式：{exception.Message}");
            }

            if (bladeThermalReadSucceeded)
            {
                try
                {
                    var boostState = await ReadBladeBoostStateAsync(blade, cancellationToken);
                    bladeCpuBoostMode = boostState.Cpu;
                    bladeGpuBoostMode = boostState.Gpu;
                    _validatedBladeBoostPath = blade.Descriptor.Id;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                {
                    errors.Add($"CPU/GPU Boost：{exception.Message}");
                }
            }

            try
            {
                bladeChargeLimitPercent = await ReadBladeChargeLimitAsync(blade, cancellationToken);
                _validatedBladeChargeLimitPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"充电上限：{exception.Message}");
            }

            try
            {
                bladeMaxFanMode = ToMaxFanMode(await ReadBladePowerModeMaskAsync(blade, cancellationToken));
                _validatedBladeMaxFanPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"Max Fan：{exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(
                    blade, "wired-battery.get", cancellationToken);
                bladeWiredBatteryPercent = BladeProduct710Protocol.ParseBatteryPercent(
                    response, CreateCapabilityRequest(blade, "wired-battery.get"));
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"有线电池电量：{exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(
                    blade, "charging-status.get", cancellationToken);
                bladeChargingStatusRaw = BladeProduct710Protocol.ParseChargingStatusRaw(
                    response, CreateCapabilityRequest(blade, "charging-status.get"));
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"充电状态：{exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(
                    blade, "auto-sleep.get", cancellationToken);
                bladeAutoSleepRaw = BladeProduct710Protocol.ParseAutoSleepRaw(
                    response, CreateCapabilityRequest(blade, "auto-sleep.get"));
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"自动休眠：{exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(
                    blade, "time-to-sleep.get", cancellationToken);
                bladeTimeToSleepSeconds = BladeProduct710Protocol.ParseTimeToSleepSeconds(
                    response, CreateCapabilityRequest(blade, "time-to-sleep.get"));
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"休眠倒计时：{exception.Message}");
            }

            try
            {
                bladeLogoMode = (await ReadBladeLogoStateAsync(blade, cancellationToken)).CombinedMode;
                _validatedBladeLogoPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"Blade Logo：{exception.Message}");
            }

        }

        var viper = FindReadyDevice(devices, "viper-184");
        if (viper is not null)
        {
            try
            {
                var response = await QueryCapabilityAsync(viper, "battery.get", cancellationToken);
                batteryPercent = ViperProduct184Protocol.ParseBatteryPercent(
                    response, CreateCapabilityRequest(viper, "battery.get"));
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"鼠标电量：{exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(viper, "polling-rate.get", cancellationToken);
                pollingRate = ViperProduct184Protocol.ParsePollingRateHertz(
                    response, CreateCapabilityRequest(viper, "polling-rate.get"));
                _validatedViperPollingPath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"鼠标轮询率：{exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(viper, "current-dpi.get", cancellationToken);
                (dpiX, dpiY) = ViperProduct184Protocol.ParseDpi(
                    response, CreateCapabilityRequest(viper, "current-dpi.get"));
                _validatedViperDpiPath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"鼠标 DPI：{exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(viper, "idle-timeout.get", cancellationToken);
                idleSeconds = ViperProduct184Protocol.ParseIdleSeconds(
                    response, CreateCapabilityRequest(viper, "idle-timeout.get"));
                _validatedViperIdlePath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"鼠标休眠：{exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(viper, "dpi-stages.get", cancellationToken);
                var parsed = ViperDpiStagesProtocol.Parse(
                    response, CreateCapabilityRequest(viper, "dpi-stages.get"));
                dpiStages = new ViperDpiStagesTelemetry(
                    parsed.ActiveStage,
                    parsed.Stages.Select(stage => new ViperDpiStageTelemetry(stage.Number, stage.X, stage.Y)).ToArray());
                _validatedViperDpiStagesPath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"鼠标 DPI 档位：{exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(
                    viper, "low-battery-threshold.get", cancellationToken);
                lowBatteryThresholdRaw = ViperLowBatteryThresholdProtocol.ParseRaw(
                    response, CreateCapabilityRequest(viper, "low-battery-threshold.get"));
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                errors.Add($"鼠标低电量阈值：{exception.Message}");
            }

        }

        return new RazerDeviceTelemetry(
            bladeBrightness,
            bladePerformanceMode,
            bladeFanMode,
            bladeFanTargetRpm,
            bladeChargeLimitPercent,
            batteryPercent,
            pollingRate,
            dpiX,
            dpiY,
            idleSeconds,
            errors,
            DateTimeOffset.UtcNow,
            bladeCpuBoostMode,
            bladeGpuBoostMode,
            bladeMaxFanMode,
            bladeCurrentFanCpuRpm,
            bladeCurrentFanGpuRpm,
            bladeAdvancedFanCpuModeRaw,
            bladeAdvancedFanGpuModeRaw,
            bladeWiredBatteryPercent,
            bladeChargingStatusRaw,
            bladeAutoSleepRaw,
            bladeTimeToSleepSeconds,
            dpiStages,
            lowBatteryThresholdRaw,
            bladeLogoMode);
    }

    public async ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladePerformanceMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("Blade 平台控制通道不可用。");
        EnsureValidated(_validatedBladePerformancePath, blade.Descriptor.Id, "请先成功读取 Blade 性能模式。");

        var original = await ReadBladeThermalStateAsync(blade, cancellationToken);
        try
        {
            await WriteBladeThermalZoneAsync(blade, 0x01, mode, original.FanMode, cancellationToken);
            await WriteBladeThermalZoneAsync(blade, 0x02, mode, original.FanMode, cancellationToken);

            var actual = await ReadBladeThermalStateAsync(blade, cancellationToken);
            if (actual.PerformanceMode != mode || actual.FanMode != original.FanMode)
            {
                throw new InvalidOperationException(
                    $"性能模式读回不一致：写入 {mode} / {original.FanMode}，读回 {actual.PerformanceMode} / {actual.FanMode}。");
            }

            return actual.PerformanceMode;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            var restored = await TryRestoreBladeThermalStateAsync(blade, original);
            var message = $"性能模式设置失败：{exception.Message} " +
                (restored
                    ? "原状态已恢复。"
                    : "原状态恢复失败；请立即在 Synapse 中检查两个风扇分区。");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<int> SetBladeChargeLimitAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int percent,
        CancellationToken cancellationToken = default)
    {
        var raw = EncodeBladeChargeLimit(percent);
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("Blade 充电控制通道不可用。");
        EnsureValidated(_validatedBladeChargeLimitPath, blade.Descriptor.Id, "请先成功读取 Blade 充电上限。");

        var original = await ReadBladeChargeLimitAsync(blade, cancellationToken);
        try
        {
            await WriteBladeChargeLimitAsync(blade, raw, cancellationToken);
            var actual = await ReadBladeChargeLimitAsync(blade, cancellationToken);
            if (actual != percent)
            {
                throw new InvalidOperationException($"充电上限读回不一致：写入 {percent}%，读回 {actual}%。");
            }

            return actual;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            var restored = await TryRestoreBladeChargeLimitAsync(blade, original);
            var message = $"充电上限设置失败：{exception.Message} " +
                (restored
                    ? "原值已恢复。"
                    : "原值恢复失败；请立即在 Synapse 中检查充电上限。");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<BladeCpuBoostMode> SetBladeCpuBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeCpuBoostMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var state = await SetBladeBoostModeAsync(
            devices, BladeBoostProtocol.CpuCluster, (byte)mode, cancellationToken);
        return state.Cpu;
    }

    public async ValueTask<BladeGpuBoostMode> SetBladeGpuBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeGpuBoostMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var state = await SetBladeBoostModeAsync(
            devices, BladeBoostProtocol.GpuCluster, (byte)mode, cancellationToken);
        return state.Gpu;
    }

    public async ValueTask<BladeMaxFanMode> SetBladeMaxFanModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeMaxFanMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("Blade Max Fan 控制通道不可用。");
        EnsureValidated(_validatedBladeMaxFanPath, blade.Descriptor.Id, "请先成功读取 Blade Max Fan 状态。");

        var thermal = await ReadBladeThermalStateAsync(blade, cancellationToken);
        if (thermal.PerformanceMode != BladePerformanceMode.Custom)
        {
            throw new InvalidOperationException("只有 Custom 性能模式允许修改 Max Fan。");
        }

        var originalMask = await ReadBladePowerModeMaskAsync(blade, cancellationToken);
        var original = ToMaxFanMode(originalMask);
        if (original == mode)
        {
            return original;
        }

        try
        {
            await WriteBladeMaxFanModeAsync(blade, mode, originalMask, cancellationToken);
            var actualMask = await ReadBladePowerModeMaskAsync(blade, cancellationToken);
            var actual = ToMaxFanMode(actualMask);
            var expectedMask = mode == BladeMaxFanMode.Enabled
                ? (byte)(originalMask | BladeMaxFanProtocol.MaxFanBit)
                : (byte)(originalMask & ~BladeMaxFanProtocol.MaxFanBit);
            if (actualMask != expectedMask)
            {
                throw new InvalidOperationException($"Max Fan 读回不一致：写入 {mode}，读回 {actual}，其它电源位也发生了变化。");
            }

            return actual;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            var restored = await TryRestoreBladeMaxFanModeAsync(blade, original, originalMask);
            var message = "Max Fan 设置失败：" + exception.Message + " " +
                (restored ? "原值已恢复。" : "原值恢复失败；请立即在 Synapse 中检查风扇。");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<byte> SetBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte brightness,
        CancellationToken cancellationToken = default)
    {
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("Blade 键盘控制通道不可用。");
        EnsureValidated(_validatedBladeBrightnessPath, blade.Descriptor.Id, "请先成功读取 Blade 键盘亮度。");

        var original = await ReadBladeBrightnessAsync(blade, cancellationToken);
        if (original == brightness)
        {
            return original;
        }

        try
        {
            await WriteBladeBrightnessAsync(blade, brightness, cancellationToken);
            var actual = await ReadBladeBrightnessAsync(blade, cancellationToken);
            if (actual != brightness)
            {
                throw new InvalidOperationException($"亮度读回不一致：写入 {brightness}，读回 {actual}。");
            }
            return actual;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            var restored = await TryRestoreBladeBrightnessAsync(blade, original);
            var message = "亮度设置失败：" + exception.Message + " " +
                (restored ? "原值已恢复。" : "原值恢复失败；请立即在 Synapse 中检查键盘亮度。");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    private async Task<byte> ReadBladeBrightnessAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(device, "keyboard-brightness.get", cancellationToken);
        EnsureDataSize(response, 2, "键盘亮度");
        return response[RazerFeatureReport.ArgumentsOffset + 1];
    }

    private Task WriteBladeBrightnessAsync(
        ReadyDevice device,
        byte brightness,
        CancellationToken cancellationToken) =>
        QueryCapabilityAsync(
            device, "keyboard-brightness.set", new byte[] { 0x01, brightness }, cancellationToken);

    private async Task<bool> TryRestoreBladeBrightnessAsync(ReadyDevice device, byte original)
    {
        try
        {
            await WriteBladeBrightnessAsync(device, original, CancellationToken.None);
            return await ReadBladeBrightnessAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public async ValueTask<BladeLogoMode> SetBladeLogoModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLogoMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is not (BladeLogoMode.Off or BladeLogoMode.Static))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode), "生产控制只允许已验证的 Logo Off 或 Static。");
        }

        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("Blade Logo 控制通道不可用。");
        EnsureValidated(_validatedBladeLogoPath, blade.Descriptor.Id, "请先成功读取 Blade Logo 状态。");

        var original = await ReadBladeLogoStateAsync(blade, cancellationToken);
        if (original.CombinedMode == mode)
        {
            return mode;
        }

        try
        {
            await WriteBladeLogoStateAsync(blade, mode, cancellationToken);
            var actual = await ReadBladeLogoStateAsync(blade, cancellationToken);
            if (actual.CombinedMode != mode ||
                (mode == BladeLogoMode.Static && actual.PoweredMode != BladeLogoMode.Static))
            {
                throw new InvalidOperationException(
                    $"Logo 读回不一致：写入 {mode}，读回 {actual.CombinedMode}/{actual.PoweredMode}。");
            }

            return actual.CombinedMode;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            var restored = await TryRestoreBladeLogoStateAsync(blade, original);
            var message = "Logo 设置失败：" + exception.Message + " " +
                (restored ? "原状态已恢复。" : "原状态恢复失败；请立即在 Synapse 中检查 Logo。");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    private async Task<BladeLogoState> ReadBladeLogoStateAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var powerResponse = await QueryCapabilityAsync(device, "logo-power.get", cancellationToken);
        var modeResponse = await QueryCapabilityAsync(device, "logo-mode.get", cancellationToken);
        var power = BladeLogoProtocol.ParsePower(
            powerResponse, CreateCapabilityRequest(device, "logo-power.get"));
        var poweredMode = BladeLogoProtocol.ParseMode(
            modeResponse, CreateCapabilityRequest(device, "logo-mode.get"));
        return new BladeLogoState(power, poweredMode);
    }

    private async Task WriteBladeLogoStateAsync(
        ReadyDevice device,
        BladeLogoMode combinedMode,
        CancellationToken cancellationToken)
    {
        if (combinedMode == BladeLogoMode.Static)
        {
            await QueryBuiltRequestAsync(
                device, "logo-mode.set", BladeLogoProtocol.CreateSetModeRequest(BladeLogoMode.Static), cancellationToken);
        }
        else if (combinedMode != BladeLogoMode.Off)
        {
            throw new ArgumentOutOfRangeException(nameof(combinedMode));
        }

        var powerRequest = BladeLogoProtocol.CreateSetPowerRequest(combinedMode != BladeLogoMode.Off);
        await QueryBuiltRequestAsync(device, "logo-power.set", powerRequest, cancellationToken);
    }

    private async Task<bool> TryRestoreBladeLogoStateAsync(
        ReadyDevice device,
        BladeLogoState original)
    {
        try
        {
            await QueryBuiltRequestAsync(
                device,
                "logo-mode.set",
                BladeLogoProtocol.CreateSetModeRequest(original.PoweredMode),
                CancellationToken.None);
            await QueryBuiltRequestAsync(
                device,
                "logo-power.set",
                BladeLogoProtocol.CreateSetPowerRequest(original.Powered),
                CancellationToken.None);
            return await ReadBladeLogoStateAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<BladeThermalState> ReadBladeThermalStateAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        BladeThermalState? state = null;
        foreach (var zone in new byte[] { 0x01, 0x02 })
        {
            var response = await QueryCapabilityAsync(
                device,
                "thermal-state.get",
                new byte[] { 0x00, zone, 0x00, 0x00 },
                cancellationToken);
            EnsureDataSize(response, 4, "Blade 性能模式");
            if (response[RazerFeatureReport.ArgumentsOffset + 1] != zone)
            {
                throw new InvalidOperationException($"Blade 返回了错误的风扇分区 {response[RazerFeatureReport.ArgumentsOffset + 1]}。");
            }

            var current = new BladeThermalState(
                ParseBladePerformanceMode(response[RazerFeatureReport.ArgumentsOffset + 2]),
                ParseBladeFanMode(response[RazerFeatureReport.ArgumentsOffset + 3]));
            if (state is not null && state != current)
            {
                throw new InvalidOperationException($"Blade 两个风扇分区状态不一致：{state} / {current}。");
            }
            state = current;
        }

        return state ?? throw new InvalidOperationException("Blade 未返回性能模式。");
    }

    private async Task<int> ReadBladeFanTargetRpmAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        int? rpm = null;
        foreach (var zone in new[] { BladeFanProtocol.ZoneCpu, BladeFanProtocol.ZoneGpu })
        {
            var request = BladeFanProtocol.CreateGetTargetRequest(zone);
            var response = await QueryBuiltRequestAsync(
                device, "fan-target.get", request, cancellationToken);
            var current = BladeFanProtocol.ParseTarget(
                response, zone, CreateConfiguredRequest(device, "fan-target.get", request));
            if (rpm is not null && rpm != current)
            {
                throw new InvalidOperationException($"Blade 两个风扇分区设定不一致：{rpm} / {current} RPM。");
            }
            rpm = current;
        }

        return rpm ?? throw new InvalidOperationException("Blade 未返回风扇转速。");
    }

    private async Task<int> ReadBladeCurrentFanRpmAsync(
        ReadyDevice device,
        byte fanId,
        CancellationToken cancellationToken)
    {
        var request = BladeThermalProtocol.CreateGetCurrentSpeedRequest(fanId);
        var response = await QueryBuiltRequestAsync(
            device, "current-fan-rpm.get", request, cancellationToken);
        return BladeThermalProtocol.ParseCurrentSpeedRpm(
            response,
            fanId,
            CreateConfiguredRequest(device, "current-fan-rpm.get", request));
    }

    private async Task<byte> ReadBladeAdvancedFanModeAsync(
        ReadyDevice device,
        byte fanId,
        CancellationToken cancellationToken)
    {
        var request = BladeThermalProtocol.CreateGetAdvancedFanModeRequest(fanId);
        var response = await QueryBuiltRequestAsync(
            device, "advanced-fan-mode.get", request, cancellationToken);
        return BladeThermalProtocol.ParseAdvancedFanMode(
            response,
            fanId,
            CreateConfiguredRequest(device, "advanced-fan-mode.get", request));
    }

    private async Task<BladeBoostState> ReadBladeBoostStateAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var cpuResponse = await QueryCapabilityAsync(
            device,
            "boost.get",
            new byte[] { 0x00, BladeBoostProtocol.CpuCluster, 0x00 },
            cancellationToken);
        var gpuResponse = await QueryCapabilityAsync(
            device,
            "boost.get",
            new byte[] { 0x00, BladeBoostProtocol.GpuCluster, 0x00 },
            cancellationToken);

        return new BladeBoostState(
            BladeBoostProtocol.ParseCpu(cpuResponse),
            BladeBoostProtocol.ParseGpu(gpuResponse));
    }

    private async Task<BladeBoostState> SetBladeBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte cluster,
        byte value,
        CancellationToken cancellationToken)
    {
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("Blade Boost 控制通道不可用。");
        EnsureValidated(_validatedBladeBoostPath, blade.Descriptor.Id, "请先成功读取 Blade CPU/GPU Boost。");

        var thermal = await ReadBladeThermalStateAsync(blade, cancellationToken);
        if (thermal.PerformanceMode != BladePerformanceMode.Custom)
        {
            throw new InvalidOperationException("只有 Custom 性能模式允许修改 CPU/GPU Boost。");
        }

        var original = await ReadBladeBoostStateAsync(blade, cancellationToken);
        if ((cluster == BladeBoostProtocol.CpuCluster && (byte)original.Cpu == value) ||
            (cluster == BladeBoostProtocol.GpuCluster && (byte)original.Gpu == value))
        {
            return original;
        }

        try
        {
            await WriteBladeBoostModeAsync(blade, cluster, value, cancellationToken);
            var actual = await ReadBladeBoostStateAsync(blade, cancellationToken);
            var matches = cluster == BladeBoostProtocol.CpuCluster
                ? (byte)actual.Cpu == value && actual.Gpu == original.Gpu
                : (byte)actual.Gpu == value && actual.Cpu == original.Cpu;
            if (!matches)
            {
                throw new InvalidOperationException(
                    $"Boost 读回不一致：写入 cluster {cluster} / value {value}，" +
                    $"读回 CPU {actual.Cpu} / GPU {actual.Gpu}。");
            }

            return actual;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            var restored = await TryRestoreBladeBoostStateAsync(blade, original);
            var message = "Boost 设置失败：" + exception.Message + " " +
                (restored
                    ? "原值已恢复。"
                    : "原值恢复失败；请立即在 Synapse 中检查 CPU/GPU Boost。");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    private Task WriteBladeBoostModeAsync(
        ReadyDevice device,
        byte cluster,
        byte value,
        CancellationToken cancellationToken) =>
        QueryCapabilityAsync(
            device, "boost.set", new byte[] { 0x00, cluster, value }, cancellationToken);

    private async Task<bool> TryRestoreBladeBoostStateAsync(
        ReadyDevice device,
        BladeBoostState state)
    {
        try
        {
            var thermal = await ReadBladeThermalStateAsync(device, CancellationToken.None);
            if (thermal.PerformanceMode != BladePerformanceMode.Custom)
            {
                return false;
            }

            await WriteBladeBoostModeAsync(
                device, BladeBoostProtocol.CpuCluster, (byte)state.Cpu, CancellationToken.None);
            await WriteBladeBoostModeAsync(
                device, BladeBoostProtocol.GpuCluster, (byte)state.Gpu, CancellationToken.None);
            return await ReadBladeBoostStateAsync(device, CancellationToken.None) == state;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<int> ReadBladeChargeLimitAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(
            device, "charge-limit.get", cancellationToken);
        EnsureDataSize(response, 1, "Blade 充电上限");
        return DecodeBladeChargeLimit(response[RazerFeatureReport.ArgumentsOffset]);
    }

    private async Task<byte> ReadBladePowerModeMaskAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(
            device, "max-fan.get", cancellationToken);
        return BladeMaxFanProtocol.ParsePowerModeMask(
            response, CreateCapabilityRequest(device, "max-fan.get"));
    }

    private Task WriteBladeMaxFanModeAsync(
        ReadyDevice device,
        BladeMaxFanMode mode,
        byte existingPowerModeMask,
        CancellationToken cancellationToken)
    {
        var request = BladeMaxFanProtocol.CreateSetRequest(mode, existingPowerModeMask);
        return QueryBuiltRequestAsync(device, "max-fan.set", request, cancellationToken);
    }

    private async Task<bool> TryRestoreBladeMaxFanModeAsync(
        ReadyDevice device,
        BladeMaxFanMode mode,
        byte originalMask)
    {
        try
        {
            await WriteBladeMaxFanModeAsync(device, mode, originalMask, CancellationToken.None);
            return await ReadBladePowerModeMaskAsync(device, CancellationToken.None) == originalMask;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static BladeMaxFanMode ToMaxFanMode(byte powerModeMask) =>
        (powerModeMask & BladeMaxFanProtocol.MaxFanBit) != 0
            ? BladeMaxFanMode.Enabled
            : BladeMaxFanMode.Disabled;

    private Task WriteBladeThermalZoneAsync(
        ReadyDevice device,
        byte zone,
        BladePerformanceMode performanceMode,
        BladeFanMode fanMode,
        CancellationToken cancellationToken) =>
        QueryCapabilityAsync(
            device,
            "thermal-state.set",
            new byte[] { 0x01, zone, (byte)performanceMode, (byte)fanMode },
            cancellationToken);

    private async Task<bool> TryRestoreBladeThermalStateAsync(
        ReadyDevice device,
        BladeThermalState state)
    {
        try
        {
            await WriteBladeThermalZoneAsync(
                device, 0x01, state.PerformanceMode, state.FanMode, CancellationToken.None);
            await WriteBladeThermalZoneAsync(
                device, 0x02, state.PerformanceMode, state.FanMode, CancellationToken.None);
            var restored = await ReadBladeThermalStateAsync(device, CancellationToken.None);
            return restored == state;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private Task WriteBladeChargeLimitAsync(
        ReadyDevice device,
        byte raw,
        CancellationToken cancellationToken) =>
        QueryCapabilityAsync(device, "charge-limit.set", new byte[] { raw }, cancellationToken);

    private async Task<bool> TryRestoreBladeChargeLimitAsync(
        ReadyDevice device,
        int percent)
    {
        try
        {
            await WriteBladeChargeLimitAsync(
                device, EncodeBladeChargeLimit(percent), CancellationToken.None);
            var restored = await ReadBladeChargeLimitAsync(device, CancellationToken.None);
            return restored == percent;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static BladePerformanceMode ParseBladePerformanceMode(byte value) => value switch
    {
        0x00 => BladePerformanceMode.Balanced,
        0x02 => BladePerformanceMode.Performance,
        0x04 => BladePerformanceMode.Custom,
        0x05 => BladePerformanceMode.Silent,
        0x06 => BladePerformanceMode.Battery,
        0x07 => BladePerformanceMode.Hyperboost,
        _ => throw new InvalidOperationException($"Blade 返回了未知性能模式 0x{value:X2}。"),
    };

    private static BladeFanMode ParseBladeFanMode(byte value) => value switch
    {
        0x00 => BladeFanMode.Automatic,
        0x01 => BladeFanMode.Manual,
        _ => throw new InvalidOperationException($"Blade 返回了未知风扇模式 0x{value:X2}。"),
    };

    private static int DecodeBladeChargeLimit(byte value) => value switch
    {
        0xB2 => 50,
        0xB7 => 55,
        0xBC => 60,
        0xC1 => 65,
        0xC6 => 70,
        0xCB => 75,
        0xD0 => 80,
        0x50 => 100,
        _ => throw new InvalidOperationException($"Blade 返回了未知充电上限代码 0x{value:X2}。"),
    };

    private static byte EncodeBladeChargeLimit(int percent) => percent switch
    {
        50 => 0xB2,
        55 => 0xB7,
        60 => 0xBC,
        65 => 0xC1,
        70 => 0xC6,
        75 => 0xCB,
        80 => 0xD0,
        100 => 0x50,
        _ => throw new ArgumentOutOfRangeException(
            nameof(percent), "充电上限只允许 50、55、60、65、70、75、80 或 100%。"),
    };

    private sealed record BladeThermalState(
        BladePerformanceMode PerformanceMode,
        BladeFanMode FanMode);

    private sealed record BladeBoostState(
        BladeCpuBoostMode Cpu,
        BladeGpuBoostMode Gpu);

    public async ValueTask<int> SetViperPollingRateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int hertz,
        CancellationToken cancellationToken = default)
    {
        var viper = FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("Viper 控制通道不可用。");
        EnsureValidated(_validatedViperPollingPath, viper.Descriptor.Id, "请先成功读取鼠标轮询率。");

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
                throw new InvalidOperationException($"轮询率读回不一致：写入 {hertz} Hz，读回 {actual} Hz。");
            }
            return actual;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            var restored = await TryRestoreViperPollingRateAsync(viper, original);
            var message = "轮询率设置失败：" + exception.Message + " " +
                (restored ? "原值已恢复。" : "原值恢复失败；请立即在 Synapse 中检查轮询率。");
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
            throw new ArgumentOutOfRangeException(nameof(x), "DPI 必须在 100 到 30000 之间，并且是 50 的倍数。");
        }
        if (y is < 100 or > 30000 || y % 50 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "DPI 必须在 100 到 30000 之间，并且是 50 的倍数。");
        }

        var viper = FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("Viper 控制通道不可用。");
        EnsureValidated(_validatedViperDpiPath, viper.Descriptor.Id, "请先成功读取鼠标 DPI。");
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
                    $"DPI 读回不一致：写入 {x} × {y}，读回 {actual.X} × {actual.Y}。");
            }
            return actual;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            var restored = await TryRestoreViperDpiAsync(viper, original);
            var message = "DPI 设置失败：" + exception.Message + " " +
                (restored ? "原值已恢复。" : "原值恢复失败；请立即在 Synapse 中检查 DPI。");
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
        var viper = FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("Viper 控制通道不可用。");
        EnsureValidated(_validatedViperDpiStagesPath, viper.Descriptor.Id, "请先成功读取鼠标 DPI 档位。");

        var original = await ReadViperDpiStagesAsync(viper, cancellationToken);
        if (AreDpiStagesEqual(original, requested))
        {
            return ToTelemetry(original);
        }

        try
        {
            await QueryBuiltRequestAsync(
                viper,
                "dpi-stages.set",
                ViperProduct184Protocol.CreateSetDpiStagesRequest(requested),
                cancellationToken);
            var actual = await ReadViperDpiStagesAsync(viper, cancellationToken);
            EnsureDpiStagesEqual(requested, actual, "鼠标 DPI 档位");
            return ToTelemetry(actual);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            var restored = await TryRestoreViperDpiStagesAsync(viper, original);
            var message = "DPI 档位设置失败：" + exception.Message + " " +
                (restored ? "原值已恢复。" : "原值恢复失败；请立即在 Synapse 中检查 DPI 档位。");
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
            throw new ArgumentOutOfRangeException(nameof(seconds), "休眠时间必须是 1 到 15 分钟的整数分钟。");
        }

        var viper = FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("Viper 控制通道不可用。");
        EnsureValidated(_validatedViperIdlePath, viper.Descriptor.Id, "请先成功读取鼠标休眠时间。");
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
                throw new InvalidOperationException($"休眠时间读回不一致：写入 {seconds} 秒，读回 {actual} 秒。");
            }
            return actual;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            var restored = await TryRestoreViperIdleSecondsAsync(viper, original);
            var message = "休眠时间设置失败：" + exception.Message + " " +
                (restored ? "原值已恢复。" : "原值恢复失败；请立即在 Synapse 中检查休眠时间。");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    private async Task<int> ReadViperPollingRateAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(device, "polling-rate.get", cancellationToken);
        return ViperProduct184Protocol.ParsePollingRateHertz(
            response, CreateCapabilityRequest(device, "polling-rate.get"));
    }

    private Task WriteViperPollingRateAsync(
        ReadyDevice device,
        int hertz,
        CancellationToken cancellationToken) =>
        QueryBuiltRequestAsync(
            device,
            "polling-rate.set",
            ViperProduct184Protocol.CreateSetPollingRateRequest(hertz),
            cancellationToken);

    private async Task<bool> TryRestoreViperPollingRateAsync(ReadyDevice device, int original)
    {
        try
        {
            await WriteViperPollingRateAsync(device, original, CancellationToken.None);
            return await ReadViperPollingRateAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<(int X, int Y)> ReadViperDpiAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(device, "current-dpi.get", cancellationToken);
        return ViperProduct184Protocol.ParseDpi(
            response, CreateCapabilityRequest(device, "current-dpi.get"));
    }

    private Task WriteViperDpiAsync(
        ReadyDevice device,
        int x,
        int y,
        CancellationToken cancellationToken) =>
        QueryBuiltRequestAsync(
            device,
            "current-dpi.set",
            ViperProduct184Protocol.CreateSetDpiRequest(x, y),
            cancellationToken);

    private async Task<bool> TryRestoreViperDpiAsync(
        ReadyDevice device,
        (int X, int Y) original)
    {
        try
        {
            await WriteViperDpiAsync(device, original.X, original.Y, CancellationToken.None);
            return await ReadViperDpiAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<int> ReadViperIdleSecondsAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(device, "idle-timeout.get", cancellationToken);
        return ViperProduct184Protocol.ParseIdleSeconds(
            response, CreateCapabilityRequest(device, "idle-timeout.get"));
    }

    private Task WriteViperIdleSecondsAsync(
        ReadyDevice device,
        int seconds,
        CancellationToken cancellationToken) =>
        QueryBuiltRequestAsync(
            device,
            "idle-timeout.set",
            ViperProduct184Protocol.CreateSetIdleTimeoutRequest(seconds),
            cancellationToken);

    private async Task<bool> TryRestoreViperIdleSecondsAsync(ReadyDevice device, int original)
    {
        try
        {
            await WriteViperIdleSecondsAsync(device, original, CancellationToken.None);
            return await ReadViperIdleSecondsAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<ViperDpiStagesState> ReadViperDpiStagesAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(device, "dpi-stages.get", cancellationToken);
        return ViperDpiStagesProtocol.Parse(
            response, CreateCapabilityRequest(device, "dpi-stages.get"));
    }

    private async Task<bool> TryRestoreViperDpiStagesAsync(
        ReadyDevice device,
        ViperDpiStagesState original)
    {
        try
        {
            await QueryBuiltRequestAsync(
                device,
                "dpi-stages.set",
                ViperProduct184Protocol.CreateSetDpiStagesRequest(original),
                CancellationToken.None);
            var restored = await ReadViperDpiStagesAsync(device, CancellationToken.None);
            EnsureDpiStagesEqual(original, restored, "DPI 档位恢复");
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
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
                $"{operation}读回不一致：写入 {ViperDpiStagesProtocol.Format(expected)}，" +
                $"读回 {ViperDpiStagesProtocol.Format(actual)}。");
        }
    }

    private static bool AreDpiStagesEqual(
        ViperDpiStagesState left,
        ViperDpiStagesState right) =>
        left.ActiveStage == right.ActiveStage &&
        left.Stages.Count == right.Stages.Count &&
        left.Stages.SequenceEqual(right.Stages);

    private Task<byte[]> QueryCapabilityAsync(
        ReadyDevice device,
        string capabilityId,
        CancellationToken cancellationToken) =>
        QueryCapabilityAsync(
            device,
            capabilityId,
            device.Manifest.GetRequiredCapability(capabilityId).Arguments,
            cancellationToken);

    private Task<byte[]> QueryCapabilityAsync(
        ReadyDevice device,
        string capabilityId,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken,
        byte? dataSize = null)
    {
        var request = device.Manifest.GetRequiredCapability(capabilityId);
        var actualDataSize = dataSize ?? request.MaximumDataSize;
        if (actualDataSize > request.MaximumDataSize || arguments.Length > actualDataSize)
        {
            throw new InvalidOperationException(
                $"capability '{capabilityId}' 的动态参数超过 manifest 上限。");
        }

        return _transport.QueryAsync(
            device.Descriptor.Id,
            request.TransactionId,
            actualDataSize,
            request.CommandClass,
            request.CommandId,
            arguments,
            request.Wait,
            cancellationToken,
            request.AllowRemainingPacketsMismatch);
    }

    private Task<byte[]> QueryBuiltRequestAsync(
        ReadyDevice device,
        string capabilityId,
        byte[] builtRequest,
        CancellationToken cancellationToken)
    {
        var request = CreateConfiguredRequest(device, capabilityId, builtRequest);

        return QueryCapabilityAsync(
            device,
            capabilityId,
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            cancellationToken,
            request[6]);
    }

    private static byte[] CreateCapabilityRequest(ReadyDevice device, string capabilityId) =>
        device.Manifest.GetRequiredCapability(capabilityId).CreateRequest();

    private static byte[] CreateConfiguredRequest(
        ReadyDevice device,
        string capabilityId,
        byte[] builtRequest)
    {
        if (builtRequest.Length != RazerFeatureReport.Length)
        {
            throw new InvalidOperationException("强类型 builder 返回了无效的 feature report 长度。");
        }

        return device.Manifest.GetRequiredCapability(capabilityId).CreateRequest(
            builtRequest.AsSpan(RazerFeatureReport.ArgumentsOffset, builtRequest[6]),
            builtRequest[6]);
    }

    private ReadyDevice? FindReadyDevice(
        IReadOnlyList<DeviceDescriptor> devices,
        string protocolFamily)
    {
        foreach (var device in devices)
        {
            if (device.Access != DeviceAccessState.Available ||
                device.Capability != DeviceCapabilityState.PendingValidation)
            {
                continue;
            }

            var manifest = _registry.Find(device.VendorId, device.ProductId);
            if (manifest?.ProtocolFamily == protocolFamily)
            {
                return new ReadyDevice(device, manifest);
            }
        }

        return null;
    }

    private static void EnsureDataSize(byte[] response, byte minimum, string query)
    {
        if (response[6] < minimum)
        {
            throw new InvalidOperationException($"{query}响应长度不足：{response[6]} < {minimum}。");
        }
    }

    private static void EnsureValidated(string? validatedPath, string currentPath, string message)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(validatedPath, currentPath))
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record ReadyDevice(
        DeviceDescriptor Descriptor,
        RazerDeviceManifest Manifest);

    private sealed record BladeLogoState(
        bool Powered,
        BladeLogoMode PoweredMode)
    {
        internal BladeLogoMode CombinedMode => BladeLogoProtocol.Combine(Powered, PoweredMode);
    }

}
