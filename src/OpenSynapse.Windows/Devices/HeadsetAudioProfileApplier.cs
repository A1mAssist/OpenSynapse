using System.ComponentModel;
using System.Globalization;
using OpenSynapse.Core.Audio;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Audio;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Applies the persisted Feature91 headset settings through the same audio,
/// THX and Chroma commands the device page uses. THX is Windows-side and also
/// replays for the 37/64-byte transports; unsupported settings stay out of the
/// results instead of being reported as applied.
/// </summary>
public sealed partial class HeadsetAudioProfileApplier : IDeviceProfileApplier
{
    private static readonly DeviceCategory[] SupportedCategories = [DeviceCategory.Headset];
    private static readonly RazerAudioSource[] Sources =
        [RazerAudioSource.None, RazerAudioSource.Spdif, RazerAudioSource.Aux, RazerAudioSource.Bluetooth, RazerAudioSource.Usb];
    private static readonly RazerAncState[] AncStates =
        [RazerAncState.Off, RazerAncState.On, RazerAncState.Ambient];

    private readonly Func<DeviceDescriptor, RazerHeadsetAudioConnection?> _resolveConnection;
    private readonly IRazerFeatureTransport _transport;
    private readonly TimeSpan _deviceWait;

    public HeadsetAudioProfileApplier(
        Func<DeviceDescriptor, RazerHeadsetAudioConnection?> resolveConnection)
        : this(resolveConnection, new RazerFeatureTransport(), TimeSpan.FromMilliseconds(20))
    {
    }

    internal HeadsetAudioProfileApplier(
        Func<DeviceDescriptor, RazerHeadsetAudioConnection?> resolveConnection,
        IRazerFeatureTransport transport,
        TimeSpan deviceWait)
    {
        _resolveConnection = resolveConnection ?? throw new ArgumentNullException(nameof(resolveConnection));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _deviceWait = deviceWait;
    }

    public IReadOnlyCollection<DeviceCategory> Categories => SupportedCategories;

    public bool Accepts(DeviceDescriptor device) =>
        device.Category == DeviceCategory.Headset &&
        !device.Id.StartsWith("ble:", StringComparison.OrdinalIgnoreCase) &&
        !device.Name.Contains("BlackShark", StringComparison.OrdinalIgnoreCase) &&
        RazerHeadsetAudioService.UsesFeatureReportTransport(device.ProductId);

    public async Task<ProfileDeviceApplyResult> ApplyAsync(
        DeviceProfileApplyContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var connection = _resolveConnection(context.Device);
        if (connection is null)
        {
            return new ProfileDeviceApplyResult(
                context.Device.Id,
                ProfileDeviceApplyStatus.Failed,
                [new ProfileSettingApplyResult(
                    "device",
                    ProfileSettingApplyStatus.Failed,
                    "The headset audio connection is not available.")]);
        }

        if (!connection.HasControlInterface)
        {
            return new ProfileDeviceApplyResult(
                context.Device.Id,
                ProfileDeviceApplyStatus.Skipped,
                [new ProfileSettingApplyResult(
                    "device",
                    ProfileSettingApplyStatus.Skipped,
                    "The headset control interface is not available.")]);
        }

        var profile = context.Profile.Headset;
        var results = new List<ProfileSettingApplyResult>();

        if (connection.Transport != RazerHeadsetAudioTransport.Feature91)
        {
            // THX is a Windows-side component keyed by PID/container, so it still
            // replays for headsets whose audio settings use a 37/64-byte transport.
            await ApplyThxAsync(connection, profile, results, cancellationToken);
            await ApplyMxicDndAsync(connection, profile, results, cancellationToken);
            return CreateResult(context.Device.Id, results);
        }

        var service = new RazerHeadsetAudioService(
            _transport, connection.DevicePath, _deviceWait);
        var state = await TryReadStateAsync(service, cancellationToken);

        await ApplyThxAsync(connection, profile, results, cancellationToken);
        await ApplyEqualizerAsync(service, state, profile, results, cancellationToken);
        await ApplyMicrophoneAsync(service, state, profile, results, cancellationToken);
        await ApplyChromaAsync(connection, profile, results, cancellationToken);
        await ApplyAncAndAdvancedAsync(service, state, profile, results, cancellationToken);

        return CreateResult(context.Device.Id, results);
    }

