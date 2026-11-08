using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Applies a headset profile to the Audio AW BLE, BlackShark and Kraken paths.
/// Each family keeps its own session resolver; the resolver owns the lifetime and
/// this applier never disposes a service it did not open.
/// </summary>
public sealed class HeadsetPeripheralProfileApplier : IDeviceProfileApplier
{
    private static readonly DeviceCategory[] SupportedCategories = [DeviceCategory.Headset];
    private readonly Func<DeviceDescriptor, CancellationToken, Task<RazerAwBleHeadsetService?>>? _resolveAwBle;
    private readonly Func<DeviceDescriptor, CancellationToken, Task<BlackSharkHeadsetService?>>? _resolveBlackShark;
    private readonly Func<DeviceDescriptor, CancellationToken,
        Task<(RazerHidSpecialLightingService Service, RazerHidSpecialLightingConnection Connection)?>>? _resolveKraken;

    public HeadsetPeripheralProfileApplier(
        Func<DeviceDescriptor, CancellationToken, Task<RazerAwBleHeadsetService?>>? resolveAwBle = null,
        Func<DeviceDescriptor, CancellationToken, Task<BlackSharkHeadsetService?>>? resolveBlackShark = null,
        Func<DeviceDescriptor, CancellationToken,
            Task<(RazerHidSpecialLightingService Service, RazerHidSpecialLightingConnection Connection)?>>? resolveKraken = null)
    {
        _resolveAwBle = resolveAwBle;
        _resolveBlackShark = resolveBlackShark;
        _resolveKraken = resolveKraken;
    }

    public IReadOnlyCollection<DeviceCategory> Categories => SupportedCategories;

    public bool Accepts(DeviceDescriptor device) =>
        device.Category == DeviceCategory.Headset && Classify(device) != HeadsetFamily.Unknown;

    public async Task<ProfileDeviceApplyResult> ApplyAsync(
        DeviceProfileApplyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Device.Category != DeviceCategory.Headset)
        {
            return new ProfileDeviceApplyResult(context.Device.Id, ProfileDeviceApplyStatus.Skipped, []);
        }

