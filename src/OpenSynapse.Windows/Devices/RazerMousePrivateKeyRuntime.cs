using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Switches a mouse into driver mode and keeps its DPI buttons working once the
/// firmware stops acting on them. Driver mode is what turns the DPI, profile and
/// clutch keys into report 4 private keys; in normal mode the firmware acts on
/// the key and only reports the result, so no macro trigger ever arrives.
/// </summary>
internal sealed class RazerMousePrivateKeyRuntime
{
    private const ushort RazerVendorId = 0x1532;
    // Mouse manifests rarely declare the mode builders; 0x1F is the transaction
    // Razer's own protocol-25 bundle sends for them.
    private const byte TransactionId = 0x1F;
    private const byte DpiStorage = 0x01;
    private const byte DriverDeviceMode = 0x03;
    private const byte NormalDeviceMode = 0x00;
    private const byte DpiStageUpKey = 32;
    private const byte DpiStageDownKey = 33;
    private const byte DpiCycleKey = 82;
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(300);

    private readonly RazerFeatureTransport _transport = new();
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private readonly Dictionary<ushort, string> _paths = [];
    // The mode this run established for a pid, plus the pids already probed
    // once, so a periodic refresh cannot turn into a stream of device reads.
    private readonly Dictionary<ushort, bool> _applied = [];
    private readonly HashSet<ushort> _probed = [];
    private readonly Dictionary<ushort, byte> _activeStage = [];
    private bool _recovered;

    internal RazerMousePrivateKeyRuntime(Action<string>? log = null) => _log = log;

    internal bool IsDriverMode(ushort productId)
    {
        lock (_gate)
        {
            return _applied.TryGetValue(productId, out var enabled) && enabled;
        }
    }

    /// <summary>
    /// Brings every mouse in line with the bindings. The first call also probes
    /// the mice that are present, so one a crashed session left in driver mode
    /// goes back to its firmware without a replug.
    /// </summary>
    internal async Task SyncAsync(
        IReadOnlyCollection<ushort> wanted,
        CancellationToken cancellationToken)
    {
        var targets = new HashSet<ushort>(wanted.Where(productId =>
            IsMouseProduct(productId) && !OwnsItsOwnMode(productId)));
        lock (_gate)
        {
            targets.UnionWith(_applied.Keys);
        }

        if (!_recovered)
        {
            foreach (var productId in await PresentMiceAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!OwnsItsOwnMode(productId))
                {
                    targets.Add(productId);
                }
            }
            _recovered = true;
        }

        foreach (var productId in targets)
        {
            var enable = wanted.Contains(productId);
            lock (_gate)
            {
                if (_applied.TryGetValue(productId, out var applied) && applied == enable)
                {
                    continue;
                }
                if (!enable && !_probed.Add(productId))
                {
                    continue;
                }
            }

            var mode = await ReadModeAsync(productId, cancellationToken).ConfigureAwait(false);
            if (mode is null)
            {
                lock (_gate)
                {
                    _applied[productId] = false;
                }
                continue;
            }

            var changed = (mode == DriverDeviceMode) != enable;
            if (changed &&
                !await WriteModeAsync(
                        productId,
                        enable ? DriverDeviceMode : NormalDeviceMode,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                continue;
            }

            lock (_gate)
            {
                _applied[productId] = enable;
                if (!enable)
                {
                    _activeStage.Remove(productId);
                }
            }

            if (changed)
            {
                _log?.Invoke(
                    $"1532:{productId:X4} driver mode {(enable ? "on for a private-key macro" : "off")}");
            }
            if (!enable)
            {
                continue;
            }

            var table = await ReadStagesAsync(productId, cancellationToken).ConfigureAwait(false);
            if (table is not null)
            {
                lock (_gate)
                {
                    _activeStage[productId] = table[1];
                }
            }
        }
    }

