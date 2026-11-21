using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    private static readonly TimeSpan CamyT1Timeout = TimeSpan.FromMilliseconds(600);

    private async Task<RazerHeadsetAudioState> ReadCamyT1StateAsync(
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        byte? volume = null;
        byte? mute = null;
        byte? hpf = null;
        byte? limiter = null;

        async Task ReadAsync(byte command, Action<byte> apply, string label)
        {
            try
            {
                apply(await QueryCamyT1Async(command, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _diagnostic?.Invoke($"camyt1 {label} read failed: {exception.Message}");
                errors.Add($"{label}: {exception.Message}");
            }
        }

        await ReadAsync(RazerCamyT1Protocol.MicVolume, value => volume = value, "microphone volume")
            .ConfigureAwait(false);
        await ReadAsync(RazerCamyT1Protocol.MicMute, value => mute = value, "microphone mute")
            .ConfigureAwait(false);
        await ReadAsync(RazerCamyT1Protocol.MicHighPassFilter, value => hpf = value, "high-pass filter")
            .ConfigureAwait(false);
        await ReadAsync(RazerCamyT1Protocol.MicLimiter, value => limiter = value, "microphone limiter")
            .ConfigureAwait(false);

        return new RazerHeadsetAudioState(
            null,
            null,
            null,
            null,
            null,
            null,
            MicMuted: mute is null ? null : mute == 1,
            MicVolume: volume,
            AudioSource: null,
            NoiseSuppression: null,
            AncState: null,
            AncLevel: null,
            errors)
        {
            MicHpf = hpf is null ? null : hpf == 1,
            MicLimiterEnabled = limiter is null ? null : limiter == 1,
        };
    }

    public Task SetMicHpfAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SetCamyT1Async(
            RazerCamyT1Protocol.MicHighPassFilter,
            enabled ? (byte)1 : (byte)0,
            "microphone high-pass filter",
            cancellationToken);

    public Task SetMicLimiterEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SetCamyT1Async(
            RazerCamyT1Protocol.MicLimiter,
            enabled ? (byte)1 : (byte)0,
            "microphone limiter",
            cancellationToken);

    private async Task<byte> QueryCamyT1Async(byte command, CancellationToken cancellationToken)
    {
        var response = await _hidReports.QueryInputReportAsync(
                _devicePath,
                RazerCamyT1Protocol.CreateReadRequest(command),
                command,
                CamyT1Timeout,
                cancellationToken)
            .ConfigureAwait(false);
        return RazerCamyT1Protocol.ParseValue(response, command);
    }

    private async Task SetCamyT1Async(
        byte command,
        byte value,
        string label,
        CancellationToken cancellationToken)
    {
        await _hidReports.SendOutputReportAsync(
                _devicePath,
                RazerCamyT1Protocol.CreateWriteRequest(command, value),
                CamyT1Timeout,
                cancellationToken)
            .ConfigureAwait(false);
        var readBack = await QueryCamyT1Async(command, cancellationToken).ConfigureAwait(false);
        if (readBack != value)
        {
            throw new InvalidDataException(
                $"CamyT1 {label} read back {readBack} after writing {value}.");
        }
    }
}
