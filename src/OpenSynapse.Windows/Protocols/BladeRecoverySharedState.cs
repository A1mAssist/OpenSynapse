using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;

namespace OpenSynapse.Windows.Protocols;

public readonly record struct BladeRecoverySyntheticKey(
    ushort ScanCode, bool Extended, bool IsMouseButton = false, ushort VirtualKey = 0);

public sealed class BladeRecoverySharedState : IDisposable
{
    public const int CurrentVersion = 2;
    public const int MaximumKeys = 64;
    private const uint Magic = 0x4B52534F;
    private const int HeaderLength = 24;
    private const int EntryLength = 4;
    private const int TotalLength = HeaderLength + MaximumKeys * EntryLength;
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private readonly object _sync = new();

    private BladeRecoverySharedState(MemoryMappedFile map)
    {
        _map = map;
        _view = map.CreateViewAccessor(0, TotalLength, MemoryMappedFileAccess.ReadWrite);
    }

    public static BladeRecoverySharedState CreateOwner(string name)
    {
        BladeRecoveryProtocol.ValidateObjectName(name, "RecoveryKeys");
        var state = new BladeRecoverySharedState(MemoryMappedFile.CreateNew(name, TotalLength, MemoryMappedFileAccess.ReadWrite));
        state.Write([]);
        return state;
    }

    public static BladeRecoverySharedState OpenExisting(string name)
    {
        BladeRecoveryProtocol.ValidateObjectName(name, "RecoveryKeys");
        return new(MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite));
    }

    public void Write(IReadOnlyCollection<BladeRecoverySyntheticKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var normalized = keys.Distinct().OrderBy(static key => key.IsMouseButton)
            .ThenBy(static key => key.ScanCode).ThenBy(static key => key.Extended)
            .ThenBy(static key => key.VirtualKey).ToArray();
        if (normalized.Length > MaximumKeys || normalized.Any(static key => !IsValidKey(key)))
            throw new ArgumentException("Synthetic key state is invalid.", nameof(keys));

        lock (_sync)
        {
            var sequence = _view.ReadUInt32(12);
            if ((sequence & 1) != 0) sequence++;
            var odd = unchecked(sequence + 1) | 1u;
            var even = unchecked(odd + 1);
            _view.Write(12, odd);
            Thread.MemoryBarrier();

            var payload = new byte[TotalLength];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, Magic);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), CurrentVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), MaximumKeys);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), odd);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), (uint)normalized.Length);
            for (var index = 0; index < normalized.Length; index++)
            {
                var offset = HeaderLength + index * EntryLength;
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset),
                    normalized[index].VirtualKey != 0 ? normalized[index].VirtualKey : normalized[index].ScanCode);
                payload[offset + 2] = normalized[index].Extended ? (byte)1 : (byte)0;
                payload[offset + 3] = normalized[index].IsMouseButton ? (byte)1 :
                    normalized[index].VirtualKey != 0 ? (byte)2 : (byte)0;
            }
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), CalculateChecksum(payload));
            _view.WriteArray(0, payload, 0, payload.Length);
            Thread.MemoryBarrier();
            _view.Write(12, even);
            _view.Flush();
        }
    }

    public bool TryRead(out IReadOnlyList<BladeRecoverySyntheticKey> keys)
    {
        lock (_sync)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var first = _view.ReadUInt32(12);
                if ((first & 1) != 0) { Thread.Yield(); continue; }
                var payload = new byte[TotalLength];
                _view.ReadArray(0, payload, 0, payload.Length);
                Thread.MemoryBarrier();
                var second = _view.ReadUInt32(12);
                if (first != second || (second & 1) != 0) continue;
                var version = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4));
                if (BinaryPrimitives.ReadUInt32LittleEndian(payload) != Magic ||
                    version is not (1 or CurrentVersion) ||
                    BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(8)) != MaximumKeys)
                    break;
                var count = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(16));
                var checksum = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(20));
                if (count > MaximumKeys || checksum != CalculateChecksum(payload)) break;
                if (payload.AsSpan(HeaderLength + checked((int)count) * EntryLength).ContainsAnyExcept((byte)0))
                    break;
                var result = new List<BladeRecoverySyntheticKey>((int)count);
                var unique = new HashSet<BladeRecoverySyntheticKey>();
                for (var index = 0; index < count; index++)
                {
                    var offset = HeaderLength + index * EntryLength;
                    var scanCode = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset));
                    var flags = payload[offset + 2];
                    var kind = payload[offset + 3];
                    if (scanCode == 0 || (flags & ~1) != 0 || kind > 2 ||
                        version == 1 && kind != 0)
                    {
                        keys = [];
                        return false;
                    }
                    var key = kind == 2
                        ? new BladeRecoverySyntheticKey(0, (flags & 1) != 0, VirtualKey: scanCode)
                        : new BladeRecoverySyntheticKey(scanCode, (flags & 1) != 0, kind == 1);
                    if (!IsValidKey(key) || !unique.Add(key)) { keys = []; return false; }
                    result.Add(key);
                }
                keys = result;
                return true;
            }
        }
        keys = [];
        return false;
    }

    public void Dispose()
    {
        _view.Dispose();
        _map.Dispose();
    }

    private static bool IsValidKey(BladeRecoverySyntheticKey key) => key.IsMouseButton
        ? key.ScanCode is >= 1 and <= 5 && !key.Extended && key.VirtualKey == 0
        : key.VirtualKey != 0
            ? key.ScanCode == 0 && key.VirtualKey <= 0xFF
            : key.ScanCode != 0;

    private static uint CalculateChecksum(byte[] payload)
    {
        var copy = payload.ToArray();
        copy.AsSpan(12, 4).Clear();
        copy.AsSpan(20, 4).Clear();
        var hash = 2166136261u;
        foreach (var value in copy) hash = unchecked((hash ^ value) * 16777619u);
        return hash;
    }
}
