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
            _diagnostic?.Invoke($"krakenbase firmware read failed: {exception.Message}");
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
            _diagnostic?.Invoke($"krakenbase serial number read failed: {exception.Message}");
            errors.Add($"serial number: {exception.Message}");
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
            null,
            null,
            errors)
        {
            SerialNumber = serial,
        };
    }

    private Task<byte[]> QueryKrakenBaseAsync(
        byte[] request,
        CancellationToken cancellationToken) =>
        _hidReports.QueryAsync(
            _devicePath,
            RazerKrakenBaseProtocol.PadOutputReport(request, _outputReportLength),
            _deviceWait,
            cancellationToken);

}
