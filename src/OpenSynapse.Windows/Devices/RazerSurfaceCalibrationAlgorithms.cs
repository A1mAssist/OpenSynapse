using System.Buffers.Binary;

namespace OpenSynapse.Windows.Devices;

public static partial class RazerSurfaceCalibrationAlgorithms
{
    public static byte[] PixartManual(RazerSurfaceCalibrationRawData raw, int lod, IReadOnlyList<double> multipliers)
    {
        if (raw.Parameters.Length != 9) throw new ArgumentException("Pixart sampling requires all nine raw parameters.", nameof(raw));
        if (lod < 1 || lod > multipliers.Count) throw new ArgumentOutOfRangeException(nameof(lod));
        if (lod == 1) return [raw.Parameters[0], raw.Parameters[1], 2, 0, 0, 0, 0, 16];
        // JS stores raw parm8 as Param7 and rounds halves toward positive infinity.
        return [10, 16, 131, 0, 0, 0, 0, checked((byte)Math.Max(3, Math.Floor(raw.Parameters[8] * multipliers[lod - 1] + .5)))];
    }

    public static byte[] SmartPixartManual(int distance, byte a, byte b, bool? liftToTrack = null)
    {
        if (distance is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(distance));
        var first = a - (distance - 1) * ((a - 8d) / 10);
        if (distance == 10 && first > 10) first = 10;
        var third = b + 3 * (distance - 1);
        var last = distance switch { 1 => 15, 2 => 13, <= 5 => 10, _ => 8 };
        byte[] result = [JsByte(first), (byte)(distance > 6 ? 56 : 48), JsByte(third), 8,
            JsByte(Math.Max(10, third)), (byte)(distance > 6 ? 5 : 0), (byte)(distance > 6 ? 15 : 14), (byte)last];
        if (liftToTrack == false) return [result[0], result[1], result[2], result[4], result[7]];
        if (liftToTrack == true) result[5] = (byte)(distance == 1 ? 5 : distance > 6 ? 0 : 15);
        return result;
    }

    public static (double Mean, double StandardDeviation, double VPower) PhilipsStatistics(RazerSurfaceCalibrationRawData raw)
    {
        if (raw.Z.Length == 0 || raw.Z.Length != raw.Q.Length)
            throw new InvalidDataException("Philips calibration requires matching, nonempty Z/Q samples.");
        double mean = 0, m2 = 0, positive = 0, negative = 0;
        var count = 0;
        var positiveCount = 0;
        var negativeCount = 0;
        foreach (var z in raw.Z)
        {
            count++;
            var delta = z * 32d - mean;
            mean += delta / count;
            m2 += delta * (z * 32d - mean);
        }
        foreach (var q in raw.Q)
        {
            var power = Math.Pow(2, (q & 127) / 4d);
            if ((q & 128) == 0) { positive += power; positiveCount++; }
            else { negative += power; negativeCount++; }
        }
        var vPower = raw.Q.Length > 31744 ? 10000 : Math.Max(
            positiveCount == 0 ? 0 : positive / positiveCount,
            negativeCount == 0 ? 0 : negative / negativeCount);
        return (mean, count > 1 ? Math.Sqrt(m2 / (count - 1)) : 0, vPower);
    }

    public static (byte[] Data, ushort Threshold) Philips(
        RazerSurfaceCalibrationRawData raw, int lod, RazerCalibrationPreset? preset = null)
    {
        if (raw.CalibrationData.Length != 48) throw new InvalidDataException("Philips calibration requires 48 bytes of raw sensor data.");
        if (lod is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(lod));
        var (mean, deviation, power) = PhilipsStatistics(raw);
        double threshold;
        double offset = mean;
        if (preset is not null)
        {
            if (preset.SurfaceDistance is not { } surface || preset.SqList.Length < lod)
                throw new InvalidDataException("The product's Philips preset table is incomplete.");
            offset -= 5 * surface;
            threshold = preset.SqList[lod - 1];
        }
        else
        {
            double n;
            for (n = 4.3; n >= 2.8 && !(179.65 * power * Math.Exp(-2.06 * n) > 800); n -= .1) { }
            var minimum = Math.Clamp(3 * deviation / 5, 100, 350);
            var maximum = Math.Clamp(1000 * (n - 2.3), minimum + 50, 2000);
            threshold = lod switch
            {
                1 => minimum,
                2 => minimum + 25,
                3 => minimum + 50,
                10 => maximum,
                _ => minimum + 50 + (lod - 3) * ((maximum - (minimum + 50)) / 7),
            };
        }
        var data = raw.CalibrationData.ToArray();
        foreach (var index in new[] { 3, 4, 10, 11 })
        {
            var span = data.AsSpan(index * 4, 4);
            var value = BinaryPrimitives.ReadUInt32BigEndian(span) + offset;
            // JS bitwise packing truncates and wraps to uint32, including negative heights.
            var wrapped = ((Math.Truncate(value) % 4294967296d) + 4294967296d) % 4294967296d;
            BinaryPrimitives.WriteUInt32BigEndian(span, (uint)wrapped);
        }
        return (data, checked((ushort)Math.Truncate(threshold)));
    }

    private static byte JsByte(double value) => unchecked((byte)(int)Math.Truncate(value));
}
