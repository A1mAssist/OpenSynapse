using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record BlackSharkHeadsetConnection(
    string DevicePath,
    ushort ProductId,
    string Name,
    bool Wireless,
    int InputReportLength);

public sealed record BlackSharkHeadsetState(
    byte? BatteryPercent,
    bool? Charging,
    byte? Sidetone,
    bool? Thx,
    BlackSharkAncMode? AncMode,
    int? AncLevel,
    int? GameChatBalance);

public sealed class BlackSharkHeadsetService : IAsyncDisposable
{
    private readonly BlackSharkHeadsetConnection _connection;
    private readonly BlackSharkHeadsetTransport _transport;
    private readonly Action<string>? _diagnostic;

    private BlackSharkHeadsetService(
        BlackSharkHeadsetConnection connection,
        BlackSharkHeadsetTransport transport,
        Action<string>? diagnostic)
    {
        _connection = connection;
        _transport = transport;
        _diagnostic = diagnostic;
    }

    public BlackSharkHeadsetConnection Connection => _connection;

    public static async Task<BlackSharkHeadsetConnection?> FindAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(0x1532, cancellationToken)
            .ConfigureAwait(false);
        // The control shape is also used by other Razer headsets (Kraken V4 enumerates
        // FF13/FF14 with a 64-byte output report), so shape alone is not an identity
        // check. Require the BlackShark product name or one of the PIDs observed on the
        // V3 Pro; this keeps a Kraken from being shown in the BlackShark panel.
        var candidates = interfaces
            .Where(item => IsBlackSharkControlInterface(
                item.ProductId,
                item.ProductName,
                item.UsagePage,
                item.Usage,
                item.OutputReportByteLength))
            .ToArray();
        if (candidates.Length == 0)
        {
            diagnostic?.Invoke(
                $"BlackShark found no admitted 1532 control interface " +
                $"(BlackShark product name or observed PID, FF13/FF14, " +
                $"{BlackSharkV3ProProtocol.ReportLength}-byte output report); " +
                $"enumerated={string.Join(",", interfaces.Select(DescribeInterface))}");
            return null;
        }

        var chosen = candidates
            .Where(item => item.UsagePage == 0xFF14)
            .OrderByDescending(item => item.Access == DeviceAccessState.Available)
            .ThenBy(item => ProductPriority(item.ProductId))
            .FirstOrDefault()
            ?? candidates
                .OrderByDescending(item => item.Access == DeviceAccessState.Available)
                .ThenBy(item => ProductPriority(item.ProductId))
                .First();

