using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class ViperLowBatteryThresholdValidationSafetyTests
{
    [Fact]
    public async Task WritesTargetThenRestoresExactOriginalRawValue()
    {
        var transport = new ThresholdTransport(0x4D);

        var result = await ViperLowBatteryThresholdValidation.ExecuteAsync(
            transport, "viper", 0x4D, 35);

        Assert.Null(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.Equal((byte)0x4D, result.SameValueReadbackRaw);
        Assert.Equal(30, result.SameValueReadbackPercent);
        Assert.Equal((byte)0x5A, result.TargetReadbackRaw);
        Assert.Equal(35, result.TargetReadbackPercent);
        Assert.Equal((byte)0x4D, result.RestorationReadbackRaw);
        Assert.Equal(new byte[] { 0x4D, 0x5A, 0x4D }, transport.Writes);
    }

    [Fact]
    public async Task TargetReadbackMismatchStillRestoresOriginalValue()
    {
        var transport = new ThresholdTransport(0x4D) { IgnoreWriteNumber = 2 };

        var result = await ViperLowBatteryThresholdValidation.ExecuteAsync(
            transport, "viper", 0x4D, 35);

        Assert.Contains("目标阈值读回不一致", result.OperationError, StringComparison.Ordinal);
        Assert.Null(result.RestorationError);
        Assert.Equal((byte)0x4D, result.RestorationReadbackRaw);
        Assert.Equal(new byte[] { 0x4D, 0x5A, 0x4D }, transport.Writes);
    }

    [Fact]
    public async Task RefusesWriteWhenOriginalRawCannotBeExactlyRestored()
    {
        var transport = new ThresholdTransport(0x4C);

        var result = await ViperLowBatteryThresholdValidation.ExecuteAsync(
            transport, "viper", 0x4C, 35);

        Assert.Contains("原值或目标值不在官方可写集合内", result.OperationError, StringComparison.Ordinal);
        Assert.Null(result.RestorationError);
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task TargetWriteFailureStillAttemptsRestoration()
    {
        var transport = new ThresholdTransport(0x4D) { FailWriteNumber = 2 };

        var result = await ViperLowBatteryThresholdValidation.ExecuteAsync(
            transport, "viper", 0x4D, 35);

        Assert.Contains("Simulated target write failure", result.OperationError, StringComparison.Ordinal);
        Assert.Null(result.RestorationError);
        Assert.Equal((byte)0x4D, result.RestorationReadbackRaw);
        Assert.Equal(new byte[] { 0x4D, 0x5A, 0x4D }, transport.Writes);
    }

    private sealed class ThresholdTransport(byte initialRaw) : IRazerFeatureTransport
    {
        private byte _raw = initialRaw;

        public int? IgnoreWriteNumber { get; set; }
        public int? FailWriteNumber { get; set; }
        public List<byte> Writes { get; } = [];

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
            if (commandClass != 0x07 || commandId is not (0x01 or 0x81))
            {
                throw new InvalidOperationException($"Unexpected command {commandClass:X2}{commandId:X2}.");
            }

            if (commandId == 0x01)
            {
                var value = arguments.Span[0];
                Writes.Add(value);
                if (FailWriteNumber == Writes.Count)
                {
                    throw new InvalidOperationException("Simulated target write failure.");
                }
                if (IgnoreWriteNumber == Writes.Count)
                {
                }
                else
                {
                    _raw = value;
                }
            }

            var responseArguments = new byte[] { commandId == 0x81 ? _raw : arguments.Span[0] };
            var response = RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, responseArguments);
            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }
}
