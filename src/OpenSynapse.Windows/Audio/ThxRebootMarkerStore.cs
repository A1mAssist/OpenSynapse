using System.Text.Json;
using Microsoft.Win32;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Audio;

/// <summary>
/// Remembers a THX installer that asked for a reboot. A service-only check
/// otherwise looks healthy while the APO is still inactive, so the installer
/// entry reappears and downloads the package again.
/// </summary>
internal sealed class ThxRebootMarkerStore
{
    private readonly string _path;
    private readonly Func<uint?> _readBootId;

    internal ThxRebootMarkerStore(string? path = null, Func<uint?>? readBootId = null)
    {
        _path = path ?? AppDataPaths.Combine("thx-reboot-pending.json");
        _readBootId = readBootId ?? ReadBootId;
    }

    internal bool IsPending(ushort productId)
    {
        if (_readBootId() is not { } bootId || TryRead() is not { } marker)
        {
            return false;
        }

        if (marker.BootId != bootId)
        {
            Clear();
            return false;
        }

        return marker.EffectiveProductIds.Contains(productId);
    }

    internal bool MarkPending(ushort productId)
    {
        if (_readBootId() is not { } bootId)
        {
            return false;
        }

        var productIds = TryRead() is { } existing && existing.BootId == bootId
            ? existing.EffectiveProductIds
            : [];
        return TrySave(new Marker(bootId, productIds.Append(productId).Distinct().ToArray()));
    }

    internal void Clear(ushort productId)
    {
        if (TryRead() is not { } marker)
        {
            return;
        }

        var remaining = marker.EffectiveProductIds.Where(id => id != productId).ToArray();
        if (remaining.Length == marker.EffectiveProductIds.Length)
        {
            return;
        }
        if (remaining.Length == 0)
        {
            Clear();
            return;
        }
        TrySave(marker with { ProductIds = remaining, ProductId = 0 });
    }

    internal void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private Marker? TryRead()
    {
        try
        {
            return JsonSerializer.Deserialize<Marker>(File.ReadAllText(_path));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private bool TrySave(Marker marker)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporary, JsonSerializer.Serialize(marker));
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static uint? ReadBootId()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters");
            return key?.GetValueKind("BootId") == RegistryValueKind.DWord &&
                key.GetValue("BootId") is int value ? unchecked((uint)value) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private sealed record Marker(uint BootId, ushort[]? ProductIds = null, ushort ProductId = 0)
    {
        // Older builds wrote a single ProductId. Keep reading their file so a
        // pending install is not silently forgotten after an upgrade.
        internal ushort[] EffectiveProductIds =>
            (ProductIds ?? []).Append(ProductId).Where(id => id != 0).Distinct().ToArray();
    }
}
