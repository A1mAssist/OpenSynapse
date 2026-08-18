using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class ViperButtonMappingTransactionTests
{
    private static readonly DeviceDescriptor Viper = new(
        "viper-path", "Viper", 0x1532, 0x00B8,
        DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
        "viper-184");

    [Fact]
    public async Task ReadsExactProductMetadataAndAllSixteenAssignments()
    {
        var transport = new MappingTransport();
        var reader = new RazerDeviceTelemetryReader(transport);

        var assignments = await reader.ReadViperButtonAssignmentsAsync([Viper]);

        Assert.Equal(16, assignments.Count);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 9, 10, 96 },
            assignments.Select(item => item.ButtonId).Distinct());
        Assert.All(assignments, item => Assert.Equal((byte)1, item.ProfileId));
        Assert.Equal(8, assignments.Count(item => item.Layer == ViperButtonMappingLayer.Normal));
        Assert.Equal(8, assignments.Count(item => item.Layer == ViperButtonMappingLayer.HyperShift));
        Assert.Equal(
            ViperButtonMappingFunction.Dpi,
            assignments.Single(item => item.ButtonId == 96 &&
                                       item.Layer == ViperButtonMappingLayer.Normal).Function);
        Assert.Equal(
            new[] { "META:MAX", "META:COUNT", "META:PROFILES", "META:BUTTONS" },
            transport.Commands.Take(4));
    }

    [Fact]
    public async Task RejectsSetUntilCurrentPathHasCompletedFullRead()
    {
        var transport = new MappingTransport();
        var reader = new RazerDeviceTelemetryReader(transport);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetViperButtonAssignmentAsync([Viper], Off(5)));

        Assert.Contains("全部 16 条", exception.Message, StringComparison.Ordinal);
        Assert.Empty(transport.Commands);
    }

    [Fact]
    public async Task OrdinaryTelemetryRefreshClearsCompletedMappingValidation()
    {
        var transport = new MappingTransport();
        var reader = new RazerDeviceTelemetryReader(transport);

        await reader.ReadViperButtonAssignmentsAsync([Viper]);
        transport.Commands.Clear();
        await reader.ReadAsync([]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetViperButtonAssignmentAsync([Viper], Off(5)));
        Assert.Empty(transport.Commands);
    }

    [Theory]
    [InlineData(2, 5)]
    [InlineData(1, 6)]
    public async Task RejectsUnknownProfileOrButtonBeforeTransport(byte profileId, byte buttonId)
    {
        var transport = new MappingTransport();
        var reader = new RazerDeviceTelemetryReader(transport);
        var assignment = Off(buttonId) with { ProfileId = profileId };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await reader.SetViperButtonAssignmentAsync([Viper], assignment));

        Assert.Empty(transport.Commands);
    }

    [Fact]
    public async Task VerifiedKeyboardKeyUsesReadbackAndPreservesSiblingLayer()
    {
        var transport = new MappingTransport();
        var reader = new RazerDeviceTelemetryReader(transport);
        var assignment = new ViperButtonAssignment(
            1,
            5,
            ViperButtonMappingLayer.HyperShift,
            ViperButtonMappingFunction.KeyboardKey,
            new byte[] { 0, 4 });

        await reader.ReadViperButtonAssignmentsAsync([Viper]);
        transport.Commands.Clear();
        var actual = await reader.SetViperButtonAssignmentAsync([Viper], assignment);

        Assert.Equal(ViperButtonMappingFunction.KeyboardKey, actual.Function);
        Assert.Equal(new byte[] { 0, 4 }, actual.FunctionData);
        Assert.Equal(
            new[] { "GET:5:1", "GET:5:0", "SET:5:1", "GET:5:1", "GET:5:0" },
            transport.Commands);
        Assert.Equal(ViperObmFunctionId.KeyCode, transport.Get(5, ViperObmMappingMode.HyperShift).Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.Get(5, ViperObmMappingMode.Normal).Function);
    }

    [Theory]
    [InlineData(ViperButtonMappingFunction.Dpi)]
    [InlineData(ViperButtonMappingFunction.MediaKey)]
    [InlineData(ViperButtonMappingFunction.HyperShift)]
    [InlineData(ViperButtonMappingFunction.KeyboardTurbo)]
    [InlineData(ViperButtonMappingFunction.MouseTurbo)]
    public async Task VerifiedExtendedFunctionFamiliesUseReadbackAndPreserveSiblingLayer(
        ViperButtonMappingFunction function)
    {
        var transport = new MappingTransport();
        var reader = new RazerDeviceTelemetryReader(transport);
        var assignment = new ViperButtonAssignment(
            1,
            5,
            ViperButtonMappingLayer.HyperShift,
            function,
            function switch
            {
                ViperButtonMappingFunction.Dpi => [6],
                ViperButtonMappingFunction.MediaKey => [0xCD, 0x00],
                ViperButtonMappingFunction.HyperShift => [1],
                ViperButtonMappingFunction.KeyboardTurbo => [0, 4, 100, 0],
                ViperButtonMappingFunction.MouseTurbo => [1, 100, 0],
                _ => throw new ArgumentOutOfRangeException(nameof(function)),
            });

        await reader.ReadViperButtonAssignmentsAsync([Viper]);
        transport.Commands.Clear();
        var actual = await reader.SetViperButtonAssignmentAsync([Viper], assignment);

        Assert.Equal(function, actual.Function);
        Assert.Equal(assignment.FunctionData, actual.FunctionData);
        Assert.Equal(
            new[] { "GET:5:1", "GET:5:0", "SET:5:1", "GET:5:1", "GET:5:0" },
            transport.Commands);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.Get(5, ViperObmMappingMode.Normal).Function);
    }

    [Fact]
    public async Task VerifiedDoubleClickUsesReadbackAndPreservesSiblingLayer()
    {
        var transport = new MappingTransport();
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadViperButtonAssignmentsAsync([Viper]);
        transport.Commands.Clear();
        var assignment = new ViperButtonAssignment(
            1, 5, ViperButtonMappingLayer.Normal,
            ViperButtonMappingFunction.DoubleClick, new byte[] { 1 });

        var actual = await reader.SetViperButtonAssignmentAsync([Viper], assignment);

        Assert.Equal(ViperButtonMappingFunction.DoubleClick, actual.Function);
        Assert.Equal(ViperObmFunctionId.DoubleClick, transport.Get(5, ViperObmMappingMode.Normal).Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.Get(5, ViperObmMappingMode.HyperShift).Function);
    }

    [Fact]
    public async Task SetUsesGetReadbackAndPreservesSiblingLayer()
    {
        var transport = new MappingTransport();
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadViperButtonAssignmentsAsync([Viper]);
        transport.Commands.Clear();

        var actual = await reader.SetViperButtonAssignmentAsync([Viper], Off(5));

        Assert.Equal(ViperButtonMappingFunction.Off, actual.Function);
        Assert.Equal(
            new[] { "GET:5:1", "GET:5:0", "SET:5:1", "GET:5:1", "GET:5:0" },
            transport.Commands);
        Assert.Equal(ViperObmFunctionId.Off, transport.Get(5, ViperObmMappingMode.HyperShift).Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.Get(5, ViperObmMappingMode.Normal).Function);
    }

    [Fact]
    public async Task SetAckIsNotAcceptedAsStateAndMismatchRestoresOriginal()
    {
        var transport = new MappingTransport { IgnoreWriteNumber = 1 };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadViperButtonAssignmentsAsync([Viper]);
        transport.Commands.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetViperButtonAssignmentAsync([Viper], Off(5)));

        Assert.Contains("目标映射读回不一致", exception.Message, StringComparison.Ordinal);
        Assert.Contains("已恢复并读回确认", exception.Message, StringComparison.Ordinal);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.Get(5, ViperObmMappingMode.HyperShift).Function);
        Assert.Equal(
            new[] { "GET:5:1", "GET:5:0", "SET:5:1", "GET:5:1", "SET:5:1", "GET:5:1", "GET:5:0" },
            transport.Commands);
    }

    [Fact]
    public async Task CancellationAfterApplyStillRestoresWithCancellationTokenNone()
    {
        var transport = new MappingTransport { CancelWriteNumberAfterApplying = 1 };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadViperButtonAssignmentsAsync([Viper]);
        transport.Commands.Clear();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await reader.SetViperButtonAssignmentAsync([Viper], Off(5), new CancellationTokenSource().Token));

        Assert.Contains("已恢复并读回确认", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, transport.SetCancellationTokens.Count);
        Assert.True(transport.SetCancellationTokens[0].CanBeCanceled);
        Assert.False(transport.SetCancellationTokens[1].CanBeCanceled);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.Get(5, ViperObmMappingMode.HyperShift).Function);
    }

    [Fact]
    public async Task IoFailureAfterApplyStillRestoresOriginal()
    {
        var transport = new MappingTransport { IoFailWriteNumberAfterApplying = 1 };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadViperButtonAssignmentsAsync([Viper]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetViperButtonAssignmentAsync([Viper], Off(5)));

        Assert.Contains("已恢复并读回确认", exception.Message, StringComparison.Ordinal);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.Get(5, ViperObmMappingMode.HyperShift).Function);
    }

    [Fact]
    public async Task RestorationFailureIsExplicit()
    {
        var transport = new MappingTransport
        {
            IgnoreWriteNumber = 1,
            FailWriteNumber = 2,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadViperButtonAssignmentsAsync([Viper]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetViperButtonAssignmentAsync([Viper], Off(5)));

        Assert.Contains("原映射恢复失败", exception.Message, StringComparison.Ordinal);
        Assert.Contains("恢复写入失败", exception.Message, StringComparison.Ordinal);

        transport.Commands.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetViperButtonAssignmentAsync([Viper], Off(5)));
        Assert.Empty(transport.Commands);
    }

    [Fact]
    public async Task MetadataOutsideProduct184ScopeKeepsSetLocked()
    {
        var transport = new MappingTransport { ButtonIds = [1, 2, 3, 4, 5, 9, 10] };
        var reader = new RazerDeviceTelemetryReader(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.ReadViperButtonAssignmentsAsync([Viper]));
        transport.Commands.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetViperButtonAssignmentAsync([Viper], Off(5)));

        Assert.Empty(transport.Commands);
    }

    [Fact]
    public async Task MalformedAssignmentKeepsSetLocked()
    {
        var transport = new MappingTransport { ReturnTruncatedAssignment = true };
        var reader = new RazerDeviceTelemetryReader(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.ReadViperButtonAssignmentsAsync([Viper]));
        transport.Commands.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetViperButtonAssignmentAsync([Viper], Off(5)));
        Assert.Empty(transport.Commands);
    }

    [Fact]
    public async Task ConcurrentWritesCannotInterleaveTransactions()
    {
        var transport = new MappingTransport { BlockFirstWriteResponse = true };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadViperButtonAssignmentsAsync([Viper]);
        transport.Commands.Clear();

        var first = reader.SetViperButtonAssignmentAsync([Viper], Off(5)).AsTask();
        await transport.FirstWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second = reader.SetViperButtonAssignmentAsync([Viper], Off(9)).AsTask();
        await Task.Delay(50);

        Assert.DoesNotContain(transport.Commands, command => command.Contains(":9:", StringComparison.Ordinal));
        transport.ReleaseFirstWriteResponse.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(
            new[]
            {
                "GET:5:1", "GET:5:0", "SET:5:1", "GET:5:1", "GET:5:0",
                "GET:9:1", "GET:9:0", "SET:9:1", "GET:9:1", "GET:9:0",
            },
            transport.Commands);
    }

    private static ViperButtonAssignment Off(byte buttonId) =>
        new(1, buttonId, ViperButtonMappingLayer.HyperShift,
            ViperButtonMappingFunction.Off, Array.Empty<byte>());

    private sealed class MappingTransport : IRazerFeatureTransport
    {
        private readonly Dictionary<(byte ButtonId, ViperObmMappingMode Mode), ViperObmAssignment> _assignments =
            CreateAssignments();
        private int _writeCount;

        public byte[] ButtonIds { get; set; } = [1, 2, 3, 4, 5, 9, 10, 96];
        public int? IgnoreWriteNumber { get; set; }
        public int? FailWriteNumber { get; set; }
        public int? CancelWriteNumberAfterApplying { get; set; }
        public int? IoFailWriteNumberAfterApplying { get; set; }
        public bool BlockFirstWriteResponse { get; set; }
        public bool ReturnTruncatedAssignment { get; set; }
        public TaskCompletionSource FirstWriteEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstWriteResponse { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Commands { get; } = [];
        public List<CancellationToken> SetCancellationTokens { get; } = [];

        public ViperObmAssignment Get(byte buttonId, ViperObmMappingMode mode) =>
            _assignments[(buttonId, mode)];

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("viper-path", devicePath);
            Assert.Equal((byte)0x1F, transactionId);
            var args = arguments.Span;

            if (commandClass == 0x05)
            {
                var (name, values) = commandId switch
                {
                    0x8A => ("MAX", new byte[] { 1 }),
                    0x80 => ("COUNT", new byte[] { 1 }),
                    0x81 => ("PROFILES", new byte[] { 1, 1 }),
                    _ => throw new InvalidOperationException($"Unexpected Profile command 0x{commandId:X2}."),
                };
                Commands.Add("META:" + name);
                return Task.FromResult(Response(transactionId, dataSize, commandClass, commandId, values));
            }

            if ((commandClass, commandId) == (0x02, 0x84))
            {
                Commands.Add("META:BUTTONS");
                return Task.FromResult(Response(
                    transactionId,
                    dataSize,
                    commandClass,
                    commandId,
                    new[] { checked((byte)ButtonIds.Length) }.Concat(ButtonIds).ToArray()));
            }

            var buttonId = args[1];
            var mode = (ViperObmMappingMode)args[2];
            if ((commandClass, commandId) == (0x02, 0x8C))
            {
                Commands.Add($"GET:{buttonId}:{(byte)mode}");
                var state = _assignments[(buttonId, mode)];
                var values = new byte[10];
                values[0] = state.ProfileId;
                values[1] = state.ButtonId;
                values[2] = 1; // Product 184 echoes one even for a Normal request.
                values[3] = (byte)state.Function;
                values[4] = checked((byte)state.FunctionData.Count);
                state.FunctionData.ToArray().CopyTo(values, 5);
                var getResponse = Response(transactionId, 10, commandClass, commandId, values);
                if (ReturnTruncatedAssignment && buttonId == 1 && mode == ViperObmMappingMode.Normal)
                {
                    getResponse[6] = 5;
                    getResponse[89] = RazerFeatureReport.CalculateCrc(getResponse);
                }
                return Task.FromResult(getResponse);
            }

            if ((commandClass, commandId) != (0x02, 0x0C))
            {
                throw new InvalidOperationException($"Unexpected OBM command {commandClass:X2}{commandId:X2}.");
            }

            Commands.Add($"SET:{buttonId}:{(byte)mode}");
            SetCancellationTokens.Add(cancellationToken);
            _writeCount++;
            if (FailWriteNumber == _writeCount)
            {
                throw new InvalidOperationException($"Simulated SET failure {_writeCount}." );
            }
            if (IgnoreWriteNumber != _writeCount)
            {
                var size = args[4];
                _assignments[(buttonId, mode)] = new(
                    args[0],
                    buttonId,
                    mode,
                    (ViperObmFunctionId)args[3],
                    args.Slice(5, size).ToArray());
            }
            if (CancelWriteNumberAfterApplying == _writeCount)
            {
                throw new OperationCanceledException("Simulated cancellation after SET applied.");
            }
            if (IoFailWriteNumberAfterApplying == _writeCount)
            {
                throw new IOException("Simulated I/O failure after SET applied.");
            }

            // A successful SET response echoes the requested bytes, even when IgnoreWriteNumber
            // simulates firmware that did not persist them. Production must issue a separate GET.
            var response = Response(transactionId, dataSize, commandClass, commandId, args.ToArray());
            if (BlockFirstWriteResponse && _writeCount == 1)
            {
                FirstWriteEntered.TrySetResult();
                return CompleteBlockedWriteAsync(response, cancellationToken);
            }
            return Task.FromResult(response);
        }

        private async Task<byte[]> CompleteBlockedWriteAsync(
            byte[] response,
            CancellationToken cancellationToken)
        {
            await ReleaseFirstWriteResponse.Task.WaitAsync(cancellationToken);
            return response;
        }

        private static Dictionary<(byte, ViperObmMappingMode), ViperObmAssignment> CreateAssignments()
        {
            var assignments = new Dictionary<(byte, ViperObmMappingMode), ViperObmAssignment>();
            foreach (var buttonId in new byte[] { 1, 2, 3, 4, 5, 9, 10, 96 })
            {
                foreach (var mode in new[] { ViperObmMappingMode.Normal, ViperObmMappingMode.HyperShift })
                {
                    assignments[(buttonId, mode)] = buttonId == 96
                        ? new(1, buttonId, mode, ViperObmFunctionId.Dpi, new byte[] { 6 })
                        : new(1, buttonId, mode, ViperObmFunctionId.ButtonCode, new byte[] { buttonId });
                }
            }
            return assignments;
        }

        private static byte[] Response(
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlySpan<byte> arguments)
        {
            var response = RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, arguments);
            response[1] = 0x02;
            return response;
        }
    }
}
