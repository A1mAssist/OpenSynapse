namespace OpenSynapse.Core.Profiles;

/// <summary>Roll back only the edited preference when persistence fails, including cancellation.</summary>
public static class BladePerformanceProfileEdit
{
    public static async Task SaveModeAsync(BladeProfileSettings target, byte mode,
        Func<CancellationToken, Task<bool>> save, CancellationToken cancellationToken, string failureMessage)
    {
        var previous = target.PerformanceMode;
        target.PerformanceMode = mode;
        try
        {
            if (!await save(cancellationToken)) throw new InvalidOperationException(failureMessage);
        }
        catch
        {
            if (target.PerformanceMode == mode) target.PerformanceMode = previous;
            throw;
        }
    }

    public static async Task SaveMaxFanAsync(BladeProfileSettings target, byte mode,
        Func<CancellationToken, Task<bool>> save, CancellationToken cancellationToken, string failureMessage)
    {
        var previous = target.MaxFanMode;
        target.MaxFanMode = mode;
        try
        {
            if (!await save(cancellationToken)) throw new InvalidOperationException(failureMessage);
        }
        catch
        {
            if (target.MaxFanMode == mode) target.MaxFanMode = previous;
            throw;
        }
    }

    public static async Task SaveChargeLimitAsync(BladeProfileSettings target, int percent,
        Func<CancellationToken, Task<bool>> save, CancellationToken cancellationToken, string failureMessage)
    {
        var previous = target.ChargeLimitPercent;
        target.ChargeLimitPercent = percent;
        try
        {
            if (!await save(cancellationToken)) throw new InvalidOperationException(failureMessage);
        }
        catch
        {
            if (target.ChargeLimitPercent == percent) target.ChargeLimitPercent = previous;
            throw;
        }
    }

    public static async Task SaveRefreshRateAsync(BladeProfileSettings target, int hertz,
        Func<CancellationToken, Task<bool>> save, CancellationToken cancellationToken, string failureMessage)
    {
        var previous = target.RefreshRateHertz;
        target.RefreshRateHertz = hertz;
        try
        {
            if (!await save(cancellationToken)) throw new InvalidOperationException(failureMessage);
        }
        catch
        {
            if (target.RefreshRateHertz == hertz) target.RefreshRateHertz = previous;
            throw;
        }
    }
}
