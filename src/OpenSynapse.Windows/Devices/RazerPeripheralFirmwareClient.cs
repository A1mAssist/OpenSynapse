using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerPeripheralFirmwareCheck(
    ushort ProductId,
    string? Name,
    string? CurrentVersion,
    string LatestVersion,
    int? Comparison,
    string? DownloadUrl,
    string? GuideUrl,
    string? FileSize,
    string? ReleaseDate,
    IReadOnlyList<string> ReleaseNotes)
{
    public bool HasUpdate => Comparison > 0;

    public string? TargetUrl =>
        string.IsNullOrWhiteSpace(DownloadUrl) ? GuideUrl : DownloadUrl;
}

public enum RazerFirmwareInstallFailure
{
    None,
    DownloadFailed,
    InvalidPackage,
    Cancelled,
    LaunchFailed,
}

public sealed record RazerFirmwareInstallResult(
    bool Success,
    RazerFirmwareInstallFailure Failure = RazerFirmwareInstallFailure.None,
    string? Error = null);

/// <summary>
/// Razer publishes peripheral firmware manifests for Synapse Web under
/// fwsdk.razersynapse.com; older models only have a support article, whose
/// search index carries the version and the official updater link.
/// </summary>
public static class RazerPeripheralFirmwareClient
{
    private const string ManifestRoot = "https://fwsdk.razersynapse.com/FW-releases/";

    // Public search widget credentials published by mysupport.razer.com.
    private const string SupportSearchUrl =
        "https://W236KITOIH-dsn.algolia.net/1/indexes/razerCSAlgoliaSupportIndex/query" +
        "?x-algolia-application-id=W236KITOIH&x-algolia-api-key=d85b74c0a72a5ff70854bf5cc6ea5caf";

    private static readonly Regex FirmwareVersionPattern = new(
        @"(?:Firmware(?:\s+(?:updater|update|version))?|FW)\s+v?([0-9]+(?:\.[0-9]+)+)(?:_r[0-9]+)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DownloadLinkPattern = new(
        @"https://rzr\.to/[A-Za-z0-9]+",
        RegexOptions.CultureInvariant);

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static readonly HttpClient DownloadClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    internal static string FirmwareCacheDirectory => AppDataPaths.Combine("FirmwareCache");

    public static async Task<RazerPeripheralFirmwareCheck?> CheckAsync(
        ushort productId,
        string? currentVersion,
        string? deviceName = null,
        CancellationToken cancellationToken = default)
    {
        var manifestProductId = RazerProductAliases.Canonical(productId);
        var configJson = await TryReadAsync(
            $"{ManifestRoot}{manifestProductId}/deviceConfig.json", cancellationToken).ConfigureAwait(false);
        if (configJson is null)
        {
            return string.IsNullOrWhiteSpace(deviceName)
                ? null
                : await CheckSupportAsync(productId, currentVersion, deviceName, cancellationToken)
                    .ConfigureAwait(false);
        }

        var latestVersion = (string?)ParseObject(configJson)?["lastVersion"];
        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            return string.IsNullOrWhiteSpace(deviceName)
                ? null
                : await CheckSupportAsync(productId, currentVersion, deviceName, cancellationToken)
                    .ConfigureAwait(false);
        }

        string? releaseJson = null;
        if (RazerFirmwareUpdateClient.CompareVersions(currentVersion, latestVersion) > 0)
        {
            releaseJson = await TryReadAsync(
                $"{ManifestRoot}{manifestProductId}/{Uri.EscapeDataString(latestVersion)}.json",
                cancellationToken).ConfigureAwait(false);
        }

