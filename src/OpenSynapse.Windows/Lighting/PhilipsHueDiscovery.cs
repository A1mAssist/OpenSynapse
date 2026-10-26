using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenSynapse.Windows.Lighting;

/// <summary>A bridge found by SSDP or Philips' N-UPnP service.</summary>
public sealed record PhilipsHueBridge(
    string Host,
    string? Id,
    string? Name,
    string? SoftwareVersion)
{
    public string DisplayName => string.Join(
        " · ",
        new[] { Name, Id, Host, SoftwareVersion }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
}

/// <summary>
/// Discovers Hue bridges without the middleware DLL. It mirrors the two wire-level
/// searches the Razer module uses: SSDP and N-UPnP.
/// </summary>
public sealed class PhilipsHueDiscovery : IDisposable
{
    private const string SsdpAddress = "239.255.255.250";
    private const int SsdpPort = 1900;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    private readonly HttpClient _http;
    private readonly Action<string>? _diagnostic;

    public PhilipsHueDiscovery(HttpMessageHandler? handler = null, Action<string>? diagnostic = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: true)
        {
            Timeout = DefaultTimeout,
        };
        _diagnostic = diagnostic;
    }

    public async Task<IReadOnlyList<PhilipsHueBridge>> DiscoverAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var scanTimeout = timeout ?? DefaultTimeout;
        var tasks = new[]
        {
            IgnoreFailuresAsync(DiscoverSsdpAsync(scanTimeout, cancellationToken), cancellationToken),
            IgnoreFailuresAsync(DiscoverNupnpAsync(cancellationToken), cancellationToken),
        };
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return Merge(results[0], results[1]);
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Parses one SSDP reply; non-Hue UPnP devices are ignored.</summary>
    public static PhilipsHueBridge? ParseSsdpResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in response.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        var location = headers.GetValueOrDefault("LOCATION");
        var server = headers.GetValueOrDefault("SERVER") ?? string.Empty;
        var bridgeId = headers.GetValueOrDefault("hue-bridgeid")?.Trim();
        if (string.IsNullOrWhiteSpace(location) ||
            (string.IsNullOrWhiteSpace(bridgeId) &&
             !server.Contains("IpBridge", StringComparison.OrdinalIgnoreCase)) ||
            !Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(bridgeId))
        {
            var usn = headers.GetValueOrDefault("USN") ?? string.Empty;
            bridgeId = Regex.Match(usn, "-([0-9a-zA-Z]{12})::").Groups[1].Value;
        }

        var version = Regex.Match(server, "IpBridge/([^\\s]+)", RegexOptions.IgnoreCase).Groups[1].Value;
        return new PhilipsHueBridge(
            uri.Host,
            string.IsNullOrWhiteSpace(bridgeId) ? null : bridgeId,
            null,
            string.IsNullOrWhiteSpace(version) ? null : version);
    }

    /// <summary>Parses the JSON returned by https://discovery.meethue.com.</summary>
    public static IReadOnlyList<PhilipsHueBridge> ParseNupnpResponse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var items = root as JsonArray ?? root?["data"] as JsonArray;
            if (items is null) return [];

            return items
                .Select(item => ReadString(item?["internalipaddress"]) is { } host &&
                                IPAddress.TryParse(host, out var address) &&
                                address.AddressFamily == AddressFamily.InterNetwork
                    ? new PhilipsHueBridge(host, ReadString(item?["id"]), null, null)
                    : null)
                .Where(bridge => bridge is not null)
                .Select(bridge => bridge!)
                .ToArray();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private async Task<IReadOnlyList<PhilipsHueBridge>> DiscoverSsdpAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var request = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "Man: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            "ST: ssdp:all\r\n\r\n");
        await udp.SendAsync(request, new IPEndPoint(IPAddress.Parse(SsdpAddress), SsdpPort), cancellationToken)
            .ConfigureAwait(false);

        var found = new Dictionary<string, PhilipsHueBridge>(StringComparer.OrdinalIgnoreCase);
        using var scan = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scan.CancelAfter(timeout);
        while (true)
        {
            UdpReceiveResult reply;
            try
            {
                reply = await udp.ReceiveAsync(scan.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (ParseSsdpResponse(Encoding.UTF8.GetString(reply.Buffer)) is not { } bridge) continue;
            found[bridge.Host] = bridge;
        }

        return found.Values.ToArray();
    }

    private async Task<IReadOnlyList<PhilipsHueBridge>> DiscoverNupnpAsync(CancellationToken cancellationToken)
    {
        var endpoints = new[]
        {
            "https://discovery.meethue.com",
            "https://discovery.meethue.com/api/nupnp",
        };
        var results = await Task.WhenAll(endpoints.Select(
            endpoint => DiscoverNupnpEndpointAsync(endpoint, cancellationToken))).ConfigureAwait(false);
        return results.FirstOrDefault(bridges => bridges.Count > 0) ?? [];
    }

    private async Task<IReadOnlyList<PhilipsHueBridge>> DiscoverNupnpEndpointAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await _http.GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return ParseNupnpResponse(json);
        }
        catch (HttpRequestException exception)
        {
            _diagnostic?.Invoke($"Hue discovery {endpoint} failed: {exception.Message}");
            return [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _diagnostic?.Invoke($"Hue discovery {endpoint} timed out.");
            return [];
        }
    }

    private async Task<IReadOnlyList<PhilipsHueBridge>> IgnoreFailuresAsync(
        Task<IReadOnlyList<PhilipsHueBridge>> task,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        catch (Exception exception) when (
            exception is HttpRequestException or SocketException or IOException or JsonException)
        {
            _diagnostic?.Invoke($"Hue discovery failed: {exception.Message}");
            return [];
        }
    }

    private static IReadOnlyList<PhilipsHueBridge> Merge(
        IReadOnlyList<PhilipsHueBridge> first,
        IReadOnlyList<PhilipsHueBridge> second)
    {
        var merged = new Dictionary<string, PhilipsHueBridge>(StringComparer.OrdinalIgnoreCase);
        foreach (var bridge in first.Concat(second))
        {
            if (!merged.TryGetValue(bridge.Host, out var existing))
            {
                merged[bridge.Host] = bridge;
                continue;
            }

            merged[bridge.Host] = existing with
            {
                Id = existing.Id ?? bridge.Id,
                Name = existing.Name ?? bridge.Name,
                SoftwareVersion = existing.SoftwareVersion ?? bridge.SoftwareVersion,
            };
        }

        return merged.Values
            .OrderBy(bridge => bridge.Host, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