        var settings = ProfileResolver.ResolveHeadset(
            context.Document,
            context.PowerSource,
            context.Device.Id);
        return Classify(context.Device) switch
        {
            HeadsetFamily.AwBle => await ApplyAwBleAsync(
                context, settings, cancellationToken),
            HeadsetFamily.BlackShark => await ApplyBlackSharkAsync(
                context, settings, cancellationToken),
            HeadsetFamily.Kraken => await ApplyKrakenAsync(
                context, settings, cancellationToken),
            _ => new ProfileDeviceApplyResult(
                context.Device.Id, ProfileDeviceApplyStatus.Skipped, []),
        };
    }

    private async Task<ProfileDeviceApplyResult> ApplyAwBleAsync(
        DeviceProfileApplyContext context,
        HeadsetProfileSettings settings,
        CancellationToken cancellationToken)
    {
        if (_resolveAwBle is null)
        {
            return FailedDevice(context.Device.Id, "The Audio AW BLE resolver is not configured.");
        }

        var service = await _resolveAwBle(context.Device, cancellationToken);
        if (service is null)
        {
            return FailedDevice(context.Device.Id, "The Audio AW BLE headset is not connected.");
        }

        var results = new List<ProfileSettingApplyResult>();
        if (settings.VolumePercent is { } volume)
        {
            await ApplySettingAsync(
                results, "headset.volume",
                async token => (int)await service.ReadVolumeAsync(token),
                async (value, token) => _ = await service.SetVolumeAsync(value, token),
                volume, (left, right) => left == right, cancellationToken);
        }

        if (settings.SideTone is { } sidetone)
        {
            await ApplySettingAsync(
                results, "headset.sidetone",
                async token => (int)await service.ReadSidetoneAsync(token),
                async (value, token) => _ = await service.SetSidetoneAsync(value, token),
                sidetone, (left, right) => left == right, cancellationToken);
        }

        if (settings.GameModeEnabled is { } gameMode)
        {
            await ApplySettingAsync(
                results, "headset.game-mode",
                token => service.ReadGameModeAsync(token),
                (value, token) => service.SetGameModeAsync(value, token),
                gameMode, (left, right) => left == right, cancellationToken);
        }

        if (TryParseByte(settings.EqPreset, out var eqPreset))
        {
            await ApplySettingAsync(
                results, "headset.eq-preset",
                token => service.ReadEqPresetAsync(token),
                (value, token) => service.SetEqPresetAsync(value, token),
                eqPreset, (left, right) => left == right, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(settings.EqPreset))
        {
            results.Add(Failed("headset.eq-preset", "The EQ preset is not a byte value."));
        }

        await ApplyBandsAsync(
            service, settings.EqBands, "headset.custom-eq", microphone: false,
            results, cancellationToken);

        if (TryParseByte(settings.MicEqPreset, out var micPreset))
        {
            await ApplySettingAsync(
                results, "headset.mic-eq-preset",
                token => service.ReadMicEqPresetAsync(token),
                (value, token) => service.SetMicEqPresetAsync(value, token),
                micPreset, (left, right) => left == right, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(settings.MicEqPreset))
        {
            results.Add(Failed("headset.mic-eq-preset", "The microphone EQ preset is not a byte value."));
        }

        await ApplyBandsAsync(
            service, settings.MicEqBands, "headset.mic-custom-eq", microphone: true,
            results, cancellationToken);

        if (settings.AutoShutdownMinutes is { } autoPowerOff)
        {
            await ApplySettingAsync(
                results, "headset.auto-power-off",
                token => service.ReadAutoPowerOffMinutesAsync(token),
                (value, token) => service.SetAutoPowerOffMinutesAsync(value, token),
                autoPowerOff, (left, right) => left == right, cancellationToken);
        }

        if (settings.Bass is { } bass)
        {
            await ApplySettingAsync(
                results, "headset.bass",
                async token => (int)await service.ReadBassAsync(token),
                async (value, token) => _ = await service.SetBassAsync(value, token),
                bass, (left, right) => left == right, cancellationToken);
        }

        if (settings.HapticsStrength is { } haptics)
        {
            await ApplySettingAsync(
                results, "headset.haptics",
                async token => (int)await service.ReadHapticsAsync(token),
                async (value, token) => _ = await service.SetHapticsAsync(value, token),
                haptics, (left, right) => left == right, cancellationToken);
        }

        if (settings.ChromaBrightness is { } brightness)
        {
            await ApplySettingAsync(
                results, "headset.chroma-brightness",
                async token => (int)await service.ReadChromaBrightnessAsync(token),
                async (value, token) => _ = await service.SetChromaBrightnessAsync(value, token),
                brightness, (left, right) => left == right, cancellationToken);
        }

        await ApplyChromaEffectAsync(service, settings, results, cancellationToken);
        return Summary(context.Device.Id, results);
    }

    private async Task<ProfileDeviceApplyResult> ApplyBlackSharkAsync(
        DeviceProfileApplyContext context,
        HeadsetProfileSettings settings,
        CancellationToken cancellationToken)
    {
        if (_resolveBlackShark is null)
        {
            return FailedDevice(context.Device.Id, "The BlackShark resolver is not configured.");
        }

        var service = await _resolveBlackShark(context.Device, cancellationToken);
        if (service is null)
        {
            return FailedDevice(context.Device.Id, "The BlackShark headset is not connected.");
        }

        var results = new List<ProfileSettingApplyResult>();
        BlackSharkHeadsetState state;
        try
        {
            state = await service.ReadStateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return FailedDevice(context.Device.Id, exception.Message);
        }

        if (settings.SideTone is { } sidetone)
        {
            await ApplyBlackSharkSettingAsync(
                results, "headset.sidetone",
                state.Sidetone is { } current ? current == sidetone : null,
                token => service.SetSidetoneAsync(sidetone, token), cancellationToken);
        }

        if (settings.ThxEnabled is { } thx)
        {
            await ApplyBlackSharkSettingAsync(
                results, "headset.thx",
                state.Thx is { } current ? current == thx : null,
                token => service.SetThxAsync(thx, token), cancellationToken);
        }

        if (settings.AncModeIndex is { } ancIndex and >= 0 and <= 2)
        {
            var mode = (BlackSharkAncMode)ancIndex;
            var level = Math.Clamp(settings.AncLevel ?? state.AncLevel ?? 1, 1, 4);
            var matches = state.AncMode == mode &&
                (mode != BlackSharkAncMode.Anc || state.AncLevel == level);
            await ApplyBlackSharkSettingAsync(
                results, "headset.anc", matches,
                token => service.SetAncAsync(mode, level, token), cancellationToken);
        }
        else if (settings.AncModeIndex is not null)
        {
            results.Add(Failed("headset.anc", "The ANC mode index is out of range."));
        }

        if (settings.GameChatBalance is { } gameChat)
        {
            await ApplyBlackSharkSettingAsync(
                results, "headset.game-chat",
                state.GameChatBalance is { } current ? current == gameChat : null,
                token => service.SetGameChatAsync(gameChat, token), cancellationToken);
        }

        return Summary(context.Device.Id, results);
    }

    private async Task<ProfileDeviceApplyResult> ApplyKrakenAsync(
        DeviceProfileApplyContext context,
        HeadsetProfileSettings settings,
        CancellationToken cancellationToken)
    {
        if (_resolveKraken is null)
        {
            return FailedDevice(context.Device.Id, "The Kraken resolver is not configured.");
        }

        var session = await _resolveKraken(context.Device, cancellationToken);
        if (session is null)
        {
            return FailedDevice(context.Device.Id, "The Kraken headset is not connected.");
        }

        if (string.IsNullOrWhiteSpace(settings.ChromaEffect))
        {
            return new ProfileDeviceApplyResult(
                context.Device.Id, ProfileDeviceApplyStatus.Skipped, []);
        }

        if (!TryBuildKrakenLighting(settings, out var lighting, out var intensity, out var error))
        {
            return new ProfileDeviceApplyResult(
                context.Device.Id,
                ProfileDeviceApplyStatus.Failed,
                [new ProfileSettingApplyResult(
                    "headset.chroma-effect", ProfileSettingApplyStatus.Failed, error)]);
        }

        try
        {
            await session.Value.Service.SetKrakenLightingAsync(
                session.Value.Connection,
                lighting,
                intensity,
                cancellationToken);
            return new ProfileDeviceApplyResult(
                context.Device.Id,
                ProfileDeviceApplyStatus.Applied,
                [new ProfileSettingApplyResult(
                    "headset.chroma-effect", ProfileSettingApplyStatus.AppliedUnverified)]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ProfileDeviceApplyResult(
                context.Device.Id,
                ProfileDeviceApplyStatus.Failed,
                [new ProfileSettingApplyResult(
                    "headset.chroma-effect", ProfileSettingApplyStatus.Failed, exception.Message)]);
        }
    }

    private static async Task ApplyBandsAsync(
        RazerAwBleHeadsetService service,
        IReadOnlyDictionary<string, int>? bands,
        string key,
        bool microphone,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (bands is null)
        {
            return;
        }

        var parsed = new List<(int Frequency, sbyte Level)>();
        foreach (var (frequencyText, level) in bands)
        {
            if (!int.TryParse(frequencyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frequency) ||
                level is < -5 or > 5)
            {
                results.Add(Failed(key, "Every EQ band needs a numeric frequency and a level from -5 to +5."));
                return;
            }

            parsed.Add((frequency, (sbyte)level));
        }

        if (parsed.Count != RazerAwBleProtocol.CustomEqBandCount)
        {
            results.Add(Failed(key, $"Exactly {RazerAwBleProtocol.CustomEqBandCount} EQ bands are required."));
            return;
        }

        var desired = parsed.OrderBy(item => item.Frequency).Select(item => item.Level).ToArray();
        await ApplySettingAsync(
            results, key,
            token => microphone
                ? ReadMicBandsAsync(service, token)
                : service.ReadCustomEqBandsAsync(token),
            async (value, token) =>
            {
                if (microphone)
                {
                    _ = await service.SetMicCustomEqBandsAsync(value, token);
                }
                else
                {
                    _ = await service.SetCustomEqBandsAsync(value, token);
                }
            },
            desired,
            (left, right) => left.SequenceEqual(right),
            cancellationToken);
    }

    private static async Task<IReadOnlyList<sbyte>> ReadMicBandsAsync(
        RazerAwBleHeadsetService service,
        CancellationToken cancellationToken) =>
        await service.ReadMicCustomEqBandsAsync(cancellationToken);

    private static async Task ApplyChromaEffectAsync(
        RazerAwBleHeadsetService service,
        HeadsetProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ChromaEffect))
        {
            return;
        }

        if (!Enum.TryParse<RazerAwChromaEffect>(settings.ChromaEffect, ignoreCase: true, out var effect))
        {
            results.Add(Failed("headset.chroma-effect", "The Chroma effect name is unknown."));
            return;
        }

        var primary = ParseColor(settings.ChromaParameters, "primary");
        var secondary = ParseColor(settings.ChromaParameters, "secondary");
        var desired = new RazerAwChromaState(effect, primary, secondary);
        await ApplySettingAsync(
            results, "headset.chroma-effect",
            token => service.ReadChromaEffectAsync(token),
            async (value, token) => _ = await service.SetChromaEffectAsync(
                value.Effect, value.PrimaryColor, value.SecondaryColor, token),
            desired,
            ChromaEquals,
            cancellationToken);
    }

    private static bool ChromaEquals(RazerAwChromaState left, RazerAwChromaState right) =>
        left.Effect == right.Effect &&
        left.PrimaryColor.SequenceEqual(right.PrimaryColor) &&
        left.SecondaryColor.SequenceEqual(right.SecondaryColor);
    private static byte[] ParseColor(IReadOnlyDictionary<string, string>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var value) || value.Length != 6)
        {
            return [];
        }

        return byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) &&
            byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) &&
            byte.TryParse(value[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue)
            ? [red, green, blue]
            : [];
    }
    private static bool TryBuildKrakenLighting(
        HeadsetProfileSettings settings,
        out RazerHidLightingSettings lighting,
        out byte? intensity,
        out string? error)
    {
        lighting = new RazerHidLightingSettings(RazerHidLightingEffect.Off);
        intensity = null;
        error = null;
        if (!Enum.TryParse<RazerHidLightingEffect>(
                settings.ChromaEffect, ignoreCase: true, out var effect))
        {
            error = "The Kraken lighting effect name is unknown.";
            return false;
        }

        var primary = ParseColor(settings.ChromaParameters, "primary");
        var secondary = ParseColor(settings.ChromaParameters, "secondary");
        var tertiary = ParseColor(settings.ChromaParameters, "tertiary");
        var rawIntensity = settings.ChromaParameters is { } parameters &&
            parameters.TryGetValue("intensity", out var text) &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? Math.Round(parsed)
                : Math.Round((settings.ChromaBrightness ?? 100) * 255d / 100d);
        intensity = (byte)Math.Clamp(rawIntensity, 0, 255);
        lighting = new RazerHidLightingSettings(
            effect,
            Primary: ToColor(primary),
            Secondary: ToColor(secondary),
            Tertiary: ToColor(tertiary));
        return true;
    }
    private static async Task ApplyBlackSharkSettingAsync(
        List<ProfileSettingApplyResult> results,
        string key,
        bool? matches,
        Func<CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (matches == true)
        {
            results.Add(Applied(key));
            return;
        }

        try
        {
            await write(cancellationToken);
            results.Add(Applied(key));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            results.Add(Failed(key, exception));
        }
    }
    private static Task ApplySettingAsync<T>(
        List<ProfileSettingApplyResult> results,
        string key,
        Func<CancellationToken, Task<T>> read,
        Func<T, CancellationToken, Task> write,
        T desired,
        Func<T, T, bool> equals,
        CancellationToken cancellationToken) =>
        ApplySettingCoreAsync(results, key, read, write, desired, equals, cancellationToken);
    private static async Task ApplySettingCoreAsync<T>(
        List<ProfileSettingApplyResult> results,
        string key,
        Func<CancellationToken, Task<T>> read,
        Func<T, CancellationToken, Task> write,
        T desired,
        Func<T, T, bool> equals,
        CancellationToken cancellationToken)
    {
        try
        {
            var readOk = false;
            T current = default!;
            try
            {
                current = await read(cancellationToken);
                readOk = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // The write below still verifies itself on devices that refuse a pre-read.
            }

            if (readOk && equals(current, desired))
            {
                results.Add(Applied(key));
                return;
            }

            await write(desired, cancellationToken);
            results.Add(Applied(key));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            results.Add(Failed(key, exception));
        }
    }

    private static ProfileDeviceApplyResult Summary(
        string deviceId,
        IReadOnlyList<ProfileSettingApplyResult> results)
    {
        var status = results.Any(item => item.Status == ProfileSettingApplyStatus.Failed)
            ? ProfileDeviceApplyStatus.Failed
            : results.Any(item => item.Status is ProfileSettingApplyStatus.Applied or
                ProfileSettingApplyStatus.AppliedUnverified)
                ? ProfileDeviceApplyStatus.Applied
                : ProfileDeviceApplyStatus.Skipped;
        return new ProfileDeviceApplyResult(deviceId, status, results);
    }

    private static HeadsetFamily Classify(DeviceDescriptor device)
    {
        if (device.Id.StartsWith("ble:", StringComparison.OrdinalIgnoreCase) ||
            Contains(device.ProtocolFamily, "aw-ble") ||
            Contains(device.ProtocolFamily, "awble"))
        {
            return HeadsetFamily.AwBle;
        }

        if (Contains(device.ProtocolFamily, "blackshark") ||
            Contains(device.Name, "BlackShark"))
        {
            return HeadsetFamily.BlackShark;
        }

        return Contains(device.ProtocolFamily, "kraken") || Contains(device.Name, "Kraken")
            ? HeadsetFamily.Kraken
            : HeadsetFamily.Unknown;
    }

    private static bool Contains(string? value, string token) =>
        value?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;

    private static bool TryParseByte(string? value, out byte parsed) =>
        byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

    private static RazerHidColor ToColor(byte[] color) => color.Length == 3
        ? new RazerHidColor(color[0], color[1], color[2])
        : default;

    private static ProfileDeviceApplyResult FailedDevice(string deviceId, string error) =>
        new(deviceId, ProfileDeviceApplyStatus.Failed,
            [new ProfileSettingApplyResult("device", ProfileSettingApplyStatus.Failed, error)]);

    private static ProfileSettingApplyResult Applied(string key) =>
        new(key, ProfileSettingApplyStatus.Applied);

    private static ProfileSettingApplyResult Failed(string key, Exception exception) =>
        new(key, ProfileSettingApplyStatus.Failed, exception.Message);

    private static ProfileSettingApplyResult Failed(string key, string error) =>
        new(key, ProfileSettingApplyStatus.Failed, error);

    private enum HeadsetFamily
    {
        Unknown,
        AwBle,
        BlackShark,
        Kraken,
    }
}