        return Parse(configJson, releaseJson, currentVersion,
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    /// <summary>
    /// Resolves the Razer short link, downloads the official updater, verifies
    /// its Authenticode signature and starts it elevated for the user.
    /// </summary>
    public static async Task<RazerFirmwareInstallResult> DownloadAndLaunchAsync(
        string downloadUrl,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return new(false, RazerFirmwareInstallFailure.DownloadFailed,
                "The updater link is not an HTTPS URL.");
        }

        try
        {
            using var response = await DownloadClient.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null ||
                !IsAllowedInstallerHost(finalUri.Host) ||
                !finalUri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return new(false, RazerFirmwareInstallFailure.DownloadFailed,
                    "Razer returned an unexpected updater location.");
            }

            Directory.CreateDirectory(FirmwareCacheDirectory);
            PruneCache();
            var path = Path.Combine(
                FirmwareCacheDirectory, Path.GetFileName(finalUri.AbsolutePath));
            if (!File.Exists(path) || !IsVerified(path))
            {
                if (!await DownloadAsync(response, path, progress, cancellationToken).ConfigureAwait(false))
                {
                    return new(false, RazerFirmwareInstallFailure.DownloadFailed,
                        "The updater download did not complete.");
                }
            }

            try
            {
                RazerDriverInstaller.VerifyPackage(path);
            }
            catch (InvalidDataException exception)
            {
                TryDelete(path);
                return new(false, RazerFirmwareInstallFailure.InvalidPackage, exception.Message);
            }

            progress?.Report(100);
            return Launch(path);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, RazerFirmwareInstallFailure.DownloadFailed,
                "The firmware download timed out.");
        }
        catch (HttpRequestException exception)
        {
            return new(false, RazerFirmwareInstallFailure.DownloadFailed, exception.Message);
        }
        catch (IOException exception)
        {
            return new(false, RazerFirmwareInstallFailure.DownloadFailed, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new(false, RazerFirmwareInstallFailure.DownloadFailed, exception.Message);
        }
    }

    private static async Task<bool> DownloadAsync(
        HttpResponseMessage response,
        string path,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var temporary = path + ".download";
        var length = response.Content.Headers.ContentLength;
        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        try
        {
            long total = 0;
            var lastPercent = -1;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                if (length > 0)
                {
                    var percent = (int)(total * 100 / length.Value);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(percent);
                    }
                }
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            target.Close();
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        File.Move(temporary, path, overwrite: true);
        return true;
    }

    private static RazerFirmwareInstallResult Launch(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path)!,
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            return Process.Start(startInfo) is null
                ? new(false, RazerFirmwareInstallFailure.LaunchFailed, "The updater did not start.")
                : new(true);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new(false, RazerFirmwareInstallFailure.Cancelled);
        }
        catch (Win32Exception exception)
        {
            return new(false, RazerFirmwareInstallFailure.LaunchFailed, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new(false, RazerFirmwareInstallFailure.LaunchFailed, exception.Message);
        }
    }

    internal static bool IsAllowedInstallerHost(string host) =>
        host.Equals("dl.razerzone.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("cdn.razersynapse.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsVerified(string path)
    {
        try
        {
            RazerDriverInstaller.VerifyPackage(path);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static void PruneCache()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(FirmwareCacheDirectory))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30))
                {
                    TryDelete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<RazerPeripheralFirmwareCheck?> CheckSupportAsync(
        ushort productId,
        string? currentVersion,
        string deviceName,
        CancellationToken cancellationToken)
    {
        var query = new JsonObject
        {
            ["query"] = $"{SearchName(deviceName)} Firmware Updater",
            ["hitsPerPage"] = 10,
        }.ToJsonString();
        using var request = new HttpRequestMessage(HttpMethod.Post, SupportSearchUrl)
        {
            Content = new StringContent(query, Encoding.UTF8, "application/json"),
        };
        request.Headers.Referrer = new Uri("https://mysupport.razer.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://mysupport.razer.com");

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = ParseObject(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return ParseSupport(payload, productId, currentVersion, deviceName);
    }

    internal static RazerPeripheralFirmwareCheck? ParseSupport(
        JsonObject? payload,
        ushort productId,
        string? currentVersion,
        string deviceName)
    {
        if (payload?["hits"] is not JsonArray hits)
        {
            return null;
        }

        var modelKey = ModelKey(deviceName);
        foreach (var hit in hits.OfType<JsonObject>())
        {
            var title = (string?)hit["Title_en_us"];
            var answer = (string?)hit["Answer_en_us"];
            if (title is null || answer is null || !TitleMatches(title, modelKey))
            {
                continue;
            }

            var version = FirmwareVersionPattern.Match(answer);
            var link = DownloadLinkPattern.Match(answer);
            if (!version.Success || !link.Success)
            {
                continue;
            }

            var latestVersion = version.Groups[1].Value;
            return new RazerPeripheralFirmwareCheck(
                productId,
                deviceName,
                currentVersion,
                latestVersion,
                RazerFirmwareUpdateClient.CompareVersions(currentVersion, latestVersion),
                link.Value,
                (string?)hit["URL_en_us"],
                null,
                null,
                ReleaseNotes(answer));
        }

        return null;
    }

    private static bool TitleMatches(string title, string modelKey)
    {
        var suffix = title.IndexOf("Firmware Updater", StringComparison.OrdinalIgnoreCase);
        var device = suffix > 0 ? title[..suffix] : title;
        return ModelKey(device) == modelKey;
    }

    private static string ModelKey(string value)
    {
        var text = SearchName(value).ToLowerInvariant();
        text = text.Replace("razer", string.Empty, StringComparison.Ordinal);
        return new string(text.Where(char.IsLetterOrDigit).ToArray());
    }

    internal static string SearchName(string value) =>
        Regex.Replace(
            value,
            @"\s*\((?:wired|wireless|bluetooth|2\.4\s*ghz)\)\s*$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IReadOnlyList<string> ReleaseNotes(string answer)
    {
        var start = answer.IndexOf("Release notes", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return [];
        }

        start += "Release notes".Length;
        var end = answer.Length;
        foreach (var marker in new[]
                 {
                     "Follow the detailed instructions",
                     "Installation Instructions",
                     "Step-by-step Process",
                 })
        {
            var index = answer.IndexOf(marker, start, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index < end)
            {
                end = index;
            }
        }

        var notes = answer[start..end].Trim();
        return notes.Length == 0 ? [] : [notes];
    }

    internal static RazerPeripheralFirmwareCheck? Parse(
        string configJson,
        string? releaseJson,
        string? currentVersion,
        string language)
    {
        if (ParseObject(configJson) is not { } config)
        {
            return null;
        }

        var latestVersion = (string?)config["lastVersion"];
        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            return null;
        }

        var release = releaseJson is null ? null : ParseObject(releaseJson);
        var notes = release?["notes"] is JsonObject notesByLanguage
            ? notesByLanguage[language] ?? notesByLanguage["en"] ?? notesByLanguage["zh"]
            : null;

        return new RazerPeripheralFirmwareCheck(
            ushort.TryParse((string?)config["productId"], out var productId) ? productId : (ushort)0,
            (string?)config["name"],
            currentVersion,
            latestVersion,
            RazerFirmwareUpdateClient.CompareVersions(currentVersion, latestVersion),
            (string?)release?["cfuDownloadUrl"],
            (string?)release?["guideUrl"],
            (string?)release?["fileSize"],
            (string?)release?["releaseDate"],
            notes is JsonArray array
                ? array
                    .OfType<JsonValue>()
                    .Select(value => value.TryGetValue<string>(out var text) ? text : null)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Cast<string>()
                    .ToArray()
                : []);
    }

    private static async Task<string?> TryReadAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject? ParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
