using System.Globalization;
using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Profile persistence for one BLE mouse. The value-level helpers keep
/// <see cref="RazerBleDeviceViewModel"/> inside the source-size budget.
/// </summary>
public sealed partial class RazerBleDeviceViewModel
{
    private static void Register(string key) =>
        ProfileSettingCatalog.Register(new ProfileSettingDescriptor(
            key, DeviceCategory.Mouse, PowerScoped: false));

    /// <summary>
    /// Re-applies the saved profile to the open BLE session. Used on profile and
    /// power changes; the service keeps its lifetime from <see cref="LoadAsync"/>.
    /// </summary>
    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        if (_service is null || _getProfile?.Invoke() is not { } document)
        {
            return;
        }

        var descriptor = new DeviceDescriptor(
            ProfileDeviceKey,
            _device.Name,
            0x1532,
            0,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "ble",
            DeviceCategory.Mouse);
        var applier = new RazerBleProfileApplier(
            id => string.Equals(id, ProfileDeviceKey, StringComparison.OrdinalIgnoreCase) ? _device : null,
            (_, _) => Task.FromResult<RazerBleDeviceService?>(_service));
        await applier.ApplyAsync(
            new DeviceProfileApplyContext(
                document,
                descriptor,
                powerSource,
                ProfileResolver.Resolve(document, descriptor, powerSource)),
            cancellationToken);
    }

    private async Task<bool> RunWriteAsync<T>(
        Func<RazerBleDeviceService, Task<T>> write,
        Action<T> apply,
        Func<T, CancellationToken, Task<bool>>? persist = null,
        Func<CancellationToken, Task<T>>? readPrevious = null,
        Func<T, CancellationToken, Task>? restorePrevious = null,
        CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return false;
        T? previous = default;
        var hasPrevious = false;
        var appliedHardware = false;
        try
        {
            if (persist is not null && readPrevious is not null)
            {
                try
                {
                    previous = await readPrevious(cancellationToken);
                    hasPrevious = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // A device that refuses the read still gets the write; only the rollback is lost.
                }
            }

            var result = await write(_service);
            apply(result);
            appliedHardware = true;
            if (persist is not null && !await persist(result, cancellationToken))
            {
                await RollbackAsync();
                ErrorText = AppStrings.Text("ViperSettingAppliedProfileSaveFailed");
                OperationStatusText = string.Empty;
                return false;
            }

            OperationStatusText = AppStrings.Text("BlackSharkSaved");
            ErrorText = string.Empty;
            return true;
        }
        catch (OperationCanceledException)
        {
            if (appliedHardware) await RollbackAsync();
            OperationStatusText = string.Empty;
            return false;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (appliedHardware) await RollbackAsync();
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
            return false;
        }
        finally
        {
            EndOperation();
        }

        async Task RollbackAsync()
        {
            if (!hasPrevious || restorePrevious is null) return;
            try
            {
                await restorePrevious(previous!, CancellationToken.None);
                apply(previous!);
            }
            catch
            {
                // The original write or save error is the one the user must see.
            }
        }
    }

    private Task<bool> PersistMouseAsync(
        Action<MouseProfileSettings> mutate,
        CancellationToken cancellationToken) =>
        PersistDeviceAsync(device => mutate(device.Viper), cancellationToken);

    private Task<bool> PersistLightingAsync(
        LightingProfile lighting,
        CancellationToken cancellationToken) =>
        PersistDeviceAsync(device => device.Lighting = lighting, cancellationToken);

    /// <summary>
    /// Mutates the BLE device entry and saves it. A failed save restores the whole
    /// entry so the in-memory profile never disagrees with the hardware rollback.
    /// </summary>
    private async Task<bool> PersistDeviceAsync(
        Action<DeviceProfileSettings> mutate,
        CancellationToken cancellationToken)
    {
        if (_getProfile?.Invoke() is not { } document || _saveProfile is null) return true;

        var created = !document.Devices.TryGetValue(ProfileDeviceKey, out var device);
        device ??= new DeviceProfileSettings();
        var previous = JsonSerializer.SerializeToNode(device);
        mutate(device);
        if (created)
        {
            document.Devices[ProfileDeviceKey] = device;
        }

        try
        {
            if (await _saveProfile(cancellationToken)) return true;
        }
        catch
        {
            Restore();
            throw;
        }

        Restore();
        return false;

        void Restore()
        {
            if (created)
            {
                document.Devices.Remove(ProfileDeviceKey);
                return;
            }

            document.Devices[ProfileDeviceKey] =
                previous?.Deserialize<DeviceProfileSettings>() ?? new DeviceProfileSettings();
        }
    }

    private LightingProfile BuildLightingProfile() => new()
    {
        Effect = SelectedLightingEffect.ToString(),
        Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["brightness"] = Brightness.ToString(CultureInfo.InvariantCulture),
            ["direction"] = SelectedLightingDirectionIndex.ToString(CultureInfo.InvariantCulture),
            ["color"] = $"{LightingColor.R:X2}{LightingColor.G:X2}{LightingColor.B:X2}",
        },
    };

    private async Task RestoreLightingAsync(RazerBleLightingState state, CancellationToken cancellationToken)
    {
        var effect = (RazerBleLightingEffect)state.EffectId;
        await _service!.SetBrightnessAsync(state.BrightnessPercent, cancellationToken);
        var flags = effect switch
        {
            RazerBleLightingEffect.Breathing => (byte)1,
            RazerBleLightingEffect.Wave => state.Direction == 2 ? (byte)2 : (byte)1,
            RazerBleLightingEffect.Wheel => Math.Max((byte)1, state.Direction),
            _ => (byte)0,
        };
        await _service.SetEffectAsync(
            effect,
            flags,
            state.Rate,
            state.Colors as byte[] ?? state.Colors.ToArray(),
            cancellationToken);
    }

    private static void ApplyDpiStagesToProfile(MouseProfileSettings mouse, RazerBleDpiStages state)
    {
        mouse.DpiStages = new ViperDpiStagesProfile
        {
            ActiveStage = state.ActiveStage,
            Stages = state.Stages
                .Select(stage => new ViperDpiStageProfile
                {
                    Number = stage.Index,
                    X = stage.X,
                    Y = stage.Y,
                })
                .ToList(),
        };
        var active = state.Stages.FirstOrDefault(stage => stage.Index == state.ActiveStage);
        if (active is not null)
        {
            mouse.DpiX = active.X;
            mouse.DpiY = active.Y;
        }
    }

    private static void ApplyScrollToProfile(MouseProfileSettings mouse, RazerBleScrollState state)
    {
        if (state.Mode is { } mode and <= 2)
        {
            mouse.ScrollMode = ((MouseScrollMode)mode).ToString();
        }
        if (state.SmartReel is { } smartReel)
        {
            mouse.SmartReelEnabled = smartReel;
        }
        if (state.SmartReelThreshold is { } threshold)
        {
            mouse.SmartReelThreshold = threshold;
        }
        if (state.Acceleration is { } acceleration)
        {
            mouse.ScrollAccelerationEnabled = acceleration;
        }
    }

    private static void ApplyPowerToProfile(MouseProfileSettings mouse, RazerBlePowerState state)
    {
        if (state.LowBatteryPercent is { } lowBattery)
        {
            mouse.LowBatteryThresholdPercent = lowBattery;
        }
        if (state.SleepMinutes is { } sleepMinutes)
        {
            mouse.SleepTimeoutSeconds = sleepMinutes * 60;
        }
        if (state.AutoSleep is { } autoSleep)
        {
            mouse.AutoSleepSeconds = autoSleep
                ? (state.SleepMinutes ?? 5) * 60
                : 0;
        }
    }
}
