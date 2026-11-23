using System.Collections.Concurrent;
using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerRipsawState(byte[] Volumes, bool[] Muted,
    RazerRipsawHdmiStatus? Hdmi, string? Firmware, string? Serial, string? Edition,
    IReadOnlyList<string> Errors);

public interface IRazerRipsawService
{
    Task<RazerRipsawState> ReadStateAsync(CancellationToken cancellationToken = default);
    Task SetVolumeAsync(RazerRipsawChannel channel, int volume, CancellationToken cancellationToken = default);
    Task SetMuteAsync(RazerRipsawChannel channel, bool muted, CancellationToken cancellationToken = default);
}

public sealed class RazerRipsawService : IRazerRipsawService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate;
    private readonly Func<byte[], CancellationToken, Task<byte[]>> _query;
    private bool _initialized;

    public RazerRipsawService(string devicePath)
        : this(devicePath, new RazerRipsawTransport(devicePath).QueryAsync) { }

    internal RazerRipsawService(string devicePath, Func<byte[], CancellationToken, Task<byte[]>> query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _gate = Gates.GetOrAdd(devicePath, static _ => new SemaphoreSlim(1, 1));
    }

    public async Task<RazerRipsawState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            var muted = RazerRipsawProtocol.ReadMutes(await QueryAsync(RazerRipsawProtocol.CreateReadMute(), cancellationToken).ConfigureAwait(false));
            var volumes = RazerRipsawProtocol.ReadVolumes(await QueryAsync(RazerRipsawProtocol.CreateReadVolume(), cancellationToken).ConfigureAwait(false));
            var errors = new List<string>();
            var hdmi = await OptionalAsync("HDMI", async () => RazerRipsawProtocol.ReadHdmiStatus(
                await QueryAsync(RazerRipsawProtocol.CreateReadHdmiStatus(), cancellationToken).ConfigureAwait(false)), errors).ConfigureAwait(false);
            var firmware = await OptionalAsync("Firmware", async () =>
            {
                var bytes = await QueryAsync(RazerRipsawProtocol.CreateReadFirmware(), cancellationToken).ConfigureAwait(false);
                return string.Join(".", bytes[6..10]);
            }, errors).ConfigureAwait(false);
            var serial = await OptionalAsync("Serial", async () =>
            {
                var high = await QueryAsync(RazerRipsawProtocol.CreateReadSerialHigh(), cancellationToken).ConfigureAwait(false);
                var low = await QueryAsync(RazerRipsawProtocol.CreateReadSerialLow(), cancellationToken).ConfigureAwait(false);
                return RazerRipsawProtocol.ReadSerial(high, low);
            }, errors).ConfigureAwait(false);
            var edition = await OptionalAsync("Edition", async () =>
            {
                var bytes = await QueryAsync(RazerRipsawProtocol.CreateReadEdition(), cancellationToken).ConfigureAwait(false);
                return $"{bytes[6]}.{bytes[7]}";
            }, errors).ConfigureAwait(false);
            return new(volumes, muted, hdmi, firmware, serial, edition, errors);
        }
        finally { _gate.Release(); }
    }

    public async Task SetVolumeAsync(RazerRipsawChannel channel, int volume, CancellationToken cancellationToken = default)
    {
        if (volume is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(volume));
        var request = RazerRipsawProtocol.CreateSetVolume(channel, (byte)volume);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            await QueryAsync(request, cancellationToken).ConfigureAwait(false);
            var actual = RazerRipsawProtocol.ReadVolumes(await QueryAsync(RazerRipsawProtocol.CreateReadVolume(), cancellationToken).ConfigureAwait(false));
            if (actual[(int)channel] != volume) throw new IOException("Ripsaw HD volume readback did not match.");
        }
        finally { _gate.Release(); }
    }

    public async Task SetMuteAsync(RazerRipsawChannel channel, bool muted, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            var previous = await QueryAsync(RazerRipsawProtocol.CreateReadMute(), cancellationToken).ConfigureAwait(false);
            var request = RazerRipsawProtocol.CreateSetMute(previous, channel, muted);
            await QueryAsync(request, cancellationToken).ConfigureAwait(false);
            var actual = RazerRipsawProtocol.ReadMutes(await QueryAsync(RazerRipsawProtocol.CreateReadMute(), cancellationToken).ConfigureAwait(false));
            if (!actual.SequenceEqual(request[7..12].Select(value => value != 0)))
                throw new IOException("Ripsaw HD mute readback did not match.");
        }
        finally { _gate.Release(); }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await QueryAsync(RazerRipsawProtocol.CreateInit(), cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    private async Task<byte[]> QueryAsync(byte[] request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _query(request, cancellationToken).ConfigureAwait(false);
            RazerRipsawProtocol.ValidateResponse(response);
            return response;
        }
        catch
        {
            _initialized = false;
            throw;
        }
    }

    private static async Task<T?> OptionalAsync<T>(string label, Func<Task<T>> read, List<string> errors) where T : class
    {
        try { return await read().ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or Win32Exception or TimeoutException or UnauthorizedAccessException)
        {
            errors.Add($"{label}: {exception.Message}");
            return null;
        }
    }
}