    private static ProfileDeviceApplyResult CreateResult(
        string deviceId,
        List<ProfileSettingApplyResult> results)
    {
        var status = results.Any(item => item.Status == ProfileSettingApplyStatus.Failed)
            ? ProfileDeviceApplyStatus.Failed
            : results.Any(item => item.Status is ProfileSettingApplyStatus.Applied or
                ProfileSettingApplyStatus.AppliedUnverified)
                ? ProfileDeviceApplyStatus.Applied
                : ProfileDeviceApplyStatus.Skipped;
        return new ProfileDeviceApplyResult(deviceId, status, results);
    }

    private static async Task ApplyMxicDndAsync(
        RazerHeadsetAudioConnection connection,
        HeadsetProfileSettings profile,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.Dnd is not { } dnd ||
            connection.Transport is not (RazerHeadsetAudioTransport.Mxic64 or RazerHeadsetAudioTransport.ValkyrieT3Feature64) ||
            RazerProtocol40HeadsetProtocol.IsSupported(connection.ProductId))
        {
            return;
        }

        await using var service = new RazerMxicHeadsetService(
            connection.ProductId, connection.DevicePath);
        await ApplyAsync(
            "headset.dnd",
            token => service.SetAudioDndAsync(dnd, token),
            results,
            cancellationToken);
    }

    private async Task ApplyChromaAsync(
        RazerHeadsetAudioConnection connection,
        HeadsetProfileSettings profile,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.ChromaBrightness is null && profile.ChromaEffect is null)
        {
            return;
        }

        var service = new RazerChromaStandardService(
            _transport, connection.DevicePath, _deviceWait);
        RazerChromaStandardState? state = null;
        try
        {
            state = await service.ReadStateAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // The writes still verify themselves; only the same-value skip is lost.
        }

        if (profile.ChromaBrightness is { } brightness)
        {
            if (state?.BrightnessPercent == brightness)
            {
                results.Add(Skipped("headset.lighting.brightness"));
            }
            else
            {
                await ApplyAsync(
                    "headset.lighting.brightness",
                    token => service.SetBrightnessAsync((byte)Math.Clamp(brightness, 0, 100), token),
                    results,
                    cancellationToken);
            }
        }

        if (profile.ChromaEffect is not { Length: > 0 } effectName)
        {
            return;
        }

        if (!Enum.TryParse<RazerChromaEffect>(effectName, ignoreCase: true, out var effect) ||
            !Enum.IsDefined(effect))
        {
            results.Add(Failed("headset.lighting.effect", "Unknown Chroma effect."));
            return;
        }

        var colors = BuildChromaColors(effect, profile.ChromaParameters, out var colorError);
        if (colorError is not null)
        {
            results.Add(Failed("headset.lighting.effect", colorError));
            return;
        }

        if (state?.Effect == effect &&
            (colors.Length == 0 ||
                (state.Colors.Count >= colors.Length && state.Colors.Take(colors.Length).SequenceEqual(colors))))
        {
            results.Add(Skipped("headset.lighting.effect"));
            return;
        }

        await ApplyAsync(
            "headset.lighting.effect",
            token => service.SetEffectAsync(effect, colors, token),
            results,
            cancellationToken);
    }

    private async Task ApplyAncAndAdvancedAsync(
        RazerHeadsetAudioService service,
        RazerHeadsetAudioState? state,
        HeadsetProfileSettings profile,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (TryBuildAnc(profile, out var ancState, out var ancLevel))
        {
            if (state?.AncState == ancState && state.AncLevel == ancLevel)
            {
                results.Add(Skipped("headset.anc"));
            }
            else
            {
                await ApplyAsync(
                    "headset.anc",
                    token => service.SetAncAsync(ancState, ancLevel, token),
                    results,
                    cancellationToken);
            }
        }
        else if (profile.AncEnabled is not null || profile.AncModeIndex is not null)
        {
            results.Add(Failed("headset.anc", "Unknown ANC mode."));
        }

        await ApplyBooleanAsync("headset.dnd", profile.Dnd, state?.Dnd,
            value => service.SetAudioDndAsync(value, cancellationToken), results, cancellationToken);
        await ApplyBooleanAsync("headset.auto-pause", profile.AutoPause, state?.AutoPause,
            value => service.SetAudioAutoPauseAsync(value, cancellationToken), results, cancellationToken);
        await ApplyBooleanAsync("headset.gesture-control", profile.GestureControl, state?.GestureControl,
            value => service.SetAudioGestureControlAsync(value, cancellationToken), results, cancellationToken);
        await ApplyBooleanAsync("headset.dolby", profile.Dolby, state?.Dolby,
            value => service.SetDolbyStateAsync(value, cancellationToken), results, cancellationToken);
        await ApplyBooleanAsync("headset.night-mode", profile.NightMode, state?.NightMode,
            value => service.SetEqNightModeAsync(value, cancellationToken), results, cancellationToken);
        await ApplyBooleanAsync("headset.voice-prompts", profile.VoicePrompts, state?.VoicePrompts?.Enabled,
            value => service.SetAudioVoicePromptsAsync(value, cancellationToken: cancellationToken),
            results, cancellationToken);

        if (profile.MicDeEsser is { } deEsser)
        {
            if (state?.MicDeEsser == deEsser)
            {
                results.Add(Skipped("headset.mic.de-esser"));
            }
            else
            {
                await ApplyAsync(
                    "headset.mic.de-esser",
                    token => service.SetMicDeEsserAsync((byte)Math.Clamp(deEsser, 0, 100), token),
                    results,
                    cancellationToken);
            }
        }

        if (profile.MicLimiterDb is { } limiter)
        {
            if (state?.MicLimiter == limiter)
            {
                results.Add(Skipped("headset.mic.limiter"));
            }
            else
            {
                await ApplyAsync(
                    "headset.mic.limiter",
                    token => service.SetMicLimiterAsync((sbyte)Math.Clamp(limiter, -60, 0), token),
                    results,
                    cancellationToken);
            }
        }

        if (TryBuildReverb(profile, out var roomMode, out var intensity))
        {
            if (state?.MicReverb == (roomMode, intensity))
            {
                results.Add(Skipped("headset.mic.reverb"));
            }
            else
            {
                await ApplyAsync(
                    "headset.mic.reverb",
                    token => service.SetMicReverbAsync(roomMode, intensity, token),
                    results,
                    cancellationToken);
            }
        }
        else if (profile.MicReverbIndex is not null)
        {
            results.Add(Failed("headset.mic.reverb", "Unknown reverb room mode."));
        }
    }

    private static async Task ApplyBooleanAsync(
        string key,
        bool? desired,
        bool? current,
        Func<bool, Task> write,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (desired is null)
        {
            return;
        }
        if (current == desired)
        {
            results.Add(Skipped(key));
            return;
        }

        await ApplyAsync(key, _ => write(desired.Value), results, cancellationToken);
    }

    private static async Task ApplyAsync(
        string key,
        Func<CancellationToken, Task> action,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await action(cancellationToken);
            results.Add(new ProfileSettingApplyResult(key, ProfileSettingApplyStatus.Applied));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(Failed(key, exception.Message));
        }
    }

    private static async Task<RazerHeadsetAudioState?> TryReadStateAsync(
        RazerHeadsetAudioService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return await service.ReadStateAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return null;
        }
    }

    private static byte[]? ReadBands(
        IReadOnlyDictionary<string, int>? source,
        int count,
        out string? error)
    {
        error = null;
        if (source is null)
        {
            return null;
        }

        var expected = count is 8 or 10 ? count : 10;
        var bands = new byte[expected];
        for (var index = 0; index < expected; index++)
        {
            if (!source.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out var value))
            {
                error = "The equalizer band map is incomplete.";
                return null;
            }
            bands[index] = (byte)Math.Clamp(value, 0, 100);
        }
        return bands;
    }

    private static double[]? ReadThxBands(IReadOnlyDictionary<string, int>? source)
    {
        if (source is null)
        {
            return null;
        }

        var bands = new double[source.Count];
        for (var index = 0; index < bands.Length; index++)
        {
            if (!source.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out var value))
            {
                return null;
            }
            bands[index] = value;
        }
        return bands;
    }

    private static byte? TryParseByte(string? value) =>
        byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool TryBuildParametricEq(
        HeadsetProfileSettings profile,
        RazerParametricEqBand? current,
        out RazerParametricEqBand value)
    {
        if (profile.ParametricEqEnabled is null ||
            profile.ParametricEqBandIndex is not { } index ||
            profile.ParametricEqFrequency is not { } frequency ||
            profile.ParametricEqGain is not { } gain ||
            profile.ParametricEqQ is not { } q)
        {
            value = default;
            return false;
        }

        value = new RazerParametricEqBand(
            (byte)Math.Clamp(profile.ParametricEqProfile ?? current?.Profile ?? 0, 0, 255),
            (byte)Math.Clamp(profile.ParametricEqDirection ?? current?.Direction ?? 0, 0, 255),
            (byte)Math.Clamp(index, 0, 9),
            profile.ParametricEqEnabled.Value ? (byte)1 : (byte)0,
            (ushort)Math.Clamp(frequency, 0, ushort.MaxValue),
            (sbyte)Math.Clamp(gain, -128, 127),
            (byte)Math.Clamp(q, 0, 255),
            (byte)Math.Clamp(profile.ParametricEqFilterType ?? current?.FilterType ?? 0, 0, 255));
        return true;
    }

    private static bool HasParametricEq(HeadsetProfileSettings profile) =>
        profile.ParametricEqEnabled is not null || profile.ParametricEqBandIndex is not null;

    private static bool TryBuildMicCompressor(
        HeadsetProfileSettings profile,
        RazerMicCompressor? current,
        out RazerMicCompressor value)
    {
        if (profile.MicCompressorEnabled is null ||
            profile.MicCompressorProfile is not { } profileId ||
            profile.MicCompressorDb is not { } db ||
            profile.MicCompressorRatio is not { } ratio ||
            profile.MicCompressorAttack is not { } attack ||
            profile.MicCompressorRelease is not { } release)
        {
            value = default;
            return false;
        }

        value = new RazerMicCompressor(
            (byte)Math.Clamp(profileId, 0, 255),
            profile.MicCompressorEnabled.Value ? (byte)1 : (byte)0,
            (sbyte)Math.Clamp(db, -128, 0),
            (ushort)Math.Clamp(ratio, 0, ushort.MaxValue),
            (ushort)Math.Clamp(attack, 0, ushort.MaxValue),
            (ushort)Math.Clamp(release, 0, ushort.MaxValue));
        return true;
    }

    private static bool TryBuildAnc(
        HeadsetProfileSettings profile,
        out RazerAncState state,
        out byte level)
    {
        state = RazerAncState.Off;
        level = (byte)Math.Clamp(profile.AncLevel ?? 0, 0, 10);
        if (profile.AncModeIndex is { } index)
        {
            if (index < 0 || index >= AncStates.Length)
            {
                return false;
            }
            state = AncStates[index];
            return true;
        }
        if (profile.AncEnabled is { } enabled)
        {
            state = enabled ? RazerAncState.On : RazerAncState.Off;
            return true;
        }
        return false;
    }

    private static bool TryBuildReverb(
        HeadsetProfileSettings profile,
        out RazerMicReverbRoomMode roomMode,
        out byte intensity)
    {
        roomMode = RazerMicReverbRoomMode.Small;
        intensity = (byte)Math.Clamp(profile.MicReverbIntensity ?? 0, 0, 100);
        if (profile.MicReverbIndex is not { } index || index < 0 || index > 3)
        {
            return false;
        }
        roomMode = (RazerMicReverbRoomMode)index;
        return true;
    }

    private static byte[] BuildChromaColors(
        RazerChromaEffect effect,
        IReadOnlyDictionary<string, string>? parameters,
        out string? error)
    {
        error = null;
        if (effect is not (RazerChromaEffect.Static or RazerChromaEffect.Breathing))
        {
            return [];
        }

        if (!TryParseColor(parameters, "primary", out var primary))
        {
            error = "The primary lighting color is invalid.";
            return [];
        }
        if (effect == RazerChromaEffect.Static)
        {
            return primary;
        }
        if (!TryParseColor(parameters, "secondary", out var secondary))
        {
            error = "The secondary lighting color is invalid.";
            return [];
        }
        return [.. primary, .. secondary];
    }

    private static bool TryParseColor(
        IReadOnlyDictionary<string, string>? parameters,
        string key,
        out byte[] rgb)
    {
        rgb = [];
        if (parameters is null || !parameters.TryGetValue(key, out var text) || text.Length != 6)
        {
            return false;
        }

        rgb = new byte[3];
        return byte.TryParse(text.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb[0]) &&
            byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb[1]) &&
            byte.TryParse(text.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb[2]);
    }

    private static ProfileSettingApplyResult Skipped(string key) =>
        new(key, ProfileSettingApplyStatus.Skipped);

    private static ProfileSettingApplyResult Failed(string key, string error) =>
        new(key, ProfileSettingApplyStatus.Failed, error);

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException or ArgumentException;
}
