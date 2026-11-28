#if DEBUG
using System.Buffers.Binary;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App;

internal sealed class HapticScrollQaTransport : IRazerFeatureTransport
{
    public HapticScrollProfile State { get; } = new()
    {
        ActiveStage = 1, StageTypes = [129, 130, 131, 132, 133, 134], Custom = new HapticScrollCurve().Generate(1),
    };
    public List<byte[]> Requests { get; } = [];
    public int Writes { get; private set; }
    public int FailAfterWrites { get; set; }
    private bool _corruptNextReadback;
    public bool CorruptNextReadback
    {
        get => _corruptNextReadback;
        set { _corruptNextReadback = value; if (value) _readback = false; }
    }
    public bool WrongTableOffset { get; set; }
    public Action? AfterWrite { get; set; }
    private bool _readback;

    public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize, byte commandClass,
        byte commandId, ReadOnlyMemory<byte> arguments, TimeSpan deviceWait, CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

    public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
        TimeSpan deviceWait, CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frame = request.ToArray();
        Requests.Add(frame);
        if (frame[7] != 0x15) throw new InvalidOperationException("The fake accepts only Hapticore commands.");
        var data = frame.AsSpan(9, frame[6]);
        var reply = (byte[])frame.Clone();
        reply[1] = 2;
        var output = reply.AsSpan(9, frame[6]);
        var custom = State.Custom!;
        static float F(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]);
        static ushort U(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
        static void SetF(Span<byte> bytes, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes[offset..], value);
        static void SetU(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(bytes[offset..], (ushort)value);
        switch (frame[8])
        {
            case 0x80: output[1] = State.ActiveStage; break;
            case 0x87:
                output.Clear(); output[0] = data[0]; output[1] = State.ActiveStage; output[2] = (byte)State.StageTypes.Count;
                State.StageTypes.ToArray().CopyTo(output[3..]); break;
            case 0x89: custom.Metadata.CopyTo(output); output[0] = data[0]; break;
            case 0x83:
                output[2] = custom.Enabled ? (byte)1 : (byte)0; output[3] = custom.AlternateCurrent;
                SetF(output, 4, custom.ReductionAngleCw); SetF(output, 8, custom.IndexCountAngleCw);
                SetF(output, 12, custom.ReductionAngleCcw); SetF(output, 16, custom.IndexCountAngleCcw);
                SetU(output, 20, custom.Cw.Count); SetU(output, 22, custom.Ccw.Count); SetU(output, 24, custom.Velocity.Count);
                if (_readback && CorruptNextReadback) { output[8] ^= 1; CorruptNextReadback = false; }
                break;
            case 0x85: SetU(output, 0, 128); SetU(output, 2, 128); SetU(output, 4, 128); break;
            case 0x09: custom.Metadata = data[..40].ToArray(); break;
            case 0x03:
                custom.Enabled = data[2] != 0; custom.AlternateCurrent = data[3];
                custom.ReductionAngleCw = F(data, 4); custom.IndexCountAngleCw = F(data, 8);
                custom.ReductionAngleCcw = F(data, 12); custom.IndexCountAngleCcw = F(data, 16);
                custom.Cw = Enumerable.Repeat(default(HapticScrollPoint), U(data, 20)).ToList();
                custom.Ccw = Enumerable.Repeat(default(HapticScrollPoint), U(data, 22)).ToList();
                custom.Velocity = Enumerable.Repeat(default(HapticScrollPoint), U(data, 24)).ToList(); break;
            case 0x84:
            case 0x04:
                var table = data[2] switch { 1 => custom.Cw, 2 => custom.Ccw, 3 => custom.Velocity, _ => throw new InvalidDataException() };
                for (var i = 0; i < U(data, 5); i++)
                {
                    var offset = U(data, 3) + i;
                    if (frame[8] == 4) table[offset] = new(F(data, 7 + i * 8), F(data, 11 + i * 8));
                    else { SetF(output, 7 + i * 8, table[offset].X); SetF(output, 11 + i * 8, table[offset].Y); }
                }
                if (frame[8] == 0x84 && WrongTableOffset) SetU(output, 3, U(data, 3) + 1);
                break;
            case 0x00: State.ActiveStage = data[1]; break;
            case 0x07: State.ActiveStage = data[1]; State.StageTypes = data.Slice(3, data[2]).ToArray().ToList(); _readback = true; break;
            default: throw new NotSupportedException($"Unexpected fake command {frame[8]:X2}.");
        }
        if (frame[8] < 0x80)
        {
            Writes++;
            AfterWrite?.Invoke();
            if (FailAfterWrites > 0 && --FailAfterWrites == 0) throw new IOException("Injected Hapticore write failure after device mutation.");
        }
        reply[89] = RazerFeatureReport.CalculateCrc(reply);
        return Task.FromResult(reply);
    }
}
#endif
