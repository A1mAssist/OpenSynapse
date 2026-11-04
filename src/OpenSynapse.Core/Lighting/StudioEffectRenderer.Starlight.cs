namespace OpenSynapse.Core.Lighting;

public static partial class StudioEffectRenderer
{
    private const int StarlightFramesPerSecond = 25;
    private const int StarlightRegen = 10;
    private const int StarlightCycles = 1;

    [ThreadStatic]
    private static Dictionary<ulong, StarlightRunner>? _starlightRunners;

    /// <summary>
    /// Native CStarlightEffect (RzLightingEngineApi 4.0.55.0, RVA 0x60790/0x60950):
    /// stars spawn on a cycle boundary, walk one gradient entry per 25 fps tick
    /// for a random lifetime, then die after one palette traversal.
    /// </summary>
    private static StudioRgb[] RenderStarlight(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time,
        uint? seed)
    {
        var stops = StarlightStops(parameters);
        var resolvedSeed = seed ?? StarlightSeed(parameters, stops);
        var fingerprint = StarlightFingerprint(resolvedSeed, rows, columns, stops);
        var ticks = (long)Math.Floor(Math.Max(0, time) * StarlightFramesPerSecond + 1e-6);
        // A bogus time value must not spin the per-tick simulation for minutes.
        ticks = Math.Min(ticks, 24L * 60 * 60 * StarlightFramesPerSecond);
        var runners = _starlightRunners ??= new Dictionary<ulong, StarlightRunner>();
        if (!runners.TryGetValue(fingerprint, out var runner) || runner.Tick > ticks)
        {
            if (runners.Count >= 8) runners.Clear();
            runner = new StarlightRunner(resolvedSeed, rows, columns, parameters, stops);
            runners[fingerprint] = runner;
        }

        return runner.AdvanceTo(ticks);
    }

    private static StudioColorStop[] StarlightStops(StudioEffectParams parameters)
    {
        var source = parameters.ColorStops.Count >= 2
            ? parameters.ColorStops.OrderBy(stop => stop.Stop).ToArray()
            : new[] { new StudioColorStop(0, parameters.Primary), new StudioColorStop(100, parameters.Secondary) };
        if (source[0].Stop != 0)
        {
            source = new[] { new StudioColorStop(0, source[0].Color) }.Concat(source).ToArray();
        }

        if (source[^1].Stop != 100)
        {
            source = source.Append(new StudioColorStop(100, source[^1].Color)).ToArray();
        }

        return source;
    }

    /// <summary>
    /// Native seeds Starlight from floor(unix / 1800000) * 1800000 + 31 * hash.
    /// The preview keeps the hash and drops the 20.8-day wall-clock bucket so a
    /// project renders the same stars on every play; pass
    /// <see cref="StudioEffectInput.StarlightSeed"/> to replay a specific native run.
    /// </summary>
    private static uint StarlightSeed(StudioEffectParams parameters, IReadOnlyList<StudioColorStop> stops)
    {
        unchecked
        {
            var maxStar = (int)(Math.Clamp(parameters.Density, 0, 10) * 10);
            uint hash = (uint)maxStar;
            hash += (uint)Math.Max(0, parameters.DurationMs);
            hash += StarlightRegen + StarlightCycles;
            hash += parameters.RandomColor ? 1u : 0u;
            hash += (uint)Math.Max(0, parameters.PauseMs);
            foreach (var stop in stops)
            {
                hash += (uint)((stop.Color.R << 16) | (stop.Color.G << 8) | stop.Color.B);
            }

            return hash * 31u;
        }
    }

    private static ulong StarlightFingerprint(
        uint seed,
        int rows,
        int columns,
        IReadOnlyList<StudioColorStop> stops)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            void Mix(ulong value) => hash = (hash ^ value) * 1099511628211UL;

            Mix(seed);
            Mix((uint)rows);
            Mix((uint)columns);
            foreach (var stop in stops)
            {
                Mix((uint)stop.Stop);
                Mix((uint)((stop.Color.R << 16) | (stop.Color.G << 8) | stop.Color.B));
            }

