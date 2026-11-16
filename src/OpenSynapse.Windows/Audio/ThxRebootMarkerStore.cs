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

        return marker.ProductId == productId;
    }

    internal bool MarkPending(ushort productId)
    {
        return _readBootId() is { } bootId &&
            TrySave(new Marker(productId, bootId));
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

    private sealed record Marker(ushort ProductId, uint BootId);
}
