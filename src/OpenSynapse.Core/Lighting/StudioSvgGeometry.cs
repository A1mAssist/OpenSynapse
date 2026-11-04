using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenSynapse.Core.Lighting;

/// <summary>SVG 2D affine transform: x' = A*x + C*y + E, y' = B*x + D*y + F.</summary>
internal readonly record struct SvgMatrix(double A, double B, double C, double D, double E, double F)
{
    public static readonly SvgMatrix Identity = new(1, 0, 0, 1, 0, 0);

    private static readonly Regex Function = new(
        @"\s*(?<name>[A-Za-z]+)\s*\((?<args>[^)]*)\)\s*,?",
        RegexOptions.CultureInvariant);

    public bool IsIdentity => A == 1 && B == 0 && C == 0 && D == 1 && E == 0 && F == 0;

    /// <summary>Returns parent * child, matching SVG's transform-list order.</summary>
    public SvgMatrix Multiply(SvgMatrix child) => new(
        A * child.A + C * child.B,
        B * child.A + D * child.B,
        A * child.C + C * child.D,
        B * child.C + D * child.D,
        A * child.E + C * child.F + E,
        B * child.E + D * child.F + F);

    /// <summary>Parses an SVG transform list. Any invalid list falls back to identity.</summary>
    public static SvgMatrix Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Identity;
        value = value.Trim();
        var current = Identity;
        var position = 0;
        while (position < value.Length)
        {
            var match = Function.Match(value, position);
            if (!match.Success || match.Index != position) return Identity;
            if (!TryFunction(match.Groups["name"].Value, match.Groups["args"].Value, out var next)) return Identity;
            current = current.Multiply(next);
            position = match.Index + match.Length;
        }

        return current;
    }

    private static bool TryFunction(string name, string args, out SvgMatrix matrix)
    {
        matrix = Identity;
        var numbers = SvgNumber.ReadList(args);
        if (numbers is null) return false;

        switch (name.ToLowerInvariant())
        {
            case "matrix":
                if (numbers.Length != 6) return false;
                matrix = new SvgMatrix(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]);
                return true;
            case "translate":
                if (numbers.Length is not (1 or 2)) return false;
                matrix = new SvgMatrix(1, 0, 0, 1, numbers[0], numbers.Length == 2 ? numbers[1] : 0);
                return true;
            case "scale":
                if (numbers.Length is not (1 or 2)) return false;
                matrix = new SvgMatrix(numbers[0], 0, 0, numbers.Length == 2 ? numbers[1] : numbers[0], 0, 0);
                return true;
            case "rotate":
                if (numbers.Length != 1 && numbers.Length != 3) return false;
                var radians = numbers[0] * Math.PI / 180;
                var cosine = Math.Cos(radians);
                var sine = Math.Sin(radians);
                matrix = new SvgMatrix(cosine, sine, -sine, cosine, 0, 0);
                if (numbers.Length == 3)
                {
                    matrix = new SvgMatrix(1, 0, 0, 1, numbers[1], numbers[2])
                        .Multiply(matrix)
                        .Multiply(new SvgMatrix(1, 0, 0, 1, -numbers[1], -numbers[2]));
                }

                return true;
            case "skewx":
                if (numbers.Length != 1) return false;
                matrix = new SvgMatrix(1, 0, Math.Tan(numbers[0] * Math.PI / 180), 1, 0, 0);
                return true;
            case "skewy":
                if (numbers.Length != 1) return false;
                matrix = new SvgMatrix(1, Math.Tan(numbers[0] * Math.PI / 180), 0, 1, 0, 0);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>Rewrites SVG path data into absolute commands with an affine transform baked in.</summary>
internal static class SvgPathData
{
    public static string Transform(string data, SvgMatrix matrix)
    {
        if (matrix.IsIdentity || string.IsNullOrWhiteSpace(data)) return data;
        // Vendor path data can be malformed; keep the original rather than dropping the shape.
        return TryTransform(data, matrix, out var result) ? result : data;
    }

    private static bool TryTransform(string data, SvgMatrix matrix, out string result)
    {
        result = string.Empty;
        var reader = new Reader(data);
        var builder = new StringBuilder(data.Length + 64);
        var command = '\0';
        var previous = '\0';
        double x = 0, y = 0, startX = 0, startY = 0;
        double? cubicX = null, cubicY = null, quadX = null, quadY = null;

        while (!reader.End)
        {
            if (reader.TryReadCommand(out var next))
            {
                command = next;
            }
            else if (command == '\0')
            {
                return false;
            }

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    if (!ReadPoint(reader, command, x, y, out var moveX, out var moveY)) return false;
                    x = moveX;
                    y = moveY;
                    startX = x;
                    startY = y;
                    builder.Append('M');
                    AppendPoint(builder, matrix, x, y);
                    while (reader.PeekNumber())
                    {
                        if (!ReadPoint(reader, char.IsLower(command) ? 'l' : 'L', x, y, out var lineX, out var lineY)) return false;
                        x = lineX;
                        y = lineY;
                        builder.Append('L');
                        AppendPoint(builder, matrix, x, y);
                    }

                    previous = 'M';
                    command = '\0';
                    ClearControls(ref cubicX, ref cubicY, ref quadX, ref quadY);
                    break;

                case 'L':
                    if (!ReadPoint(reader, command, x, y, out var endX, out var endY)) return false;
                    x = endX;
                    y = endY;
                    builder.Append('L');
                    AppendPoint(builder, matrix, x, y);
                    previous = 'L';
                    ClearControls(ref cubicX, ref cubicY, ref quadX, ref quadY);
                    break;

                case 'H':
                    if (!reader.TryReadNumber(out var horizontal)) return false;
                    x = char.IsLower(command) ? x + horizontal : horizontal;
                    builder.Append('L');
                    AppendPoint(builder, matrix, x, y);
                    previous = 'H';
                    ClearControls(ref cubicX, ref cubicY, ref quadX, ref quadY);
                    break;

                case 'V':
                    if (!reader.TryReadNumber(out var vertical)) return false;
                    y = char.IsLower(command) ? y + vertical : vertical;
                    builder.Append('L');
                    AppendPoint(builder, matrix, x, y);
                    previous = 'V';
                    ClearControls(ref cubicX, ref cubicY, ref quadX, ref quadY);
                    break;

                case 'C':
                    if (!reader.TryReadNumber(out var c1x) || !reader.TryReadNumber(out var c1y) ||
                        !reader.TryReadNumber(out var c2x) || !reader.TryReadNumber(out var c2y) ||
                        !reader.TryReadNumber(out var cx) || !reader.TryReadNumber(out var cy)) return false;
                    if (char.IsLower(command))
                    {
                        c1x += x;
                        c1y += y;
                        c2x += x;
                        c2y += y;
                        cx += x;
                        cy += y;
                    }

                    builder.Append('C');
                    AppendPoint(builder, matrix, c1x, c1y);
                    builder.Append(' ');
                    AppendPoint(builder, matrix, c2x, c2y);
                    builder.Append(' ');
                    AppendPoint(builder, matrix, cx, cy);
                    x = cx;
                    y = cy;
                    cubicX = c2x;
                    cubicY = c2y;
                    quadX = null;
                    quadY = null;
                    previous = 'C';
                    break;

                case 'S':
                    if (!reader.TryReadNumber(out var s2x) || !reader.TryReadNumber(out var s2y) ||
                        !reader.TryReadNumber(out var sx) || !reader.TryReadNumber(out var sy)) return false;
                    if (char.IsLower(command))
                    {
                        s2x += x;
                        s2y += y;
                        sx += x;
                        sy += y;
                    }

                    var sc1x = previous is 'C' or 'S' && cubicX is { } lastCubicX ? 2 * x - lastCubicX : x;
                    var sc1y = previous is 'C' or 'S' && cubicY is { } lastCubicY ? 2 * y - lastCubicY : y;
                    builder.Append('C');
                    AppendPoint(builder, matrix, sc1x, sc1y);
                    builder.Append(' ');
                    AppendPoint(builder, matrix, s2x, s2y);
                    builder.Append(' ');
                    AppendPoint(builder, matrix, sx, sy);
                    x = sx;
                    y = sy;
                    cubicX = s2x;
                    cubicY = s2y;
                    quadX = null;
                    quadY = null;
                    previous = 'S';
                    break;

                case 'Q':
                    if (!reader.TryReadNumber(out var q1x) || !reader.TryReadNumber(out var q1y) ||
                        !reader.TryReadNumber(out var qx) || !reader.TryReadNumber(out var qy)) return false;
                    if (char.IsLower(command))
                    {
                        q1x += x;
                        q1y += y;
                        qx += x;
                        qy += y;
                    }

                    builder.Append('Q');
                    AppendPoint(builder, matrix, q1x, q1y);
                    builder.Append(' ');
                    AppendPoint(builder, matrix, qx, qy);
                    x = qx;
                    y = qy;
                    quadX = q1x;
                    quadY = q1y;
                    cubicX = null;
                    cubicY = null;
                    previous = 'Q';
                    break;

                case 'T':
                    if (!reader.TryReadNumber(out var tx) || !reader.TryReadNumber(out var ty)) return false;
                    if (char.IsLower(command))
                    {
                        tx += x;
                        ty += y;
                    }

                    var tq1x = previous is 'Q' or 'T' && quadX is { } lastQuadX ? 2 * x - lastQuadX : x;
                    var tq1y = previous is 'Q' or 'T' && quadY is { } lastQuadY ? 2 * y - lastQuadY : y;
                    builder.Append('Q');
                    AppendPoint(builder, matrix, tq1x, tq1y);
                    builder.Append(' ');
                    AppendPoint(builder, matrix, tx, ty);
                    x = tx;
                    y = ty;
                    quadX = tq1x;
                    quadY = tq1y;
                    cubicX = null;
                    cubicY = null;
                    previous = 'T';
                    break;

                case 'A':
                    if (!reader.TryReadNumber(out var rx) || !reader.TryReadNumber(out var ry) ||
                        !reader.TryReadNumber(out var rotation) ||
                        !reader.TryReadFlag(out var largeArc) || !reader.TryReadFlag(out var sweep) ||
                        !reader.TryReadNumber(out var arcX) || !reader.TryReadNumber(out var arcY)) return false;
                    if (char.IsLower(command))
                    {
                        arcX += x;
                        arcY += y;
                    }

                    AppendArc(builder, matrix, x, y, rx, ry, rotation, largeArc, sweep, arcX, arcY);
                    x = arcX;
                    y = arcY;
                    previous = 'A';
                    ClearControls(ref cubicX, ref cubicY, ref quadX, ref quadY);
                    break;

                case 'Z':
                    builder.Append('Z');
                    x = startX;
                    y = startY;
                    previous = 'Z';
                    command = '\0';
                    ClearControls(ref cubicX, ref cubicY, ref quadX, ref quadY);
                    break;

                default:
                    return false;
            }
        }

        result = builder.ToString();
        return true;
    }

    private static bool ReadPoint(Reader reader, char command, double currentX, double currentY, out double x, out double y)
    {
        if (!reader.TryReadNumber(out var rawX) || !reader.TryReadNumber(out var rawY))
        {
            x = 0;
            y = 0;
            return false;
        }

        var relative = char.IsLower(command);
        x = relative ? currentX + rawX : rawX;
        y = relative ? currentY + rawY : rawY;
        return true;
    }

    private static void AppendArc(
        StringBuilder builder, SvgMatrix matrix,
        double x1, double y1, double rx, double ry, double rotation,
        bool largeArc, bool sweep, double x2, double y2)
    {
        if (x1 == x2 && y1 == y2) return;
        if (rx == 0 || ry == 0)
        {
            builder.Append('L');
            AppendPoint(builder, matrix, x2, y2);
            return;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        var phi = rotation % 360 * Math.PI / 180;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);
        var dx = (x1 - x2) / 2;
        var dy = (y1 - y2) / 2;
        var x1p = cosPhi * dx + sinPhi * dy;
        var y1p = -sinPhi * dx + cosPhi * dy;

        var scale = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (scale > 1)
        {
            var factor = Math.Sqrt(scale);
            rx *= factor;
            ry *= factor;
        }

        var denominator = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        var numerator = rx * rx * ry * ry - denominator;
        var coefficient = denominator == 0
            ? 0
            : (largeArc == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, numerator / denominator));
        var cxp = coefficient * rx * y1p / ry;
        var cyp = -coefficient * ry * x1p / rx;
        var centerX = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2;
        var centerY = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2;

        var ux = (x1p - cxp) / rx;
        var uy = (y1p - cyp) / ry;
        var vx = (-x1p - cxp) / rx;
        var vy = (-y1p - cyp) / ry;
        var startAngle = Math.Atan2(uy, ux);
        var delta = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        if (sweep && delta < 0) delta += 2 * Math.PI;

        // shortcut: cubic approximation, upgrade if arcs need exact analytic transforms.
        var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2)));
        var step = delta / segments;
        for (var index = 0; index < segments; index++)
        {
            var from = startAngle + step * index;
            var to = from + step;
            var alpha = 4d / 3 * Math.Tan((to - from) / 4);
            var cosFrom = Math.Cos(from);
            var sinFrom = Math.Sin(from);
            var cosTo = Math.Cos(to);
            var sinTo = Math.Sin(to);

            var p0x = centerX + rx * cosFrom * cosPhi - ry * sinFrom * sinPhi;
            var p0y = centerY + rx * cosFrom * sinPhi + ry * sinFrom * cosPhi;
            var p3x = centerX + rx * cosTo * cosPhi - ry * sinTo * sinPhi;
            var p3y = centerY + rx * cosTo * sinPhi + ry * sinTo * cosPhi;
            var q1x = p0x + alpha * (-rx * sinFrom * cosPhi - ry * cosFrom * sinPhi);
            var q1y = p0y + alpha * (-rx * sinFrom * sinPhi + ry * cosFrom * cosPhi);
            var q2x = p3x - alpha * (-rx * sinTo * cosPhi - ry * cosTo * sinPhi);
            var q2y = p3y - alpha * (-rx * sinTo * sinPhi + ry * cosTo * cosPhi);

            builder.Append('C');
            AppendPoint(builder, matrix, q1x, q1y);
            builder.Append(' ');
            AppendPoint(builder, matrix, q2x, q2y);
            builder.Append(' ');
            AppendPoint(builder, matrix, p3x, p3y);
        }
    }

    private static void AppendPoint(StringBuilder builder, SvgMatrix matrix, double x, double y)
    {
        builder.Append(SvgNumber.Format(matrix.A * x + matrix.C * y + matrix.E));
        builder.Append(',');
        builder.Append(SvgNumber.Format(matrix.B * x + matrix.D * y + matrix.F));
    }

    private static void ClearControls(ref double? cubicX, ref double? cubicY, ref double? quadX, ref double? quadY)
    {
        cubicX = null;
        cubicY = null;
        quadX = null;
        quadY = null;
    }

    private sealed class Reader(string text)
    {
        private readonly string _text = text;
        private int _index;

        public bool End
        {
            get
            {
                var index = Skip(_index);
                return index >= _text.Length;
            }
        }

        public bool TryReadCommand(out char command)
        {
            _index = Skip(_index);
            if (_index < _text.Length && char.IsAsciiLetter(_text[_index]))
            {
                command = _text[_index++];
                return true;
            }

            command = '\0';
            return false;
        }

        public bool TryReadNumber(out double value)
        {
            _index = Skip(_index);
            var end = SvgNumber.Scan(_text, _index);
            if (end == _index)
            {
                value = 0;
                return false;
            }

            if (!double.TryParse(_text.AsSpan(_index, end - _index), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                !double.IsFinite(value))
            {
                _index = end;
                value = 0;
                return false;
            }

            _index = end;
            return true;
        }

        public bool PeekNumber()
        {
            var index = Skip(_index);
            return index < _text.Length && SvgNumber.Scan(_text, index) != index;
        }

        public bool TryReadFlag(out bool flag)
        {
            _index = Skip(_index);
            if (_index < _text.Length && _text[_index] is '0' or '1')
            {
                flag = _text[_index++] == '1';
                return true;
            }

            flag = false;
            return false;
        }

        private int Skip(int index)
        {
            while (index < _text.Length && _text[index] is ' ' or '\t' or '\r' or '\n' or ',') index++;
            return index;
        }
    }
}

internal static class SvgNumber
{
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }

    public static double[]? ReadList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var values = new List<double>();
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == ',')) index++;
            if (index >= text.Length) break;
            var end = Scan(text, index);
            if (end == index) return null;
            if (!double.TryParse(text.AsSpan(index, end - index), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value)) return null;
            values.Add(value);
            index = end;
        }

        return values.ToArray();
    }

    public static int Scan(string text, int index)
    {
        var start = index;
        if (index < text.Length && text[index] is '+' or '-') index++;
        var digits = false;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
            digits = true;
        }

        if (index < text.Length && text[index] == '.')
        {
            index++;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
                digits = true;
            }
        }

        if (!digits) return start;
        if (index < text.Length && text[index] is 'e' or 'E')
        {
            var exponent = index + 1;
            if (exponent < text.Length && text[exponent] is '+' or '-') exponent++;
            var exponentDigits = false;
            while (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
            {
                exponent++;
                exponentDigits = true;
            }

            if (exponentDigits) index = exponent;
        }

        return index;
    }

    public static string Format(double value)
    {
        // shortcut: 3-decimal coordinates, upgrade if device geometry needs sub-millimetre precision.
        if (Math.Abs(value) < 0.0005) value = 0;
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
