using System.Text;
using System.Xml;

namespace OpenSynapse.Core.Lighting;

/// <summary>Converts SVG basic shapes into path data.</summary>
internal static class SvgShapePath
{
    public static string? Create(XmlReader reader) => reader.LocalName switch
    {
        "rect" => Rect(reader),
        "circle" => Circle(reader),
        "ellipse" => Ellipse(reader),
        "line" => Line(reader),
        "polygon" => Poly(reader, closed: true),
        "polyline" => Poly(reader, closed: false),
        _ => null,
    };

    private static string? Rect(XmlReader reader)
    {
        var x = Value(reader, "x") ?? 0;
        var y = Value(reader, "y") ?? 0;
        var width = Value(reader, "width");
        var height = Value(reader, "height");
        if (width is not > 0 || height is not > 0) return null;

        var w = width.Value;
        var h = height.Value;
        var rx = Value(reader, "rx") ?? 0;
        var ry = Value(reader, "ry") ?? 0;
        if (rx <= 0 && ry <= 0) return $"M{F(x)},{F(y)}H{F(x + w)}V{F(y + h)}H{F(x)}Z";

        if (rx <= 0) rx = ry;
        if (ry <= 0) ry = rx;
        rx = Math.Min(rx, w / 2);
        ry = Math.Min(ry, h / 2);
        return $"M{F(x + rx)},{F(y)}H{F(x + w - rx)}A{F(rx)},{F(ry)} 0 0 1 {F(x + w)},{F(y + ry)}" +
               $"V{F(y + h - ry)}A{F(rx)},{F(ry)} 0 0 1 {F(x + w - rx)},{F(y + h)}" +
               $"H{F(x + rx)}A{F(rx)},{F(ry)} 0 0 1 {F(x)},{F(y + h - ry)}" +
               $"V{F(y + ry)}A{F(rx)},{F(ry)} 0 0 1 {F(x + rx)},{F(y)}Z";
    }

    private static string? Circle(XmlReader reader)
    {
        var centerX = Value(reader, "cx") ?? 0;
        var centerY = Value(reader, "cy") ?? 0;
        var radius = Value(reader, "r");
        if (radius is not > 0) return null;
        var r = radius.Value;
        return $"M{F(centerX - r)},{F(centerY)}A{F(r)},{F(r)} 0 1 0 {F(centerX + r)},{F(centerY)}" +
               $"A{F(r)},{F(r)} 0 1 0 {F(centerX - r)},{F(centerY)}Z";
    }

    private static string? Ellipse(XmlReader reader)
    {
        var centerX = Value(reader, "cx") ?? 0;
        var centerY = Value(reader, "cy") ?? 0;
        var rx = Value(reader, "rx");
        var ry = Value(reader, "ry");
        if (rx is not > 0 || ry is not > 0) return null;
        return $"M{F(centerX - rx.Value)},{F(centerY)}A{F(rx.Value)},{F(ry.Value)} 0 1 0 {F(centerX + rx.Value)},{F(centerY)}" +
               $"A{F(rx.Value)},{F(ry.Value)} 0 1 0 {F(centerX - rx.Value)},{F(centerY)}Z";
    }

    private static string? Line(XmlReader reader)
    {
        var x1 = Value(reader, "x1") ?? 0;
        var y1 = Value(reader, "y1") ?? 0;
        var x2 = Value(reader, "x2") ?? 0;
        var y2 = Value(reader, "y2") ?? 0;
        return x1 == x2 && y1 == y2 ? null : $"M{F(x1)},{F(y1)}L{F(x2)},{F(y2)}";
    }

    private static string? Poly(XmlReader reader, bool closed)
    {
        var points = SvgNumber.ReadList(reader.GetAttribute("points"));
        if (points is null || points.Length < 4 || points.Length % 2 != 0) return null;
        var builder = new StringBuilder();
        builder.Append('M').Append(F(points[0])).Append(',').Append(F(points[1]));
        for (var index = 2; index < points.Length; index += 2)
            builder.Append('L').Append(F(points[index])).Append(',').Append(F(points[index + 1]));
        if (closed) builder.Append('Z');
        return builder.ToString();
    }

    private static double? Value(XmlReader reader, string name) =>
        SvgNumber.TryParse(reader.GetAttribute(name), out var value) ? value : null;

    private static string F(double value) => SvgNumber.Format(value);
}
