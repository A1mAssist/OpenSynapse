using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class LogoValidationSafetyTests
{
    [Fact]
    public void LeaveTargetAcceptsOnlyNamedLogoTargets()
    {
        foreach (var target in new[] { "off", "static", "breathing" })
        {
            var options = LogoValidation.Options.Parse(
                ["--logo", target, "--leave-target", "--output", $"logo-leave-{target}-test.json"]);

            Assert.True(options.LeaveTarget);
        }

        Assert.True(LogoValidation.Options.Parse(
            ["--logo", "off", "--leave-off", "--output", "logo-legacy-leave-off-test.json"]).LeaveTarget);
        Assert.Throws<ArgumentException>(() => LogoValidation.Options.Parse(
            ["--logo", "static", "--leave-off", "--output", "logo-legacy-leave-static-test.json"]));
        Assert.Throws<ArgumentException>(() => LogoValidation.Options.Parse(
            ["--logo", "static", "--raw", "02 00", "--output", "logo-raw-test.json"]));
    }

    [Fact]
    public void Product710SequenceAcceptsOnlyRestoredBreathingRun()
    {
        var options = LogoValidation.Options.Parse([
            "--logo", "breathing", "--product710-sequence", "--output", "logo-product710-test.json"]);

        Assert.True(options.Product710Sequence);
        Assert.Throws<ArgumentException>(() => LogoValidation.Options.Parse([
            "--logo", "static", "--product710-sequence", "--output", "logo-product710-static-test.json"]));
        Assert.Throws<ArgumentException>(() => LogoValidation.Options.Parse([
            "--logo", "breathing", "--product710-sequence", "--leave-target", "--output", "logo-product710-leave-test.json"]));
    }

    [Fact]
    public async Task Product710SequenceSendsEffectThenStateAndRestoresOriginal()
    {
        var original = new LogoValidation.LogoState(false, BladeLogoMode.Static);
        var transport = new RestoreTransport { Powered = original.Powered, Mode = original.Mode };

        var result = await LogoValidation.ExecuteProduct710Async(
            transport,
            "blade",
            original,
            BladeLogoMode.Breathing,
            () => Task.CompletedTask);

        Assert.True(result.TargetApplied);
        Assert.Equal("effect+state ACK", result.TargetAcknowledgement);
        Assert.Null(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.Equal(original, result.RestorationReadback);
        Assert.Equal(new byte[] { 0x02, 0x00 }, transport.Commands.Take(2));
        Assert.False(transport.Powered);
    }

    [Theory]
    [InlineData(BladeLogoMode.Static, 0x00)]
    [InlineData(BladeLogoMode.Breathing, 0x02)]
    public async Task LeaveTargetEndsWithModeThenPower(BladeLogoMode mode, byte modeValue)
    {
        var transport = new RestoreTransport
        {
            Powered = false,
            Mode = BladeLogoMode.Static,
        };

        var result = await LogoValidation.ExecuteAsync(
            transport,
            "blade",
            new LogoValidation.LogoState(false, BladeLogoMode.Static),
            new LogoValidation.LogoState(true, mode),
            leaveTarget: true,
            () => Task.CompletedTask);

        Assert.Null(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.True(result.TargetApplied);
        Assert.Equal(
            new[]
            {
                (CommandId: (byte)0x02, Value: modeValue),
                (CommandId: (byte)0x00, Value: (byte)0x01),
            },
            transport.Writes.TakeLast(2));
    }

    [Fact]
    public async Task LeaveTargetReadbackMismatchRestoresOriginalState()
    {
        var original = new LogoValidation.LogoState(false, BladeLogoMode.Static);
        var transport = new RestoreTransport
        {
            Powered = original.Powered,
            Mode = original.Mode,
            NextModeRead = BladeLogoMode.Static,
        };

        var result = await LogoValidation.ExecuteAsync(
            transport,
            "blade",
            original,
            new LogoValidation.LogoState(true, BladeLogoMode.Breathing),
            leaveTarget: true,
            () => Task.CompletedTask);

        Assert.False(result.TargetApplied);
        Assert.Contains("目标状态读回不一致", result.OperationError, StringComparison.Ordinal);
        Assert.Null(result.RestorationError);
        Assert.Equal(original, result.RestorationReadback);
        Assert.False(transport.Powered);
        Assert.Equal(BladeLogoMode.Static, transport.Mode);
        Assert.Equal((CommandId: (byte)0x00, Value: (byte)0x00), transport.Writes[^1]);
    }

    [Fact]
    public async Task RestoreOffDisablesPowerLastEvenWhenModeRestoreFails()
    {
        var transport = new RestoreTransport
        {
            Powered = true,
            Mode = BladeLogoMode.Breathing,
            FailNextModeWrite = true,
        };

        var result = await LogoValidation.RestoreAsync(
            transport,
            "blade",
            new LogoValidation.LogoState(false, BladeLogoMode.Static));

        Assert.False(transport.Powered);
        Assert.Equal(2, transport.PowerWriteCount);
        Assert.Equal(1, transport.ModeWriteCount);
        Assert.Equal(new byte[] { 0x02, 0x00, 0x80, 0x82, 0x00 }, transport.Commands);
        Assert.Contains("恢复 Logo 底层模式", result.Error, StringComparison.Ordinal);
        Assert.Contains("恢复读回不一致", result.Error, StringComparison.Ordinal);
    }

    private sealed class RestoreTransport : IRazerFeatureTransport
    {
        public bool Powered { get; set; }
        public BladeLogoMode Mode { get; set; }
        public bool FailNextModeWrite { get; set; }
        public BladeLogoMode? NextModeRead { get; set; }
        public int PowerWriteCount { get; private set; }
        public int ModeWriteCount { get; private set; }
        public List<byte> Commands { get; } = [];
        public List<(byte CommandId, byte Value)> Writes { get; } = [];

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
            var requestArguments = arguments.ToArray();
            Commands.Add(commandId);
            switch (commandId)
            {
                case 0x00:
                    Writes.Add((commandId, requestArguments[2]));
                    PowerWriteCount++;
                    Powered = requestArguments[2] == 0x01;
                    break;
                case 0x02:
                    Writes.Add((commandId, requestArguments[2]));
                    ModeWriteCount++;
                    if (FailNextModeWrite)
                    {
                        FailNextModeWrite = false;
                        throw new InvalidOperationException("Simulated mode write failure.");
                    }
                    Mode = requestArguments[2] == 0x02 ? BladeLogoMode.Breathing : BladeLogoMode.Static;
                    break;
                case 0x80:
                    requestArguments[2] = Powered ? (byte)0x01 : (byte)0x00;
                    break;
                case 0x82:
                    var readMode = NextModeRead ?? Mode;
                    NextModeRead = null;
                    requestArguments[2] = readMode == BladeLogoMode.Breathing ? (byte)0x02 : (byte)0x00;
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected Logo command {commandId:X2}.");
            }

            var response = RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, requestArguments);
            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }
}
