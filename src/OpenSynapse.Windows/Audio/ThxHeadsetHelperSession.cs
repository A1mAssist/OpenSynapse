using System.Globalization;
using System.Text.Json;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

/// <summary>
/// One helper process owns one headset. V3 talks to the flat container API;
/// V4 registers the device with its Razer product ID and uses the property API.
/// </summary>
internal sealed class ThxHeadsetHelperSession
{
    private const string PresetGroupId = "profile_presets";
    private const string SpatialPropertyId = "spatial_enabled";
    private const string EqualizerPropertyId = "eq_enabled";
    private const string NormalizationEnabledPropertyId = "volume_leveling_enabled";
    private const string NormalizationLevelPropertyId = "volume_leveling_level";
    private const string VoiceClarityEnabledPropertyId = "voice_clarity_enabled";
    private const string VoiceClarityLevelPropertyId = "voice_clarity_level";
    private const string FloatPropertyType = "Float";
    private const int SidetoneMinimum = 1;
    private const int SidetoneMaximum = 3162;
    private static readonly int[] Frequencies = [31, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    private ThxHeadsetProfile? _profile;
    private ThxV3NativeClient? _v3;
    private ThxAudioNativeClient? _v4;
    private string? _v4Device;

    internal ThxAudioHelperResponse Execute(ThxAudioHelperRequest request)
    {
        if (!TryParseProductId(request.ProductId, out var productId) ||
            ThxHeadsetAdmission.GetProfile(productId) is not { } profile)
        {
            return Fail("ThxHeadsetUnsupported");
        }

        try
        {
            EnsureClient(profile, request.ContainerId);
            var accepted = request.Operation switch
            {
                "read" => true,
                "setSpatial" => SetSpatial(profile, request.ContainerId, request.Enabled),
                "setEqualizer" => SetEqualizer(profile, request.Enabled),
                "selectPreset" => SelectPreset(profile, request.ContainerId, request),
                "setGains" => SetGains(profile, request.ContainerId, request),
                "setEnhancement" => SetEnhancement(profile, request.ContainerId, request),
                _ => false,
            };
            if (!accepted)
            {
                return Fail("ThxOperationRejected");
            }

            return new ThxAudioHelperResponse(
                ThxAudioHelperProtocol.Version,
                true,
                new ThxAudioStatus(
                    ThxAudioAvailability.Available,
                    null,
                    ReadState(profile, request.ContainerId)),
                null);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or InvalidDataException or ArgumentException or JsonException)
        {
            return Fail(exception is ArgumentException ? "ThxInvalidRequest" : "ThxNativeFailure");
        }
    }

    private void EnsureClient(ThxHeadsetProfile profile, string? containerId)
    {
        if (_profile is not null && _profile.ProductId != profile.ProductId)
        {
            throw new InvalidOperationException("The THX headset changed inside one helper session.");
        }
        _profile = profile;

        if (profile.NativeVersion == ThxNativeVersion.V3)
        {
            if (string.IsNullOrWhiteSpace(containerId))
            {
                throw new ArgumentException("A V3 THX endpoint container ID is required.");
            }
            if (containerId.Length > 128 || containerId.Any(char.IsControl))
            {
                throw new ArgumentException("The THX endpoint container ID is invalid.");
            }
            _v3 ??= new ThxV3NativeClient(
                ThxHeadsetAdmission.NativeDllPath(profile),
                profile.NativeDllLength,
                profile.NativeDllSha256);
            _v3.Initialize(containerId);
            return;
        }

        _v4 ??= new ThxAudioNativeClient(
            ThxHeadsetAdmission.NativeDllPath(profile),
            profile.NativeDllLength,
            profile.NativeDllSha256);
        if (_v4Device is not null)
        {
            return;
        }

        var device = JsonSerializer.Serialize(new
        {
            productId = profile.V4ProductId,
            endpointId = profile.V4EndpointId ?? string.Empty,
        });
        if (!_v4.AddDevice(device))
        {
            throw new InvalidOperationException("The THX V4 device registration failed.");
        }
        _v4Device = device;
    }

    private bool SetSpatial(ThxHeadsetProfile profile, string? containerId, bool? enabled)
    {
        if (enabled is not { } value)
        {
            throw new ArgumentException("The THX spatial value is missing.");
        }

        if (profile.NativeVersion == ThxNativeVersion.V3)
        {
            return _v3!.GetSpatial(containerId!) == value || _v3.SetSpatial(containerId!, value);
        }

        var current = ThxVendorJson.ParseBoolean(_v4!.GetProperty(_v4Device!, SpatialPropertyId));
        return current == value ||
            _v4.SetProperty(_v4Device!, SpatialPropertyId, value ? "true" : "false", null);
    }

    private bool SetEqualizer(ThxHeadsetProfile profile, bool? enabled)
    {
        if (enabled is not { } value)
        {
            throw new ArgumentException("The THX equalizer value is missing.");
        }

        // V3 has no separate EQ switch; the selected preset owns it.
        if (profile.NativeVersion == ThxNativeVersion.V3)
        {
            return true;
        }

        var current = ThxVendorJson.ParseBoolean(_v4!.GetProperty(_v4Device!, EqualizerPropertyId));
        return current == value ||
            _v4.SetProperty(_v4Device!, EqualizerPropertyId, value ? "true" : "false", null);
    }

    private bool SelectPreset(ThxHeadsetProfile profile, string? containerId, ThxAudioHelperRequest request)
    {
        if (request.PresetId is not { } presetId ||
            string.IsNullOrWhiteSpace(presetId) ||
            presetId.Length > 256 ||
            !string.Equals(presetId, presetId.Trim(), StringComparison.Ordinal) ||
            presetId.Any(char.IsControl))
        {
            throw new ArgumentException("The THX preset ID is invalid.");
        }

        if (profile.NativeVersion == ThxNativeVersion.V3)
        {
            if (!_v3!.SetPreset(containerId!, presetId))
            {
                return false;
            }
            return request.GainsDb is not { } gains || SetV3Gains(containerId!, gains);
        }

        if (!_v4!.SelectPreset(_v4Device!, PresetGroupId, presetId))
        {
            return false;
        }
        return request.GainsDb is not { } v4Gains || SetV4Gains(v4Gains);
    }

    private bool SetGains(ThxHeadsetProfile profile, string? containerId, ThxAudioHelperRequest request)
    {
        if (request.GainsDb is not { } gains)
        {
            throw new ArgumentException("The THX equalizer gains are missing.");
        }
        return profile.NativeVersion == ThxNativeVersion.V3
            ? SetV3Gains(containerId!, gains)
            : SetV4Gains(gains);
    }

    private bool SetV3Gains(string containerId, IReadOnlyList<double> gains)
    {
        if (gains.Count != 10 || !ValidGains(gains))
        {
            throw new ArgumentException("The THX equalizer gains are invalid.");
        }
        return _v3!.SetEqGains(containerId, JsonSerializer.Serialize(gains));
    }

    private bool SetV4Gains(IReadOnlyList<double> gains)
    {
        if (gains.Count != 10 || !ValidGains(gains))
        {
            throw new ArgumentException("The THX equalizer gains are invalid.");
        }

        for (var index = 0; index < gains.Count; index++)
        {
            if (!_v4!.SetProperty(
                    _v4Device!,
                    $"eq_band_{index + 1}_gain_db",
                    JsonSerializer.Serialize(gains[index]),
                    FloatPropertyType))
            {
                return false;
            }
        }
        return true;
    }

    private bool SetEnhancement(ThxHeadsetProfile profile, string? containerId, ThxAudioHelperRequest request)
    {
        if (request.Enhancement is not { } kind ||
            request.Enabled is not { } enabled ||
            request.LevelPercent is not (null or >= 0 and <= 100))
        {
            throw new ArgumentException("The THX enhancement value is invalid.");
        }

        if (kind == "sidetone")
        {
            if (!profile.HasSidetone || profile.NativeVersion != ThxNativeVersion.V3)
            {
                throw new ArgumentException("THX sidetone is not available on this device.");
            }

            var currentEnabled = _v3!.GetSidetone(containerId!);
            if (currentEnabled != enabled && !_v3.SetSidetone(containerId!, enabled))
            {
                return false;
            }
            if (!enabled || request.LevelPercent is not { } percent)
            {
                return true;
            }

            var raw = (int)Math.Round(
                SidetoneMinimum + percent / 100d * (SidetoneMaximum - SidetoneMinimum),
                MidpointRounding.AwayFromZero);
            return _v3.GetSidetoneLevel(containerId!) == raw ||
                _v3.SetSidetoneLevel(containerId!, raw);
        }

        if (profile.NativeVersion == ThxNativeVersion.V3)
        {
            var (getEnabled, setEnabled, getLevel, setLevel) = kind switch
            {
                "voiceClarity" =>
                    ((Func<bool>)(() => _v3!.GetVocalClarity(containerId!)),
                     (Func<bool, bool>)(value => _v3!.SetVocalClarity(containerId!, value)),
                     (Func<int>)(() => _v3!.GetVocalClarityLevel(containerId!)),
                     (Func<int, bool>)(value => _v3!.SetVocalClarityLevel(containerId!, value))),
                "soundNormalization" =>
                    ((Func<bool>)(() => _v3!.GetNormalization(containerId!)),
                     (Func<bool, bool>)(value => _v3!.SetNormalization(containerId!, value)),
                     (Func<int>)(() => _v3!.GetNormalizationLevel(containerId!)),
                     (Func<int, bool>)(value => _v3!.SetNormalizationLevel(containerId!, value))),
                _ => throw new ArgumentException("The THX enhancement kind is invalid."),
            };

            if (getEnabled() != enabled && !setEnabled(enabled))
            {
                return false;
            }
            if (!enabled || request.LevelPercent is not { } level)
            {
                return true;
            }
            return getLevel() == level || setLevel(level);
        }

        var (enabledProperty, levelProperty) = kind switch
        {
            "voiceClarity" => (VoiceClarityEnabledPropertyId, VoiceClarityLevelPropertyId),
            "soundNormalization" => (NormalizationEnabledPropertyId, NormalizationLevelPropertyId),
            _ => throw new ArgumentException("The THX enhancement kind is invalid."),
        };

        var current = ThxVendorJson.ParseBoolean(_v4!.GetProperty(_v4Device!, enabledProperty));
        if (current != enabled &&
            !_v4.SetProperty(_v4Device!, enabledProperty, enabled ? "true" : "false", null))
        {
            return false;
        }
        if (!enabled || request.LevelPercent is not { } percentValue)
        {
            return true;
        }

        var target = percentValue / 100d;
        var currentLevel = ThxVendorJson.ParseFloat(_v4.GetProperty(_v4Device!, levelProperty), 0, 1);
        return Math.Abs(currentLevel - target) <= 0.000001 ||
            _v4.SetProperty(
                _v4Device!,
                levelProperty,
                JsonSerializer.Serialize(target),
                FloatPropertyType);
    }

    private ThxAudioState ReadState(ThxHeadsetProfile profile, string? containerId)
    {
        if (profile.NativeVersion == ThxNativeVersion.V3)
        {
            return ReadV3State(profile, containerId!);
        }

        var spatial = ThxVendorJson.ParseBoolean(_v4!.GetProperty(_v4Device!, SpatialPropertyId));
        var equalizer = ThxVendorJson.ParseBoolean(_v4.GetProperty(_v4Device!, EqualizerPropertyId));
        var presets = ReadV4Presets();
        var selected = ThxVendorJson.ParseSelectedPresetValue(
            _v4.GetSelectedPreset(_v4Device!, PresetGroupId));
        var gains = Enumerable.Range(1, 10)
            .Select(index => ReadV4Float($"eq_band_{index}_gain_db"))
            .ToArray();
        return new ThxAudioState(
            ThxAudioEndpoint.Headphones,
            spatial,
            equalizer,
            selected,
            presets,
            ToBands(gains),
            new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal),
            ReadV4Enhancement(NormalizationEnabledPropertyId, NormalizationLevelPropertyId),
            ReadV4Enhancement(VoiceClarityEnabledPropertyId, VoiceClarityLevelPropertyId) ??
                new ThxAudioEnhancement(false, 0),
            CanEdit: true);
    }