            return hash;
        }
    }

    /// <summary>
    /// Native 0x692e0 per-star palette: stop frames are truncated percentages,
    /// each segment fills n entries with float accumulation over n-1 steps, and
    /// the segment endpoint is written both at q1-1 and at q1. The 100 stop
    /// therefore lands on the last array entry.
    /// </summary>
    private static StudioRgb[] StarlightPalette(
        IReadOnlyList<StudioColorStop> stops,
        int length,
        int brightness)
    {
        var table = new StudioRgb[length];
        var colors = new StudioRgb[stops.Count];
        for (var index = 0; index < stops.Count; index++)
        {
            colors[index] = ScaleChannels(stops[index].Color, brightness);
        }

        for (var index = 0; index < stops.Count - 1 && stops[index].Stop != 100; index++)
        {
            var start = length * stops[index].Stop / 100;
            var end = length * stops[index + 1].Stop / 100;
            if (start >= length) break;

            table[start] = colors[index];
            var span = end - start;
            if (span >= 2)
            {
                var first = colors[index];
                var last = colors[index + 1];
                var red = (float)first.R;
                var green = (float)first.G;
                var blue = (float)first.B;
                var redStep = (last.R - first.R) / (float)(span - 1);
                var greenStep = (last.G - first.G) / (float)(span - 1);
                var blueStep = (last.B - first.B) / (float)(span - 1);
                for (var position = start + 1; position < end - 1; position++)
                {
                    red += redStep;
                    green += greenStep;
                    blue += blueStep;
                    table[position] = new StudioRgb(
                        (byte)Math.Clamp((int)red, 0, 255),
                        (byte)Math.Clamp((int)green, 0, 255),
                        (byte)Math.Clamp((int)blue, 0, 255));
                }

                if (end - 1 < length)
                {
                    table[end - 1] = last;
                }
            }

            if (end < length)
            {
                table[end] = colors[index + 1];
            }
        }

        return table;
    }

    private static StudioRgb ScaleChannels(StudioRgb color, int brightness) =>
        new(
            (byte)((float)color.R * brightness / 100f),
            (byte)((float)color.G * brightness / 100f),
            (byte)((float)color.B * brightness / 100f));

    private sealed class StarlightRunner
    {
        private readonly StudioColorStop[] _stops;
        private readonly Star?[] _stars;
        private readonly int _maxStars;
        private readonly int _paletteRange;
        private readonly int _cycleFrames;
        private readonly bool _randomColor;
        private uint _state;
        private int _cycleCursor;
        private StudioRgb[] _frame;

        public StarlightRunner(
            uint seed,
            int rows,
            int columns,
            StudioEffectParams parameters,
            StudioColorStop[] stops)
        {
            _stops = stops;
            _randomColor = parameters.RandomColor;
            _stars = new Star?[rows * columns];
            var maxStar = (int)(Math.Clamp(parameters.Density, 0, 10) * 10);
            _maxStars = Math.Max(1, (int)(rows * columns * maxStar / 100f));
            var frameMs = 1000 / StarlightFramesPerSecond;
            var duration = parameters.DurationMs > 0 ? parameters.DurationMs : 14_000;
            _paletteRange = Math.Max(stops.Length, duration / frameMs);
            _cycleFrames = Math.Max(1, Math.Max(_paletteRange >> 3, StarlightRegen / frameMs));
            _state = seed;
            _frame = new StudioRgb[_stars.Length];
            Fingerprint = StarlightFingerprint(seed, rows, columns, stops);

            // Native tick 0 spawns before the first frame that reaches the
            // caller. Prime that tick silently so Render(t=0) matches the
            // first visible oracle frame (80 ms in the engine capture).
            Spawn();
            Reap();
            _cycleCursor = (_cycleCursor + 1) % _cycleFrames;
        }

        public ulong Fingerprint { get; }

        public long Tick { get; private set; } = -1;

        public StudioRgb[] AdvanceTo(long tick)
        {
            if (Tick == tick) return _frame;
            while (Tick < tick - 1)
            {
                Step(render: false);
                Tick++;
            }

            _frame = Step(render: true)!;
            Tick++;
            return _frame;
        }

        private StudioRgb[]? Step(bool render)
        {
            var frame = render ? new StudioRgb[_stars.Length] : null;
            for (var index = 0; index < _stars.Length; index++)
            {
                var star = _stars[index];
                if (star is null || !star.Active)
                {
                    continue;
                }

                if (star.Remaining == 0)
                {
                    star.Active = false;
                    continue;
                }

                if (frame is not null)
                {
                    frame[index] = star.Palette[star.Cursor];
                }

                star.Cursor++;
                if (star.Cursor == star.Length)
                {
                    star.Cursor = 0;
                    star.Remaining--;
                }
            }

            // Native 0x60790 advances/render existing stars first, then runs
            // the cycle-boundary spawn/reap. New stars become visible on the
            // following tick.
            if (_cycleCursor == 0)
            {
                Spawn();
                Reap();
            }

            _cycleCursor = (_cycleCursor + 1) % _cycleFrames;
            return frame;
        }

        private void Spawn()
        {
            var count = 0;
            foreach (var star in _stars)
            {
                if (star is not null) count++;
            }

            if (count >= _maxStars) return;
            var requested = Draw(_maxStars - count + 1);
            if (requested == 0) return;

            for (var attempt = 0; attempt < Math.Max(1, requested); attempt++)
            {
                var index = Draw(_stars.Length);
                if (_stars[index] is not null)
                {
                    // Native retries the cell once (0x60B11..0x60B97).
                    index = Draw(_stars.Length);
                    if (_stars[index] is not null)
                    {
                        // The third draw only probes another cell. Native
                        // returns from Spawn on both branches: one extra LCG
                        // step if that cell is occupied, none otherwise.
                        var third = Draw(_stars.Length);
                        if (_stars[third] is not null)
                        {
                            Draw(1);
                        }

                        return;
                    }
                }

                var length = Math.Max(3, Draw(_paletteRange) + 5);
                var brightness = 20 + Draw(80);
                _stars[index] = new Star(length)
                {
                    Palette = BuildPalette(length, brightness),
                };
            }
        }

        private StudioRgb[] BuildPalette(int length, int brightness)
        {
            if (!_randomColor)
            {
                return StarlightPalette(_stops, length, brightness);
            }

            var table = new StudioRgb[length];
            Array.Fill(table, ScaleChannels(DrawRandomColor(), brightness));
            return table;
        }

        /// <summary>
        /// Native 0x69600: three LCG values become RGB buckets through the
        /// 0x56/0xAB thresholds, grayscale triples are rejected, and the
        /// native byte struct is B,G,R.
        /// </summary>
        private StudioRgb DrawRandomColor()
        {
            while (true)
            {
                var first = RandomChannel(Draw(255));
                var second = RandomChannel(Draw(255));
                var third = RandomChannel(Draw(255));
                if (first != second || second != third)
                {
                    return new StudioRgb(third, second, first);
                }
            }
        }

        private static byte RandomChannel(int value) =>
            value >= 0xAB ? (byte)0xFF : value >= 0x56 ? (byte)0x80 : (byte)0;

        private void Reap()
        {
            for (var index = 0; index < _stars.Length; index++)
            {
                if (_stars[index] is { Active: false })
                {
                    _stars[index] = null;
                }
            }
        }

        private int Draw(int modulus)
        {
            unchecked
            {
                _state = _state * 0x343FD + 0x269EC3;
                var value = _state ^ (_state >> 16);
                return (int)(value % (uint)modulus);
            }
        }

        private sealed class Star(int length)
        {
            public int Length { get; } = length;
            public int Cursor { get; set; }
            public int Remaining { get; set; } = StarlightCycles;
            public bool Active { get; set; } = true;
            public StudioRgb[] Palette { get; init; } = new StudioRgb[length];
        }
    }
}
