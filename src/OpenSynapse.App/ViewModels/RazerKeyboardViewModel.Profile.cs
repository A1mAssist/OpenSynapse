using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using Windows.UI;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerKeyboardViewModel
{
    private int _appliedBrightness = 100;
    private int _appliedEffectIndex;
    private bool _hasAppliedEffect;
    private Color _appliedPrimaryColor = Color.FromArgb(255, 0, 255, 0);
    private Color _appliedSecondaryColor = Color.FromArgb(255, 255, 0, 0);

    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        if (_getProfile?.Invoke() is not { } document)
        {
            return;
        }

        var descriptor = new DeviceDescriptor(
            ProfileDeviceKey,
            _connection.Name,
            0x1532,
            _connection.ProductId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            0,
            0,
            0,
            "keyboard",
            DeviceCategory.Keyboard);
        var resolved = ProfileResolver.ResolveWithDeviceKey(
            document, descriptor, powerSource, ProfileDeviceKey);
        var keyboard = resolved.Keyboard;

        if (_hasLighting)
        {
            await ApplyPersistedChromaAsync(resolved.Chroma, cancellationToken);
        }

        if (keyboard.GamingModeEnabled is bool gameMode && _hasGameMode && gameMode != GameModeEnabled)
        {
            GameModeEnabled = gameMode;
            if (await WriteProfileSettingAsync(
                    "keyboard.game-mode",
                    (service, token) => service.SetGameModeAsync(gameMode, token),
                    cancellationToken))
            {
                _appliedGameMode = gameMode;
            }
            else
            {
                GameModeEnabled = _appliedGameMode;
            }
        }

        if (keyboard.PollingRateHertz is int rate && _hasPolling)
        {
            var index = Array.IndexOf(PollingRates, rate);
            if (index >= 0 && index != SelectedPollingIndex)
            {
                SelectedPollingIndex = index;
                if (await WriteProfileSettingAsync(
                        "keyboard.polling-rate",
                        (service, token) => service.SetPollingRateAsync(rate, token),
                        cancellationToken))
                {
                    _appliedPollingIndex = index;
                }
                else
                {
                    SelectedPollingIndex = _appliedPollingIndex;
                }
            }
        }

        if (keyboard.ButtonMappings is { Count: > 0 } mappings)
        {
            await ApplyPersistedMappingsAsync(mappings, cancellationToken);
        }
    }

    [NotProfileScoped]
    private async Task ApplyPersistedChromaAsync(
        ChromaProfileSettings settings,
        CancellationToken cancellationToken)
    {
        if (_hasBrightness &&
            settings.BrightnessPercent is int brightness &&
            _appliedBrightness != brightness &&
            await WriteChromaProfileSettingAsync(
                "chroma.brightness",
                (service, token) => service.SetBrightnessAsync((byte)brightness, token),
                cancellationToken))
        {
            _appliedBrightness = brightness;
            Brightness = brightness;
        }

        if (string.IsNullOrWhiteSpace(settings.Effect))
        {
            return;
        }

        if (!Enum.TryParse<RazerChromaEffect>(settings.Effect, ignoreCase: true, out var effect) ||
            !Effects.Contains(effect))
        {
            _diagnostic?.Invoke($"profile chroma.effect: unsupported effect {settings.Effect}");
            return;
        }

        if (!TryBuildColors(effect, settings, out var colors))
        {
            _diagnostic?.Invoke("profile chroma.effect: saved colors are invalid");
            return;
        }

        var index = Array.IndexOf(Effects, effect);
        var appliedColors = EffectColors(
            Effects[_appliedEffectIndex], _appliedPrimaryColor, _appliedSecondaryColor);
        if (_hasAppliedEffect && index == _appliedEffectIndex && appliedColors.SequenceEqual(colors))
        {
            return;
        }

        if (!await WriteChromaProfileSettingAsync(
                "chroma.effect",
                (service, token) => service.SetEffectAsync(effect, colors, token),
                cancellationToken))
        {
            return;
        }

        SelectedLightingEffectIndex = index;
        _appliedEffectIndex = index;
        _hasAppliedEffect = true;
        if (TryParseColor(settings.PrimaryColor, out var primary))
        {
            PrimaryColor = primary;
            _appliedPrimaryColor = primary;
        }
        if (TryParseColor(settings.SecondaryColor, out var secondary))
        {
            SecondaryColor = secondary;
            _appliedSecondaryColor = secondary;
        }
    }

    [ProfileSetting("chroma.brightness", DeviceCategory.Keyboard)]
    public async Task ApplyBrightnessAsync(CancellationToken cancellationToken = default)
    {
        var desired = (int)Math.Round(Brightness);
        var previous = _appliedBrightness;
        if (await RunChromaWriteAsync(
                (service, token) => service.SetBrightnessAsync((byte)desired, token),
                cancellationToken,
                device =>
                {
                    device.Chroma ??= new ChromaProfileSettings();
                    device.Chroma.BrightnessPercent = desired;
                },
                (service, token) => service.SetBrightnessAsync((byte)previous, token),
                () => Brightness = previous))
        {
            _appliedBrightness = desired;
        }
    }

    [ProfileSetting("chroma.effect", DeviceCategory.Keyboard)]
    public async Task ApplyLightingAsync(CancellationToken cancellationToken = default)
    {
        var index = SelectedLightingEffectIndex;
        var effect = Effects[index];
        var primary = PrimaryColor;
        var secondary = SecondaryColor;
        var previousIndex = _appliedEffectIndex;
        var previousPrimary = _appliedPrimaryColor;
        var previousSecondary = _appliedSecondaryColor;

        if (await RunChromaWriteAsync(
                (service, token) => service.SetEffectAsync(
                    effect, EffectColors(effect, primary, secondary), token),
                cancellationToken,
                device =>
                {
                    device.Chroma ??= new ChromaProfileSettings();
                    device.Chroma.Effect = effect.ToString();
                    device.Chroma.PrimaryColor = ToHex(primary);
                    device.Chroma.SecondaryColor = ToHex(secondary);
                },
                (service, token) => service.SetEffectAsync(
                    Effects[previousIndex],
                    EffectColors(Effects[previousIndex], previousPrimary, previousSecondary),
                    token),
                () =>
                {
                    SelectedLightingEffectIndex = previousIndex;
                    PrimaryColor = previousPrimary;
                    SecondaryColor = previousSecondary;
                }))
        {
            _appliedEffectIndex = index;
            _hasAppliedEffect = true;
            _appliedPrimaryColor = primary;
            _appliedSecondaryColor = secondary;
        }
    }

    private static byte[] EffectColors(
        RazerChromaEffect effect,
        Color primary,
        Color secondary) =>
        effect switch
        {
            RazerChromaEffect.Static => [primary.R, primary.G, primary.B],
            RazerChromaEffect.Breathing =>
                [primary.R, primary.G, primary.B, secondary.R, secondary.G, secondary.B],
            _ => [],
        };

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private async Task<bool> RunChromaWriteAsync(
        Func<RazerChromaStandardService, CancellationToken, Task> write,
        CancellationToken cancellationToken,
        Action<DeviceProfileSettings>? mutateProfile = null,
        Func<RazerChromaStandardService, CancellationToken, Task>? rollbackHardware = null,
        Action? rollbackUi = null)
    {
        if (!BeginOperation()) return false;
        try
        {
            await write(OpenChromaService(), cancellationToken);
            if (mutateProfile is not null)
            {
                var saved = false;
                try
                {
                    saved = await PeripheralProfileStore.SaveAsync(
                        _getProfile?.Invoke(),
                        _saveProfile,
                        ProfileDeviceKey,
                        mutateProfile,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    _diagnostic?.Invoke($"profile chroma: {exception.Message}");
                }

                if (!saved)
                {
                    rollbackUi?.Invoke();
                    if (rollbackHardware is not null)
                    {
                        try
                        {
                            await rollbackHardware(OpenChromaService(), CancellationToken.None);
                        }
                        catch
                        {
                            // The save failure is the error the user must see.
                        }
                    }

                    throw new InvalidOperationException(
                        AppStrings.Text("RazerHidSettingProfileSaveFailed"));
                }
            }

            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
        return false;
    }

    private static bool TryBuildColors(
        RazerChromaEffect effect,
        ChromaProfileSettings settings,
        out byte[] colors)
    {
        colors = [];
        if (effect is not (RazerChromaEffect.Static or RazerChromaEffect.Breathing))
        {
            return true;
        }

        if (!TryParseColor(settings.PrimaryColor, out var primary))
        {
            return false;
        }
        if (effect == RazerChromaEffect.Static)
        {
            colors = [primary.R, primary.G, primary.B];
            return true;
        }

        if (!TryParseColor(settings.SecondaryColor, out var secondary))
        {
            return false;
        }

        colors =
            [primary.R, primary.G, primary.B, secondary.R, secondary.G, secondary.B];
        return true;
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        var hex = value?.Trim().TrimStart('#');
        if (hex is not { Length: 6 } ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            return false;
        }

        color = Color.FromArgb(
            255, (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        return true;
    }

    private async Task<bool> WriteChromaProfileSettingAsync(
        string key,
        Func<RazerChromaStandardService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        try
        {
            await write(OpenChromaService(), cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnostic?.Invoke($"profile {key}: {exception.Message}");
            return false;
        }
    }

    [NotProfileScoped]
    private async Task ApplyPersistedMappingsAsync(
        IReadOnlyList<ButtonMappingProfile> mappings,
        CancellationToken cancellationToken)
    {
        var transport = new RazerKeyboardMappingTransport(OpenService());
        foreach (var mapping in mappings)
        {
            if (mapping.FunctionId is not { } functionId)
            {
                continue;
            }

            var mode = mapping.Mode ?? 0;
            var key = $"keyboard.mapping.{mode}.{mapping.ButtonId}";
            try
            {
                var data = mapping.FunctionData?.ToArray() ?? [];
                var current = await transport.GetAssignmentAsync(mapping.ButtonId, mode, cancellationToken);
                if (current.FunctionId == functionId && current.Data.SequenceEqual(data))
                {
                    continue;
                }

                await transport.SetAssignmentAsync(
                    mapping.ButtonId, mode, functionId, data, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"profile {key}: {exception.Message}");
            }
        }
    }

    private async Task<bool> WriteProfileSettingAsync(
        string key,
        Func<RazerKeyboardService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        try
        {
            await write(OpenService(), cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"profile {key}: {exception.Message}");
            return false;
        }
    }

    private async Task SaveKeyboardSettingAsync(
        Action<KeyboardProfileSettings> edit,
        Action<KeyboardProfileSettings> revert,
        Action? revertUi,
        Func<CancellationToken, Task>? revertHardware,
        CancellationToken cancellationToken)
    {
        if (_getProfile is null || _saveProfile is null)
        {
            return;
        }

        var document = _getProfile();
        var created = !document.Devices.TryGetValue(ProfileDeviceKey, out var device);
        device ??= new DeviceProfileSettings();
        edit(device.Keyboard);
        if (created)
        {
            document.Devices[ProfileDeviceKey] = device;
        }

        var saved = false;
        try
        {
            saved = await _saveProfile(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            saved = false;
        }
        if (saved)
        {
            return;
        }

        revert(device.Keyboard);
        if (created)
        {
            document.Devices.Remove(ProfileDeviceKey);
        }
        revertUi?.Invoke();
        if (revertHardware is not null)
        {
            try
            {
                await revertHardware(CancellationToken.None);
            }
            catch
            {
                // The save failure is the error the user must see.
            }
        }
        ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
    }
}
