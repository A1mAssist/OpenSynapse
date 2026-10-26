using System.Security.Cryptography;
using System.Text.Json;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

public sealed class WindowsThxAudioController : IThxAudioController, IAsyncDisposable
{
    private static readonly HttpClient ComponentClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public static bool IsSupportedModel() => ThxAudioAdmission.IsSupportedHostModel();
    private readonly ThxHelperProcess _helper = new();

    public Task<ThxAudioStatus> ReadAsync(
        ThxAudioEndpoint endpoint,
        CancellationToken cancellationToken = default) =>
        SendAsync(new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            "read",
            EndpointName(endpoint)), cancellationToken);

    public Task<ThxAudioStatus> SetSpatialAsync(
        ThxAudioEndpoint endpoint,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SendAsync(new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            "setSpatial",
            EndpointName(endpoint),
            Enabled: enabled), cancellationToken);

    public Task<ThxAudioStatus> SetEqualizerAsync(
        ThxAudioEndpoint endpoint,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SendAsync(new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            "setEqualizer",
            EndpointName(endpoint),
            Enabled: enabled), cancellationToken);

    public Task<ThxAudioStatus> SelectPresetAsync(
        ThxAudioEndpoint endpoint,
        string presetId,
        IReadOnlyList<double> gainsDb,
        CancellationToken cancellationToken = default) =>
        SendAsync(new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            "selectPreset",
            EndpointName(endpoint),
            PresetId: presetId,
            GainsDb: gainsDb.ToArray()), cancellationToken);

    public Task<ThxAudioStatus> SetEnhancementAsync(
        ThxAudioEndpoint endpoint,
        ThxAudioEnhancementKind kind,
        bool enabled,
        int? levelPercent,
        CancellationToken cancellationToken = default) =>
        SendAsync(new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            "setEnhancement",
            EndpointName(endpoint),
            Enabled: enabled,
            Enhancement: kind == ThxAudioEnhancementKind.VoiceClarity
                ? "voiceClarity"
                : "soundNormalization",
            LevelPercent: levelPercent), cancellationToken);

    public async Task<ThxAudioStatus> InstallNativeComponentAsync(
        ThxAudioEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var admission = ThxAudioAdmission.Check();
        if (admission.Status.IsAvailable)
        {
            return await ReadAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        if (admission.Status.Error != "ThxNativeMissing")
        {
            return admission.Status;
        }

        var destination = ThxAudioAdmission.NativeDllPath;
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The THX component path has no directory.");
        var temporary = Path.Combine(
            directory,
            $"{ThxAudioAdmission.NativeDllName}.{Guid.NewGuid():N}.download");
        try
        {
            Directory.CreateDirectory(directory);
            using var response = await ComponentClient.GetAsync(
                ThxAudioAdmission.NativeDllUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } declaredLength &&
                declaredLength != ThxAudioAdmission.NativeDllLength)
            {
                return DownloadFailure("ThxDownloadVerificationFailed");
            }

            await using (var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temporary).Length != ThxAudioAdmission.NativeDllLength)
            {
                return DownloadFailure("ThxDownloadVerificationFailed");
            }

            using (var stream = new FileStream(
                temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!string.Equals(
                        Convert.ToHexString(SHA256.HashData(stream)),
                        ThxAudioAdmission.NativeDllSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return DownloadFailure("ThxDownloadVerificationFailed");
                }
            }

            File.Move(temporary, destination, overwrite: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DownloadFailure("ThxDownloadFailed");
        }
        catch (HttpRequestException)
        {
            return DownloadFailure("ThxDownloadFailed");
        }
        catch (UnauthorizedAccessException)
        {
            return DownloadFailure("ThxDownloadWriteFailed");
        }
        catch (IOException)
        {
            return DownloadFailure("ThxDownloadFailed");
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return await ReadAsync(endpoint, cancellationToken).ConfigureAwait(false);
    }

    private static ThxAudioStatus DownloadFailure(string error) =>
        new(ThxAudioAvailability.ComponentsUnavailable, error, null);

    public ValueTask DisposeAsync() => _helper.DisposeAsync();

    private async Task<ThxAudioStatus> SendAsync(
        ThxAudioHelperRequest request,
        CancellationToken cancellationToken)
    {
        var admission = ThxAudioAdmission.Check();
        if (admission.Profile is null || !admission.Status.IsAvailable)
        {
            return admission.Status;
        }

        return await _helper.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static string EndpointName(ThxAudioEndpoint endpoint) =>
        endpoint == ThxAudioEndpoint.Speakers ? "speakers" : "headphones";
}
