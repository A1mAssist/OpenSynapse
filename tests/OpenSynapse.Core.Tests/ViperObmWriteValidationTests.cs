using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class ViperObmWriteValidationTests
{
    [Fact]
    public async Task ChangesOnlyHyperShiftAndAlwaysRestoresOriginal()
    {
        var transport = new ObmTransport();

        var result = await ViperObmWriteValidation.ExecuteAsync(transport, "viper");

        Assert.Null(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.Equal(ViperObmFunctionId.ButtonCode, result.SameValueReadback!.Function);
        Assert.Equal(ViperObmFunctionId.Off, result.TargetReadback!.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, result.NormalAfterTarget!.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, result.RestorationHyperShiftReadback!.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, result.RestorationNormalReadback!.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.HyperShift.Function);
        Assert.Equal(new[] { "GET:0", "GET:1", "SET:1", "GET:1", "SET:1", "GET:1", "GET:0", "SET:1", "GET:1", "GET:0" }, transport.Commands);
    }

    [Fact]
    public async Task FailedTargetReadbackStillRestoresOriginal()
    {
        var transport = new ObmTransport { IgnoreWriteNumber = 2 };

        var result = await ViperObmWriteValidation.ExecuteAsync(transport, "viper");

        Assert.Contains("目标映射读回不一致", result.OperationError, StringComparison.Ordinal);
        Assert.Null(result.RestorationError);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.HyperShift.Function);
    }

    [Fact]
    public async Task UnexpectedBaselineStopsBeforeAnyWrite()
    {
        var transport = new ObmTransport
        {
            HyperShift = new(1, 5, ViperObmMappingMode.HyperShift,
                ViperObmFunctionId.Off, Array.Empty<byte>()),
        };

        var result = await ViperObmWriteValidation.ExecuteAsync(transport, "viper");

        Assert.Contains("HyperShift 基线映射读回不一致", result.OperationError, StringComparison.Ordinal);
        Assert.Null(result.RestorationError);
        Assert.Equal(new[] { "GET:0", "GET:1" }, transport.Commands);
    }

    [Fact]
    public async Task RestorationFailureIsReportedSeparately()
    {
        var transport = new ObmTransport { FailWriteNumber = 3 };

        var result = await ViperObmWriteValidation.ExecuteAsync(transport, "viper");

        Assert.Null(result.OperationError);
        Assert.Contains("恢复写入", result.RestorationError, StringComparison.Ordinal);
        Assert.Contains("恢复 HyperShift 读回", result.RestorationError, StringComparison.Ordinal);
        Assert.Equal(ViperObmFunctionId.Off, transport.HyperShift.Function);
    }

    [Fact]
    public async Task KeyboardATargetIsObservedAndBothLayersAreRestored()
    {
        var transport = new ObmTransport();
        var checkpointed = false;
        var held = false;

        var result = await ViperObmWriteValidation.ExecuteKeyboardAsync(
            transport,
            "viper",
            (normal, hyperShift) =>
            {
                checkpointed = normal.Function == ViperObmFunctionId.ButtonCode &&
                    hyperShift.Function == ViperObmFunctionId.ButtonCode;
                return Task.CompletedTask;
            },
            () =>
            {
                held = transport.Normal.Function == ViperObmFunctionId.KeyCode &&
                    transport.Normal.FunctionData.SequenceEqual(new byte[] { 0x00, 0x04 });
                return Task.CompletedTask;
            });

        Assert.True(checkpointed);
        Assert.True(held);
        Assert.Null(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.Equal(ViperObmFunctionId.KeyCode, result.TargetReadback!.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, result.HyperShiftAfterTarget!.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, result.RestorationNormalReadback!.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, result.RestorationHyperShiftReadback!.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.Normal.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.HyperShift.Function);
    }

    [Fact]
    public async Task KeyboardATargetReadbackFailureStillRestoresOriginal()
    {
        var transport = new ObmTransport { IgnoreWriteNumber = 1 };

        var result = await ViperObmWriteValidation.ExecuteKeyboardAsync(
            transport,
            "viper",
            (_, _) => Task.CompletedTask,
            () => Task.CompletedTask);

        Assert.Contains("Keyboard A 目标映射读回不一致", result.OperationError, StringComparison.Ordinal);
        Assert.Null(result.RestorationError);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.Normal.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.HyperShift.Function);
    }

    [Fact]
    public async Task DoubleClickTargetIsObservedAndRestored()
    {
        var transport = new ObmTransport();
        var target = new ViperObmAssignment(
            1, 5, ViperObmMappingMode.Normal,
            ViperObmFunctionId.DoubleClick, new byte[] { 1 });
        var held = false;

        var result = await ViperObmWriteValidation.ExecuteFunctionAsync(
            transport,
            "viper",
            target,
            "DoubleClick",
            (_, _) => Task.CompletedTask,
            () =>
            {
                held = transport.Normal.Function == ViperObmFunctionId.DoubleClick;
                return Task.CompletedTask;
            });

        Assert.True(held);
        Assert.Null(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.Equal(ViperObmFunctionId.DoubleClick, result.TargetReadback!.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.Normal.Function);
        Assert.Equal(ViperObmFunctionId.ButtonCode, transport.HyperShift.Function);
    }


    private sealed class ObmTransport : IRazerFeatureTransport
    {
        private int _writeCount;

        public int? IgnoreWriteNumber { get; set; }
        public int? FailWriteNumber { get; set; }
        public List<string> Commands { get; } = [];
        public ViperObmAssignment Normal { get; private set; } = Assignment(ViperObmMappingMode.Normal);
        public ViperObmAssignment HyperShift { get; set; } = Assignment(ViperObmMappingMode.HyperShift);

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
            Assert.Equal((byte)0x1F, transactionId);
            Assert.Equal((byte)0x02, commandClass);
            var mode = (ViperObmMappingMode)arguments.Span[2];
            Commands.Add($"{(commandId == 0x0C ? "SET" : "GET")}:{(byte)mode}");

            if (commandId == 0x0C)
            {
                _writeCount++;
                if (FailWriteNumber == _writeCount)
                {
                    throw new InvalidOperationException($"Simulated SET failure {_writeCount}.");
                }
                if (IgnoreWriteNumber != _writeCount)
                {
                    var assignment = ParseSet(arguments.Span);
                    if (mode == ViperObmMappingMode.Normal)
                    {
                        Normal = assignment;
                    }
                    else
                    {
                        HyperShift = assignment;
                    }
                }
                return Task.FromResult(Response(transactionId, dataSize, commandClass, commandId, arguments.Span));
            }

            Assert.Equal((byte)0x8C, commandId);
            var state = mode == ViperObmMappingMode.Normal ? Normal : HyperShift;
            var responseArguments = new byte[10];
            responseArguments[0] = state.ProfileId;
            responseArguments[1] = state.ButtonId;
            responseArguments[2] = 1;
            responseArguments[3] = (byte)state.Function;
            responseArguments[4] = checked((byte)state.FunctionData.Count);
            state.FunctionData.ToArray().CopyTo(responseArguments, 5);
            return Task.FromResult(Response(transactionId, 0x0A, commandClass, commandId, responseArguments));
        }

        private static ViperObmAssignment ParseSet(ReadOnlySpan<byte> arguments)
        {
            var size = arguments[4];
            return new(
                arguments[0],
                arguments[1],
                (ViperObmMappingMode)arguments[2],
                (ViperObmFunctionId)arguments[3],
                arguments.Slice(5, size).ToArray());
        }

        private static ViperObmAssignment Assignment(ViperObmMappingMode mode) =>
            new(1, 5, mode, ViperObmFunctionId.ButtonCode, new byte[] { 5 });

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
