using System.Text.Json;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

internal sealed class ThxAudioHelperSession
{
    private const string PresetGroupId = "profile_presets";
    private const string SpatialPropertyId = "spatial_enabled";
    private const string EqualizerPropertyId = "eq_enabled";
    private const string NormalizationEnabledPropertyId = "volume_leveling_enabled";
    private const string NormalizationLevelPropertyId = "volume_leveling_level";
    private const string VoiceClarityEnabledPropertyId = "voice_clarity_enabled";
    private const string VoiceClarityLevelPropertyId = "voice_clarity_level";
    private const string FloatPropertyType = "Float";

    private static readonly IReadOnlyDictionary<string, string> LogicalToNativePreset =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["game"] = "game",
            ["movie"] = "movie",
            ["music"] = "music",
            ["voice"] = "podcast",
            ["custom"] = "custom",
            ["default"] = "default",
        };

    private static readonly IReadOnlyDictionary<string, string> PresetNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["game"] = "Game",
            ["movie"] = "Movie",
            ["music"] = "Music",
            ["voice"] = "Voice",
            ["custom"] = "Custom",
            ["default"] = "Default",
        };

    private ThxAudioNativeClient? _native;
    private ThxAudioProfile? _profile;
    private readonly Dictionary<ThxAudioEndpoint, string> _devices = [];

    internal ThxAudioHelperResponse Execute(ThxAudioHelperRequest request)
    {
        var admission = ThxAudioAdmission.Check();
        if (admission.Profile is null || !admission.Status.IsAvailable)
        {
            return new ThxAudioHelperResponse(
                ThxAudioHelperProtocol.Version,
                false,
                admission.Status,
                admission.Status.Error ?? "ThxUnavailable");
        }

        try
        {
            var endpoint = ParseEndpoint(request.Endpoint);
            var profile = admission.Profile;
            if (_profile is not null && _profile != profile)
            {
                return Fail("ThxDeviceChanged");
            }

            _profile = profile;
            _native ??= new ThxAudioNativeClient();
            var device = EnsureDevice(profile, endpoint);
            if (!_native.CheckStatus(device))
            {
                return Fail("ThxServiceNotReady");
            }

            var accepted = request.Operation switch
            {
                "read" => true,
                "setSpatial" => SetBoolean(device, SpatialPropertyId, request.Enabled),
                "setEqualizer" => SetBoolean(device, EqualizerPropertyId, request.Enabled),
                "selectPreset" => SelectPreset(profile, endpoint, device, request),
                "setEnhancement" => SetEnhancement(endpoint, device, request),
                _ => false,
            };
            if (!accepted)
            {
                return Fail("ThxOperationRejected");
            }

            var state = ReadState(profile, endpoint, device);
            return new ThxAudioHelperResponse(
                ThxAudioHelperProtocol.Version,
                true,
                new ThxAudioStatus(ThxAudioAvailability.Available, null, state),
                null);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or InvalidDataException or ArgumentException or JsonException)
        {
            return Fail(exception is ArgumentException ? "ThxInvalidRequest" : "ThxNativeFailure");
        }
    }

    private string EnsureDevice(ThxAudioProfile profile, ThxAudioEndpoint endpoint)
    {
        if (_devices.TryGetValue(endpoint, out var existing))
        {
            return existing;
        }

        var device = JsonSerializer.Serialize(new
        {
            productId = profile.ProductId,
            endpointId = endpoint == ThxAudioEndpoint.Speakers ? "speakers" : "headphones",
        });
        if (!_native!.AddDevice(device))
        {
            throw new InvalidOperationException("The THX device registration failed.");
        }
        _devices[endpoint] = device;
        return device;
    }

    private bool SetBoolean(string device, string propertyId, bool? enabled)
    {
        if (enabled is null)
        {
            throw new ArgumentException("The THX Boolean value is missing.");
        }

        var current = ThxVendorJson.ParseBoolean(_native!.GetProperty(device, propertyId));
        return current == enabled.Value ||
            _native.SetProperty(device, propertyId, enabled.Value ? "true" : "false", null);
    }

    private bool SelectPreset(
        ThxAudioProfile profile,
        ThxAudioEndpoint endpoint,
        string device,
        ThxAudioHelperRequest request)
    {
        if (request.PresetId is not { } presetId ||
            !LogicalToNativePreset.TryGetValue(presetId, out var nativePreset) ||
            request.GainsDb is not { } gains ||
            !ValidGains(endpoint, gains))
        {
            throw new ArgumentException("The THX preset or gain value is invalid.");
        }

        var presets = ThxVendorJson.ParsePresetIds(_native!.GetPresetList(device, PresetGroupId));
        if (!presets.Contains(nativePreset, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The selected THX preset is not available.");
        }

        var target = ThxAudioAdmission.ToNativeGains(profile, endpoint, gains);
        var current = ReadNativeGains(device);
        var selected = ThxVendorJson.ParseSelectedPreset(_native.GetSelectedPreset(device, PresetGroupId));
        if (!string.Equals(selected, nativePreset, StringComparison.Ordinal) &&
            !_native.SelectPreset(device, PresetGroupId, nativePreset))
        {
            return false;
        }

        if (current.SequenceEqual(target))
        {
            return true;
        }

        for (var index = 0; index < target.Count; index++)
        {
            if (!_native.SetProperty(
                    device,
                    $"eq_band_{index + 1}_gain_db",
                    JsonSerializer.Serialize(target[index]),
                    FloatPropertyType))
            {
                return false;
            }
        }
        return true;
    }

    private bool SetEnhancement(
        ThxAudioEndpoint endpoint,
        string device,
        ThxAudioHelperRequest request)
    {
        if (request.Enhancement is not { } kind ||
            request.Enabled is not { } enabled ||
            request.LevelPercent is not (null or >= 0 and <= 100))
        {
            throw new ArgumentException("The THX enhancement value is invalid.");
        }

        var (enabledProperty, levelProperty) = kind switch
        {
            "voiceClarity" => (VoiceClarityEnabledPropertyId, VoiceClarityLevelPropertyId),
            "soundNormalization" when endpoint == ThxAudioEndpoint.Speakers =>
                (NormalizationEnabledPropertyId, NormalizationLevelPropertyId),
            _ => throw new ArgumentException("The THX enhancement kind is invalid."),
        };

        var currentEnabled = ThxVendorJson.ParseBoolean(_native!.GetProperty(device, enabledProperty));
        if (currentEnabled != enabled &&
            !_native.SetProperty(device, enabledProperty, enabled ? "true" : "false", null))
        {
            return false;
        }

        if (!enabled || request.LevelPercent is not { } percent)
        {
            return true;
        }

        var currentLevel = ThxVendorJson.ParseFloat(
            _native.GetProperty(device, levelProperty), 0, 1);
        var targetLevel = percent / 100d;
        return Math.Abs(currentLevel - targetLevel) <= 0.000001 ||
            _native.SetProperty(
                device,
                levelProperty,
                JsonSerializer.Serialize(targetLevel),
                FloatPropertyType);
    }

    private ThxAudioState ReadState(
        ThxAudioProfile profile,
        ThxAudioEndpoint endpoint,
        string device)
    {
        var spatial = ThxVendorJson.ParseBoolean(_native!.GetProperty(device, SpatialPropertyId));
        var equalizer = ThxVendorJson.ParseBoolean(_native.GetProperty(device, EqualizerPropertyId));
        var nativePresets = ThxVendorJson.ParsePresetIds(_native.GetPresetList(device, PresetGroupId));
        var selectedNative = ThxVendorJson.ParseSelectedPreset(
            _native.GetSelectedPreset(device, PresetGroupId));
        var selected = selectedNative is null
            ? null
            : LogicalToNativePreset.FirstOrDefault(pair =>
                string.Equals(pair.Value, selectedNative, StringComparison.Ordinal)).Key;
        var presets = LogicalToNativePreset
            .Where(pair => nativePresets.Contains(pair.Value, StringComparer.Ordinal))
            .Select(pair => new ThxAudioPreset(pair.Key, PresetNames[pair.Key]))
            .ToArray();
        var gains = ReadNativeGains(device);
        var voiceClarity = ReadEnhancement(device, VoiceClarityEnabledPropertyId, VoiceClarityLevelPropertyId);
        var normalization = endpoint == ThxAudioEndpoint.Speakers
            ? ReadEnhancement(device, NormalizationEnabledPropertyId, NormalizationLevelPropertyId)
            : null;
        var factoryCurves = presets.ToDictionary(
            preset => preset.Id,
            preset => ThxAudioAdmission.FactoryCurve(profile, endpoint, preset.Id),
            StringComparer.Ordinal);
        return new ThxAudioState(
            endpoint,
            spatial,
            equalizer,
            selected,
            presets,
            ThxAudioAdmission.ToBands(profile, endpoint, gains),
            factoryCurves,
            normalization,
            voiceClarity,
            CanEdit: true);
    }

    private IReadOnlyList<double> ReadNativeGains(string device) =>
        Enumerable.Range(1, 10)
            .Select(index => ThxVendorJson.ParseFloat(
                _native!.GetProperty(device, $"eq_band_{index}_gain_db"),
                -12,
                12))
            .ToArray();

    private ThxAudioEnhancement ReadEnhancement(
        string device,
        string enabledProperty,
        string levelProperty)
    {
        var enabled = ThxVendorJson.ParseBoolean(_native!.GetProperty(device, enabledProperty));
        var level = ThxVendorJson.ParseFloat(_native.GetProperty(device, levelProperty), 0, 1);
        return new ThxAudioEnhancement(
            enabled,
            checked((int)Math.Round(level * 100, MidpointRounding.AwayFromZero)));
    }

    private static bool ValidGains(ThxAudioEndpoint endpoint, IReadOnlyList<double> gains) =>
        (gains.Count == 10 || endpoint == ThxAudioEndpoint.Speakers && gains.Count == 7) &&
        gains.All(value => double.IsFinite(value) && value is >= -12 and <= 12);

    private static ThxAudioEndpoint ParseEndpoint(string value) => value switch
    {
        "speakers" => ThxAudioEndpoint.Speakers,
        "headphones" => ThxAudioEndpoint.Headphones,
        _ => throw new ArgumentException("The THX endpoint is invalid."),
    };

    private static ThxAudioHelperResponse Fail(string error) => new(
        ThxAudioHelperProtocol.Version,
        false,
        null,
        error);
}