    private ThxAudioState ReadV3State(ThxHeadsetProfile profile, string containerId)
    {
        var spatial = _v3!.GetSpatial(containerId);
        var presets = ReadV3Presets(containerId);
        var selected = TryRead(() => ThxVendorJson.ParseSelectedPresetValue(_v3.GetPreset(containerId)));
        var gains = TryRead(() => ThxVendorJson.ParseGains(_v3.GetEqGains(containerId))) ?? [];
        return new ThxAudioState(
            ThxAudioEndpoint.Headphones,
            spatial,
            true,
            selected,
            presets,
            ToBands(gains),
            new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal),
            new ThxAudioEnhancement(
                _v3.GetNormalization(containerId),
                ClampPercent(_v3.GetNormalizationLevel(containerId))),
            new ThxAudioEnhancement(
                _v3.GetVocalClarity(containerId),
                ClampPercent(_v3.GetVocalClarityLevel(containerId))),
            true,
            new ThxAudioEnhancement(
                _v3.GetSidetone(containerId),
                SidetonePercent(_v3.GetSidetoneLevel(containerId))));
    }

    private IReadOnlyList<ThxAudioPreset> ReadV3Presets(string containerId)
    {
        var entries = TryRead(() => ThxVendorJson.ParsePresetEntries(_v3!.GetPresetList(containerId)));
        return entries?.Select(entry => new ThxAudioPreset(entry.Id, entry.Name)).ToArray() ?? [];
    }

    private IReadOnlyList<ThxAudioPreset> ReadV4Presets()
    {
        var entries = TryRead(() => ThxVendorJson.ParsePresetEntries(
            _v4!.GetPresetList(_v4Device!, PresetGroupId)));
        return entries?.Select(entry => new ThxAudioPreset(entry.Id, entry.Name)).ToArray() ?? [];
    }

    private ThxAudioEnhancement? ReadV4Enhancement(string enabledProperty, string levelProperty)
    {
        try
        {
            var enabled = ThxVendorJson.ParseBoolean(_v4!.GetProperty(_v4Device!, enabledProperty));
            var level = ThxVendorJson.ParseFloat(_v4.GetProperty(_v4Device!, levelProperty), 0, 1);
            return new ThxAudioEnhancement(
                enabled,
                checked((int)Math.Round(level * 100, MidpointRounding.AwayFromZero)));
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            return null;
        }
    }

    private double ReadV4Float(string propertyId)
    {
        try
        {
            return ThxVendorJson.ParseFloat(_v4!.GetProperty(_v4Device!, propertyId), -24, 24);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            return 0;
        }
    }

    private static IReadOnlyList<ThxAudioBand> ToBands(IReadOnlyList<double> gains) =>
        Frequencies
            .Select((frequency, index) => new ThxAudioBand(
                frequency,
                index < gains.Count ? gains[index] : 0))
            .ToArray();

    private static bool ValidGains(IReadOnlyList<double> gains) =>
        gains.All(value => double.IsFinite(value) && value is >= -12 and <= 12);

    private static int ClampPercent(int value) => Math.Clamp(value, 0, 100);

    private static int SidetonePercent(int raw)
    {
        if (raw <= SidetoneMinimum)
        {
            return 0;
        }
        return Math.Clamp(
            (int)Math.Round(
                (raw - SidetoneMinimum) * 100d / (SidetoneMaximum - SidetoneMinimum),
                MidpointRounding.AwayFromZero),
            0,
            100);
    }

    private static T? TryRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or InvalidOperationException or JsonException)
        {
            return default;
        }
    }

    private static bool TryParseProductId(string? value, out ushort productId) =>
        ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out productId);

    private static ThxAudioHelperResponse Fail(string error) => new(
        ThxAudioHelperProtocol.Version,
        false,
        null,
        error);
}
