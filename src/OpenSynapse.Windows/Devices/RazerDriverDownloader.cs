using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerDriverDownloadProgress(
    long BytesReceived,
    long TotalBytes,
    double BytesPerSecond)
{
    public double Fraction => TotalBytes > 0
        ? Math.Clamp((double)BytesReceived / TotalBytes, 0d, 1d)
        : 0d;

    public TimeSpan? Eta => TotalBytes > 0 && BytesPerSecond > 0
        ? TimeSpan.FromSeconds(Math.Max(0d, (TotalBytes - BytesReceived) / BytesPerSecond))
        : null;
}

/// <summary>
/// Downloads Razer's signed packages with the same proxy the user's browser uses.
/// Files large enough to benefit are split into byte ranges and fetched in
/// parallel; small files stay on one stream. Progress is reported as bytes so
/// the UI can show a bar, speed and ETA instead of an opaque "downloading".
/// </summary>
internal static class RazerDriverDownloader
{
    private const int SegmentThreshold = 4 * 1024 * 1024;
    private const int DefaultSegments = 4;
    private const int BufferSize = 128 * 1024;

    internal static async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<RazerDriverDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // A long-running app can outlive a proxy restart. Re-read the system
        // proxy per download instead of inheriting the process default.
        using var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = WebRequest.GetSystemWebProxy(),
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(15),
        };

        var (totalBytes, supportsRanges) = await ProbeAsync(
            client, url, cancellationToken).ConfigureAwait(false);
        var segments = supportsRanges && totalBytes >= SegmentThreshold
            ? DefaultSegments
            : 1;

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (segments == 1)
        {
            await DownloadSingleAsync(
                client, url, destinationPath, totalBytes, progress, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await DownloadSegmentsAsync(
            client, url, destinationPath, totalBytes, segments, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(long TotalBytes, bool SupportsRanges)> ProbeAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            return (response.Content.Headers.ContentRange?.Length ?? 0, true);
        }

        response.EnsureSuccessStatusCode();
        return (response.Content.Headers.ContentLength ?? 0, false);
    }

    private static async Task DownloadSingleAsync(
        HttpClient client,
        string url,
        string destinationPath,
        long totalBytes,
        IProgress<RazerDriverDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        totalBytes = response.Content.Headers.ContentLength ?? totalBytes;

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous);
        var tracker = new ProgressTracker(totalBytes, progress);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                tracker.Add(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (totalBytes > 0 && tracker.Received != totalBytes)
        {
            throw new IOException(
                $"Razer CDN returned {tracker.Received} of {totalBytes} bytes.");
        }
        tracker.Complete();
    }

    private static async Task DownloadSegmentsAsync(
        HttpClient client,
        string url,
        string destinationPath,
        long totalBytes,
        int segments,
        IProgress<RazerDriverDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            1,
            FileOptions.Asynchronous);
        file.SetLength(totalBytes);
        var handle = file.SafeFileHandle;
        var tracker = new ProgressTracker(totalBytes, progress);
        var segmentSize = (totalBytes + segments - 1) / segments;
        var tasks = new Task[segments];
        for (var index = 0; index < segments; index++)
        {
            var start = index * segmentSize;
            var end = Math.Min(totalBytes - 1, start + segmentSize - 1);
            tasks[index] = start > end
                ? Task.CompletedTask
                : DownloadSegmentAsync(
                    client, url, handle, start, end, tracker, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        tracker.Complete();
    }

    private static async Task DownloadSegmentAsync(
        HttpClient client,
        string url,
        SafeFileHandle handle,
        long start,
        long end,
        ProgressTracker tracker,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(start, end);
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new HttpRequestException(
                $"Razer CDN ignored the byte range {start}-{end} " +
                $"(HTTP {(int)response.StatusCode}).");
        }

        await using var source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var offset = start;
            int read;
            while (offset <= end &&
                   (read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                var count = (int)Math.Min(read, end - offset + 1);
                await RandomAccess.WriteAsync(
                    handle, buffer.AsMemory(0, count), offset, cancellationToken).ConfigureAwait(false);
                offset += count;
                tracker.Add(count);
            }

            if (offset != end + 1)
            {
                throw new IOException(
                    $"Razer CDN returned {offset - start} of {end - start + 1} bytes " +
                    $"for range {start}-{end}.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed class ProgressTracker(long totalBytes, IProgress<RazerDriverDownloadProgress>? progress)
    {
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private long _received;
        private long _lastReportTicks;

        public long Received => Interlocked.Read(ref _received);

        public void Add(int count)
        {
            var received = Interlocked.Add(ref _received, count);
            var now = Stopwatch.GetTimestamp();
            var last = Interlocked.Read(ref _lastReportTicks);
            if (now - last < Stopwatch.Frequency / 10 ||
                Interlocked.CompareExchange(ref _lastReportTicks, now, last) != last)
            {
                return;
            }

            Report(received);
        }

        public void Complete() => Report(Interlocked.Read(ref _received));

        private void Report(long received)
        {
            if (progress is null) return;
            var seconds = Math.Max(0.001d, _elapsed.Elapsed.TotalSeconds);
            progress.Report(new RazerDriverDownloadProgress(
                received, totalBytes, received / seconds));
        }
    }
}
