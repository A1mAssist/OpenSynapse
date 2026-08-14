using System.ComponentModel;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Profiles;

public sealed record ProfileApplyResult(
    int AppliedCount,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// Applies only settings whose current-device readback is already verified.
/// The profile itself remains the source of requested values; this class never
/// treats a successful method call as confirmation without the reader's readback.
/// </summary>
public sealed class VerifiedProfileApplier
{
    public async Task<ProfileApplyResult> ApplyAsync(
        ProfileDocument document,
        IReadOnlyList<DeviceDescriptor> devices,
        RazerDeviceTelemetry telemetry,
        IRazerDeviceTelemetryReader reader,
        bool? isPluggedIn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(reader);

        var errors = new List<string>();
        var applied = 0;

        async Task ApplyValueAsync<T>(
            ICollection<string> deviceErrors,
            string label,
            T requested,
            T? current,
            Func<T, CancellationToken, ValueTask<T>> setter)
            where T : struct
        {
            if (current is not T currentValue)
            {
                deviceErrors.Add($"{label}未成功读回，已跳过配置应用。");
                return;
            }
            if (EqualityComparer<T>.Default.Equals(requested, currentValue))
            {
                return;
            }

            try
            {
                await setter(requested, cancellationToken);
                applied++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                deviceErrors.Add($"{label}：{exception.Message}");
            }
        }

        var blade = devices.FirstOrDefault(device =>
            device.ProtocolFamily == "blade-710" && device.Access == DeviceAccessState.Available);
        if (blade is not null)
        {
            var bladeErrors = new List<string>();
            var profile = ProfileResolver.Resolve(document, blade, isPluggedIn);
            var effectivePerformanceMode = telemetry.BladePerformanceMode;
            var performanceValueInvalid = false;
            if (profile.Blade.PerformanceMode is byte rawPerformanceMode)
            {
                if (!Enum.IsDefined(typeof(BladePerformanceMode), rawPerformanceMode))
                {
                    performanceValueInvalid = true;
                    effectivePerformanceMode = null;
                    bladeErrors.Add($"Blade 性能模式值无效：{rawPerformanceMode}。");
                }
                else if (telemetry.BladePerformanceMode is not BladePerformanceMode current)
                {
                    bladeErrors.Add("Blade 性能模式未成功读回，已跳过配置应用。");
                }
                else
                {
                    var requested = (BladePerformanceMode)rawPerformanceMode;
                    effectivePerformanceMode = current;
                    try
                    {
                        if (requested != current)
                        {
                            effectivePerformanceMode = await reader.SetBladePerformanceModeAsync(
                                devices, requested, cancellationToken);
                            applied++;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (IsExpectedHardwareException(exception))
                    {
                        bladeErrors.Add($"Blade 性能模式：{exception.Message}");
                    }
                }
            }

            var hasCustomOnlySetting = profile.Blade.CpuBoostMode is not null ||
                profile.Blade.GpuBoostMode is not null || profile.Blade.MaxFanMode is not null;
            if (hasCustomOnlySetting && effectivePerformanceMode is null && !performanceValueInvalid)
            {
                bladeErrors.Add("Blade 性能模式未成功读回，已跳过 CPU/GPU Boost 和 Max Fan 配置应用。");
            }
            else if (effectivePerformanceMode == BladePerformanceMode.Custom)
            {
                if (profile.Blade.CpuBoostMode is byte rawCpuBoost)
                {
                    if (!Enum.IsDefined(typeof(BladeCpuBoostMode), rawCpuBoost))
                    {
                        bladeErrors.Add($"Blade CPU Boost 值无效：{rawCpuBoost}。");
                    }
                    else
                    {
                        await ApplyValueAsync(
                            bladeErrors,
                            "Blade CPU Boost",
                            (BladeCpuBoostMode)rawCpuBoost,
                            telemetry.BladeCpuBoostMode,
                            (value, token) => reader.SetBladeCpuBoostModeAsync(devices, value, token));
                    }
                }

                if (profile.Blade.GpuBoostMode is byte rawGpuBoost)
                {
                    if (!Enum.IsDefined(typeof(BladeGpuBoostMode), rawGpuBoost))
                    {
                        bladeErrors.Add($"Blade GPU Boost 值无效：{rawGpuBoost}。");
                    }
                    else
                    {
                        await ApplyValueAsync(
                            bladeErrors,
                            "Blade GPU Boost",
                            (BladeGpuBoostMode)rawGpuBoost,
                            telemetry.BladeGpuBoostMode,
                            (value, token) => reader.SetBladeGpuBoostModeAsync(devices, value, token));
                    }
                }

                if (profile.Blade.MaxFanMode is byte rawMaxFan)
                {
                    if (!Enum.IsDefined(typeof(BladeMaxFanMode), rawMaxFan))
                    {
                        bladeErrors.Add($"Blade Max Fan 值无效：{rawMaxFan}。");
                    }
                    else
                    {
                        await ApplyValueAsync(
                            bladeErrors,
                            "Blade Max Fan",
                            (BladeMaxFanMode)rawMaxFan,
                            telemetry.BladeMaxFanMode,
                            (value, token) => reader.SetBladeMaxFanModeAsync(devices, value, token));
                    }
                }
            }

            if (profile.Blade.KeyboardBrightness is byte brightness)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade 键盘亮度",
                    brightness,
                    telemetry.BladeKeyboardBrightness,
                    (value, token) => reader.SetBladeKeyboardBrightnessAsync(devices, value, token));
            }
            if (profile.Blade.ChargeLimitPercent is int chargeLimit)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade 充电上限",
                    chargeLimit,
                    telemetry.BladeChargeLimitPercent,
                    (value, token) => reader.SetBladeChargeLimitAsync(devices, value, token));
            }
            if (profile.Blade.LogoMode is byte rawLogoMode)
            {
                var requested = (BladeLogoMode)rawLogoMode;
                if (!Enum.IsDefined(typeof(BladeLogoMode), requested) ||
                    requested is not (BladeLogoMode.Off or BladeLogoMode.Static))
                {
                    bladeErrors.Add($"Blade Logo 模式值无效或未经验证：{rawLogoMode}。");
                }
                else
                {
                    await ApplyValueAsync(
                        bladeErrors,
                        "Blade Logo",
                        requested,
                        telemetry.BladeLogoMode,
                        (value, token) => reader.SetBladeLogoModeAsync(devices, value, token));
                }
            }

            errors.AddRange(bladeErrors);
        }

        var viper = devices.FirstOrDefault(device =>
            device.ProtocolFamily == "viper-184" && device.Access == DeviceAccessState.Available);
        if (viper is not null)
        {
            var viperErrors = new List<string>();
            var profile = ProfileResolver.Resolve(document, viper, isPluggedIn);
            if (profile.Viper.DpiStages is ViperDpiStagesProfile dpiStages)
            {
                if (telemetry.ViperDpiStages is not ViperDpiStagesTelemetry current)
                {
                    viperErrors.Add("Viper DPI 档位未成功读回，已跳过配置应用。");
                }
                else
                {
                    var requested = ToTelemetry(dpiStages);
                    if (!DpiStagesEqual(requested, current))
                    {
                        try
                        {
                            await reader.SetViperDpiStagesAsync(devices, requested, cancellationToken);
                            applied++;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception) when (IsExpectedHardwareException(exception))
                        {
                            viperErrors.Add($"Viper DPI 档位：{exception.Message}");
                        }
                    }
                }
            }

            if (viperErrors.Count == 0 &&
                (profile.Viper.DpiX is not null || profile.Viper.DpiY is not null))
            {
                if (telemetry.ViperDpiX is not int currentX || telemetry.ViperDpiY is not int currentY)
                {
                    viperErrors.Add("Viper DPI 未成功读回，已跳过配置应用。");
                }
                else
                {
                    var requestedX = profile.Viper.DpiX ?? currentX;
                    var requestedY = profile.Viper.DpiY ?? currentY;
                    if (requestedX != currentX || requestedY != currentY)
                    {
                        try
                        {
                            await reader.SetViperDpiAsync(devices, requestedX, requestedY, cancellationToken);
                            applied++;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception) when (IsExpectedHardwareException(exception))
                        {
                            viperErrors.Add($"Viper DPI：{exception.Message}");
                        }
                    }
                }
            }

            if (viperErrors.Count == 0 && profile.Viper.PollingRateHertz is int pollingRate)
            {
                await ApplyValueAsync(
                    viperErrors,
                    "Viper 轮询率",
                    pollingRate,
                    telemetry.ViperPollingRateHertz,
                    (value, token) => reader.SetViperPollingRateAsync(devices, value, token));
            }

            if (viperErrors.Count == 0 && profile.Viper.IdleSeconds is int idleSeconds)
            {
                await ApplyValueAsync(
                    viperErrors,
                    "Viper 休眠时间",
                    idleSeconds,
                    telemetry.ViperIdleSeconds,
                    (value, token) => reader.SetViperIdleSecondsAsync(devices, value, token));
            }

            errors.AddRange(viperErrors);
        }

        return new ProfileApplyResult(applied, errors);
    }

    private static ViperDpiStagesTelemetry ToTelemetry(ViperDpiStagesProfile profile) =>
        new(
            profile.ActiveStage,
            profile.Stages.Select(stage =>
                new ViperDpiStageTelemetry(stage.Number, stage.X, stage.Y)).ToArray());

    private static bool DpiStagesEqual(
        ViperDpiStagesTelemetry left,
        ViperDpiStagesTelemetry right) =>
        left.ActiveStage == right.ActiveStage &&
        left.Stages is not null && right.Stages is not null &&
        left.Stages.SequenceEqual(right.Stages);

    private static bool IsExpectedHardwareException(Exception exception) =>
        exception is Win32Exception or IOException or UnauthorizedAccessException or
        InvalidOperationException or ArgumentException;
}
