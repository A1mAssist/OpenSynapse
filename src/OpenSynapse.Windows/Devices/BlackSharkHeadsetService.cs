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
        // OpenRazer ships no BlackShark driver at all, so the product ids this code started
        // from were never verified against upstream. This headset also ships a PlayStation
        // variant that enumerates under a different id, so select on the control collection
        // shape instead of a fixed pid list.
        var candidates = interfaces
            .Where(item => item.UsagePage is 0xFF13 or 0xFF14 && item.Usage == 0x0001 &&
                           item.OutputReportByteLength >= BlackSharkV3ProProtocol.ReportLength)
            .ToArray();
        if (candidates.Length == 0)
        {
            diagnostic?.Invoke(
                $"BlackShark found no 1532 control interface with a " +
                $"{BlackSharkV3ProProtocol.ReportLength}-byte output report; " +
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
        var wireless = chosen.ProductId != BlackSharkV3ProProtocol.WiredProductId;
        diagnostic?.Invoke(
            $"BlackShark selected vid_{chosen.VendorId:X4}&pid_{chosen.ProductId:X4} " +
            $"usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} out={chosen.OutputReportByteLength} " +
            $"in={chosen.InputReportByteLength} access={chosen.Access} direction={(wireless ? "wireless" : "wired")}; " +
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
            0x9E,
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
            gameChat);
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
            0x9E,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.SendAsync(
                BlackSharkV3ProProtocol.CreateSetThx(enabled, _connection.Wireless), false, cancellationToken)
                .ConfigureAwait(false);
            var actual = await ReadValueWithRetryAsync(0x9E, cancellationToken).ConfigureAwait(false);
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
        var previous = await ReadValueAsync(
            BlackSharkV3ProProtocol.CreateGetGameChat(_connection.Wireless),
            0x5C,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.SendAsync(
                BlackSharkV3ProProtocol.CreateSetGameChat(balance, _connection.Wireless), false, cancellationToken)
                .ConfigureAwait(false);
            var actual = await ReadValueWithRetryAsync(0x5C, cancellationToken).ConfigureAwait(false);
            if (actual != balance)
            {
                throw new InvalidOperationException($"BlackShark game/chat readback was {actual?.ToString() ?? "empty"}, expected {balance}.");
            }
        }
        catch
        {
            if (previous is byte restore)
            {
                await TrySendAsync(BlackSharkV3ProProtocol.CreateSetGameChat(restore, _connection.Wireless))
                    .ConfigureAwait(false);
            }

            throw;
        }
    }

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

    // Only the wireless dongle pid answers on the active link; prefer it so a plugged-in USB
    // cable cannot make a 2.4 GHz session look wired.
    private static int ProductPriority(ushort productId) => productId switch
    {
        BlackSharkV3ProProtocol.DongleProductId => 0,
        BlackSharkV3ProProtocol.WirelessProductId => 1,
        BlackSharkV3ProProtocol.WiredProductId => 2,
        _ => 3,
    };

    private static string DescribeInterface(WindowsHidDiscovery.HidInterfaceDescriptor item) =>
        $"pid_{item.ProductId:X4}:0x{item.UsagePage:X4}:0x{item.Usage:X4}:" +
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
