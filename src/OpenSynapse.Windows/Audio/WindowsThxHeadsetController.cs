using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

/// <summary>
/// THX controls for a protocol-25 headset. The native DLL stays in the helper
/// process, and the V3/V4 split is carried by NativeVersionName.
/// </summary>
public sealed class WindowsThxHeadsetController : IThxAudioController, IAsyncDisposable
{
    private readonly ushort _productId;
    private readonly string? _containerId;
    private readonly ThxHelperProcess _helper = new();

    public WindowsThxHeadsetController(ushort productId, string? containerId)
    {
        _productId = productId;
        _containerId = containerId;
    }

    public static bool IsSupported(ushort productId) => ThxHeadsetAdmission.IsSupported(productId);

    public Task<ThxAudioStatus> ReadAsync(
        ThxAudioEndpoint endpoint,
        CancellationToken cancellationToken = default) =>
        SendAsync("read", endpoint, cancellationToken);

    public Task<ThxAudioStatus> SetSpatialAsync(
        ThxAudioEndpoint endpoint,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SendAsync("setSpatial", endpoint, cancellationToken, enabled: enabled);

    public Task<ThxAudioStatus> SetEqualizerAsync(
        ThxAudioEndpoint endpoint,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SendAsync("setEqualizer", endpoint, cancellationToken, enabled: enabled);

    public Task<ThxAudioStatus> SelectPresetAsync(
        ThxAudioEndpoint endpoint,
        string presetId,
        IReadOnlyList<double> gainsDb,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "selectPreset",
            endpoint,
            cancellationToken,
            presetId: presetId,
            gainsDb: gainsDb.ToArray());

    public Task<ThxAudioStatus> SetGainsAsync(
        ThxAudioEndpoint endpoint,
        IReadOnlyList<double> gainsDb,
        CancellationToken cancellationToken = default) =>
        SendAsync("setGains", endpoint, cancellationToken, gainsDb: gainsDb.ToArray());

    public Task<ThxAudioStatus> SetEnhancementAsync(
        ThxAudioEndpoint endpoint,
        ThxAudioEnhancementKind kind,
        bool enabled,
        int? levelPercent,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "setEnhancement",
            endpoint,
            cancellationToken,
            enabled: enabled,
            enhancement: kind switch
            {
                ThxAudioEnhancementKind.VoiceClarity => "voiceClarity",
                ThxAudioEnhancementKind.SoundNormalization => "soundNormalization",
                ThxAudioEnhancementKind.Sidetone => "sidetone",
                _ => "voiceClarity",
            },
            levelPercent: levelPercent);

    public async Task<ThxAudioStatus> InstallNativeComponentAsync(
        ThxAudioEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var status = ThxHeadsetAdmission.Check(_productId);
        if (status.Availability == ThxHeadsetAvailability.UnsupportedDevice ||
            status.Availability == ThxHeadsetAvailability.ServiceMissing)
        {
            return new ThxAudioStatus(ThxAudioAvailability.ComponentsUnavailable, status.ErrorCode, null);
        }
        if (status.Availability == ThxHeadsetAvailability.Available)
        {
            return await ReadAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }

        var installed = await ThxHeadsetAdmission.InstallNativeComponentAsync(
            _productId,
            cancellationToken).ConfigureAwait(false);
        if (installed.Availability != ThxHeadsetAvailability.Available)
        {
            return new ThxAudioStatus(
                ThxAudioAvailability.ComponentsUnavailable,
                installed.ErrorCode,
                null);
        }
        return await ReadAsync(endpoint, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _helper.DisposeAsync();

    private async Task<ThxAudioStatus> SendAsync(
        string operation,
        ThxAudioEndpoint endpoint,
        CancellationToken cancellationToken,
        bool? enabled = null,
        string? presetId = null,
        double[]? gainsDb = null,
        string? enhancement = null,
        int? levelPercent = null)
    {
        var admission = ThxHeadsetAdmission.Check(_productId);
        if (admission.Availability != ThxHeadsetAvailability.Available)
        {
            return new ThxAudioStatus(
                ThxAudioAvailability.ComponentsUnavailable,
                admission.ErrorCode,
                null);
        }

        var profile = ThxHeadsetAdmission.GetProfile(_productId)
            ?? throw new InvalidOperationException("The THX headset profile disappeared.");
        if (profile.NativeVersion == ThxNativeVersion.V3 &&
            string.IsNullOrWhiteSpace(_containerId))
        {
            return new ThxAudioStatus(
                ThxAudioAvailability.ComponentsUnavailable,
                "ThxContainerMissing",
                null);
        }

        var request = new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            operation,
            endpoint == ThxAudioEndpoint.Speakers ? "speakers" : "headphones",
            enabled,
            presetId,
            gainsDb,
            enhancement,
            levelPercent,
            profile.NativeVersionName,
            _productId.ToString("X4"),
            _containerId);
        return await _helper.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
