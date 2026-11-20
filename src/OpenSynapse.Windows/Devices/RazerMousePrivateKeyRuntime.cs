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
    private const byte DefaultModeTransaction = 0x00;
    private const byte DpiStageUpKey = 32;
    private const byte DpiStageDownKey = 33;
    private const byte DpiCycleKey = 82;
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(300);
    // A replugged dongle forgets the mode, so an enabled mouse is verified again
    // without turning every refresh into a query.
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(30);

    private readonly RazerFeatureTransport _transport = new();
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly Dictionary<ushort, string> _paths = [];
    // The last mode this run observed, so a periodic refresh cannot turn into a
    // stream of device reads, and the mode each pid had before this run took it
    // over. Only an entry here is a mode this run may write back on exit.
    private readonly Dictionary<ushort, bool> _applied = [];
    private readonly Dictionary<ushort, byte> _originalModes = [];
    private readonly Dictionary<ushort, byte> _activeStage = [];
    private readonly Dictionary<ushort, DateTime> _verifiedAt = [];
    // The mode pair is not uniform across mice: legacy firmware answers 00:84/00:04,
    // every rzDevice30 mouse answers 01:82/01:02. Probe once, then keep the winner.
    private readonly Dictionary<ushort, bool> _protocol25Modes = [];
    // The last full set of pids that need driver mode, so a retry cannot drop the
    // devices a concurrent caller already armed.
    private HashSet<ushort> _wanted = [];
    private bool _retrying;

    internal RazerMousePrivateKeyRuntime(Action<string>? log = null) => _log = log;

    internal enum DeviceModeAction
    {
        None,
        Write,
        Release,
        Unreadable,
    }

    internal bool IsDriverMode(ushort productId)
    {
        lock (_gate)
        {
            return _originalModes.ContainsKey(productId);
        }
    }

    /// <summary>
    /// Brings the mice that carry a private-key binding into driver mode. A mode
    /// that this run did not write - Razer Synapse holding the same mouse, or a
    /// firmware default - is observed and left alone.
    /// </summary>
    internal async Task SyncAsync(
        IReadOnlyCollection<ushort> wanted,
        CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SyncCoreAsync(wanted, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task SyncCoreAsync(
        IReadOnlyCollection<ushort> wanted,
        CancellationToken cancellationToken)
    {
        var targets = new HashSet<ushort>(wanted.Where(productId =>
            IsMouseProduct(productId) && !OwnsItsOwnMode(productId)));
        lock (_gate)
        {
            targets.UnionWith(_applied.Keys);
            targets.UnionWith(_originalModes.Keys);
        }

        foreach (var productId in targets)
        {
            var enable = wanted.Contains(productId);
            lock (_gate)
            {
                if (_applied.TryGetValue(productId, out var applied) && applied == enable &&
                    (!enable ||
                     DateTime.UtcNow - _verifiedAt.GetValueOrDefault(productId) < RecheckInterval))
                {
                    continue;
                }
            }

            var mode = await ReadModeAsync(productId, cancellationToken).ConfigureAwait(false);
            bool owned;
            lock (_gate)
            {
                owned = _originalModes.ContainsKey(productId);
            }

            switch (Decide(mode, enable, owned))
            {
                case DeviceModeAction.Unreadable:
                    lock (_gate)
                    {
                        _applied[productId] = false;
                    }
                    continue;
                case DeviceModeAction.None:
                    lock (_gate)
                    {
                        _applied[productId] = enable;
                        _verifiedAt[productId] = DateTime.UtcNow;
                    }
                    if (enable)
                    {
                        RazerDeviceModeOwnership.Acquire(productId);
                    }
                    break;
                case DeviceModeAction.Release:
                    lock (_gate)
                    {
                        _applied.Remove(productId);
                    }
                    RazerDeviceModeOwnership.Release(productId);
                    _log?.Invoke(
                        $"1532:{productId:X4} driver mode belongs to another owner; left alone");
                    continue;
                case DeviceModeAction.Write:
                    var target = enable ? DriverDeviceMode : NormalDeviceMode;
                    if (!await WriteModeAsync(productId, target, cancellationToken).ConfigureAwait(false))
                    {
                        // A failed arming must be retried; a failed restore keeps its
                        // old entry so the next sync tries again too.
                        if (enable)
                        {
                            lock (_gate)
                            {
                                _applied[productId] = false;
                            }
                        }
                        continue;
                    }

                    lock (_gate)
                    {
                        if (enable && !_originalModes.ContainsKey(productId))
                        {
                            _originalModes[productId] = mode!.Value;
                        }
                        _applied[productId] = enable;
                        _verifiedAt[productId] = DateTime.UtcNow;
                        if (!enable)
                        {
                            _originalModes.Remove(productId);
                            _activeStage.Remove(productId);
                        }
                    }
                    if (enable)
                    {
                        RazerDeviceModeOwnership.Acquire(productId);
                    }
                    else
                    {
                        RazerDeviceModeOwnership.Release(productId);
                    }
                    _log?.Invoke(
                        $"1532:{productId:X4} device mode 0x{mode!.Value:X2} -> 0x{target:X2}" +
                        (enable ? " for a private-key macro" : string.Empty));
                    break;
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

        lock (_gate)
        {
            _wanted = [..wanted];
        }
        EnsureRetry();
    }

    /// <summary>
    /// A wireless mouse stops answering feature reports while it sleeps, so the
    /// boot-time sync fails and used to wait for the user's first key press. Keep
    /// the switch in the background instead of making it part of that press.
    /// </summary>
    private void EnsureRetry()
    {
        lock (_gate)
        {
            if (_retrying || !NeedsRetry(_wanted, _applied))
            {
                return;
            }
            _retrying = true;
        }
        _ = RetryAsync();
    }

    private async Task RetryAsync()
    {
        var delay = TimeSpan.FromSeconds(2);
        try
        {
            while (true)
            {
                await Task.Delay(delay, _lifetime.Token).ConfigureAwait(false);
                // Slow down once an unattended dongle stays silent: the switch
                // itself is cheap, a failed probe is not.
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
                HashSet<ushort> wanted;
                lock (_gate)
                {
                    if (!NeedsRetry(_wanted, _applied))
                    {
                        return;
                    }
                    wanted = [.._wanted];
                }
                await SyncAsync(wanted, _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                _retrying = false;
            }
        }
    }

    internal static bool NeedsRetry(
        IReadOnlyCollection<ushort> wanted,
        IReadOnlyDictionary<ushort, bool> applied) =>
        wanted.Any(productId =>
            IsMouseProduct(productId) &&
            !OwnsItsOwnMode(productId) &&
            !applied.GetValueOrDefault(productId));

    // Decides what a sync does for one device. Equal modes never write: a driver
    // mode this run did not set belongs to whoever set it.
    internal static DeviceModeAction Decide(byte? current, bool enable, bool owned)
    {
        if (current is not { } mode)
        {
            return DeviceModeAction.Unreadable;
        }
        if ((mode == DriverDeviceMode) == enable)
        {
            return DeviceModeAction.None;
        }
        return !enable && !owned ? DeviceModeAction.Release : DeviceModeAction.Write;
    }

    /// <summary>
    /// Hands every mouse this run moved to driver mode back to the mode it had
    /// before, so a mode another owner set is never overwritten on exit.
    /// </summary>
    internal async Task ResetAsync()
    {
        _lifetime.Cancel();
        KeyValuePair<ushort, byte>[] restore;
        lock (_gate)
        {
            restore = _originalModes.ToArray();
        }
        if (restore.Length == 0)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        foreach (var (productId, original) in restore)
        {
            try
            {
                if (!await WriteModeAsync(productId, original, timeout.Token).ConfigureAwait(false))
                {
                    continue;
                }
                lock (_gate)
                {
                    _applied[productId] = original == DriverDeviceMode;
                    _originalModes.Remove(productId);
                    _activeStage.Remove(productId);
                }
                RazerDeviceModeOwnership.Release(productId);
            }
            catch (Exception exception) when (
                exception is Win32Exception or IOException or OperationCanceledException)
            {
                _log?.Invoke(
                    $"1532:{productId:X4} could not restore device mode 0x{original:X2}: {exception.Message}");
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
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(RazerVendorId, productId);
        bool? cached = null;
        lock (_gate)
        {
            if (_protocol25Modes.TryGetValue(productId, out var known))
            {
                cached = known;
            }
        }

        foreach (var protocol25 in Dialects(cached))
        {
            byte[]? response;
            try
            {
                response = await ReadModeCoreAsync(
                        productId, definition, protocol25, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                continue;
            }
            if (!TryParseDeviceMode(response, out var mode))
            {
                continue;
            }

            lock (_gate)
            {
                _protocol25Modes[productId] = protocol25;
            }
            return mode;
        }

        return null;
    }

    private async Task<bool> WriteModeAsync(
        ushort productId,
        byte mode,
        CancellationToken cancellationToken)
    {
        if (await ReadModeAsync(productId, cancellationToken).ConfigureAwait(false) is not { } current)
        {
            return false;
        }

        if (current == mode)
        {
            return true;
        }

        var definition = RazerHidDeviceCatalog.BuiltIn.Find(RazerVendorId, productId);
        bool protocol25;
        lock (_gate)
        {
            protocol25 = _protocol25Modes.GetValueOrDefault(productId);
        }
        await SendModeAsync(productId, definition, protocol25, mode, cancellationToken)
            .ConfigureAwait(false);

        var readback = await ReadModeAsync(productId, cancellationToken).ConfigureAwait(false);
        if (readback == mode)
        {
            return true;
        }

        // A frame the device answered for the read can still be the wrong write
        // pair; retry once with the dialect the read-back identified.
        bool retry;
        lock (_gate)
        {
            retry = _protocol25Modes.TryGetValue(productId, out var identified) &&
                identified != protocol25;
            if (retry)
            {
                protocol25 = identified;
            }
        }
        if (retry)
        {
            await SendModeAsync(productId, definition, protocol25, mode, cancellationToken)
                .ConfigureAwait(false);
            readback = await ReadModeAsync(productId, cancellationToken).ConfigureAwait(false);
            if (readback == mode)
            {
                return true;
            }
        }

        _log?.Invoke(
            $"1532:{productId:X4} device mode is " +
            $"{(readback is { } answered ? $"0x{answered:X2}" : "unknown")}, expected 0x{mode:X2}");
        return false;
    }

    private async Task<byte[]?> ReadModeCoreAsync(
        ushort productId,
        RazerHidDeviceDefinition? definition,
        bool protocol25,
        CancellationToken cancellationToken)
    {
        var report = definition is null
            ? RazerHidStandardProtocol.GetDeviceMode(DefaultModeTransaction, protocol25).Report
            : RazerHidStandardProtocol.GetDeviceMode(definition, protocol25).Report;
        return await ExchangeAsync(
                productId,
                report[6],
                report[7],
                report[8],
                report.AsMemory(RazerFeatureReport.ArgumentsOffset, report[6]),
                cancellationToken,
                report[2])
            .ConfigureAwait(false);
    }

    private async Task SendModeAsync(
        ushort productId,
        RazerHidDeviceDefinition? definition,
        bool protocol25,
        byte mode,
        CancellationToken cancellationToken)
    {
        var report = definition is null
            ? RazerHidStandardProtocol.SetDeviceMode(DefaultModeTransaction, mode, protocol25).Report
            : RazerHidStandardProtocol.SetDeviceMode(definition, mode, protocol25).Report;
        // Synapse treats a driver-mode timeout as success; the caller's read-back decides.
        try
        {
            await ExchangeAsync(
                    productId,
                    report[6],
                    report[7],
                    report[8],
                    report.AsMemory(RazerFeatureReport.ArgumentsOffset, report[6]),
                    cancellationToken,
                    report[2])
                .ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
        }
    }

    private static IEnumerable<bool> Dialects(bool? cached)
    {
        if (cached is { } known)
        {
            yield return known;
            yield return !known;
            yield break;
        }

        yield return false;
        yield return true;
    }

    private static bool TryParseDeviceMode(byte[]? response, out byte mode)
    {
        mode = 0;
        if (response is null)
        {
            return false;
        }

        try
        {
            mode = RazerHidStandardProtocol.ParseDeviceMode(response);
            return mode <= DriverDeviceMode;
        }
        catch (InvalidDataException)
        {
            return false;
        }
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
        CancellationToken cancellationToken,
        byte? transactionId = null)
    {
        var paths = await ResolvePathsAsync(productId, cancellationToken).ConfigureAwait(false);
        foreach (var path in paths)
        {
            try
            {
                var response = await _transport
                    .QueryAsync(path, transactionId ?? TransactionId, dataSize, commandClass, commandId,
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
