namespace OpenSynapse.Windows.Devices;

public enum RazerSurfaceCalibrationEventKind
{
    Started, Ended, Halted, Reset, NormalSpeed, TooFast,
    LeftButtonDown, Escape, Sample, Disconnected, LeftButtonDownAny, Confirm,
}

public readonly record struct RazerSurfaceCalibrationInput(
    string InstanceId, Guid SessionId, RazerSurfaceCalibrationEventKind Kind,
    byte Z = 0, byte Q = 0);

public static class RazerSurfaceCalibrationEventDecoder
{
    public static bool TryDecode(ReadOnlySpan<byte> report, bool philips,
        out RazerSurfaceCalibrationEventKind kind, out byte z, out byte q)
    {
        kind = default;
        z = q = 0;
        if (report.Length < 3) return false;

        // Protocol25Parser: report ID, notification ID, value. Cancelled=6 is a JS event only.
        if (report[0] == 0x05 && report[1] == 0x0A && report[2] <= 5)
        {
            kind = (RazerSurfaceCalibrationEventKind)report[2];
            return true;
        }

        if (philips && report[0] == 0x5A)
        {
            kind = RazerSurfaceCalibrationEventKind.Sample;
            z = report[1];
            q = report[2];
            return true;
        }

        if (philips && report[0] == 0x4D && report[1] == 0 && report[2] == 1)
        {
            kind = RazerSurfaceCalibrationEventKind.LeftButtonDown;
            return true;
        }
        return false;
    }
}
