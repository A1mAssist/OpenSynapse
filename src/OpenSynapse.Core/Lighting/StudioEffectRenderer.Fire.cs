namespace OpenSynapse.Core.Lighting;

public static partial class StudioEffectRenderer
{
    private const int FireWidth = 23;
    private const int FireHeight = 7;
    private const int FirePixels = FireWidth * FireHeight;
    private const int FireFramesPerSecond = 25;

    // CFireEffect 0x1f52c0: row-wise attenuation for the 7x23 heat field.
    private static readonly byte[] FireAttenuation =
    [
        128, 128, 96, 128, 128, 96, 64, 32, 64, 96, 128, 128, 96, 128, 128, 128, 96, 64, 64, 96, 128, 128, 128,
        128, 96, 64, 96, 96, 64, 32, 16, 32, 64, 96, 96, 64, 96, 128, 96, 64, 32, 32, 64, 96, 128, 128,
        96, 64, 32, 64, 64, 32, 16, 8, 16, 32, 64, 64, 32, 64, 96, 64, 32, 16, 16, 32, 64, 96, 128,
        64, 32, 16, 32, 32, 16, 8, 6, 8, 16, 32, 32, 16, 32, 64, 32, 16, 8, 8, 16, 32, 64, 96,
        32, 16, 8, 16, 16, 8, 6, 4, 6, 8, 16, 16, 8, 16, 32, 16, 8, 6, 6, 8, 16, 32, 64,
        16, 8, 6, 8, 8, 6, 4, 2, 4, 6, 8, 8, 6, 8, 16, 8, 6, 4, 4, 6, 8, 16, 32,
        8, 6, 4, 6, 6, 4, 2, 2, 2, 4, 6, 6, 4, 6, 8, 6, 4, 2, 2, 4, 6, 8, 16,
    ];

    // CFireEffect 0x1f5370: fixed palette selector for each heat-field pixel.
    private static readonly byte[] FireDetail =
    [
        224, 224, 192, 224, 224, 192, 160, 128, 160, 192, 224, 224, 192, 224, 224, 224, 192, 160, 160, 192, 224, 224, 224,
        224, 192, 160, 192, 192, 160, 128, 96, 128, 160, 192, 192, 160, 192, 224, 192, 160, 128, 128, 160, 192, 224, 224,
        192, 160, 128, 160, 160, 128, 96, 64, 96, 128, 160, 160, 128, 160, 192, 160, 128, 96, 96, 128, 160, 192, 224,
        160, 128, 96, 128, 128, 96, 64, 32, 64, 96, 128, 128, 96, 128, 160, 128, 96, 64, 64, 96, 128, 160, 192,
        128, 96, 64, 96, 96, 64, 32, 16, 32, 64, 96, 96, 64, 96, 128, 96, 64, 32, 32, 64, 96, 128, 160,
        96, 64, 32, 64, 64, 32, 16, 8, 16, 32, 64, 64, 32, 64, 96, 64, 32, 16, 16, 32, 64, 96, 128,
        64, 32, 16, 32, 32, 16, 8, 8, 8, 16, 32, 32, 16, 32, 64, 32, 16, 8, 8, 16, 32, 64, 96,
    ];

    [ThreadStatic]
    private static Dictionary<ulong, FireRunner>? _fireRunners;

    /// <summary>
    /// Native CFireEffect (RzLightingEngineApi 4.0.55.0, RVA 0x625d0/0x633b0):
    /// 25 fps seed frames, a circular 7x23 heat field and a pre-expanded frame table.
    /// </summary>
    private static StudioRgb[] RenderFire(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time,
        int seed,
        int rate)
    {
        var fingerprint = FireFingerprint(parameters, seed, rate);
        var runners = _fireRunners ??= new Dictionary<ulong, FireRunner>();
        if (!runners.TryGetValue(fingerprint, out var runner))
        {
            if (runners.Count >= 8) runners.Clear();
            runner = new FireRunner(parameters, seed, rate);
            runners[fingerprint] = runner;
        }

        return runner.Render(time, rows, columns);
    }

    private static ulong FireFingerprint(StudioEffectParams parameters, int seed, int rate)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            void Mix(ulong value) => hash = (hash ^ value) * 1099511628211UL;

            Mix((uint)seed);
            Mix((uint)rate);
            Mix((uint)parameters.Primary.R);
            Mix((uint)parameters.Primary.G);
            Mix((uint)parameters.Primary.B);
            Mix((uint)parameters.Secondary.R);
            Mix((uint)parameters.Secondary.G);
            Mix((uint)parameters.Secondary.B);
            Mix((uint)parameters.ColorStops.Count);
            foreach (var stop in parameters.ColorStops)
            {
                Mix((uint)stop.Stop);
                Mix((uint)((stop.Color.R << 16) | (stop.Color.G << 8) | stop.Color.B));
            }