        // The transport byte differs between the 2.4 GHz link and the wired cable. Only the pids
        // we have actually observed on hardware map to a direction; an unknown variant such as the
        // PlayStation build starts on the wireless byte, and the diagnostic below records the
        // choice so a failing link becomes evidence instead of another guess.
        var wireless = !BlackSharkV3ProProtocol.IsWiredProductId(chosen.ProductId);
        diagnostic?.Invoke(
            $"BlackShark selected vid_{chosen.VendorId:X4}&pid_{chosen.ProductId:X4} " +
            $"product='{chosen.ProductName}' usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} " +
            $"out={chosen.OutputReportByteLength} in={chosen.InputReportByteLength} " +
            $"access={chosen.Access} direction={(wireless ? "wireless" : "wired")}; " +
            $"candidates={string.Join(",", candidates.Select(item => $"pid_{item.ProductId:X4}:0x{item.UsagePage:X4}:0x{item.Usage:X4}:out{item.OutputReportByteLength}"))}");
        return new BlackSharkHeadsetConnection(
            chosen.DevicePath,
            chosen.ProductId,
            BlackSharkV3ProProtocol.DescribeProduct(chosen.ProductId),
            wireless,
            chosen.InputReportByteLength);
    }

    public static async Task<BlackSharkHeadsetService> OpenAsync(
        BlackSharkHeadsetConnection connection,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var transport = new BlackSharkHeadsetTransport(connection.DevicePath, connection.InputReportLength);
        try
        {
            foreach (var report in BlackSharkV3ProProtocol.CreateWakeSequence())
            {
                await transport.SendAsync(report, false, cancellationToken).ConfigureAwait(false);
                await Task.Delay(2, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new BlackSharkHeadsetService(connection, transport, diagnostic);
    }

    public async Task<BlackSharkHeadsetState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var battery = await ReadValueAsync(
            BlackSharkV3ProProtocol.CreateGetBattery(_connection.Wireless),
            0x21,
            cancellationToken).ConfigureAwait(false);
        var charging = await ReadValueAsync(
            BlackSharkV3ProProtocol.CreateGetCharging(_connection.Wireless),
            0x2A,
            cancellationToken).ConfigureAwait(false);
        var sidetone = await ReadValueAsync(
            BlackSharkV3ProProtocol.CreateGetSidetone(_connection.Wireless),
            0x19,
            cancellationToken).ConfigureAwait(false);
        var thx = await ReadValueAsync(
            BlackSharkV3ProProtocol.CreateGetThx(_connection.Wireless),
            0x1E,
            cancellationToken).ConfigureAwait(false);
        var anc = await ReadAncAsync(cancellationToken).ConfigureAwait(false);
        var gameChat = await ReadValueAsync(
            BlackSharkV3ProProtocol.CreateGetGameChat(_connection.Wireless),
            0x5C,
            cancellationToken).ConfigureAwait(false);

        return new BlackSharkHeadsetState(
            battery,
            charging is null ? null : charging != 0,
            sidetone,
            thx is null ? null : thx != 0,
            anc.Mode,
            anc.Level,
            gameChat is byte rawGameChat ? InvertGameChat(rawGameChat) : null);
    }

    public async Task SetSidetoneAsync(int level, CancellationToken cancellationToken = default)
    {
        var previous = await ReadValueAsync(
            BlackSharkV3ProProtocol.CreateGetSidetone(_connection.Wireless),
            0x19,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.SendAsync(
                BlackSharkV3ProProtocol.CreateSetSidetone(level, _connection.Wireless), false, cancellationToken)
                .ConfigureAwait(false);
            var actual = await ReadValueWithRetryAsync(0x19, cancellationToken).ConfigureAwait(false);
            if (actual != level)
            {
                throw new InvalidOperationException($"BlackShark sidetone readback was {actual?.ToString() ?? "empty"}, expected {level}.");
            }
        }
        catch
        {
            if (previous is byte restore)
            {
                await TryRestoreSidetoneAsync(restore).ConfigureAwait(false);
            }

            throw;
        }
    }

    public async Task SetThxAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var previous = await ReadValueAsync(
            BlackSharkV3ProProtocol.CreateGetThx(_connection.Wireless),
            0x1E,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.SendAsync(
                BlackSharkV3ProProtocol.CreateSetThx(enabled, _connection.Wireless), false, cancellationToken)
                .ConfigureAwait(false);
            var actual = await ReadValueWithRetryAsync(0x1E, cancellationToken).ConfigureAwait(false);
            if (actual is null || (actual != 0) != enabled)
            {
                throw new InvalidOperationException("BlackShark THX readback did not match the requested state.");
            }
        }
        catch
        {
            if (previous is byte restore)
            {
                await TrySendAsync(BlackSharkV3ProProtocol.CreateSetThx(restore != 0, _connection.Wireless))
                    .ConfigureAwait(false);
            }

            throw;
        }
    }

    public async Task SetAncAsync(
        BlackSharkAncMode mode,
        int level,
        CancellationToken cancellationToken = default)
    {
        var previous = await ReadAncAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.SendAsync(
                BlackSharkV3ProProtocol.CreateSetAnc(mode, level, _connection.Wireless), false, cancellationToken)
                .ConfigureAwait(false);
            var actual = await ReadAncWithRetryAsync(cancellationToken).ConfigureAwait(false);
            if (actual.Mode != mode || (mode == BlackSharkAncMode.Anc && actual.Level != Math.Clamp(level, 1, 4)))
            {
                throw new InvalidOperationException("BlackShark ANC readback did not match the requested state.");
            }
        }
        catch
        {
            if (previous.Mode is BlackSharkAncMode restoreMode)
            {
                await TrySendAsync(BlackSharkV3ProProtocol.CreateSetAnc(
                    restoreMode, previous.Level ?? 1, _connection.Wireless)).ConfigureAwait(false);
            }

            throw;
        }
    }

    public async Task SetGameChatAsync(int balance, CancellationToken cancellationToken = default)
    {
        var previousWire = await ReadValueAsync(
            BlackSharkV3ProProtocol.CreateGetGameChat(_connection.Wireless),
            0x5C,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.SendAsync(
                BlackSharkV3ProProtocol.CreateSetGameChat(balance, _connection.Wireless), false, cancellationToken)
                .ConfigureAwait(false);
            var actual = await ReadValueWithRetryAsync(0x5C, cancellationToken).ConfigureAwait(false);
            var actualBalance = actual is byte raw ? InvertGameChat(raw) : (byte?)null;
            if (actualBalance != balance)
            {
                throw new InvalidOperationException(
                    $"BlackShark game/chat readback was {actualBalance?.ToString() ?? "empty"}, expected {balance}.");
            }
        }
        catch
        {
            if (previousWire is byte restore)
            {
                await TrySendAsync(BlackSharkV3ProProtocol.CreateSetGameChat(
                    InvertGameChat(restore), _connection.Wireless)).ConfigureAwait(false);
            }

            throw;
        }
    }

    private static byte InvertGameChat(byte wireValue) =>
        checked((byte)(20 - Math.Clamp(wireValue, (byte)0, (byte)20)));

    private async Task<(BlackSharkAncMode? Mode, int? Level)> ReadAncAsync(CancellationToken cancellationToken)
    {
        var response = await _transport.SendAsync(
            BlackSharkV3ProProtocol.CreateGetAnc(_connection.Wireless), true, cancellationToken)
            .ConfigureAwait(false);
        if (response?.Arguments.Length >= 2 && TryMapAncMode(response.Arguments[0], out var mode))
        {
            return (mode, response.Arguments[1]);
        }

        return (null, null);
    }

    private async Task<byte?> ReadValueWithRetryAsync(byte commandClass, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var value = await ReadValueAsync(
                BlackSharkV3ProProtocol.Create(commandClass, 0x00, _connection.Wireless),
                commandClass,
                cancellationToken).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }

            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<(BlackSharkAncMode? Mode, int? Level)> ReadAncWithRetryAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var value = await ReadAncAsync(cancellationToken).ConfigureAwait(false);
            if (value.Mode is not null)
            {
                return value;
            }

            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }

        return (null, null);
    }

    private async Task<byte?> ReadValueAsync(
        byte[] request,
        byte expectedClass,
        CancellationToken cancellationToken)
    {
        var response = await _transport.SendAsync(request, true, cancellationToken).ConfigureAwait(false);
        if (response is null || response.CommandClass != expectedClass || response.Arguments.Length == 0)
        {
            _diagnostic?.Invoke($"BlackShark GET class 0x{expectedClass:X2} returned no usable response.");
            return null;
        }

        _diagnostic?.Invoke(
            $"BlackShark GET class 0x{expectedClass:X2} answered value=0x{response.Arguments[0]:X2} " +
            $"direction=0x{request[9]:X2}");
        return response.Arguments[0];
    }

    private async Task TryRestoreSidetoneAsync(byte level)
    {
        try
        {
            await _transport.SendAsync(
                BlackSharkV3ProProtocol.CreateSetSidetone(level, _connection.Wireless), false, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _diagnostic?.Invoke($"BlackShark sidetone restore failed: {exception.Message}");
        }
    }

    private async Task TrySendAsync(byte[] report)
    {
        try
        {
            await _transport.SendAsync(report, false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _diagnostic?.Invoke($"BlackShark restore write failed: {exception.Message}");
        }
    }

    internal static bool IsBlackSharkControlInterface(
        ushort productId,
        string? productName,
        ushort usagePage,
        ushort usage,
        int outputReportByteLength)
    {
        // Catalog-declared identities must be the dedicated V3 Pro panel; unknown PIDs keep
        // the product-name fallback for unlisted SKUs, but known non-dedicated BlackShark
        // devices (0528, 0A4E, 101E, ...) must not be pulled into the V3 Pro protocol.
        var channel = RazerHeadsetChannelCatalog.Resolve(productId);
        var namedBlackShark =
            productName?.Contains("BlackShark", StringComparison.OrdinalIgnoreCase) == true;
        // A known catalog entry that resolves Unsupported is a deliberate exclusion, not an
        // unknown SKU; the product-name fallback would pull 0565 into the V3 Pro protocol.
        if (channel == RazerHeadsetChannel.Unsupported &&
            RazerHeadsetChannelCatalog.IsKnown(productId))
        {
            return false;
        }

        if (channel != RazerHeadsetChannel.DedicatedBlackShark &&
            (channel != RazerHeadsetChannel.Unsupported || !namedBlackShark))
        {
            return false;
        }

        if (usagePage is not (0xFF13 or 0xFF14) || usage != 0x0001 ||
            outputReportByteLength < BlackSharkV3ProProtocol.ReportLength)
        {
            return false;
        }

        return namedBlackShark ||
            channel == RazerHeadsetChannel.DedicatedBlackShark;
    }

    // Only the wireless dongle pid answers on the active link; prefer it so a plugged-in USB
    // cable cannot make a 2.4 GHz session look wired.
    private static int ProductPriority(ushort productId) => productId switch
    {
        BlackSharkV3ProProtocol.DongleProductId => 0,
        BlackSharkV3ProProtocol.WirelessProductId => 1,
        BlackSharkV3ProProtocol.WiredProductId => 2,
        BlackSharkV3ProProtocol.UsbProductId => 2,
        _ => 3,
    };

    private static string DescribeInterface(WindowsHidDiscovery.HidInterfaceDescriptor item) =>
        $"product='{item.ProductName}' pid_{item.ProductId:X4}:0x{item.UsagePage:X4}:0x{item.Usage:X4}:" +
        $"in{item.InputReportByteLength}/out{item.OutputReportByteLength}/{item.Access}";

    private static bool TryMapAncMode(byte raw, out BlackSharkAncMode mode)
    {
        switch (raw)
        {
            case (byte)BlackSharkAncMode.Off:
                mode = BlackSharkAncMode.Off;
                return true;
            case (byte)BlackSharkAncMode.Anc:
                mode = BlackSharkAncMode.Anc;
                return true;
            case (byte)BlackSharkAncMode.Ambient:
                mode = BlackSharkAncMode.Ambient;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
