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

        var blade = devices.FirstOrDefault(device =>
            device.ProtocolFamily == "blade-710" && device.Access == DeviceAccessState.Available);
        if (blade is not null)
        {
            var profile = ProfileResolver.Resolve(document, blade, isPluggedIn);
            if (profile.Blade.KeyboardBrightness is byte brightness)
            {
                if (telemetry.BladeKeyboardBrightness is not byte current)
                {
                    errors.Add("Blade 键盘亮度未成功读回，已跳过配置应用。");
                }
                else if (brightness != current)
                {
                    try
                    {
                        await reader.SetBladeKeyboardBrightnessAsync(devices, brightness, cancellationToken);
                        applied++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (IsExpectedHardwareException(exception))
                    {
                        errors.Add($"Blade 键盘亮度：{exception.Message}");
                    }
                }
            }

            if (errors.Count == 0 && profile.Blade.PerformanceMode is byte rawMode)
            {
                if (!Enum.IsDefined(typeof(BladePerformanceMode), rawMode))
                {
                    errors.Add($"Blade 性能模式值无效：{rawMode}。");
                }
                else if (telemetry.BladePerformanceMode is not BladePerformanceMode current)
                {
                    errors.Add("Blade 性能模式未成功读回，已跳过配置应用。");
                }
                else if (current != (BladePerformanceMode)rawMode)
                {
                    try
                    {
                        await reader.SetBladePerformanceModeAsync(
                            devices, (BladePerformanceMode)rawMode, cancellationToken);
                        applied++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (IsExpectedHardwareException(exception))
                    {
                        errors.Add($"Blade 性能模式：{exception.Message}");
                    }
                }
            }

            if (errors.Count == 0 && profile.Blade.ChargeLimitPercent is int chargeLimit)
            {
                if (telemetry.BladeChargeLimitPercent is not int current)
                {
                    errors.Add("Blade 充电上限未成功读回，已跳过配置应用。");
                }
                else if (chargeLimit != current)
                {
                    try
                    {
                        await reader.SetBladeChargeLimitAsync(devices, chargeLimit, cancellationToken);
                        applied++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (IsExpectedHardwareException(exception))
                    {
                        errors.Add($"Blade 充电上限：{exception.Message}");
                    }
                }
            }

            if (errors.Count == 0 && profile.Blade.MaxFanMode is byte rawMaxFan)
            {
                if (!Enum.IsDefined(typeof(BladeMaxFanMode), rawMaxFan))
                {
                    errors.Add($"Blade Max Fan 值无效：{rawMaxFan}。");
                }
                else if (telemetry.BladeMaxFanMode is not BladeMaxFanMode current)
                {
                    errors.Add("Blade Max Fan 未成功读回，已跳过配置应用。");
                }
                else if (current != (BladeMaxFanMode)rawMaxFan)
                {
                    try
                    {
                        await reader.SetBladeMaxFanModeAsync(
                            devices, (BladeMaxFanMode)rawMaxFan, cancellationToken);
                        applied++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (IsExpectedHardwareException(exception))
                    {
                        errors.Add($"Blade Max Fan：{exception.Message}");
                    }
                }
            }
        }

        var viper = devices.FirstOrDefault(device =>
            device.ProtocolFamily == "viper-184" && device.Access == DeviceAccessState.Available);
        if (viper is not null && errors.Count == 0)
        {
            var profile = ProfileResolver.Resolve(document, viper, isPluggedIn);
            if (profile.Viper.DpiX is not null || profile.Viper.DpiY is not null)
            {
                if (telemetry.ViperDpiX is not int currentX || telemetry.ViperDpiY is not int currentY)
                {
                    errors.Add("Viper DPI 未成功读回，已跳过配置应用。");
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
                            errors.Add($"Viper DPI：{exception.Message}");
                        }
                    }
                }
            }

            if (errors.Count == 0 && profile.Viper.PollingRateHertz is int pollingRate)
            {
                if (telemetry.ViperPollingRateHertz is not int current)
                {
                    errors.Add("Viper 轮询率未成功读回，已跳过配置应用。");
                }
                else if (pollingRate != current)
                {
                    try
                    {
                        await reader.SetViperPollingRateAsync(devices, pollingRate, cancellationToken);
                        applied++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (IsExpectedHardwareException(exception))
                    {
                        errors.Add($"Viper 轮询率：{exception.Message}");
                    }
                }
            }

            if (errors.Count == 0 && profile.Viper.IdleSeconds is int idleSeconds)
            {
                if (telemetry.ViperIdleSeconds is not int current)
                {
                    errors.Add("Viper 休眠时间未成功读回，已跳过配置应用。");
                }
                else if (idleSeconds != current)
                {
                    try
                    {
                        await reader.SetViperIdleSecondsAsync(devices, idleSeconds, cancellationToken);
                        applied++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (IsExpectedHardwareException(exception))
                    {
                        errors.Add($"Viper 休眠时间：{exception.Message}");
                    }
                }
            }
        }

        return new ProfileApplyResult(applied, errors);
    }

    private static bool IsExpectedHardwareException(Exception exception) =>
        exception is Win32Exception or IOException or UnauthorizedAccessException or
        InvalidOperationException or ArgumentOutOfRangeException;
}
