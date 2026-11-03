using System.Text;

namespace OpenSynapse.Windows.Lighting;

internal sealed record ChromaRestRequest(string Method, string Path, string Body)
{
    internal sealed class InvalidRequest(int status) : IOException
    {
        internal int Status { get; } = status;
    }

    internal static async Task<ChromaRestRequest?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var requestLine = await ReadLineAsync(stream, 8192, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine)) return null;
        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) throw new InvalidRequest(400);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var remaining = 32 * 1024;
        while (true)
        {
            var line = await ReadLineAsync(stream, Math.Min(8192, remaining), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidRequest(400);
            remaining -= line.Length + 2;
            if (remaining < 0) throw new InvalidRequest(413);
            if (line.Length == 0) break;
            var separator = line.IndexOf(':');
            if (separator <= 0 || !headers.TryAdd(line[..separator].Trim(), line[(separator + 1)..].Trim()))
                throw new InvalidRequest(400);
        }
        if (headers.TryGetValue("Transfer-Encoding", out var encoding) &&
            !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)) throw new InvalidRequest(400);
        var length = 0;
        if (headers.TryGetValue("Content-Length", out var rawLength) &&
            (!int.TryParse(rawLength, out length) || length < 0)) throw new InvalidRequest(400);
        if (length > 2 * 1024 * 1024) throw new InvalidRequest(413);
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        try { return new(parts[0], parts[1], new UTF8Encoding(false, true).GetString(body)); }
        catch (DecoderFallbackException) { throw new InvalidRequest(400); }
    }

    private static async Task<string?> ReadLineAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) != 0)
        {
            if (one[0] == '\n')
            {
                if (bytes.Count > 0 && bytes[^1] == '\r') bytes.RemoveAt(bytes.Count - 1);
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
            if (bytes.Count >= limit) throw new InvalidRequest(413);
            bytes.Add(one[0]);
        }
        if (bytes.Count != 0) throw new InvalidRequest(400);
        return null;
    }
}
