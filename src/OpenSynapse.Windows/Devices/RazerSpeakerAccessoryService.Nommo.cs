using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed partial class RazerSpeakerAccessoryService
{
    private readonly ushort? _nommoProductId;
    private readonly Func<byte[], CancellationToken, Task<byte[]>>? _nommoQuery;
    private readonly Func<int?, bool?, CancellationToken, Task<(int Volume, bool Muted)>>? _endpoint;

    internal RazerSpeakerAccessoryService(ushort productId,
        Func<byte[], CancellationToken, Task<byte[]>> query,
        Func<int?, bool?, CancellationToken, Task<(int Volume, bool Muted)>> endpoint)
    {
        if (productId is not (0x0517 or 0x0518)) throw new ArgumentOutOfRangeException(nameof(productId));
        _nommoProductId = productId;
        _nommoQuery = query;
        _endpoint = endpoint;
    }

    internal static RazerSpeakerAccessoryService OpenNommo(RazerHidDeviceConnection connection)
    {
        if (!connection.IsReady || string.IsNullOrWhiteSpace(connection.DevicePath))
            throw new InvalidOperationException("The speaker HID endpoint is unavailable.");
        var transport = new RazerFeatureTransport();
        var container = connection.InstanceId.StartsWith("container:", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(connection.InstanceId[10..], out var id) ? id : Guid.Empty;
        return new(connection.Definition.ProductId,
            (frame, ct) => transport.QueryAsync(connection.DevicePath, frame[2], frame[6], frame[7], frame[8],
                frame.AsMemory(9, frame[6]), connection.Definition.DeviceWait, ct),
            (volume, mute, ct) => WindowsSpeakerEndpoint.UpdateAsync(container, volume, mute, ct));
    }

    private async Task<byte[]> QueryNommoAsync(byte[] request, CancellationToken ct)
    {
        var response = await _nommoQuery!(request, ct).ConfigureAwait(false);
        if (!RazerAccessoryProtocol.MatchesCommand(response, request[7], request[8]) || response[1] != 2)
            throw new InvalidDataException("The speaker returned an unsuccessful or mismatched reply.");
        return response;
    }

    private async Task SetEndpointAsync(int? volume, bool? mute, CancellationToken ct) =>
        _ = await _endpoint!(volume, mute, ct).ConfigureAwait(false);

    private async Task<RazerSpeakerState> ReadNommoStateAsync(CancellationToken ct)
    {
        var errors = new List<string>();
        int? volume = null; bool? mute = null;
        try { (volume, mute) = await _endpoint!(null, null, ct).ConfigureAwait(false); }
        catch (Exception e) when (IsExpected(e)) { errors.Add(e.Message); }
        var eq = await TryReadAsync("EQ preset", 0x82, ReadNommoScalar, errors, ct).ConfigureAwait(false);
        int? bass = null, input = null; bool? dolby = null; IReadOnlyList<int>? bands = null;
        if (ProductId == 0x0518)
        {
            bass = await TryReadAsync("bass", 0x87, ReadNommoScalar, errors, ct).ConfigureAwait(false);
            input = await TryReadAsync("input", 0x8A, ReadNommoScalar, errors, ct).ConfigureAwait(false);
            try { bands = await ReadNommoBandsAsync(ct).ConfigureAwait(false); }
            catch (Exception e) when (IsExpected(e)) { errors.Add(e.Message); }
            try { dolby = await ReadNommoDolbyAsync(ct).ConfigureAwait(false); }
            catch (Exception e) when (IsExpected(e)) { errors.Add(e.Message); }
        }
        return new(volume, mute, bass, eq, input, null, null, null,
            errors.Count == 0 ? null : string.Join("; ", errors)) { EqBands = bands, Dolby = dolby };
    }

    private static int ReadNommoScalar(byte[] response)
    {
        var payload = RazerAccessoryProtocol.Arguments(response);
        if (payload.Length < 2 || payload[0] != 0) throw new InvalidDataException("Invalid Nommo profile scalar reply.");
        return payload[1];
    }

    private async Task<IReadOnlyList<int>> ReadNommoBandsAsync(CancellationToken ct)
    {
        var request = RazerAccessoryProtocol.CreateRequest(91, 0, 0x1F, 9, 8, 0x84, new byte[9]);
        var response = await QueryNommoAsync(request, ct).ConfigureAwait(false);
        var data = RazerAccessoryProtocol.Arguments(response).ToArray();
        if (data.Length != 9 || data[0] != 0 || data.Skip(1).Any(v => v > 100))
            throw new InvalidDataException("Nommo Pro EQ must contain profile 0 and eight levels from 0 to 100.");
        return data.Skip(1).Select(v => (int)v).ToArray();
    }

    private async Task<bool> ReadNommoDolbyAsync(CancellationToken ct)
    {
        var value = ReadNommoScalar(await QueryNommoAsync(
            RazerAccessoryProtocol.CreateRequest(91, 0, 0x1F, 11, 8, 0x8C, new byte[11]), ct).ConfigureAwait(false));
        return value switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid Nommo Dolby state.") };
    }

    internal async Task SetNommoDolbyAsync(bool enabled, CancellationToken ct)
    {
        if (ProductId != 0x0518) throw new NotSupportedException("Dolby is available on Nommo Pro only.");
        var payload = new byte[11]; payload[1] = enabled ? (byte)1 : (byte)0;
        await QueryNommoAsync(RazerAccessoryProtocol.CreateRequest(91, 0, 0x1F, 11, 8, 0x0C, payload), ct).ConfigureAwait(false);
        if (await ReadNommoDolbyAsync(ct).ConfigureAwait(false) != enabled)
            throw new InvalidOperationException("Speaker Dolby readback did not match.");
    }

    internal Task ApplyNommoProfileAsync(ProfileDocument document, DeviceDescriptor device,
        PowerSourceKind powerSource, CancellationToken ct)
    {
        var settings = new DeviceProfileSettings { Headset = ProfileResolver.Resolve(document, device, powerSource).Headset };
        // Resolve synchronizes the public Devices view to the active profile; Extras stay device-scoped.
        if (document.Devices.TryGetValue(ProfileResolver.GetDeviceKey(device), out var saved) &&
            saved.Extras.TryGetValue("nommo.muted", out var mute)) settings.Extras["nommo.muted"] = mute;
        return ApplyNommoProfileAsync(settings, ct);
    }

    internal async Task ApplyNommoProfileAsync(DeviceProfileSettings settings, CancellationToken ct)
    {
        var h = settings.Headset;
        int? presetIndex = null;
        if (h.EqPreset is { } preset)
        {
            if (!int.TryParse(preset, out var index) ||
                index is < 0 or > 3 && !(ProductId == 0x0518 && index == 16))
                throw new InvalidDataException("Invalid Nommo EQ preset.");
            presetIndex = index;
        }
        if (h.VolumePercent is < 0 or > 100 || h.Bass is < 0 or > 100)
            throw new InvalidDataException("Nommo volume and bass must be from 0 to 100.");
        if (ProductId == 0x0518 && h.AudioSourceIndex is < 1 or > 4)
            throw new InvalidDataException("Invalid Nommo input source.");
        var customBands = ProductId == 0x0518 && presetIndex == 16 ? h.EqBands : null;
        if (customBands is not null && (customBands.Count != 8 ||
            Enumerable.Range(0, 8).Any(i => !customBands.ContainsKey(i.ToString())) ||
            customBands.Values.Any(v => v is < 0 or > 100)))
            throw new InvalidDataException("Nommo Pro profile requires all eight EQ bands from 0 to 100.");
        if (ProductId == 0x0518 && presetIndex == 16 && h.Dolby == true)
            throw new InvalidDataException("Custom EQ requires Dolby to be off.");
        // The middleware selects Dolby before the EQ preset; custom EQ first disables Dolby.
        if (ProductId == 0x0518 && (h.Dolby is not null || presetIndex == 16))
            await SetNommoDolbyAsync(h.Dolby == true, ct).ConfigureAwait(false);
        if (presetIndex is { } selected) await SetEqPresetAsync(selected, ct).ConfigureAwait(false);
        if (ProductId == 0x0518)
        {
            if (customBands is not null)
                await SetEqBandsAsync(Enumerable.Range(0, 8).Select(i => customBands[i.ToString()]).ToArray(), ct).ConfigureAwait(false);
            if (h.Bass is { } bass) await SetBassAsync(bass, ct).ConfigureAwait(false);
        }
        if (h.VolumePercent is { } volume) await SetVolumeAsync(volume, ct).ConfigureAwait(false);
        if (settings.Extras.TryGetValue("nommo.muted", out var value) && bool.TryParse(value.Value, out var mute))
            await SetMuteAsync(mute, ct).ConfigureAwait(false);
        if (ProductId == 0x0518 && h.AudioSourceIndex is { } source)
        {
            await SetInputSourceAsync(source, ct).ConfigureAwait(false);
        }
    }
}
