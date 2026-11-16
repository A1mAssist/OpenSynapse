using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerMappingAssetService
{
    private static AssetManifest? TryReadCachedManifest(string path)
    {
        try
        {
            return File.Exists(path)
                ? AssetManifest.Parse(File.ReadAllText(path))
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsUsableCache(string path, int maxBytes)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length > 0 && file.Length <= maxBytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static async Task WriteCacheAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string DeviceDirectory(ushort productId) =>
        Path.Combine(_cacheRoot, productId.ToString(CultureInfo.InvariantCulture));

    private static string ResolveProductUrl(ushort productId, string relativeUrl) =>
        FormattableString.Invariant(
            $"{UiRoot}{UiPageId(productId)}/ui/{relativeUrl.TrimStart('.', '/')}");
}
