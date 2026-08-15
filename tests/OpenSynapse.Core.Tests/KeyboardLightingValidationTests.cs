using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class KeyboardLightingValidationTests
{
    [Theory]
    [InlineData("static-red", "StaticRed")]
    [InlineData("matrix-locator", "MatrixLocator")]
    public void ParsesOnlyRestrictedTargets(string value, string expected)
    {
        var options = KeyboardLightingValidation.Options.Parse(
            ["--keyboard-lighting", value, "--hold-seconds", "5", "--output", $"keyboard-{value}.json"]);

        Assert.Equal(expected, options.Target.ToString());
        Assert.Equal(5, options.HoldSeconds);
    }

    [Theory]
    [InlineData("--keyboard-lighting", "wave", "--output", "keyboard-wave.json")]
    [InlineData("--keyboard-lighting", "static-red", "--leave-target", "--output", "keyboard-leave.json")]
    [InlineData("--logo", "off", "--output", "keyboard-logo.json")]
    [InlineData("--keyboard-lighting", "static-red", "--hold-seconds", "4", "--output", "keyboard-short.json")]
    [InlineData("--keyboard-lighting", "static-red", "--output", "keyboard.txt")]
    public void RejectsAnythingOutsideRestrictedSurface(params string[] args) =>
        Assert.ThrowsAny<ArgumentException>(() => KeyboardLightingValidation.Options.Parse(args));

    [Fact]
    public async Task StaticRedAcknowledgesThenRestoresGreen()
    {
        var transport = new RecordingTransport();

        var result = await KeyboardLightingValidation.ExecuteAsync(
            transport,
            "blade",
            KeyboardLightingValidation.Target.StaticRed,
            () => Task.CompletedTask);

        Assert.Null(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.True(result.TargetAcknowledged);
        Assert.True(result.RestorationAcknowledged);
        Assert.Equal(2, transport.Commands.Count);
        Assert.Equal(0x0A, transport.Commands[0].CommandId);
        Assert.Equal(new byte[] { 0x06, 0xFF, 0x00, 0x00 }, transport.Commands[0].Arguments);
        Assert.Equal(0x0A, transport.Commands[1].CommandId);
        Assert.Equal(new byte[] { 0x06, 0x99, 0xDD, 0x72 }, transport.Commands[1].Arguments);
    }

    [Fact]
    public async Task StaticFailureStillAttemptsRestore()
    {
        var transport = new RecordingTransport { FailAtCall = 1 };

        var result = await KeyboardLightingValidation.ExecuteAsync(
            transport,
            "blade",
            KeyboardLightingValidation.Target.StaticRed,
            () => Task.CompletedTask);

        Assert.NotNull(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.False(result.TargetAcknowledged);
        Assert.True(result.RestorationAcknowledged);
        Assert.Equal(2, transport.Commands.Count);
        Assert.Equal(new byte[] { 0x06, 0x99, 0xDD, 0x72 }, transport.Commands[1].Arguments);
    }

    [Fact]
    public async Task CancellationStillRestoresStaticGreen()
    {
        var transport = new RecordingTransport();

        var result = await KeyboardLightingValidation.ExecuteAsync(
            transport,
            "blade",
            KeyboardLightingValidation.Target.StaticRed,
            () => throw new OperationCanceledException("simulated cancel"));

        Assert.Contains("simulated cancel", result.OperationError);
        Assert.True(result.RestorationAcknowledged);
        Assert.Equal(new byte[] { 0x06, 0x99, 0xDD, 0x72 }, transport.Commands[^1].Arguments);
    }

    [Fact]
    public async Task MatrixSendsSixCompleteRowsThenRestores()
    {
        var transport = new RecordingTransport();

        var result = await KeyboardLightingValidation.ExecuteAsync(
            transport,
            "blade",
            KeyboardLightingValidation.Target.MatrixLocator,
            () => Task.CompletedTask);

        Assert.Null(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.True(result.TargetAcknowledged);
        Assert.True(result.RestorationAcknowledged);
        Assert.Equal(6, result.MatrixRowsAcknowledged);
        Assert.Equal(17, transport.Commands.Count);
        Assert.Equal((0x00, 0x04),
            (transport.Commands[0].CommandClass, transport.Commands[0].CommandId));
        Assert.Equal(new byte[] { 0x03, 0x00 }, transport.Commands[0].Arguments);
        Assert.Equal((0x0F, 0x02),
            (transport.Commands[1].CommandClass, transport.Commands[1].CommandId));
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08, 0x00, 0x00, 0x00 },
            transport.Commands[1].Arguments);
        Assert.Equal(Enumerable.Range(1, 6).Select(value => (byte)value),
            transport.Commands.Skip(2).Take(6).Select(command => command.TransactionId));
        Assert.Equal(Enumerable.Range(0, 6).Select(value => (byte)value),
            transport.Commands.Skip(2).Take(6).Select(command => command.Arguments[1]));
        Assert.All(transport.Commands.Skip(2).Take(6), command =>
        {
            Assert.Equal(0x03, command.CommandClass);
            Assert.Equal(0x0B, command.CommandId);
            Assert.Equal(0x37, command.DataSize);
            Assert.Equal(55, command.Arguments.Length);
            Assert.Equal(0xFF, command.Arguments[0]);
            Assert.Equal(0, command.Arguments[2]);
            Assert.Equal(16, command.Arguments[3]);
        });
        Assert.Equal(new RazerRgb(0xFF, 0x00, 0x00), ReadColor(transport.Commands[2], 0));
        Assert.Equal(new RazerRgb(0xFF, 0xFF, 0xFF), ReadColor(transport.Commands[2], 1));
        Assert.Equal(new RazerRgb(0xFF, 0xFF, 0xFF), ReadColor(transport.Commands[2], 16));
        Assert.Equal(new RazerRgb(0x00, 0x00, 0xFF), ReadColor(transport.Commands[7], 0));
        Assert.Equal(new RazerRgb(0xFF, 0xFF, 0xFF), ReadColor(transport.Commands[7], 1));
        Assert.Equal(new RazerRgb(0xFF, 0xFF, 0xFF), ReadColor(transport.Commands[7], 16));
        Assert.Equal((0x03, 0x0A),
            (transport.Commands[8].CommandClass, transport.Commands[8].CommandId));
        Assert.Equal(new byte[] { 0x05, 0x00 }, transport.Commands[8].Arguments);
        Assert.All(transport.Commands.Skip(9).Take(6), command =>
        {
            Assert.Equal(0x0B, command.CommandId);
            Assert.Equal(0x99, command.Arguments[4]);
            Assert.Equal(0xDD, command.Arguments[5]);
            Assert.Equal(0x72, command.Arguments[6]);
        });
        Assert.Equal((0x03, 0x0A),
            (transport.Commands[15].CommandClass, transport.Commands[15].CommandId));
        Assert.Equal((0x00, 0x04),
            (transport.Commands[^1].CommandClass, transport.Commands[^1].CommandId));
        Assert.Equal(new byte[] { 0x00, 0x00 }, transport.Commands[^1].Arguments);
    }

    [Fact]
    public async Task MatrixKeepsPublishingUntilVisualHoldCompletes()
    {
        var transport = new RecordingTransport();
        var releaseHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = KeyboardLightingValidation.ExecuteAsync(
            transport,
            "blade",
            KeyboardLightingValidation.Target.MatrixLocator,
            () => releaseHold.Task);

        await transport.WaitForCallsAsync(14);
        releaseHold.TrySetResult();
        var result = await execution;

        Assert.Null(result.OperationError);
        Assert.True(result.TargetAcknowledged);
        Assert.True(transport.Commands.Count >= 21);
        Assert.Contains(transport.Commands, command =>
            (command.CommandClass, command.CommandId) == (0x0F, 0x02));
    }

    [Fact]
    public async Task MatrixRowFailureStillRestoresAndDoesNotClaimTarget()
    {
        var transport = new RecordingTransport { FailAtCall = 3 };

        var result = await KeyboardLightingValidation.ExecuteAsync(
            transport,
            "blade",
            KeyboardLightingValidation.Target.MatrixLocator,
            () => Task.CompletedTask);

        Assert.NotNull(result.OperationError);
        Assert.False(result.TargetAcknowledged);
        Assert.True(result.RestorationAcknowledged);
        Assert.Null(result.MatrixRowsAcknowledged);
        Assert.Equal(0x0B, transport.Commands[^3].CommandId);
        Assert.Equal(new byte[] { 0x99, 0xDD, 0x72 }, transport.Commands[^3].Arguments[4..7]);
        Assert.Equal((0x00, 0x04),
            (transport.Commands[^1].CommandClass, transport.Commands[^1].CommandId));
    }

    [Fact]
    public async Task ReportsRestorationFailureSeparately()
    {
        var transport = new RecordingTransport { FailAtCall = 2 };

        var result = await KeyboardLightingValidation.ExecuteAsync(
            transport,
            "blade",
            KeyboardLightingValidation.Target.StaticRed,
            () => Task.CompletedTask);

        Assert.Null(result.OperationError);
        Assert.NotNull(result.RestorationError);
        Assert.True(result.TargetAcknowledged);
        Assert.False(result.RestorationAcknowledged);
    }

    private sealed record Command(
        byte TransactionId,
        byte DataSize,
        byte CommandClass,
        byte CommandId,
        byte[] Arguments);

    private static RazerRgb ReadColor(Command command, int column)
    {
        var offset = 4 + column * 3;
        return new RazerRgb(
            command.Arguments[offset],
            command.Arguments[offset + 1],
            command.Arguments[offset + 2]);
    }

    private sealed class RecordingTransport : IRazerFeatureTransport
    {
        private readonly List<Command> _commands = [];
        private readonly object _sync = new();

        public int? FailAtCall { get; init; }

        public IReadOnlyList<Command> Commands
        {
            get
            {
                lock (_sync)
                {
                    return _commands.ToArray();
                }
            }
        }

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
            int call;
            lock (_sync)
            {
                _commands.Add(new Command(
                    transactionId,
                    dataSize,
                    commandClass,
                    commandId,
                    arguments.ToArray()));
                call = _commands.Count;
            }

            if (FailAtCall == call)
            {
                throw new InvalidOperationException($"Simulated failure at call {call}.");
            }

            var response = new byte[RazerFeatureReport.Length];
            response[1] = 0x02;
            response[2] = transactionId;
            response[6] = dataSize;
            response[7] = commandClass;
            response[8] = commandId;
            arguments.CopyTo(response.AsMemory(RazerFeatureReport.ArgumentsOffset));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }

        public async Task WaitForCallsAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (Commands.Count < count)
            {
                await Task.Delay(5, timeout.Token);
            }
        }
    }
}