            return hash;
        }
    }

    private sealed class FireRunner
    {
        private readonly int _seed;
        private readonly int _rate;
        private readonly int _step;
        private readonly int _frameCount;
        private readonly byte[] _seedFrames;
        private readonly byte[] _heat;
        private readonly StudioRgb[] _palette;

        public FireRunner(StudioEffectParams parameters, int seed, int rate)
        {
            // The native constructor maps Seed=0 to 50 and Rate<40 to 40.
            _seed = seed == 0 ? 50 : seed;
            _rate = Math.Max(40, rate);
            _step = _rate / 40 + 1;
            _frameCount = _seed * _step;
            _seedFrames = new byte[_seed * FirePixels];
            _heat = new byte[_frameCount * FirePixels];
            _palette = BuildPalette(parameters);
            BuildSeedFrames();
            ExpandFrames();
        }

        public StudioRgb[] Render(double time, int rows, int columns)
        {
            var tick = (long)Math.Floor(Math.Max(0, time) * FireFramesPerSecond + 1e-6);
            var frame = (int)(tick % _frameCount);
            var result = new StudioRgb[rows * columns];
            var small = rows <= 6 && columns <= 22;
            var rowScale = 7f / rows;
            var heatBase = frame * FirePixels;

            for (var row = 0; row < rows; row++)
            {
                var sourceRow = small
                    ? FireHeight - rows + row
                    : FireHeight - 1 - (int)((rows - 1 - row) * rowScale);
                var heatRow = heatBase + sourceRow * FireWidth;
                for (var column = 0; column < columns; column++)
                {
                    var sourceColumn = small ? column : column % FireWidth;
                    var heat = _heat[heatRow + sourceColumn];
                    var color = _palette[FireDetail[sourceRow * FireWidth + sourceColumn]];
                    result[row * columns + column] = new StudioRgb(
                        (byte)(heat * color.R / 255),
                        (byte)(heat * color.G / 255),
                        (byte)(heat * color.B / 255));
                }
            }

            return result;
        }

        private static StudioRgb[] BuildPalette(StudioEffectParams parameters)
        {
            if (parameters.ColorStops.Count >= 2) return GradientTable(parameters, 256);

            var palette = new StudioEffectParams
            {
                Primary = parameters.Primary,
                Secondary = parameters.Secondary,
            };
            palette.ColorStops.Add(new StudioColorStop(0, parameters.Primary));
            palette.ColorStops.Add(new StudioColorStop(50, parameters.Secondary));
            palette.ColorStops.Add(new StudioColorStop(100, StudioRgb.Black));
            return GradientTable(palette, 256);
        }

        private void BuildSeedFrames()
        {
            var state = 1u;
            for (var frame = 0; frame < _seed; frame++)
            {
                var row = frame * FirePixels + (FireHeight - 1) * FireWidth;
                for (var column = 0; column < FireWidth; column++)
                {
                    var value = 32 + (int)(NextRandom(ref state) % 192);
                    value -= value * FireAttenuation[(FireHeight - 1) * FireWidth + column] / 255;
                    _seedFrames[row + column] = (byte)value;
                }
            }

            for (var row = FireHeight - 1; row >= 1; row--)
            {
                var sourceRow = row * FireWidth;
                var destRow = (row - 1) * FireWidth;
                for (var frame = 0; frame < _seed; frame++)
                {
                    var destFrame = (frame + 1) % _seed;
                    for (var column = 0; column < FireWidth; column++)
                    {
                        var value = (int)_seedFrames[frame * FirePixels + sourceRow + column];
                        value -= value * FireAttenuation[destRow + column] / 255;
                        _seedFrames[destFrame * FirePixels + destRow + column] = (byte)value;
                    }
                }
            }
        }

        private void ExpandFrames()
        {
            for (var start = 0; start < _frameCount; start += _step)
            {
                var end = start + _step;
                if (end >= _frameCount) end = 0;
                var first = start / _step * FirePixels;
                var last = end / _step * FirePixels;
                var span = _step - 1;
                for (var offset = 0; offset < _step; offset++)
                {
                    var destination = (start + offset) * FirePixels;
                    if (offset == 0)
                    {
                        _seedFrames.AsSpan(first, FirePixels).CopyTo(_heat.AsSpan(destination, FirePixels));
                        continue;
                    }

                    if (offset == span)
                    {
                        _seedFrames.AsSpan(last, FirePixels).CopyTo(_heat.AsSpan(destination, FirePixels));
                        continue;
                    }

                    for (var pixel = 0; pixel < FirePixels; pixel++)
                    {
                        var a = _seedFrames[first + pixel];
                        var b = _seedFrames[last + pixel];
                        var value = (float)a;
                        var delta = (b - a) / (float)span;
                        for (var step = 0; step < offset; step++) value += delta;
                        _heat[destination + pixel] = (byte)(int)value;
                    }
                }
            }
        }

        private static uint NextRandom(ref uint state)
        {
            state = state * 0x343fdu + 0x269ec3u;
            return (state >> 16) & 0x7fffu;
        }
    }
}
