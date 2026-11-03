using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed partial class RazerChromaStandardTests
{
    [Theory]
    [InlineData(RazerChromaEffect.Off)]
    [InlineData(RazerChromaEffect.Spectrum)]
    public async Task ColorlessUpdateAcceptsResidualFirmwareRgb(RazerChromaEffect target)
    {
        var simulator = new ChromaSimulator
        {
            Effect = RazerChromaEffect.Static, Colors = [1, 2, 3],
        };
        var transport = new FakeTransport(request =>
        {
            var previousColors = simulator.Colors;
            var response = simulator.Handle(request);
            if (request[8] == 0x03 && request[12] == 0) simulator.Colors = previousColors;
            return response;
        });
        var service = new RazerChromaStandardService(transport, "test", TimeSpan.Zero);

        await service.SetEffectAsync(target, []);

        Assert.Equal(target, simulator.Effect);
        Assert.Equal(new byte[] { 1, 2, 3 }, simulator.Colors);
        Assert.Single(transport.Requests, request => request[8] == 0x03);
        Assert.Equal(0x83, transport.Requests[^1][8]);
    }

    [Fact]
    public async Task RestoreOfColorlessPreviousEffectStillRequiresItsCompleteRgbSnapshot()
    {
        var simulator = new ChromaSimulator
        {
            Effect = RazerChromaEffect.Spectrum, Colors = [1, 2, 3], IgnoreWrites = true,
        };
        var writes = 0;
        var transport = new FakeTransport(request =>
        {
            var response = simulator.Handle(request);
            if (request[8] == 0x03 && ++writes == 2) simulator.Colors = [4, 5, 6];
            return response;
        });
        var service = new RazerChromaStandardService(transport, "test", TimeSpan.Zero);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetEffectAsync(RazerChromaEffect.Static, [7, 8, 9]));

        Assert.Contains("could not be restored", error.Message);
        Assert.IsType<AggregateException>(error.InnerException);
        Assert.Equal(2, writes);
    }
}
