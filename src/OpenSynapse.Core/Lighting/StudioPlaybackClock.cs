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
        StudioEffectKind.Wave => 2_500,
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
public sealed class StudioPlaybackClock : IStudioPlaybackClock
{
    private readonly object _gate = new();
    private readonly double _randomPhase;
    private bool _running;
    private bool _started;
    private double _startedAt;
    private double _phase;
    private StudioEffectKind? _signatureEffect;
    private StudioPlaybackStart _signatureStart;
    private StudioPlaybackEnd _signatureEnd;

    public StudioPlaybackClock(double randomPhase = 0)
    {
        _randomPhase = Math.Clamp(randomPhase, 0, 0.9999);
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _running;
        }
    }

    public void Start(double nowSeconds)
    {
        lock (_gate) StartCore(nowSeconds);
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    public void Toggle(double nowSeconds)
    {
        lock (_gate)
        {
            if (_running) StopCore();
            else StartCore(nowSeconds);
        }
    }

    /// <summary>Restarts a one-shot effect such as reactive or ripple.</summary>
    public void Trigger(double nowSeconds)
    {
        lock (_gate) StartCore(nowSeconds);
    }

    /// <summary>
    /// Samples playback against the renderer's own cycle, so an "after N
    /// cycles" stop lines up with the visible frames instead of the duration
    /// control alone.
    /// </summary>
    public StudioPlaybackState Sample(
        double nowSeconds,
        StudioEffectParams parameters,
        int rows = 0,
        int columns = 0)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var duration = StudioEffectRenderer.CycleSeconds(parameters, rows, columns);
        var pause = Math.Max(0, parameters.PauseMs) / 1000d;
        var cycleLength = duration + pause;

        lock (_gate)
        {
            // Reusing a layer clock after a one-shot effect must not leave the
            // next effect permanently stopped. Reset only when playback
            // semantics change; color and speed edits keep the current clock.
            if (_signatureEffect != parameters.Effect ||
                _signatureStart != parameters.PlaybackStart ||
                _signatureEnd != parameters.PlaybackEnd)
            {
                var firstSample = _signatureEffect is null;
                _signatureEffect = parameters.Effect;
                _signatureStart = parameters.PlaybackStart;
                _signatureEnd = parameters.PlaybackEnd;
                if (!(firstSample && _started))
                {
                    var wasRunning = _running;
                    _started = wasRunning;
                    _startedAt = wasRunning ? nowSeconds : 0;
                }
                _phase = 0;
            }

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

            // Preview and hardware clocks can be reset independently; a trigger
            // timestamp from the other timeline must not freeze the effect.
            if (nowSeconds < _startedAt)
            {
                _startedAt = nowSeconds;
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

    private void StartCore(double nowSeconds)
    {
        _running = true;
        _started = true;
        _startedAt = nowSeconds;
        _phase = 0;
    }

    private void StopCore()
    {
        _running = false;
        // Keep Random layers stopped until an explicit Start/Trigger; otherwise
        // the next Sample would auto-start a Toggle that the user just turned off.
        _started = true;
    }
}
