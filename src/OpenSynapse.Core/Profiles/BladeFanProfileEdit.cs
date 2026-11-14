using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Profiles;

/// <summary>A fan edit rolls back its own fields, never the entire profile document.</summary>
public sealed class BladeFanProfileEdit
{
    private readonly BladeProfileSettings _target;
    private readonly BladeFanControlMode? _controlMode;
    private readonly BladeFanCurveProfile? _curve;
    private readonly byte? _mode;
    private readonly int? _rpm;
    private readonly byte? _maxFanMode;
    private readonly byte? _performanceMode;
    private readonly bool _restorePerformance;

    public BladeFanProfileEdit(BladeProfileSettings target, bool restorePerformance = false)
    {
        _target = target;
        _controlMode = target.FanControlMode;
        _curve = target.FanCurve;
        _mode = target.FanMode;
        _rpm = target.FanTargetRpm;
        _maxFanMode = target.MaxFanMode;
        _performanceMode = target.PerformanceMode;
        _restorePerformance = restorePerformance;
    }

    public void Restore()
    {
        _target.FanCurve = _curve;
        _target.FanControlMode = _controlMode;
        _target.FanMode = _mode;
        _target.FanTargetRpm = _rpm;
        _target.MaxFanMode = _maxFanMode;
        if (_restorePerformance) _target.PerformanceMode = _performanceMode;
    }

    public async Task SaveAsync(Func<CancellationToken, Task<bool>> save,
        CancellationToken cancellationToken, string failureMessage)
    {
        try
        {
            if (!await save(cancellationToken)) throw new InvalidOperationException(failureMessage);
        }
        catch
        {
            Restore();
            throw;
        }
    }
}
