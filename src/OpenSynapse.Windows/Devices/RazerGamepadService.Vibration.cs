using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerGamepadService
{
    private readonly SemaphoreSlim _vibrationGate = new(1, 1);

    public async Task SetVibrationScaleAsync(byte scale, CancellationToken cancellationToken = default)
    {
        await _vibrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = _profileId;
            var previous = new Dictionary<byte, byte>();
            foreach (var motor in VibrationMotorIds)
                previous[motor] = RazerGamepadProtocol.ReadVibrationScale(await QueryAsync(
                    t => RazerGamepadProtocol.CreateGetVibrationScaleRequest(t, profile, motor),
                    $"read vibration {motor}", cancellationToken).ConfigureAwait(false), motor);
            if (previous.Values.All(value => value == scale)) return;
            var attempted = new List<byte>();
            try
            {
                foreach (var motor in VibrationMotorIds)
                {
                    attempted.Add(motor);
                    await QueryAsync(t => RazerGamepadProtocol.CreateSetVibrationScaleRequest(t, profile, motor, scale),
                        $"set vibration {motor}", cancellationToken).ConfigureAwait(false);
                }
                foreach (var motor in VibrationMotorIds)
                {
                    var actual = RazerGamepadProtocol.ReadVibrationScale(await QueryAsync(
                        t => RazerGamepadProtocol.CreateGetVibrationScaleRequest(t, profile, motor),
                        $"read back vibration {motor}", cancellationToken).ConfigureAwait(false), motor);
                    if (actual != scale) throw new InvalidOperationException($"Motor {motor} readback was {actual}, expected {scale}.");
                }
            }
            catch
            {
                foreach (var motor in attempted.AsEnumerable().Reverse())
                {
                    try
                    {
                        await QueryAsync(t => RazerGamepadProtocol.CreateSetVibrationScaleRequest(t, profile, motor, previous[motor]),
                            $"restore vibration {motor}", CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (IsExpected(exception))
                    { _diagnostic?.Invoke($"gamepad restore vibration {motor} failed: {exception.Message}"); }
                }
                throw;
            }
        }
        finally { _vibrationGate.Release(); }
    }
}
