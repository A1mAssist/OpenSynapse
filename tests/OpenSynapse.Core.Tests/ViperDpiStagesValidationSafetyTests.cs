using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class ViperDpiStagesValidationSafetyTests
{
    private static readonly ViperDpiStagesState Original = new(3,
    [
        new(1, 400, 400),
        new(2, 800, 800),
        new(3, 1600, 1600),
        new(4, 3200, 3200),
        new(5, 6400, 6400),
    ]);

    [Fact]
    public async Task WritesSameValueThenTargetAndRestoresOriginal()
    {
        var transport = new DpiStagesTransport(Original);

        var result = await ViperDpiStagesValidation.ExecuteAsync(transport, "viper");

        Assert.Null(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.Equal((byte)1, result.ChangedStage);
        Assert.Equal(50, result.Delta);
        AssertState(Original, result.SameValueReadback);
        Assert.Equal(450, result.Target!.Stages[0].X);
        AssertState(result.Target, result.TargetReadback);
        AssertState(Original, result.RestorationReadback);
        Assert.Equal(new[] { "GET", "SET", "GET", "SET", "GET", "SET", "GET" }, transport.Commands);
        AssertState(Original, transport.State);
    }

    [Fact]
    public async Task TargetReadbackMismatchStillRestoresOriginal()
    {
        var transport = new DpiStagesTransport(Original) { IgnoreWriteNumber = 2 };

        var result = await ViperDpiStagesValidation.ExecuteAsync(transport, "viper");

        Assert.Contains("目标 DPI 档位读回不一致", result.OperationError, StringComparison.Ordinal);
        Assert.Null(result.RestorationError);
        AssertState(Original, result.RestorationReadback);
        AssertState(Original, transport.State);
    }

    [Fact]
    public async Task TargetSetExceptionStillRestoresOriginal()
    {
        var transport = new DpiStagesTransport(Original) { FailWriteNumber = 2 };

        var result = await ViperDpiStagesValidation.ExecuteAsync(transport, "viper");

        Assert.Contains("Simulated SET failure 2", result.OperationError, StringComparison.Ordinal);
        Assert.Null(result.RestorationError);
        AssertState(Original, result.RestorationReadback);
        AssertState(Original, transport.State);
    }

    [Fact]
    public async Task RestorationFailureIsReportedSeparately()
    {
        var transport = new DpiStagesTransport(Original) { FailWriteNumber = 3 };

        var result = await ViperDpiStagesValidation.ExecuteAsync(transport, "viper");

        Assert.Null(result.OperationError);
        Assert.Contains("恢复写入", result.RestorationError, StringComparison.Ordinal);
        Assert.Contains("恢复读回", result.RestorationError, StringComparison.Ordinal);
        Assert.NotEqual(Original.Stages[0].X, transport.State.Stages[0].X);
    }

    private static void AssertState(ViperDpiStagesState expected, ViperDpiStagesState? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.ActiveStage, actual.ActiveStage);
        Assert.Equal(expected.Stages, actual.Stages);
    }

    private sealed class DpiStagesTransport(ViperDpiStagesState initial) : IRazerFeatureTransport
    {
        private int _writeCount;

        public int? IgnoreWriteNumber { get; set; }
        public int? FailWriteNumber { get; set; }
        public List<string> Commands { get; } = [];
        public ViperDpiStagesState State { get; private set; } = Copy(initial);

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
            if (transactionId != 0x1F || commandClass != 0x04 || commandId is not (0x06 or 0x86))
            {
                throw new InvalidOperationException($"Unexpected command {commandClass:X2}{commandId:X2}.");
            }

            if (commandId == 0x06)
            {
                Commands.Add("SET");
                _writeCount++;
                if (FailWriteNumber == _writeCount)
                {
                    throw new InvalidOperationException($"Simulated SET failure {_writeCount}.");
                }
                if (IgnoreWriteNumber != _writeCount)
                {
                    State = ParseSet(arguments.Span);
                }

                return Task.FromResult(Response(transactionId, dataSize, commandClass, commandId, arguments.Span));
            }

            Commands.Add("GET");
            return Task.FromResult(GetResponse(State));
        }

        private static ViperDpiStagesState ParseSet(ReadOnlySpan<byte> arguments)
        {
            Assert.Equal((byte)0x01, arguments[0]);
            var count = arguments[2];
            var stages = new ViperDpiStage[count];
            for (var index = 0; index < count; index++)
            {
                var offset = 3 + (7 * index);
                Assert.Equal((byte)index, arguments[offset]);
                stages[index] = new ViperDpiStage(
                    checked((byte)(index + 1)),
                    (arguments[offset + 1] << 8) | arguments[offset + 2],
                    (arguments[offset + 3] << 8) | arguments[offset + 4]);
            }
            return new ViperDpiStagesState(arguments[1], stages);
        }

        private static byte[] GetResponse(ViperDpiStagesState state)
        {
            var arguments = new byte[0x26];
            arguments[0] = 0x01;
            arguments[1] = state.ActiveStage;
            arguments[2] = checked((byte)state.Stages.Count);
            for (var index = 0; index < state.Stages.Count; index++)
            {
                var stage = state.Stages[index];
                var offset = 3 + (7 * index);
                arguments[offset] = stage.Number;
                arguments[offset + 1] = (byte)(stage.X >> 8);
                arguments[offset + 2] = (byte)stage.X;
                arguments[offset + 3] = (byte)(stage.Y >> 8);
                arguments[offset + 4] = (byte)stage.Y;
            }
            return Response(0x1F, 0x26, 0x04, 0x86, arguments);
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

        private static ViperDpiStagesState Copy(ViperDpiStagesState state) =>
            new(state.ActiveStage, state.Stages.ToArray());
    }
}
