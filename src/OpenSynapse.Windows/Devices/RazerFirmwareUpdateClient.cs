using System.Net;
using System.Text.RegularExpressions;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

public sealed record FirmwareUpdateCheckResult(
    string? PublishedBios,
    string? PublishedEc,
    string? PublishedMcu,
    string? PublishedVbios,
    IReadOnlyList<string> NewerComponents,
    string? DownloadUrl = null)
{
    public bool HasUpdate => NewerComponents.Count > 0;
}

public sealed partial class RazerFirmwareUpdateClient
{
    private const string SupportHost = "https://mysupport.razer.com";

    private static readonly HttpClient Client = CreateClient();

    public async Task<FirmwareUpdateCheckResult> CheckAsync(
        FirmwareVersions installed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installed);
        var model = RazerHostModel.Read();
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Could not identify this Razer Blade model.");
        }

        var query = RazerHostModel.ReadModelCode() ?? model;
        var html = await ReadDriversPageAsync(query, cancellationToken);
        var result = Parse(html, installed);
        if (!result.HasUpdate)
        {
            return result;
        }

        string? downloadUrl = null;
        try
        {
            downloadUrl = await FindUpdaterUrlAsync(
                html,
                result.NewerComponents[0],
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            // The version check is still useful when Razer omits a direct link.
        }

        return result with { DownloadUrl = downloadUrl };
    }

    internal static FirmwareUpdateCheckResult Parse(string html, FirmwareVersions installed)
    {
        var rows = ExtractRows(html);
        var publishedBios = FindVersion(rows, "BIOS Firmware Updater");
        var publishedEc = FindVersion(rows, "EC Firmware Updater");
        var publishedMcu = FindVersion(rows, "MCU Firmware Updater");
        var publishedVbios = FindVbiosVersions(rows);

        if (publishedBios is null && publishedEc is null && publishedMcu is null && publishedVbios is null)
        {
            throw new InvalidOperationException(
                "Razer's support page did not contain recognized firmware versions.");
        }

        var newer = new List<string>();
        if (IsNewer(installed.Bios, publishedBios)) newer.Add("BIOS");
        if (IsNewer(installed.Ec, publishedEc)) newer.Add("EC");
        if (IsNewer(installed.Mcu, publishedMcu)) newer.Add("MCU");

        return new FirmwareUpdateCheckResult(
            publishedBios,
            publishedEc,
            publishedMcu,
            publishedVbios,
            newer);
    }

    internal static string? FindDriversPageHref(string html)
    {
        string? supportPage = null;
        foreach (Match match in AnswerLinkRegex().Matches(html))
        {
            var title = Normalize(match.Groups["body"].Value);
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (title.Contains("Drivers", StringComparison.OrdinalIgnoreCase) &&
                title.Contains("Downloads", StringComparison.OrdinalIgnoreCase))
            {
                return href;
            }

            if (supportPage is null &&
                title.Contains("Support", StringComparison.OrdinalIgnoreCase) &&
                title.Contains("FAQ", StringComparison.OrdinalIgnoreCase))
            {
                supportPage = href;
            }
        }

        return supportPage;
    }

    internal static string? FindUpdaterAnswerHref(string html, string component)
    {
        string? fallback = null;
        foreach (Match match in AnswerLinkRegex().Matches(html))
        {
            var title = Normalize(match.Groups["body"].Value);
            if (!title.Contains(component, StringComparison.OrdinalIgnoreCase) ||
                !title.Contains("Updater", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("How to", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (title.Contains("Razer Blade", StringComparison.OrdinalIgnoreCase))
            {
                return href;
            }

            fallback ??= href;
        }

        return fallback;
    }

    internal static string? ExtractDownloadUrl(string html)
    {
        foreach (Match match in RzrLinkRegex().Matches(html))
        {
            if (!match.Value.EndsWith("/supporte", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }
        }

        return null;
    }

    private static async Task<string> ReadDriversPageAsync(
        string modelQuery,
        CancellationToken cancellationToken)
    {
        var searchUri = new Uri(
            $"{SupportHost}/app/answers/list/kw/{Uri.EscapeDataString(modelQuery)}");
        var searchHtml = await ReadPageAsync(searchUri, cancellationToken);
        var driversHref = FindDriversPageHref(searchHtml)
            ?? throw new InvalidOperationException(
                $"Razer's support search did not find a drivers page for {modelQuery}.");
        return await ReadPageAsync(ResolveSupportUri(driversHref), cancellationToken);
    }

    private static async Task<string?> FindUpdaterUrlAsync(
        string driversHtml,
        string component,
        CancellationToken cancellationToken)
    {
        var answerHref = FindUpdaterAnswerHref(driversHtml, component)
            ?? throw new InvalidOperationException(
                $"Razer's support page did not link the {component} updater.");
        var answerHtml = await ReadPageAsync(ResolveSupportUri(answerHref), cancellationToken);
        return ExtractDownloadUrl(answerHtml)
            ?? throw new InvalidOperationException(
                $"Razer's {component} updater page did not contain a download link.");
    }

    private static Uri ResolveSupportUri(string href) =>
        new(new Uri(SupportHost), href);

    private static async Task<string> ReadPageAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var html = await GetStringAsync(uri, cancellationToken);
        if (html.Contains("browserEnvironmentDetection", StringComparison.Ordinal))
        {
            html = await GetStringAsync(uri, cancellationToken);
        }

        return html;
    }

    private static async Task<string> GetStringAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var finalHost = response.RequestMessage?.RequestUri?.Host;
        if (finalHost is null ||
            (!finalHost.Equals("razer.com", StringComparison.OrdinalIgnoreCase) &&
             !finalHost.EndsWith(".razer.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Razer's support page resolved to an unapproved destination.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static IReadOnlyList<string> ExtractRows(string html) =>
        HtmlTableRowRegex()
            .Matches(html)
            .Select(match => Normalize(match.Groups["body"].Value))
            .ToArray();

    private static string? FindVersion(IReadOnlyList<string> rows, string label)
    {
        foreach (var row in rows)
        {
            var index = row.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var match = NumericVersionRegex().Match(row[(index + label.Length)..]);
            if (match.Success)
            {
                return match.Groups["version"].Value;
            }
        }

        return null;
    }

    private static string? FindVbiosVersions(IReadOnlyList<string> rows)
    {
        var versions = rows
            .Where(row => row.Contains("vBIOS Firmware Updater", StringComparison.OrdinalIgnoreCase))
            .SelectMany(row => VbiosVersionRegex().Matches(row))
            .Select(match => $"RTX {match.Groups["gpu"].Value.Trim()} {match.Groups["version"].Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return versions.Length == 0 ? null : string.Join(" · ", versions);
    }

    private static bool IsNewer(string installed, string? published)
    {
        return Version.TryParse(installed.Trim().TrimStart('v', 'V'), out var installedVersion) &&
               Version.TryParse(published?.Trim().TrimStart('v', 'V'), out var publishedVersion) &&
               publishedVersion > installedVersion;
    }

    private static string Normalize(string html)
    {
        var text = WebUtility.HtmlDecode(TagRegex().Replace(html, " "));
        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = 512 * 1024,
        };
    }

    [GeneratedRegex("<tr\\b[^>]*>(?<body>.*?)</tr\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlTableRowRegex();

    [GeneratedRegex(
        "<a\\b[^>]*href=[\"'](?<href>[^\"']*a_id/\\d+[^\"']*)[\"'][^>]*>(?<body>.*?)</a\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnswerLinkRegex();

    [GeneratedRegex("https://rzr\\.to/[A-Za-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex RzrLinkRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("\\bv?\\s*(?<version>\\d+(?:\\.\\d+)+)")]
    private static partial Regex NumericVersionRegex();

    [GeneratedRegex(
        "RTX\\s*(?<gpu>\\d{4}(?:\\s*Ti)?)\\s*:\\s*v?\\s*(?<version>[0-9A-F]+(?:\\.[0-9A-F]+)+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex VbiosVersionRegex();
}
