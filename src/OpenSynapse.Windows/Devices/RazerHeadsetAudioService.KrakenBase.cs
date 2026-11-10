using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    private async Task<RazerHeadsetAudioState> ReadKrakenBaseStateAsync(
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        Version? firmware = null;
        try
        {
            firmware = RazerKrakenBaseProtocol.ParseFirmware(
                await QueryKrakenBaseAsync(
                        RazerKrakenBaseProtocol.CreateFirmwareRequest(), cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"firmware: {exception.Message}");
        }

        string? serial = null;
        try
        {
            serial = RazerKrakenBaseProtocol.ParseSerialNumber(
                await QueryKrakenBaseAsync(
                        RazerKrakenBaseProtocol.CreateSerialNumberRequest(), cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"serial number: {exception.Message}");
        }

        bool? anc = null;
        try
        {
            anc = RazerKrakenBaseProtocol.ParseAncEnabled(
                await QueryKrakenBaseAsync(
                        RazerKrakenBaseProtocol.CreateAncGetRequest(), cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"ANC: {exception.Message}");
        }

        return new RazerHeadsetAudioState(
            firmware,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            anc is null ? null : anc.Value ? RazerAncState.On : RazerAncState.Off,
            null,
            errors)
        {
            SerialNumber = serial,
        };
    }

    private Task<byte[]> QueryKrakenBaseAsync(
        byte[] request,
        CancellationToken cancellationToken) =>
        _hidReports.QueryAsync(_devicePath, request, _deviceWait, cancellationToken);

}
