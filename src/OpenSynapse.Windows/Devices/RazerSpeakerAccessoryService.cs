using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed partial class RazerSpeakerAccessoryService : IAsyncDisposable
{
    private readonly RazerAccessoryCommandClient? _client;

    internal RazerSpeakerAccessoryService(RazerAccessoryCommandClient client) => _client = client;

    internal ushort ProductId => _nommoProductId ?? _client!.ProductId;
    internal int ReportLength => _nommoProductId is not null ? 91 : _client!.ReportLength;

    internal static async Task<RazerSpeakerAccessoryService?> ConnectAsync(
        ushort productId,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var preferred = productId == 0x0F64 ? 65 : 91;
        var connection = await RazerAccessoryDiscovery.FindAsync(
            productId,
            preferred,
            diagnostic,
            cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            connection = await RazerAccessoryDiscovery.FindAsync(
                productId,
                preferred == 65 ? 91 : 65,
                diagnostic,
                cancellationToken).ConfigureAwait(false);
        }

        return connection is null
            ? null
            : new RazerSpeakerAccessoryService(
                new RazerAccessoryCommandClient(connection, diagnostic));
    }

    internal async Task<RazerSpeakerState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        if (_nommoProductId is not null) return await ReadNommoStateAsync(cancellationToken).ConfigureAwait(false);
        var errors = new List<string>();
        var volume = await TryReadAsync(
            "volume", RazerSpeakerAccessoryProtocol.GetVolume, RazerSpeakerAccessoryProtocol.ReadScalar,
            errors, cancellationToken).ConfigureAwait(false);
        var muted = await TryReadAsync(
            "mute", RazerSpeakerAccessoryProtocol.GetMute, RazerSpeakerAccessoryProtocol.ReadBool,
            errors, cancellationToken).ConfigureAwait(false);
        var bass = await TryReadAsync(
            "bass", RazerSpeakerAccessoryProtocol.GetBass, RazerSpeakerAccessoryProtocol.ReadScalar,
            errors, cancellationToken).ConfigureAwait(false);
        var eq = await TryReadAsync(
            "EQ preset", RazerSpeakerAccessoryProtocol.GetEqPreset, RazerSpeakerAccessoryProtocol.ReadScalar,
            errors, cancellationToken).ConfigureAwait(false);
        var input = await TryReadAsync(
            "input source", RazerSpeakerAccessoryProtocol.GetInputSource, RazerSpeakerAccessoryProtocol.ReadScalar,
            errors, cancellationToken).ConfigureAwait(false);
        var output = await TryReadAsync(
            "output source", RazerSpeakerAccessoryProtocol.GetOutputSource, RazerSpeakerAccessoryProtocol.ReadScalar,
            errors, cancellationToken).ConfigureAwait(false);
        var micVolume = await TryReadAsync(
            "mic volume", RazerSpeakerAccessoryProtocol.GetMicVolume, RazerSpeakerAccessoryProtocol.ReadScalar,
            errors, cancellationToken).ConfigureAwait(false);
        var micMuted = await TryReadAsync(
            "mic mute", RazerSpeakerAccessoryProtocol.GetMicMute, RazerSpeakerAccessoryProtocol.ReadBool,
            errors, cancellationToken).ConfigureAwait(false);

        return new RazerSpeakerState(
            volume,
            muted,
            bass,
            eq,
            input,
            output,
            micVolume,
            micMuted,
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    internal Task SetVolumeAsync(int value, CancellationToken cancellationToken = default) =>
        _nommoProductId is not null ? SetEndpointAsync(value, null, cancellationToken) : WriteScalarAsync(
            RazerSpeakerAccessoryProtocol.SetVolume,
            RazerSpeakerAccessoryProtocol.GetVolume,
            value,
            cancellationToken);

    internal Task SetBassAsync(int value, CancellationToken cancellationToken = default) =>
        WriteScalarAsync(
            RazerSpeakerAccessoryProtocol.SetBass,
            RazerSpeakerAccessoryProtocol.GetBass,
            value,
            cancellationToken);

    internal Task SetEqPresetAsync(int value, CancellationToken cancellationToken = default) =>
        WriteScalarAsync(
            RazerSpeakerAccessoryProtocol.SetEqPreset,
            RazerSpeakerAccessoryProtocol.GetEqPreset,
            value,
            cancellationToken);

    internal Task SetInputSourceAsync(int value, CancellationToken cancellationToken = default) =>
        WriteScalarAsync(
            RazerSpeakerAccessoryProtocol.SetInputSource,
            RazerSpeakerAccessoryProtocol.GetInputSource,
            value,
            cancellationToken);

    internal Task SetOutputSourceAsync(int value, CancellationToken cancellationToken = default) =>
        WriteScalarAsync(
            RazerSpeakerAccessoryProtocol.SetOutputSource,
            RazerSpeakerAccessoryProtocol.GetOutputSource,
            value,
            cancellationToken);

    internal Task SetMicVolumeAsync(int value, CancellationToken cancellationToken = default) =>
        WriteScalarAsync(
            RazerSpeakerAccessoryProtocol.SetMicVolume,
            RazerSpeakerAccessoryProtocol.GetMicVolume,
            value,
            cancellationToken);

    internal async Task SetMuteAsync(bool value, CancellationToken cancellationToken = default)
    {
        if (_nommoProductId is not null)
        {
            await SetEndpointAsync(null, value, cancellationToken).ConfigureAwait(false);
            return;
        }
        await SendAsync(
            RazerSpeakerAccessoryProtocol.CreateSetBool(
                RazerSpeakerAccessoryProtocol.SetMute, value, ReportLength),
            cancellationToken).ConfigureAwait(false);
        var actual = await ReadAsync(
            RazerSpeakerAccessoryProtocol.GetMute,
            RazerSpeakerAccessoryProtocol.ReadBool,
            cancellationToken).ConfigureAwait(false);
        if (actual != value)
        {
            throw new InvalidOperationException("Speaker mute readback did not match.");
        }
    }

    internal async Task SetMicMuteAsync(bool value, CancellationToken cancellationToken = default)
    {
        await SendAsync(
            RazerSpeakerAccessoryProtocol.CreateSetBool(
                RazerSpeakerAccessoryProtocol.SetMicMute, value, ReportLength),
            cancellationToken).ConfigureAwait(false);
        var actual = await ReadAsync(
            RazerSpeakerAccessoryProtocol.GetMicMute,
            RazerSpeakerAccessoryProtocol.ReadBool,
            cancellationToken).ConfigureAwait(false);
        if (actual != value)
        {
            throw new InvalidOperationException("Speaker microphone mute readback did not match.");
        }
    }

    internal async Task SetEqBandsAsync(
        IReadOnlyList<int> bands,
        CancellationToken cancellationToken = default)
    {
        if (_nommoProductId is not null)
        {
            if (ProductId != 0x0518 || bands.Count != 8 || bands.Any(value => value is < 0 or > 100))
                throw new ArgumentOutOfRangeException(nameof(bands));
            await SendAsync(RazerSpeakerAccessoryProtocol.CreateSetEqSetting(91, bands), cancellationToken).ConfigureAwait(false);
            var actual = await ReadNommoBandsAsync(cancellationToken).ConfigureAwait(false);
            if (!actual.SequenceEqual(bands)) throw new InvalidOperationException("Speaker EQ readback did not match.");
            return;
        }
        await SendAsync(
            RazerSpeakerAccessoryProtocol.CreateSetEqSetting(ReportLength, bands),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteScalarAsync(
        byte setCommand,
        byte getCommand,
        int value,
        CancellationToken cancellationToken)
    {
        await SendAsync(
            RazerSpeakerAccessoryProtocol.CreateSet(setCommand, value, ReportLength),
            cancellationToken).ConfigureAwait(false);
        Func<byte[], int> parse = _nommoProductId is not null ? ReadNommoScalar : RazerSpeakerAccessoryProtocol.ReadScalar;
        var actual = await ReadAsync(getCommand, parse, cancellationToken).ConfigureAwait(false);
        if (_nommoProductId is not null ? actual != value : Math.Abs(actual - value) > 1)
        {
            throw new InvalidOperationException(
                $"Speaker readback was {actual}, expected {value}.");
        }
    }

    private Task SendAsync(byte[] request, CancellationToken cancellationToken) =>
        _nommoProductId is not null ? QueryNommoAsync(request, cancellationToken) : _client!.QueryAsync(
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerAccessoryProtocol.ArgumentsOffset, request[6]),
            response => RazerAccessoryProtocol.MatchesCommand(response, request[7], request[8]),
            cancellationToken);

    private Task<T> ReadAsync<T>(
        byte commandId,
        Func<byte[], T> parse,
        CancellationToken cancellationToken) =>
        ReadAsync(commandId, parse, cancellationToken, allowMissing: false);

    private async Task<T> ReadAsync<T>(
        byte commandId,
        Func<byte[], T> parse,
        CancellationToken cancellationToken,
        bool allowMissing)
    {
        var request = RazerSpeakerAccessoryProtocol.CreateGet(commandId, ReportLength);
        var response = _nommoProductId is not null
            ? await QueryNommoAsync(request, cancellationToken).ConfigureAwait(false)
            : await _client!.QueryAsync(
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerAccessoryProtocol.ArgumentsOffset, request[6]),
            candidate => RazerAccessoryProtocol.MatchesCommand(candidate, request[7], request[8]),
            cancellationToken).ConfigureAwait(false);
        return parse(response);
    }

    private async Task<int?> TryReadAsync(
        string label,
        byte commandId,
        Func<byte[], int> parse,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadAsync(commandId, parse, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"{label}: {exception.Message}");
            return null;
        }
    }

    private async Task<bool?> TryReadAsync(
        string label,
        byte commandId,
        Func<byte[], bool> parse,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadAsync(commandId, parse, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"{label}: {exception.Message}");
            return null;
        }
    }

    public ValueTask DisposeAsync() => _client?.DisposeAsync() ?? ValueTask.CompletedTask;

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or ArgumentException;
}
