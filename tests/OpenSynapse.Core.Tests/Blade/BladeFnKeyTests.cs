using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeFnKeyTests
{
    [Fact]
    public void QueryUsesTheValidatedFnCommand()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6);
        Assert.NotNull(manifest);
        var getter = manifest.GetRequiredCapability("fn-key.get");
        Assert.Equal(0x02, getter.CommandClass);
        Assert.Equal(0x86, getter.CommandId);

        var request = BladeSynapsePolicyProtocol.CreateGetFnKeyStateRequest();
        Assert.Equal(0x02, request[6]);
        Assert.Equal(0x02, request[7]);
        Assert.Equal(0x86, request[8]);
        Assert.Equal(0x01, request[9]);
        Assert.Equal(0x00, request[10]);
    }

    [Theory]
    [InlineData((byte)0x00, false)]
    [InlineData((byte)0x01, true)]
    public void ResponseDecodesBothFnStates(byte state, bool multimediaPrimary)
    {
        var request = BladeSynapsePolicyProtocol.CreateGetFnKeyStateRequest();
        var response = (byte[])request.Clone();
        response[1] = 0x02;
        response[10] = state;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        var parsed = BladeSynapsePolicyProtocol.ParseFnKeyState(response, request);

        Assert.Equal(multimediaPrimary, parsed.MultiFunctionPrimary);
    }
}
