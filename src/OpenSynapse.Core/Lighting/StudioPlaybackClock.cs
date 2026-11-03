namespace OpenSynapse.Core.Lighting;

/// <summary>Effect timing defaults observed in the Chroma Studio bundle.</summary>
public static class StudioEffectDefaults
{
    public static int DurationMs(StudioEffectKind effect) => effect switch
    {
        StudioEffectKind.Breathing => 14_000,
        StudioEffectKind.Spectrum => 37_740,
        StudioEffectKind.Reactive => 1_500,
        StudioEffectKind.Starlight => 1_700,
        StudioEffectKind.Wave => 2_000,
        StudioEffectKind.Ripple => 1_500,
        StudioEffectKind.Wheel => 5_000,
        StudioEffectKind.Tidal => 3_000,
        _ => 1_000,
    };
}

public readonly record struct StudioPlaybackState(
    bool Running,
    bool Paused,
    double LocalSeconds,
    int Cycle);

/// <summary>
/// Per-layer playback state. Chroma starts effects randomly, on key press, or
/// on selected keys, and can stop after N cycles or toggle on press.
/// </summary>
public sealed class StudioPlaybackClock
{
    private readonly double _randomPhase;
    private bool _running;
    private bool _started;
    private double _startedAt;
    private double _phase;

    public StudioPlaybackClock(double randomPhase = 0)
    {
        _randomPhase = Math.Clamp(randomPhase, 0, 0.9999);
    }

    public bool IsRunning => _running;

    public void Start(double nowSeconds)
    {
        _running = true;
        _started = true;
        _startedAt = nowSeconds;
        _phase = 0;
    }

    public void Stop()
    {
        _running = false;
        _started = false;
    }

    public void Toggle(double nowSeconds)
    {
        if (_running) Stop();
        else Start(nowSeconds);
    }

    /// <summary>Restarts a one-shot effect such as reactive or ripple.</summary>
    public void Trigger(double nowSeconds) => Start(nowSeconds);

    public StudioPlaybackState Sample(double nowSeconds, StudioEffectParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var duration = Math.Max(1, parameters.DurationMs > 0
            ? parameters.DurationMs
            : StudioEffectDefaults.DurationMs(parameters.Effect)) / 1000d;
        var pause = Math.Max(0, parameters.PauseMs) / 1000d;
        var cycleLength = duration + pause;

        if (parameters.PlaybackStart == StudioPlaybackStart.Random && !_started)
        {
            _started = true;
            _running = true;
            _startedAt = nowSeconds;
            _phase = _randomPhase * cycleLength;
        }

        if (!_running)
        {
            return new StudioPlaybackState(false, false, 0, 0);
        }

        var elapsed = Math.Max(0, nowSeconds - _startedAt) + _phase;

        // Continuous effects keep absolute time; only a finite run wraps cycles
        // and inserts the configured pause between them.
        if (parameters.PlaybackEnd != StudioPlaybackEnd.After)
        {
            return new StudioPlaybackState(true, false, elapsed, (int)Math.Floor(elapsed / cycleLength));
        }

        var cycle = (int)Math.Floor(elapsed / cycleLength);
        var position = elapsed - cycle * cycleLength;
        var cycles = Math.Clamp(parameters.Cycles, 1, 100);
        if (cycle >= cycles)
        {
            _running = false;
            return new StudioPlaybackState(false, false, duration, cycles);
        }

        var paused = position > duration;
        return new StudioPlaybackState(true, paused, paused ? duration : position, cycle);
    }
}
