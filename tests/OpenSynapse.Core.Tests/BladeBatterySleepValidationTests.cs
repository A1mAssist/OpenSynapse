using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeBatterySleepValidationTests
{
    [Fact]
    public async Task ReadsAllFourGetOnlyValuesAndSanitizesEnvelopes()
    {
        var transport = new PowerTransport();

        var result = await BladeBatterySleepValidation.ReadAsync(transport, "blade");

        Assert.Equal(50, result.BatteryPercent);
        Assert.Equal((byte)2, result.ChargingStatusRaw);
        Assert.Equal((byte)1, result.AutoSleepRaw);
        Assert.Equal(300, result.TimeToSleepSeconds);
        Assert.Equal(new byte[] { 0x80, 0x84, 0x88, 0x83 }, transport.Commands);
        Assert.All(result.Envelopes, envelope => Assert.Equal((byte)2, envelope.Status));
        Assert.DoesNotContain(result.Envelopes, envelope => envelope.Arguments.Contains("blade", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsExistingOrNonJsonOutput()
    {
        Assert.Throws<ArgumentException>(() =>
            BladeBatterySleepValidation.Options.Parse(["--blade-battery-sleep", "--output", "evidence.txt"]));

        var path = Path.GetTempFileName();
        try
        {
            Assert.Throws<ArgumentException>(() =>
                BladeBatterySleepValidation.Options.Parse(["--blade-battery-sleep", "--output", path]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class PowerTransport : IRazerFeatureTransport
    {
        public List<byte> Commands { get; } = [];

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan wait,
            CancellationToken cancellationToken = default,
            bool allowRemainingPacketsMismatch = false)
        {
            Commands.Add(commandId);
            byte[] responseArguments = commandId switch
            {
                0x80 => [0x00, 0x80],
                0x84 => [0x00, 0x02],
                0x88 => [0x00, 0x01],
                _ => [0x01, 0x2C],
            };
            var response = RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, responseArguments);
            response[1] = 0x02;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}
