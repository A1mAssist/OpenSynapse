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
    string? DownloadUrl = null,
    string? DeviceName = null)
{
    public bool HasUpdate => NewerComponents.Count > 0;
}

public sealed class RazerSupportPageNotFoundException : InvalidOperationException
{
    public RazerSupportPageNotFoundException(string message) : base(message)
    {
    }
}

public sealed partial class RazerFirmwareUpdateClient
{
    private const string SupportHost = "https://mysupport.razer.com";

    private static readonly HttpClient Client = CreateClient();
    private readonly RazerDeviceRegistry _registry;

    public RazerFirmwareUpdateClient()
        : this(RazerDeviceRegistry.BuiltIn)
    {
    }

    internal RazerFirmwareUpdateClient(RazerDeviceRegistry registry) =>
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public async Task<FirmwareUpdateCheckResult> CheckAsync(
        FirmwareVersions installed,
        CancellationToken cancellationToken = default)
    {
        return await CheckAsync(installed, (string?)null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirmwareUpdateCheckResult> CheckAsync(
        FirmwareVersions installed,
        string? modelName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installed);
        var model = RazerHostModel.Read();
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Could not identify this Razer Blade model.");
        }

        var queries = BuildSearchQueries(RazerHostModel.ReadModelCode(), modelName, model);
        var html = await ReadDriversPageAsync(queries, cancellationToken);
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

    public Task<FirmwareUpdateCheckResult> CheckAsync(
        FirmwareVersions installed,
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(devices);
        var device = devices.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
            device.Access == DeviceAccessState.Available);
        var manifest = device is null ? null : _registry.Find(device.VendorId, device.ProductId);
        if (TryCreateManifestResult(installed, manifest?.Device) is { } result)
        {
            return Task.FromResult(result);
        }

        return CheckAsync(
            installed,
            ResolveModelName(manifest?.Device?.Name, device?.Name),
            cancellationToken);
    }

    internal static string? ResolveModelName(string? manifestName, string? fallbackName) =>
        string.IsNullOrWhiteSpace(manifestName) ? fallbackName : manifestName.Trim();

    internal static FirmwareUpdateCheckResult? TryCreateManifestResult(
        FirmwareVersions installed,
        RazerDeviceMetadata? device)
    {
        ArgumentNullException.ThrowIfNull(installed);
        var firmware = device?.Firmware;
        if (firmware?.Latest is { } latest)
        {
            // latestFirmwareInfo is the model's firmware package version; the MCU
            // reading is the closest installed component that Razer publishes.
            var newer = IsNewerOrUnknown(installed.Mcu, latest.Version);
            return new FirmwareUpdateCheckResult(
                PublishedBios: null,
                PublishedEc: null,
                PublishedMcu: latest.Version,
                PublishedVbios: null,
                NewerComponents: newer ? [$"Firmware {latest.Version}"] : [],
                DownloadUrl: newer ? latest.Url : null,
                DeviceName: device?.Name);
        }
        if (firmware?.Driver is { } driver)
        {
            // Windows exposes no installed driver version, so the manifest link is the only offer.
            return new FirmwareUpdateCheckResult(
                PublishedBios: null,
                PublishedEc: null,
                PublishedMcu: null,
                PublishedVbios: null,
                NewerComponents: [$"Driver {driver.Version}"],
                DownloadUrl: driver.Url,
                DeviceName: device?.Name);
        }
        return null;
    }

    private static bool IsNewerOrUnknown(string installed, string published)
    {
        var value = installed.Trim();
        if (value is "" or "--" ||
            !Version.TryParse(value.TrimStart('v', 'V'), out var installedVersion))
        {
            return true;
        }
        return Version.TryParse(published.Trim().TrimStart('v', 'V'), out var publishedVersion) &&
               publishedVersion > installedVersion;
    }

    internal static IReadOnlyList<string> BuildSearchQueries(
        string? modelCode,
        string? modelName,
        string? systemModel) =>
        new[] { modelCode, modelName, systemModel }
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Select(query => query!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
        IReadOnlyList<string> modelQueries,
        CancellationToken cancellationToken)
    {
        RazerSupportPageNotFoundException? lastFailure = null;
        foreach (var modelQuery in modelQueries)
        {
            var searchUri = new Uri(
                $"{SupportHost}/app/answers/list/kw/{Uri.EscapeDataString(modelQuery)}");
            var searchHtml = await ReadPageAsync(searchUri, cancellationToken);
            var driversHref = FindDriversPageHref(searchHtml);
            if (driversHref is not null)
            {
                return await ReadPageAsync(ResolveSupportUri(driversHref), cancellationToken);
            }

            lastFailure = new RazerSupportPageNotFoundException(
                $"Razer's support search did not find a drivers page for {modelQuery}.");
        }

        throw lastFailure ?? new RazerSupportPageNotFoundException(
            "Razer's support search did not find a drivers page for this model.");
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
