using System.Globalization;
using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerCoolingPadViewModel
{
    private static string ProfileKey => $"1532:{RazerCoolingPadProtocol.ProductId:X4}";

    /// <summary>
    /// Saves one cooling-pad entry. A failed save restores the whole entry so the
    /// in-memory profile cannot disagree with the hardware rollback.
    /// </summary>
    private async Task<bool> SaveProfileAsync(
        Action<CoolingPadProfileSettings> mutate,
        CancellationToken cancellationToken)
    {
        var document = _getProfile?.Invoke();
        var save = _saveProfile;
        if (document is null || save is null)
        {
            return true;
        }

        var created = !document.Devices.TryGetValue(ProfileKey, out var device);
        device ??= new DeviceProfileSettings();
        device.CoolingPad ??= new CoolingPadProfileSettings();
        var previous = JsonSerializer.SerializeToNode(device);
        mutate(device.CoolingPad);
        if (created)
        {
            document.Devices[ProfileKey] = device;
        }

        try
        {
            if (await save(cancellationToken))
            {
                return true;
            }
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
                document.Devices.Remove(ProfileKey);
                return;
            }

            document.Devices[ProfileKey] =
                previous?.Deserialize<DeviceProfileSettings>() ?? new DeviceProfileSettings();
        }
    }

    private static Dictionary<string, string> CreateLightingParameters(
        int brightness,
        byte flags,
        byte rate,
        IReadOnlyList<byte> colors) => new(StringComparer.Ordinal)
    {
        ["brightness"] = brightness.ToString(CultureInfo.InvariantCulture),
        ["flags"] = flags.ToString(CultureInfo.InvariantCulture),
        ["rate"] = rate.ToString(CultureInfo.InvariantCulture),
        ["colors"] = string.Join(",", colors),
    };

    private static BladeFanCurveProfile CreateCurveProfile(int sourceIndex) => new()
    {
        TemperatureMode = sourceIndex switch
        {
            1 => BladeFanCurveTemperatureMode.Gpu,
            2 => BladeFanCurveTemperatureMode.Both,
            _ => BladeFanCurveTemperatureMode.Cpu,
        },
    };

    private async Task<RazerCoolingPadCoolingMode?> ReadCoolingModeOrDefaultAsync(
        CancellationToken cancellationToken)
    {
        if (_service is null)
        {
            return null;
        }

        try
        {
            return await _service.ReadCoolingModeAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return null;
        }
    }

    private async Task RestoreLightingAsync(RazerCoolingPadLightingState? state)
    {
        if (_service is null || state is null)
        {
            return;
        }

        try
        {
            await _service.SetLightingAsync(
                state.BrightnessPercent,
                state.EffectId,
                state.Flags,
                state.Rate,
                state.Colors.ToArray(),
                CancellationToken.None);
            ApplyLightingState(state);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // The save failure is what the caller must see; rollback is best effort.
        }
    }

    /// <summary>Best-effort restore used after a profile save failure.</summary>
    private async Task RestoreCoolingModeAsync(
        RazerCoolingPadCoolingMode? state,
        CancellationToken cancellationToken)
    {
        if (_service is null || state is not { } previous)
        {
            return;
        }

        try
        {
            switch (previous.ModeId)
            {
                case 0:
                    await _service.SetFixedModeAsync(RazerCoolingPadFixedMode.Low, cancellationToken);
                    break;
                case 1:
                    await _service.SetFixedModeAsync(RazerCoolingPadFixedMode.Mid, cancellationToken);
                    break;
                case 2:
                    await _service.SetFixedModeAsync(RazerCoolingPadFixedMode.High, cancellationToken);
                    break;
                case 3:
                    await _service.SetSmartModeAsync(RazerCoolingPadSmartMode.Quiet, cancellationToken);
                    break;
                case 4:
                    await _service.SetSmartModeAsync(RazerCoolingPadSmartMode.Balanced, cancellationToken);
                    break;
                case 5:
                    await _service.SetSmartModeAsync(RazerCoolingPadSmartMode.Performance, cancellationToken);
                    break;
                case 6:
                    await _service.SetFixedModeAsync(RazerCoolingPadFixedMode.Off, cancellationToken);
                    break;
                default:
                    if (previous.Rpm is >= RazerCoolingPadProtocol.MinimumRpm and
                        <= RazerCoolingPadProtocol.MaximumRpm &&
                        previous.Rpm % RazerCoolingPadProtocol.RpmPerUnit == 0)
                    {
                        await _service.SetSpeedRpmAsync(previous.Rpm, cancellationToken);
                    }

                    break;
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // The save failure is what the caller must see; rollback is best effort.
        }
    }
}