    /// <summary>
    /// Hands every mouse this run moved to driver mode back to its firmware, so
    /// the private keys keep their stock behavior once OpenSynapse exits.
    /// </summary>
    internal async Task ResetAsync()
    {
        ushort[] enabled;
        lock (_gate)
        {
            enabled = _applied.Where(pair => pair.Value).Select(pair => pair.Key).ToArray();
        }
        if (enabled.Length == 0)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        foreach (var productId in enabled)
        {
            try
            {
                if (!await WriteModeAsync(productId, NormalDeviceMode, timeout.Token).ConfigureAwait(false))
                {
                    continue;
                }
                lock (_gate)
                {
                    _applied[productId] = false;
                    _activeStage.Remove(productId);
                }
            }
            catch (Exception exception) when (
                exception is Win32Exception or IOException or OperationCanceledException)
            {
                _log?.Invoke(
                    $"1532:{productId:X4} could not return to normal mode: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Applies the default action of a private key that no macro claimed. Only the
    /// DPI family is reproduced: those are the keys the firmware stops handling in
    /// driver mode on every mouse that offers them.
    /// </summary>
    internal async Task<bool> HandleAsync(
        ushort productId,
        byte keyCode,
        CancellationToken cancellationToken)
    {
        if (keyCode is not (DpiStageUpKey or DpiStageDownKey or DpiCycleKey) ||
            !IsDriverMode(productId))
        {
            return false;
        }

        var table = await ReadStagesAsync(productId, cancellationToken).ConfigureAwait(false);
        if (table is null)
        {
            return false;
        }

        var active = table[1];
        byte cached;
        lock (_gate)
        {
            _activeStage.TryGetValue(productId, out cached);
        }
        if (cached != 0 && cached != active)
        {
            // The firmware moved the stage itself; stepping again would skip one.
            lock (_gate)
            {
                _activeStage[productId] = active;
            }
            return true;
        }

        if (NextStage(table, keyCode) is not { } nextStage || nextStage == active)
        {
            return false;
        }

        var request = table.ToArray();
        request[1] = nextStage;
        var sent = await ExchangeAsync(
            productId,
            (byte)request.Length,
            0x04,
            0x06,
            request,
            cancellationToken).ConfigureAwait(false);
        if (sent is null)
        {
            return false;
        }

        lock (_gate)
        {
            _activeStage[productId] = nextStage;
        }
        return true;
    }

    /// <summary>
    /// Stage the default DPI key would move to, or null when the table cannot
    /// answer. Keys 32 and 33 step one stage; 82 cycles forward like the firmware.
    /// </summary>
    internal static byte? NextStage(byte[] table, byte keyCode)
    {
        if (table.Length < 10)
        {
            return null;
        }

        var active = table[1];
        var count = table[2];
        if (count is < 1 or > 5 || table.Length < 3 + (count * 7))
        {
            return null;
        }

        var index = -1;
        for (var stage = 0; stage < count; stage++)
        {
            if (table[3 + (stage * 7)] == active)
            {
                index = stage;
                break;
            }
        }
        if (index < 0)
        {
            return null;
        }

        var step = keyCode == DpiStageDownKey ? -1 : 1;
        return table[3 + ((((index + step) % count) + count) % count) * 7];
    }

    private async Task<byte?> ReadModeAsync(ushort productId, CancellationToken cancellationToken)
    {
        var response = await ExchangeAsync(productId, 0x02, 0x00, 0x84, ReadOnlyMemory<byte>.Empty, cancellationToken)
            .ConfigureAwait(false);
        return response?[9];
    }

    private async Task<bool> WriteModeAsync(
        ushort productId,
        byte mode,
        CancellationToken cancellationToken)
    {
        if (await ExchangeAsync(
                productId,
                0x02,
                0x00,
                0x04,
                new byte[] { mode, RazerHidStandardProtocol.NoStore },
                cancellationToken).ConfigureAwait(false) is null)
        {
            return false;
        }

        var readback = await ReadModeAsync(productId, cancellationToken).ConfigureAwait(false);
        if (readback == mode)
        {
            return true;
        }

        _log?.Invoke(
            $"1532:{productId:X4} device mode is " +
            $"{(readback is { } answered ? $"0x{answered:X2}" : "unknown")}, expected 0x{mode:X2}");
        return false;
    }

    private async Task<byte[]?> ReadStagesAsync(ushort productId, CancellationToken cancellationToken)
    {
        var response = await ExchangeAsync(
            productId,
            0x50,
            0x04,
            0x86,
            new byte[] { DpiStorage },
            cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        var count = response[11];
        var length = 3 + (count * 7);
        if (count is < 1 or > 5 || response[6] < length)
        {
            return null;
        }

        var table = new byte[length];
        Array.Copy(response, RazerFeatureReport.ArgumentsOffset, table, 0, length);
        return table;
    }

    private async Task<byte[]?> ExchangeAsync(
        ushort productId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken)
    {
        var paths = await ResolvePathsAsync(productId, cancellationToken).ConfigureAwait(false);
        foreach (var path in paths)
        {
            try
            {
                var response = await _transport
                    .QueryAsync(path, TransactionId, dataSize, commandClass, commandId,
                        arguments, DeviceWait, cancellationToken)
                    .ConfigureAwait(false);
                lock (_gate)
                {
                    _paths[productId] = path;
                }
                return response;
            }
            catch (Exception exception) when (
                exception is Win32Exception or IOException or InvalidOperationException)
            {
                // A collection that does not answer for this pid must not mask the
                // one that does; the path cache is rebuilt on the next call.
            }
        }

        lock (_gate)
        {
            _paths.Remove(productId);
        }
        return null;
    }

    private async Task<IReadOnlyList<string>> ResolvePathsAsync(
        ushort productId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_paths.TryGetValue(productId, out var cached))
            {
                return [cached];
            }
        }

        var interfaces = await WindowsHidDiscovery
            .FindVendorInterfacesAsync(RazerVendorId, cancellationToken).ConfigureAwait(false);
        return interfaces
            .Where(item => item.ProductId == productId &&
                item.FeatureReportByteLength == RazerFeatureReport.Length &&
                item.Access == DeviceAccessState.Available)
            .OrderBy(item => item.UsagePage == 0x0001 && item.Usage == 0x0002 ? 0 : 1)
            .Select(item => item.DevicePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<ushort>> PresentMiceAsync(CancellationToken cancellationToken)
    {
        var interfaces = await WindowsHidDiscovery
            .FindVendorInterfacesAsync(RazerVendorId, cancellationToken).ConfigureAwait(false);
        return interfaces
            .Where(item => item.FeatureReportByteLength == RazerFeatureReport.Length &&
                item.Access == DeviceAccessState.Available &&
                IsMouseProduct(item.ProductId))
            .Select(item => item.ProductId)
            .Distinct()
            .ToArray();
    }

    // Driver mode is a mouse contract. A keyboard's feature collection belongs to
    // Fn takeover and must not receive the mouse device-mode command.
    private static bool IsMouseProduct(ushort productId) =>
        RazerHidDeviceCatalog.BuiltIn.Find(RazerVendorId, productId)?.Category == DeviceCategory.Mouse ||
        RazerDeviceRegistry.BuiltIn.Find(RazerVendorId, productId)?.Category == DeviceCategory.Mouse;

    // These declare set_device_mode, and RazerHidWorkspace keeps them in driver mode
    // for their link. Writing normal mode here would drop the 2.4 GHz connection.
    private static bool OwnsItsOwnMode(ushort productId) =>
        RazerHidDeviceCatalog.BuiltIn.Find(RazerVendorId, productId) is { } definition &&
        definition.Transactions.ContainsKey("razer_chroma_standard_set_device_mode");
}
