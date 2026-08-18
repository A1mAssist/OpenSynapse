namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Decodes Product 710 report 0x04 after the caller has verified that the
/// source is the internal MI_01&amp;Col04 HID collection.
/// </summary>
public sealed class BladeRazerKeyReportDecoder
{
    public const byte ReportId = 0x04;

    private HashSet<byte> _pressed = [];

    public IReadOnlyList<BladeMappingInputEvent> Process(ReadOnlySpan<byte> report)
    {
        if (report.Length < 2 || report[0] != ReportId)
        {
            return [];
        }

        var current = new HashSet<byte>();
        foreach (var key in report[1..])
        {
            if (key != 0)
            {
                current.Add(key);
            }
        }

        var events = new List<BladeMappingInputEvent>();
        foreach (var key in _pressed.Except(current).Order())
        {
            events.Add(new(BladeMappingInputKind.RazerKey, key, false));
        }
        foreach (var key in current.Except(_pressed).Order())
        {
            events.Add(new(BladeMappingInputKind.RazerKey, key, true));
        }

        _pressed = current;
        return events;
    }

    public IReadOnlyList<BladeMappingInputEvent> Reset()
    {
        var events = _pressed
            .Order()
            .Select(key => new BladeMappingInputEvent(
                BladeMappingInputKind.RazerKey,
                key,
                false))
            .ToArray();
        _pressed.Clear();
        return events;
    }
}
