using System.Text.Json;
using Microsoft.Win32;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Lifecycle;

internal sealed class GpuModeRecoveryMarkerStore
{
    private readonly string _path;
    private readonly Func<uint?> _readBootId;

    internal GpuModeRecoveryMarkerStore(string? path = null, Func<uint?>? readBootId = null)
    {
        _path = path ?? AppDataPaths.Combine("gpu-mode-pending.json");
        _readBootId = readBootId ?? ReadBootId;
    }

    internal bool Exists => File.Exists(_path);

    internal bool TryWrite(in BladeGpuModeSnapshot snapshot)
    {
        return _readBootId() is { } bootId && TrySave(new RecoveryMarker(
            snapshot.DedicatedGpuEnabled, snapshot.Mux.Mode, DateTimeOffset.UtcNow, bootId));
    }

    internal bool TryPrepareWrite()
    {
        if (_readBootId() is not { } bootId) return false;
        if (!Exists) return TrySave(new RecoveryMarker(null, null, DateTimeOffset.UtcNow, bootId));
        // Recovery is itself a writer: stamp this boot before it can dispatch.
        return TryRead() is { } marker && TrySave(marker with { BootId = bootId });
    }

    internal bool CanRecoverAfterRestart()
    {
        if (_readBootId() is not { } bootId || TryRead() is not { } marker) return false;
        if (marker.BootId is { } previous) return bootId > previous;
        // Legacy records cannot prove when their writer stopped. Bind once, then require a new boot.
        TrySave(marker with { BootId = bootId });
        return false;
    }

    private RecoveryMarker? TryRead()
    {
        try { return JsonSerializer.Deserialize<RecoveryMarker>(File.ReadAllText(_path)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private bool TrySave(RecoveryMarker marker)
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
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
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
            // Keep the record if cleanup fails; a new process must still verify the old writer is gone.
        }
    }

    private static uint? ReadBootId()
    {
        try
        {
            // Microsoft's successful-boot counter; do not infer a reboot from clock/uptime or device status.
            // https://learn.microsoft.com/windows-hardware/design/device-experiences/oem-hvci-enablement
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

    private sealed record RecoveryMarker(
        bool? PreviousDedicatedGpuEnabled,
        BladeDisplayMuxMode? PreviousMuxMode,
        DateTimeOffset CreatedAt, uint? BootId = null);
}
