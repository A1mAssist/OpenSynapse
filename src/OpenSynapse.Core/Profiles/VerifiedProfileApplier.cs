using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Sensors;

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
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default,
        bool applyKeyboardBrightness = true,
        bool coolingPadConnected = true,
        bool padlessHyperBoostAccepted = false,
        IInternalDisplayController? displayController = null,
        Func<BladeProfileSettings, CancellationToken, Task<int>>? applyBladeTuningAsync = null)
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
                deviceErrors.Add($"{label} was not read back successfully; profile application was skipped.");
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
                deviceErrors.Add($"{label}: {exception.Message}");
            }
        }

        async ValueTask<int> SetTimeToDimAsync(int value, CancellationToken token) =>
            (await reader.SetBladeTimeToDimAsync(devices, value, token)).Seconds;

        async ValueTask<bool> SetGpuCardDetectionAsync(bool value, CancellationToken token) =>
            (await reader.SetBladeGpuCardDetectionAsync(devices, value, token)).Enabled;

        var blade = devices.FirstOrDefault(device =>
            device.ProtocolFamily is "blade-710" or "blade-671" &&
            device.Access == DeviceAccessState.Available);
        if (blade is not null)
        {
            var bladeErrors = new List<string>();
            var profile = ProfileResolver.Resolve(document, blade, powerSource);
            var effectivePerformanceMode = telemetry.BladePerformanceMode;
            var performanceValueInvalid = false;
            if (profile.Blade.PerformanceMode is byte rawPerformanceMode)
            {
                if (!Enum.IsDefined(typeof(BladePerformanceMode), rawPerformanceMode))
                {
                    performanceValueInvalid = true;
                    effectivePerformanceMode = null;
                    bladeErrors.Add($"Blade performance mode value is invalid: {rawPerformanceMode}.");
                }
                else if (telemetry.BladePerformanceMode is not BladePerformanceMode current)
                {
                    bladeErrors.Add("Blade performance mode was not read back successfully; profile application was skipped.");
                }
                else
                {
                    var requested = (BladePerformanceMode)rawPerformanceMode;
                    if (requested == BladePerformanceMode.Hyperboost)
                    {
                        var modeSupported = BladeModelCapabilities.IsHyperBoostAvailable(blade.ProductId);
                        var padReady = coolingPadConnected || padlessHyperBoostAccepted;
                        if (!modeSupported || !padReady)
                        {
                            requested = BladeModelCapabilities.PublishesPerformanceMode(blade.ProductId, BladePerformanceMode.Performance)
                                ? BladePerformanceMode.Performance : BladePerformanceMode.Balanced;
                            bladeErrors.Add(!BladeModelCapabilities.SupportsHyperBoost(blade.ProductId)
                                ? $"HyperBoost is not supported on this model; {requested} mode was selected instead."
                                : $"HyperBoost requires the Razer Laptop Cooling Pad and a recorded disclaimer acceptance; {requested} mode was selected instead.");
                        }
                    }
                    effectivePerformanceMode = current;
                    try
                    {
                        if (!BladeModelCapabilities.PublishesPerformanceMode(blade.ProductId, requested))
                            throw new InvalidOperationException($"Performance mode {requested} is not admitted for Blade PID {blade.ProductId:X4}.");
                        if (requested != current)
                        {
                            effectivePerformanceMode = await reader.SetBladePerformanceModeAsync(
                                devices, requested, cancellationToken, coolingPadConnected);
                            applied++;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (IsExpectedHardwareException(exception))
                    {
                        bladeErrors.Add($"Blade performance mode: {exception.Message}");
                    }
                }
            }

            var hasCustomOnlySetting = profile.Blade.CpuBoostMode is not null ||
                profile.Blade.GpuBoostMode is not null || profile.Blade.MaxFanMode is not null;
            if (hasCustomOnlySetting && effectivePerformanceMode is null && !performanceValueInvalid)
            {
                bladeErrors.Add("Blade performance mode was not read back successfully; CPU/GPU Boost and Max Fan profile settings were skipped.");
            }
            else if (bladeErrors.Count == 0 && effectivePerformanceMode == BladePerformanceMode.Custom)
            {
                if (profile.Blade.CpuBoostMode is byte rawCpuBoost)
                {
                    if (!Enum.IsDefined(typeof(BladeCpuBoostMode), rawCpuBoost))
                    {
                        bladeErrors.Add($"Blade CPU Boost value is invalid: {rawCpuBoost}.");
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

                if (bladeErrors.Count == 0 && profile.Blade.GpuBoostMode is byte rawGpuBoost)
                {
                    if (!Enum.IsDefined(typeof(BladeGpuBoostMode), rawGpuBoost))
                    {
                        bladeErrors.Add($"Blade GPU Boost value is invalid: {rawGpuBoost}.");
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

                if (bladeErrors.Count == 0 && profile.Blade.MaxFanMode is byte rawMaxFan &&
                BladeModelCapabilities.SupportsMaxFan(blade.ProductId))
                {
                    if (!Enum.IsDefined(typeof(BladeMaxFanMode), rawMaxFan))
                    {
                        bladeErrors.Add($"Blade Max Fan value is invalid: {rawMaxFan}.");
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

            if (bladeErrors.Count == 0 && profile.Blade.StartupAnimationEnabled is bool startupAnimation)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade startup animation",
                    startupAnimation,
                    telemetry.BladeStartupAnimationEnabled,
                    (value, token) => reader.SetBladeStartupAnimationAsync(devices, value, token));
            }
            if (bladeErrors.Count == 0 && profile.Blade.AutoSleepSeconds is int autoSleepSeconds)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade auto sleep",
                    autoSleepSeconds > 0,
                    telemetry.BladeAutoSleepEnabled,
                    (value, token) => reader.SetBladeAutoSleepAsync(devices, value, token));
            }
            if (bladeErrors.Count == 0 && profile.Blade.TimeToDimSeconds is int timeToDimSeconds)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade time to dim",
                    timeToDimSeconds,
                    telemetry.BladeTimeToDim?.Seconds,
                    SetTimeToDimAsync);
            }
            if (bladeErrors.Count == 0 && profile.Blade.IndicatorLedEnabled is bool indicatorLed)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade indicator LED",
                    indicatorLed,
                    telemetry.BladeIndicatorLedEnabled,
                    (value, token) => reader.SetBladeIndicatorLedAsync(devices, value, token));
            }
            if (bladeErrors.Count == 0 && profile.Blade.GpuCardDetectionEnabled is bool gpuCardDetection)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade GPU card detection",
                    gpuCardDetection,
                    telemetry.BladeGpuCardDetection?.Enabled,
                    SetGpuCardDetectionAsync);
            }
            if (bladeErrors.Count == 0 && profile.Blade.NativeDisplayModeEnabled is bool nativeFullHd)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade native display mode",
                    nativeFullHd ? BladeNativeDisplayMode.Fhd : BladeNativeDisplayMode.Uhd,
                    telemetry.BladeNativeDisplayMode,
                    (value, token) => reader.SetBladeNativeDisplayModeAsync(devices, value, token));
            }
            if (bladeErrors.Count == 0 && profile.Blade.LocalDimmingEnabled is bool localDimming)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade local dimming",
                    localDimming,
                    telemetry.BladeLocalDimmingEnabled,
                    (value, token) => reader.SetBladeLocalDimmingAsync(devices, value, token));
            }
            if (bladeErrors.Count == 0 && profile.Blade.CpuPowerBoostMode is byte rawCpuPowerBoost)
            {
                if (rawCpuPowerBoost > 1)
                {
                    bladeErrors.Add($"Blade CPU power boost value is invalid: {rawCpuPowerBoost}.");
                }
                else
                {
                    await ApplyValueAsync(
                        bladeErrors,
                        "Blade CPU power boost",
                        rawCpuPowerBoost != 0,
                        telemetry.BladeCpuPowerBoostEnabled,
                        (value, token) => reader.SetBladeCpuPowerBoostAsync(devices, value, token));
                }
            }

            if (bladeErrors.Count == 0 && applyBladeTuningAsync is not null &&
                (profile.Blade.GpuCoreClockOffsetMhz is not null ||
                 profile.Blade.GpuMemoryClockOffsetMhz is not null ||
                 profile.Blade.GpuCoreClockLimitMhz is not null ||
                 profile.Blade.CpuCurveOffset is not null ||
                 profile.Blade.CpuPowerLimitWatts is not null))
            {
                try
                {
                    applied += await applyBladeTuningAsync(profile.Blade, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExpectedHardwareException(exception))
                {
                    bladeErrors.Add($"Blade tuning: {exception.Message}");
                }
            }

            if (bladeErrors.Count == 0 && displayController is not null &&
                profile.Blade.DisplayBrightnessPercent is int displayBrightness)
            {
                try
                {
                    var current = displayController.Read().BrightnessLevel;
                    if (current is not double currentLevel)
                    {
                        bladeErrors.Add("Blade display brightness was not read back successfully; profile application was skipped.");
                    }
                    else if (Math.Abs(currentLevel * 100 - displayBrightness) >= 0.5)
                    {
                        await displayController.SetBrightnessAsync(
                            displayBrightness / 100d,
                            cancellationToken);
                        applied++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExpectedHardwareException(exception))
                {
                    bladeErrors.Add($"Blade display brightness: {exception.Message}");
                }
            }

            if (applyKeyboardBrightness && bladeErrors.Count == 0 && profile.Blade.KeyboardBrightness is byte brightness)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade keyboard brightness",
                    brightness,
                    telemetry.BladeKeyboardBrightness,
                    (value, token) => reader.SetBladeKeyboardBrightnessAsync(devices, value, token));
            }
            if (bladeErrors.Count == 0 && profile.Blade.ChargeLimitPercent is int chargeLimit)
            {
                await ApplyValueAsync(
                    bladeErrors,
                    "Blade charge limit",
                    chargeLimit,
                    telemetry.BladeChargeLimitPercent,
                    (value, token) => reader.SetBladeChargeLimitAsync(devices, value, token));
            }
            if (bladeErrors.Count == 0 && profile.Blade.LogoMode is byte rawLogoMode)
            {
                var requested = (BladeLogoMode)rawLogoMode;
                if (!Enum.IsDefined(typeof(BladeLogoMode), requested))
                {
                    bladeErrors.Add($"Blade Logo mode value is invalid or unverified: {rawLogoMode}.");
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
            var profile = ProfileResolver.Resolve(document, viper, powerSource);
            if (profile.Viper.DpiStages is ViperDpiStagesProfile dpiStages)
            {
                if (telemetry.ViperDpiStages is not ViperDpiStagesTelemetry current)
                {
                    viperErrors.Add("Viper DPI stages were not read back successfully; profile application was skipped.");
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
                            viperErrors.Add($"Viper DPI stages: {exception.Message}");
                        }
                    }
                }
            }

            if (viperErrors.Count == 0 &&
                profile.Viper.DpiStages is not { Stages.Count: > 0 } &&
                (profile.Viper.DpiX is not null || profile.Viper.DpiY is not null))
            {
                if (telemetry.ViperDpiX is not int currentX || telemetry.ViperDpiY is not int currentY)
                {
                    viperErrors.Add("Viper DPI was not read back successfully; profile application was skipped.");
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
                    viperErrors.Add($"Viper DPI: {exception.Message}");
                        }
                    }
                }
            }

            if (viperErrors.Count == 0 && profile.Viper.PollingRateHertz is int pollingRate)
            {
                await ApplyValueAsync(
                    viperErrors,
                    "Viper polling rate",
                    pollingRate,
                    telemetry.ViperPollingRateHertz,
                    (value, token) => reader.SetViperPollingRateAsync(devices, value, token));
            }

            if (viperErrors.Count == 0 && profile.Viper.IdleSeconds is int idleSeconds)
            {
                await ApplyValueAsync(
                    viperErrors,
                    "Viper idle timeout",
                    idleSeconds,
                    telemetry.ViperIdleSeconds,
                    (value, token) => reader.SetViperIdleSecondsAsync(devices, value, token));
            }

            if (viperErrors.Count == 0 &&
                profile.Viper.LowBatteryThresholdPercent is int lowBatteryPercent)
            {
                if (telemetry.ViperLowBatteryThresholdRaw is not byte currentRaw)
                {
                    viperErrors.Add(
                        "Viper low battery threshold was not read back successfully; profile application was skipped.");
                }
                else
                {
                    var currentPercent = currentRaw * 100 / 255;
                    if (currentPercent != lowBatteryPercent)
                    {
                        try
                        {
                            var actual = await reader.SetViperLowBatteryThresholdAsync(
                                devices, lowBatteryPercent, cancellationToken);
                            applied++;
                            if (actual != lowBatteryPercent)
                            {
                                viperErrors.Add(
                                    $"Viper low battery threshold read back as {actual}%.");
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception) when (IsExpectedHardwareException(exception))
                        {
                            viperErrors.Add($"Viper low battery threshold: {exception.Message}");
                        }
                    }
                }
            }

            if (viperErrors.Count == 0 && profile.Viper.BatteryChemistry is byte chemistry)
            {
                try
                {
                    await reader.SetViperBatteryChemistryAsync(
                        devices,
                        chemistry,
                        cancellationToken);
                    applied++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExpectedHardwareException(exception))
                {
                    viperErrors.Add($"Viper battery type: {exception.Message}");
                }
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
