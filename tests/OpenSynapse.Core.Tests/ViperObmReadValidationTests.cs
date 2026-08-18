using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class ViperObmReadValidationTests
{
    [Fact]
    public async Task ReadsEveryProfileButtonAndLayerFromDeviceMetadata()
    {
        var transport = new ObmTransport();

        var snapshot = await ViperObmReadValidation.ReadAsync(transport, "viper");

        Assert.Equal(5, snapshot.MaximumProfiles);
        Assert.Equal([1, 2], snapshot.ProfileIds);
        Assert.Equal([1, 5], snapshot.ButtonIds);
        Assert.Equal(8, snapshot.Assignments.Count);
        Assert.Equal(12, transport.QueryCount);
        Assert.All(
            snapshot.Assignments.Where(item => item.Mode == ViperObmMappingMode.Normal),
            item => Assert.Equal(ViperObmFunctionId.ButtonCode, item.Function));
        Assert.All(
            snapshot.Assignments.Where(item => item.Mode == ViperObmMappingMode.HyperShift),
            item => Assert.Equal(ViperObmFunctionId.KeyCode, item.Function));
    }

    private sealed class ObmTransport : IRazerFeatureTransport
    {
        public int QueryCount { get; private set; }

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
            QueryCount++;
            byte[] responseArguments = (commandClass, commandId) switch
            {
                (0x05, 0x8A) => [5],
                (0x05, 0x80) => [2],
                (0x05, 0x81) => [2, 1, 2],
                (0x02, 0x84) => [2, 1, 5],
                (0x02, 0x8C) => arguments.Span[2] == 0
                    ?
                    [
                        arguments.Span[0], arguments.Span[1], arguments.Span[2],
                        (byte)ViperObmFunctionId.ButtonCode, 1, arguments.Span[1],
                    ]
                    :
                    [
                        arguments.Span[0], arguments.Span[1], arguments.Span[2],
                        (byte)ViperObmFunctionId.KeyCode, 2, 0, arguments.Span[1],
                    ],
                _ => throw new InvalidOperationException(
                    $"Unexpected command {commandClass:X2}{commandId:X2}."),
            };

            var response = RazerFeatureReport.CreateRequest(
                transactionId,
                (commandClass, commandId) == (0x02, 0x8C) ? (byte)0x0A : dataSize,
                commandClass,
                commandId,
                responseArguments);
            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }
}
