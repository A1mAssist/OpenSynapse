using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Every parser that validates a selector argument must accept a reply built from the
/// request that carried it. 42007e8 shipped a Boost parser that demanded profile 1 while
/// the request sent 0; this table fails on that commit.
/// </summary>
public sealed class ProtocolSelectorContractTests
{
    [Fact]
    public void EverySelectorParserAcceptsTheEchoOfItsOwnRequest()
    {
        foreach (var (name, request, parse) in Cases())
        {
            try
            {
                parse(Echo(request));
            }
            catch (Exception exception)
            {
                Assert.Fail($"{name} rejected the echo of its own request: {exception.Message}");
            }
        }
    }

    private static IEnumerable<(string Name, byte[] Request, Func<byte[], object?> Parse)> Cases()
    {
        var cpu = BladeThermalProtocol.CpuFanId;
        yield return (
            "Blade advanced fan mode",
            BladeThermalProtocol.CreateGetAdvancedFanModeRequest(cpu),
            response => BladeThermalProtocol.ParseAdvancedFanMode(response, cpu));
        yield return (
            "Blade current fan speed",
            BladeThermalProtocol.CreateGetCurrentSpeedRequest(cpu),
            response => BladeThermalProtocol.ParseCurrentSpeedRpm(response, cpu));
        yield return (
            "Blade fan duty cycle",
            BladeThermalProtocol.CreateGetFanDutyCycleRequest(0x00, cpu),
            response => BladeThermalProtocol.ParseFanDutyCycle(response, 0x00, cpu));
        yield return (
            "Blade fan information",
            BladeThermalProtocol.CreateGetFanInformationRequest(),
            response => BladeThermalProtocol.ParseFanInformation(response));
        yield return (
            "Blade keyboard brightness",
            BladeLighting2023Protocol.CreateGetBrightnessRequest(),
            response => BladeLighting2023Protocol.ParseBrightness(response));
        yield return (
            "Blade keyboard effect",
            BladeLighting2023Protocol.CreateGetEffectRequest(),
            response => BladeLighting2023Protocol.ParseEffect(response));
        yield return (
            "Blade logo power",
            BladeLogoProtocol.CreateGetPowerRequest(),
            response => BladeLogoProtocol.ParsePower(response));
        yield return (
            "Blade logo mode",
            BladeLogoProtocol.CreateGetModeRequest(),
            response => BladeLogoProtocol.ParseMode(response));
        yield return (
            "Cooling pad speed",
            RazerCoolingPadProtocol.CreateGetSpeedRequest(),
            response => RazerCoolingPadProtocol.ParseSpeedRpm(response));
        yield return (
            "Cooling pad fan information",
            RazerCoolingPadProtocol.CreateGetFanInformationRequest(),
            response => RazerCoolingPadProtocol.ParseFanInformation(response));
        yield return (
            "Blade CPU Boost",
            BoostRequest(BladeBoostProtocol.CpuCluster),
            response => BladeBoostProtocol.ParseCpu(response, BladeBoostProtocol.ProfileId));
        yield return (
            "Blade GPU Boost",
            BoostRequest(BladeBoostProtocol.GpuCluster),
            response => BladeBoostProtocol.ParseGpu(response, BladeBoostProtocol.ProfileId));
    }

    private static byte[] BoostRequest(byte cluster) =>
        RazerFeatureReport.CreateRequest(
            BladeThermalProtocol.TransactionId,
            0x03,
            0x0D,
            0x87,
            BladeBoostProtocol.CreateGetArguments(cluster));

    /// <summary>Mirrors a successful device reply: the request's arguments plus status and CRC.</summary>
    private static byte[] Echo(byte[] request)
    {
        var response = (byte[])request.Clone();
        response[1] = 0x02;
        response[89] = 0;
        for (var index = 3; index <= 88; index++)
        {
            response[89] ^= response[index];
        }

        return response;
    }
}
