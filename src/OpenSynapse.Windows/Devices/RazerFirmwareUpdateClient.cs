using System.Net;
using System.Text.RegularExpressions;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

public sealed record FirmwareUpdateCheckResult(
    string? PublishedBios,
    string? PublishedEc,
    string? PublishedMcu,
    string? PublishedVbios,
    IReadOnlyList<string> NewerComponents)
{
    public bool HasUpdate => NewerComponents.Count > 0;
}

public sealed partial class RazerFirmwareUpdateClient
{
    public const string Rz090528SupportPage =
        "https://mysupport.razer.com/app/answers/detail/a_id/19821/~/razer-blade-16-%282025%29-%7C-rz09-0528x-drivers-%26-downloads";

    private static readonly HttpClient Client = CreateClient();

    public async Task<FirmwareUpdateCheckResult> CheckAsync(
        FirmwareVersions installed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installed);
        var html = await ReadPageAsync(cancellationToken);
        return Parse(html, installed);
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

    private static async Task<string> ReadPageAsync(CancellationToken cancellationToken)
    {
        var html = await GetStringAsync(cancellationToken);
        if (html.Contains("browserEnvironmentDetection", StringComparison.Ordinal))
        {
            html = await GetStringAsync(cancellationToken);
        }

        return html;
    }

    private static async Task<string> GetStringAsync(CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(
            Rz090528SupportPage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var finalHost = response.RequestMessage?.RequestUri?.Host;
        if (finalHost is null ||
            !finalHost.EndsWith("razer.com", StringComparison.OrdinalIgnoreCase))
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
