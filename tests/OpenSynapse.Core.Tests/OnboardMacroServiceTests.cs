using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OnboardMacroServiceTests
{
    [Fact]
    public async Task BackupFailureDoesNotAllocate()
    {
        var transport = new MacroTransport([0x01, 0x04]) { FailBackupSize = true };
        var service = CreateService(transport);

        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(1, KeyTap()));

        Assert.DoesNotContain(transport.Commands, command => command == 0x08);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MalformedBackupCannotAllocate(bool truncated, bool wrongSlot)
    {
        var transport = new MacroTransport([0x01, 0x04])
        {
            TruncateBackupPayload = truncated,
            WrongBackupSlot = wrongSlot,
        };
        var error = await Record.ExceptionAsync(() => CreateService(transport).WriteAsync(1, KeyTap()));
        if (truncated) Assert.IsType<InvalidOperationException>(error);
        else Assert.IsType<InvalidDataException>(error);
        Assert.DoesNotContain((byte)0x08, transport.Commands);
    }

    [Fact]
    public async Task CancellationRestoresWithIndependentTokenAndReadsBack()
    {
        var original = new byte[] { 0x01, 0x04, 0x02, 0x04 };
        using var cancellation = new CancellationTokenSource();
        var transport = new MacroTransport(original)
        {
            CancelOnInitialWrite = cancellation,
        };
        var service = CreateService(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.WriteAsync(1, KeyTap(0x30), cancellation.Token));

        Assert.Equal(original, transport.Stored);
        Assert.True(transport.RestoreReadBacks > 0);
    }

    [Fact]
    public async Task LaterChunkFailureRestoresAllOriginalBytes()
    {
        var original = Enumerable.Repeat((byte)0x11, 90).ToArray();
        var transport = new MacroTransport(original) { FailInitialChunk = 2 };
        var service = CreateService(transport);
        var items = Enumerable.Range(0, 40)
            .Select(_ => new OnboardMacroItem(OnboardMacroItemKind.Delay) { DelayMilliseconds = 1 })
            .ToArray();

        await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(1, items));

        Assert.Equal(original, transport.Stored);
        Assert.True(transport.RestoreReadBacks > 0);
    }

    [Fact]
    public async Task RestoreFailureIsReportedExplicitly()
    {
        var transport = new MacroTransport([0x01, 0x04])
        {
            FailInitialChunk = 1,
            FailRestoreAllocation = true,
        };
        var service = CreateService(transport);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.WriteAsync(1, KeyTap(0x30)));

        Assert.Contains("could not be restored", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<AggregateException>(exception.InnerException);
    }

    [Fact]
    public async Task RestoreReadbackMismatchIsReportedExplicitly()
    {
        var transport = new MacroTransport([0x01, 0x04])
        {
            FailInitialChunk = 1,
            CorruptRestoreReadback = true,
        };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(transport).WriteAsync(1, KeyTap(0x30)));
        Assert.Contains("could not be restored", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<AggregateException>(exception.InnerException);
        Assert.True(transport.RestoreReadBacks > 0);
    }

    [Fact]
    public async Task ServiceInstancesSerializeOnePhysicalDeviceTransaction()
    {
        var transport = new MacroTransport([0x01, 0x04]) { PauseFirstAllocation = true };
        var first = CreateService(transport);
        var second = CreateService(transport);

        var firstWrite = first.WriteAsync(1, KeyTap(0x30));
        await transport.FirstAllocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondWrite = second.WriteAsync(1, KeyTap(0x2E));
        try
        {
            await Task.Delay(50);
            Assert.Equal(1, transport.CountQueries);
        }
        finally
        {
            transport.ContinueFirstAllocation.TrySetResult();
        }
        await Task.WhenAll(firstWrite, secondWrite);
        Assert.Equal(2, transport.CountQueries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(255)]
    public async Task MacroIdsUseIndependentCountAndThirtyTwoItemPages(int count)
    {
        var expected = Enumerable.Range(1, count).Select(x => (byte)x).ToArray();
        var transport = new MacroTransport([0x01, 0x04], expected);
        var ids = await CreateService(transport).ReadIdsAsync();

        Assert.Equal(expected, ids);
        Assert.Equal(expected.Chunk(32).Select(page => (byte)(4 + (2 * page.Length))),
            transport.IdPageDeclaredSizes);
    }

    private static RazerOnboardMacroService CreateService(MacroTransport transport) =>
        new(transport, "device-path", TimeSpan.Zero);

    private static OnboardMacroItem[] KeyTap(int scanCode = 0x1E) =>
    [
        new(OnboardMacroItemKind.Key) { Scancode = scanCode },
        new(OnboardMacroItemKind.Key) { Scancode = scanCode, Released = true },
    ];

    private sealed class MacroTransport : IRazerFeatureTransport
    {
        private readonly byte[] _ids;
        private int _initialWriteChunks;
        private int _allocations;
        private bool _restoring;
        private byte[] _stored;

        public MacroTransport(byte[] stored, byte[]? ids = null)
        {
            _stored = stored.ToArray();
            _ids = ids ?? [1];
        }

        public byte[] Stored => _stored;
        public bool FailBackupSize { get; init; }
        public bool TruncateBackupPayload { get; init; }
        public bool WrongBackupSlot { get; init; }
        public bool CorruptRestoreReadback { get; init; }
        public int FailInitialChunk { get; init; }
        public bool FailRestoreAllocation { get; init; }
        public CancellationTokenSource? CancelOnInitialWrite { get; set; }
        public bool PauseFirstAllocation { get; init; }
        public int RestoreReadBacks { get; private set; }
        public int CountQueries { get; private set; }
        public List<byte> Commands { get; } = [];
        public List<byte> IdPageDeclaredSizes { get; } = [];
        public TaskCompletionSource FirstAllocationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueFirstAllocation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public async Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> requestMemory,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var request = requestMemory.ToArray();
            var command = request[RazerFeatureReport.CommandIdOffset];
            Commands.Add(command);
            switch (command)
            {
                case 0x8E:
                    return Storage(request);
                case 0x80:
                    CountQueries++;
                    return Response(request, [(byte)(_ids.Length >> 8), (byte)_ids.Length]);
                case 0x8B:
                    return IdPage(request);
                case 0x88:
                    if (FailBackupSize && _allocations == 0)
                    {
                        throw new IOException("backup failed");
                    }
                    return Response(request, [0, (byte)(WrongBackupSlot && _allocations == 0 ? 2 : 1),
                        0, 0, 0, (byte)_stored.Length]);
                case 0x08:
                    _allocations++;
                    _restoring = _allocations > 1;
                    if (_restoring && FailRestoreAllocation)
                    {
                        throw new IOException("restore allocation failed");
                    }
                    if (_allocations == 1 && PauseFirstAllocation)
                    {
                        FirstAllocationStarted.SetResult();
                        await ContinueFirstAllocation.Task;
                    }
                    return Response(request, []);
                case 0x09:
                    return WriteChunk(request, cancellationToken);
                case 0x89:
                    if (_restoring)
                    {
                        RestoreReadBacks++;
                    }
                    return ReadChunk(request);
                default:
                    throw new InvalidOperationException($"Unexpected macro command 0x{command:X2}.");
            }
        }

        private byte[] WriteChunk(byte[] request, CancellationToken cancellationToken)
        {
            var arguments = request.AsSpan(RazerFeatureReport.ArgumentsOffset);
            var offset = (arguments[2] << 24) | (arguments[3] << 16) | (arguments[4] << 8) | arguments[5];
            var length = arguments[6];
            if (!_restoring && ++_initialWriteChunks == FailInitialChunk)
            {
                throw new IOException("chunk failed");
            }

            if (offset == 0)
            {
                _stored = new byte[offset + length];
            }
            else if (_stored.Length < offset + length)
            {
                Array.Resize(ref _stored, offset + length);
            }
            arguments.Slice(7, length).CopyTo(_stored.AsSpan(offset));
            if (!_restoring && CancelOnInitialWrite is { } cancellation)
            {
                CancelOnInitialWrite = null;
                cancellation.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
            return Response(request, []);
        }

        private byte[] ReadChunk(byte[] request)
        {
            var arguments = request.AsSpan(RazerFeatureReport.ArgumentsOffset);
            var offset = (arguments[2] << 24) | (arguments[3] << 16) | (arguments[4] << 8) | arguments[5];
            var length = arguments[6];
            var data = new byte[7 + length];
            arguments[..7].CopyTo(data);
            _stored.AsSpan(offset, length).CopyTo(data.AsSpan(7));
            if (_restoring && CorruptRestoreReadback) data[7] ^= 0xFF;
            var response = Response(request, data);
            if (_allocations == 0 && TruncateBackupPayload) response[RazerFeatureReport.DataSizeOffset] = 7;
            return response;
        }

        private byte[] IdPage(byte[] request)
        {
            IdPageDeclaredSizes.Add(request[RazerFeatureReport.DataSizeOffset]);
            var offset = (request[RazerFeatureReport.ArgumentsOffset] << 8) |
                request[RazerFeatureReport.ArgumentsOffset + 1];
            var count = (request[RazerFeatureReport.DataSizeOffset] - 4) / 2;
            var data = new byte[4 + (count * 2)];
            data[0] = (byte)(offset >> 8);
            data[1] = (byte)offset;
            data[3] = (byte)count;
            for (var index = 0; index < count; index++)
            {
                data[5 + (index * 2)] = _ids[offset + index];
            }
            return Response(request, data);
        }

        private static byte[] Storage(byte[] request)
        {
            var data = new byte[14];
            data[1] = byte.MaxValue;
            data[4] = 0x40;
            data[8] = 0x20;
            return Response(request, data);
        }

        private static byte[] Response(byte[] request, ReadOnlySpan<byte> arguments)
        {
            var response = new byte[RazerFeatureReport.Length];
            request.AsSpan(0, RazerFeatureReport.ArgumentsOffset).CopyTo(response);
            response[RazerFeatureReport.DataSizeOffset] = request[RazerFeatureReport.DataSizeOffset];
            arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        }
    }
}
