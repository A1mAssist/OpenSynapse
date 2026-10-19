using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Locks the Blade 16 2023 lighting bytes to the captured upstream exchanges and guards
/// the model-specific admission boundary against the matrix generation.
/// </summary>
public sealed class BladeLighting2023Tests
{
    [Fact]
    public void ManifestDeclaresTheSelectorPrefixedLightingReports()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x029F);

        Assert.NotNull(manifest);
        Assert.Equal("blade-671", manifest.ProtocolFamily);

        var brightnessGet = manifest.GetRequiredCapability("keyboard-brightness.get");
        Assert.Equal(0x03, brightnessGet.TransactionId);
        Assert.Equal(0x03, brightnessGet.MaximumDataSize);
        Assert.Equal(0x03, brightnessGet.CommandClass);
        Assert.Equal(0x83, brightnessGet.CommandId);

        // The set report uses its own command id; it must not reuse the query id.
        var brightnessSet = manifest.GetRequiredCapability("keyboard-brightness.set");
        Assert.Equal(0x03, brightnessSet.MaximumDataSize);
        Assert.Equal(0x03, brightnessSet.CommandClass);
        Assert.Equal(0x03, brightnessSet.CommandId);

        var effectGet = manifest.GetRequiredCapability("keyboard-effect.get");
        Assert.Equal(0x0F, effectGet.CommandClass);
        Assert.Equal(0x82, effectGet.CommandId);
        Assert.Equal(0x0F, manifest.GetRequiredCapability("keyboard-effect.set").CommandClass);
        Assert.Equal(0x02, manifest.GetRequiredCapability("keyboard-effect.set").CommandId);
    }

    [Fact]
    public void BrightnessRequestsCarryTheStoreAndRegionSelector()
    {
        var get = BladeLighting2023Protocol.CreateGetBrightnessRequest();
        Assert.Equal(0x83, get[8]);
        Assert.Equal(0x03, get[6]);
        Assert.Equal([0x01, 0x05, 0x00], get[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 3)]);

        var set = BladeLighting2023Protocol.CreateSetBrightnessRequest(0xC5);
        Assert.Equal(0x03, set[8]);
        Assert.Equal(0x03, set[6]);
        Assert.Equal([0x01, 0x05, 0xC5], set[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 3)]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(set), set[89]);
    }

    [Theory]
    [InlineData(0x00, 3)]
    [InlineData(0x02, 3)]
    [InlineData(0x03, 3)]
    [InlineData(0x04, 4)]
    public void EffectRequestsPrefixTheEffectWithItsOwnParameters(byte effect, int expectedLength)
    {
        var request = BladeLighting2023Protocol.CreateSetEffectRequest(effect, effect == 0x04 ? [0x01] : []);

        Assert.Equal(0x0F, request[7]);
        Assert.Equal(0x02, request[8]);
        Assert.Equal(expectedLength, request[6]);
        Assert.Equal(effect, request[RazerFeatureReport.ArgumentsOffset + 2]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void WaveKeepsItsFixedRightwardDirectionByte()
    {
        var wave = BladeLighting2023Protocol.CreateWaveRequest();

        Assert.Equal(4, wave[6]);
        Assert.Equal(0x01, wave[RazerFeatureReport.ArgumentsOffset + 3]);
    }

    [Fact]
    public void BrightnessParsesFromTheSelectorPayload()
    {
        var response = BuildResponse(BladeLighting2023Protocol.CreateGetBrightnessRequest(), [0x01, 0x05, 0xC5]);

        Assert.Equal(0xC5, BladeLighting2023Protocol.ParseBrightness(response));
    }

    [Theory]
    [InlineData(0x00, BladeLighting2023Effect.Off)]
    [InlineData(0x02, BladeLighting2023Effect.Breathing)]
    [InlineData(0x03, BladeLighting2023Effect.Spectrum)]
    [InlineData(0x04, BladeLighting2023Effect.Wave)]
    public void EffectParsesEveryAdmittedWireValue(byte wire, BladeLighting2023Effect expected)
    {
        var request = BladeLighting2023Protocol.CreateGetEffectRequest();
        var response = BuildResponse(request, wire == 0x04 ? [0x01, 0x05, 0x04, 0x01] : [0x01, 0x05, wire]);

        Assert.Equal(expected, BladeLighting2023Protocol.ParseEffect(response));
    }

    [Fact]
    public void UnknownEffectValueIsRejected()
    {
        var response = BuildResponse(BladeLighting2023Protocol.CreateGetEffectRequest(), [0x01, 0x05, 0x07]);

        Assert.Throws<InvalidOperationException>(() => BladeLighting2023Protocol.ParseEffect(response));
    }

    [Fact]
    public void WrongSelectorIsRejected()
    {
        var brightness = BuildResponse(BladeLighting2023Protocol.CreateGetBrightnessRequest(), [0x02, 0x05, 0xC5]);
        var effect = BuildResponse(BladeLighting2023Protocol.CreateGetEffectRequest(), [0x01, 0x04, 0x02]);

        Assert.Throws<InvalidOperationException>(() => BladeLighting2023Protocol.ParseBrightness(brightness));
        Assert.Throws<InvalidOperationException>(() => BladeLighting2023Protocol.ParseEffect(effect));
    }

    [Fact]
    public void OutOfOrderResponseIsRejected()
    {
        var response = BuildResponse(BladeLighting2023Protocol.CreateGetBrightnessRequest(), [0x01, 0x05, 0xC5]);
        response[2] = (byte)(response[2] + 1);

        Assert.Throws<InvalidOperationException>(() => BladeLighting2023Protocol.ParseBrightness(response));
    }

    [Fact]
    public void AdmittedEffectsExcludeEverythingTheMatrixGenerationOffers()
    {
        Assert.True(BladeLighting2023Admitted.Allows(BladeLightingMode.Off));
        Assert.True(BladeLighting2023Admitted.Allows(BladeLightingMode.Wave));

        // No matrix, no color readback: these stay closed on this generation.
        Assert.False(BladeLighting2023Admitted.Allows(BladeLightingMode.Static));
        Assert.False(BladeLighting2023Admitted.Allows(BladeLightingMode.AudioMeter));
        Assert.False(BladeLighting2023Admitted.Allows(BladeLightingMode.Ambient));
        Assert.False(BladeLighting2023Admitted.Allows(BladeLightingMode.ChromaFile));
        Assert.False(BladeLighting2023Admitted.Allows(BladeLightingMode.Tidal));
    }

    [Fact]
    public void UnadmittedModeHasNoWireRepresentation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BladeLighting2023Admitted.ToWireEffect(BladeLightingMode.Reactive));
    }

    /// <summary>
    /// The 2023 selector renders a short firmware list. Indexing the full catalog against
    /// it would map the same index onto a different effect, so the lists stay disjoint
    /// beyond the effects this model genuinely shares.
    /// </summary>
    [Fact]
    public void AdmittedEffectListIsOrderedForTheShortSelector()
    {
        Assert.Equal(
            [
                BladeLightingMode.Off,
                BladeLightingMode.Breathing,
                BladeLightingMode.Spectrum,
                BladeLightingMode.Wave,
            ],
            BladeLighting2023Admitted.Effects);

        // The matrix generation still owns every effect the short list omits.
        foreach (var matrixOnly in new[]
        {
            BladeLightingMode.Static,
            BladeLightingMode.AudioMeter,
            BladeLightingMode.Tidal,
        })
        {
            Assert.DoesNotContain(matrixOnly, BladeLighting2023Admitted.Effects);
        }
    }

    private static byte[] BuildResponse(byte[] request, byte[] payload)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = request[2];
        response[3] = request[3];
        response[4] = request[4];
        response[6] = request[6];
        response[7] = request[7];
        response[8] = request[8];
        payload.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}