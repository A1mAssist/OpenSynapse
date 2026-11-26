namespace OpenSynapse.Core.Lighting;

public static partial class StudioEffectRenderer
{
    /// <summary>Samples cached LED coordinates, then blurs before the layer mask is applied.</summary>
    internal static StudioRgb[] RenderMappedAmbient(
        StudioEffectParams parameters, int rows, int columns,
        IReadOnlyList<(double U, double V)> coordinates, StudioEffectInput input)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(coordinates);
        if (rows <= 0 || columns <= 0 || (long)rows * columns > StudioCompositor.MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(rows));
        if (coordinates.Count != rows * columns)
            throw new ArgumentException("The LED coordinates do not match the matrix.", nameof(coordinates));
        var uv = input.ScreenSamplerUv;
        if (uv is null && input.ScreenSampler is { } grid)
        {
            uv = (u, v) => grid(
                Math.Clamp((int)Math.Floor(u * StudioWorldCanvas.GridSize), 0, StudioWorldCanvas.GridSize - 1),
                Math.Clamp((int)Math.Floor(v * StudioWorldCanvas.GridSize), 0, StudioWorldCanvas.GridSize - 1));
        }
        var frame = new StudioRgb[rows * columns];
        if (uv is null) return frame;
        var rect = StudioScreenRect.Resolve(parameters.Screen, parameters.ScreenRect);
        if (input.ScreenCaptureRect?.Clamp() is { } capture)
            rect = StudioScreenRect.MapIntoCapture(rect, capture);
        var dominant = parameters.ScreenMode is StudioScreenMode.Dominant or StudioScreenMode.Monet
            ? ScreenDominant(uv, rect) : default;
        for (var index = 0; index < frame.Length; index++)
        {
            var (u, v) = coordinates[index];
            var screenU = rect.X + u * rect.Width;
            var screenV = rect.Y + v * rect.Height;
            frame[index] = parameters.ScreenMode switch
            {
                StudioScreenMode.Band => Vivid(ScreenBand(uv, rect, screenU), 1.25),
                StudioScreenMode.Dominant => Vivid(dominant, 1.25),
                StudioScreenMode.Monet => Monet(dominant, u, v),
                _ => uv(screenU, screenV),
            };
        }
        return Blur(frame, rows, columns, Math.Clamp(parameters.Blur, 0, 20));
    }

    private static StudioRgb[] RenderAmbient(StudioEffectParams parameters, int rows, int columns, StudioEffectInput input)
    {
        var frame = new StudioRgb[rows * columns];
        if (input.ScreenSamplerUv is not { } uv)
        {
            if (input.ScreenSampler is { } grid)
            {
                // The integer sampler already maps the full screen to the matrix.
                for (var index = 0; index < frame.Length; index++)
                    frame[index] = grid(index % columns, index / columns);
                return Blur(frame, rows, columns, Math.Clamp(parameters.Blur, 0, 20));
            }

            return frame;
        }

        var rect = StudioScreenRect.Resolve(parameters.Screen, parameters.ScreenRect);
        if (input.ScreenCaptureRect?.Clamp() is { } capture)
        {
            rect = StudioScreenRect.MapIntoCapture(rect, capture);
        }

        // The sampler is normalized to the captured crop, so the region is used as-is.
        switch (parameters.ScreenMode)
        {
            case StudioScreenMode.Band:
                for (var column = 0; column < columns; column++)
                {
                    var u = rect.X + (column + 0.5) / columns * rect.Width;
                    var color = Vivid(ScreenBand(uv, rect, u), 1.25);
                    for (var row = 0; row < rows; row++)
                        frame[row * columns + column] = color;
                }

                break;
            case StudioScreenMode.Dominant:
                Array.Fill(frame, Vivid(ScreenDominant(uv, rect), 1.25));
                break;
            case StudioScreenMode.Monet:
            {
                var main = ScreenDominant(uv, rect);
                for (var index = 0; index < frame.Length; index++)
                {
                    var u = (index % columns + 0.5) / columns;
                    var v = (index / columns + 0.5) / rows;
                    frame[index] = Monet(main, u, v);
                }

                break;
            }
            default:
                for (var index = 0; index < frame.Length; index++)
                {
                    var u = rect.X + (index % columns + 0.5) / columns * rect.Width;
                    var v = rect.Y + (index / columns + 0.5) / rows * rect.Height;
                    frame[index] = uv(u, v);
                }

                break;
        }

        return Blur(frame, rows, columns, Math.Clamp(parameters.Blur, 0, 20));
    }

    /// <summary>
    /// One color for a column: a narrow vertical strip averaged over the region,
    /// weighted toward the bottom. Coordinates never leave the region.
    /// </summary>
    private static StudioRgb ScreenBand(
        Func<double, double, StudioRgb> uv,
        StudioScreenRect rect,
        double center)
    {
        const int steps = 4;
        double red = 0, green = 0, blue = 0, total = 0;
        for (var row = 0; row < steps; row++)
        {
            var fraction = (row + 0.5) / steps;
            var weight = 0.15 + 2 * fraction * fraction;
            var v = rect.Y + fraction * rect.Height;
            for (var column = 0; column < steps; column++)
            {
                var u = Math.Clamp(
                    center - 0.05 + (column + 0.5) / steps * 0.1,
                    rect.X,
                    rect.X + rect.Width);
                var sample = uv(u, v);
                red += sample.R * weight;
                green += sample.G * weight;
                blue += sample.B * weight;
                total += weight;
            }
        }

        return new StudioRgb(ToByte(red / total / 255), ToByte(green / total / 255), ToByte(blue / total / 255));
    }

    /// <summary>
    /// Picks the dominant hue of the region, weighted by saturation and brightness,
    /// so a colorful object wins over a large neutral background.
    /// </summary>
    private static StudioRgb ScreenDominant(Func<double, double, StudioRgb> uv, StudioScreenRect rect)
    {
        const int columns = 32;
        const int rows = 18;
        var binRed = new double[12];
        var binGreen = new double[12];
        var binBlue = new double[12];
        var binWeight = new double[12];
        double sumRed = 0, sumGreen = 0, sumBlue = 0;
        var count = 0;
        for (var row = 0; row < rows; row++)
        {
            var v = rect.Y + (row + 0.5) / rows * rect.Height;
            for (var column = 0; column < columns; column++)
            {
                var u = rect.X + (column + 0.5) / columns * rect.Width;
                var sample = uv(u, v);
                double red = sample.R / 255.0, green = sample.G / 255.0, blue = sample.B / 255.0;
                var max = Math.Max(red, Math.Max(green, blue));
                var min = Math.Min(red, Math.Min(green, blue));
                var delta = max - min;
                sumRed += red;
                sumGreen += green;
                sumBlue += blue;
                count++;
                var saturation = max > 0 ? delta / max : 0;
                var weight = saturation * saturation * max;
                if (weight <= 0.01 || delta <= 0) continue;
                var hue = max == red
                    ? (green - blue) / delta
                    : max == green
                        ? 2 + (blue - red) / delta
                        : 4 + (red - green) / delta;
                if (hue < 0) hue += 6;
                var bin = Math.Min(11, (int)(hue / 6 * 12));
                binRed[bin] += red * weight;
                binGreen[bin] += green * weight;
                binBlue[bin] += blue * weight;
                binWeight[bin] += weight;
            }
        }

        if (count == 0) return StudioRgb.Black;
        var average = new StudioRgb(
            ToByte(sumRed / count),
            ToByte(sumGreen / count),
            ToByte(sumBlue / count));
        var totalWeight = binWeight.Sum();
        if (totalWeight <= count * 0.02) return average;

        var best = 0;
        var bestWeight = double.MinValue;
        for (var index = 0; index < 12; index++)
        {
            // Neighbouring bins smooth a hue that straddles a bin boundary.
            var weight = binWeight[index] +
                0.5 * (binWeight[(index + 1) % 12] + binWeight[(index + 11) % 12]);
            if (weight <= bestWeight) continue;
            bestWeight = weight;
            best = index;
        }

        double red2 = 0, green2 = 0, blue2 = 0, weight2 = 0;
        foreach (var index in new[] { best, (best + 1) % 12, (best + 11) % 12 })
        {
            red2 += binRed[index];
            green2 += binGreen[index];
            blue2 += binBlue[index];
            weight2 += binWeight[index];
        }

        return weight2 <= 0
            ? average
            : new StudioRgb(ToByte(red2 / weight2), ToByte(green2 / weight2), ToByte(blue2 / weight2));
    }

    /// <summary>Pushes a color away from its own luminance, Chroma's saturation boost.</summary>
    private static StudioRgb Vivid(StudioRgb color, double amount)
    {
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return new StudioRgb(
            ToByte(luminance + (color.R / 255.0 - luminance) * amount),
            ToByte(luminance + (color.G / 255.0 - luminance) * amount),
            ToByte(luminance + (color.B / 255.0 - luminance) * amount));
    }

    /// <summary>Muted painterly shift of the dominant color across the matrix.</summary>
    private static StudioRgb Monet(StudioRgb color, double u, double v)
    {
        double red = color.R / 255.0, green = color.G / 255.0, blue = color.B / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        if (max <= 0.015) return StudioRgb.Black;
        var saturation = delta / max;
        if (saturation <= 0.08) return new StudioRgb(ToByte(max * 0.55), ToByte(max * 0.55), ToByte(max * 0.55));

        var hue = (max == red
            ? (green - blue) / delta
            : max == green
                ? 2 + (blue - red) / delta
                : 4 + (red - green) / delta) / 6.0;
        hue = (hue + 1.0) % 1.0 + (u - 0.5) * 0.12 + (v - 0.5) * 0.035;
        return StudioRgb.FromHsv(
            hue,
            Math.Clamp(saturation * 0.65, 0.28, 0.62),
            max * (0.64 + 0.2 * (1.0 - v)));
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
