using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ViperMappingActionsTests
{
    private static readonly ViperButtonAssignment Source =
        new(1, 1, ViperButtonMappingLayer.Normal, ViperButtonMappingFunction.Off, []);

    [Fact]
    public void MouseButtonsSixThroughEightUseTheOneByteButtonCode()
    {
        foreach (var button in new byte[] { 6, 7, 8 })
        {
            Assert.True(ViperMappingActions.TryApply(
                Source, Index(ViperButtonMappingFunction.MouseButton, button),
                0, 4, 800, 800, 7, 1, out var result));
            Assert.Equal(ViperButtonMappingFunction.MouseButton, result.Function);
            Assert.Equal([button], result.FunctionData);
        }
    }

    [Fact]
    public void DpiClutchUsesTheFiveByteXYPayload()
    {
        Assert.True(ViperMappingActions.TryApply(
            Source, Index(ViperButtonMappingFunction.Dpi, usesClutch: true),
            0, 4, 800, 1600, 7, 1, out var result));
        Assert.Equal(ViperButtonMappingFunction.Dpi, result.Function);
        Assert.Equal([0x05, 0x03, 0x20, 0x06, 0x40], result.FunctionData);
        Assert.Equal(
            Index(ViperButtonMappingFunction.Dpi, usesClutch: true),
            ViperMappingActions.IndexOf(result));
    }

    [Fact]
    public void HyperShiftToggleUsesTheModeButtonFunction()
    {
        Assert.True(ViperMappingActions.TryApply(
            Source, Index(ViperButtonMappingFunction.HyperShift), 0, 4, 800, 800, 7, 1, out var result));
        Assert.Equal(ViperButtonMappingFunction.HyperShift, result.Function);
        Assert.Equal([0x01], result.FunctionData);
    }

    [Fact]
    public void TurboKeyCarriesTheKeyboardPairAndInterval()
    {
        Assert.True(ViperMappingActions.TryApply(
            Source, Index(ViperButtonMappingFunction.KeyboardTurbo),
            1, 4, 800, 800, 7, 1, out var result));
        Assert.Equal(ViperButtonMappingFunction.KeyboardTurbo, result.Function);
        Assert.Equal([0x01, 0x04, 0x00, 0x8E], result.FunctionData);
    }

    [Fact]
    public void TurboButtonCarriesTheButtonAndInterval()
    {
        Assert.True(ViperMappingActions.TryApply(
            Source, Index(ViperButtonMappingFunction.MouseTurbo),
            0, 4, 800, 800, 10, 5, out var result));
        Assert.Equal(ViperButtonMappingFunction.MouseTurbo, result.Function);
        Assert.Equal([0x05, 0x00, 0x64], result.FunctionData);
    }

    [Theory]
    [InlineData("ViperMappingMediaEject", 0x00B8)]
    [InlineData("ViperMappingMediaCycleApps", 0x029F)]
    [InlineData("ViperMappingMediaBrightnessUp", 0x006F)]
    [InlineData("ViperMappingMediaBrightnessDown", 0x0070)]
    [InlineData("ViperMappingMediaSpotlight", 0x0221)]
    public void ExtendedMediaKeysUseTheTwoByteUsage(string nameKey, int code)
    {
        var index = IndexByName(nameKey);
        Assert.True(ViperMappingActions.TryApply(
            Source, index, 0, 4, 800, 800, 7, 1, out var result));
        Assert.Equal(ViperButtonMappingFunction.MediaKey, result.Function);
        Assert.Equal([(byte)(code >> 8), (byte)code], result.FunctionData);
        Assert.Equal(index, ViperMappingActions.IndexOf(result));
    }

    [Theory]
    [InlineData(ViperButtonMappingFunction.Profile, 4)]
    [InlineData(ViperButtonMappingFunction.Power, 130)]
    [InlineData(ViperButtonMappingFunction.ScrollWheelMode, 4)]
    [InlineData(ViperButtonMappingFunction.RazerKey, 216)]
    [InlineData(ViperButtonMappingFunction.WindowsShortcut, 99)]
    public void SimpleFunctionFamiliesUseTheOneByteCode(ViperButtonMappingFunction function, byte value)
    {
        var index = Index(function, value);
        Assert.True(ViperMappingActions.TryApply(
            Source, index, 0, 4, 800, 800, 7, 1, out var result));
        Assert.Equal(function, result.Function);
        Assert.Equal([value], result.FunctionData);
        Assert.Equal(index, ViperMappingActions.IndexOf(result));
    }

    [Fact]
    public void SpecificProfileCarriesTheProfileId()
    {
        var index = IndexByName("ViperMappingProfileSpecific");

        Assert.True(ViperMappingActions.TryApply(
            Source, index, 0, 4, 800, 800, 7, 1, out var result, profileId: 3));
        Assert.Equal(ViperButtonMappingFunction.Profile, result.Function);
        Assert.Equal([0x03, 0x03], result.FunctionData);
        Assert.Equal(index, ViperMappingActions.IndexOf(result));
    }

    [Theory]
    [InlineData(ViperObmFunctionId.Profile, new byte[] { 3, 1 })]
    [InlineData(ViperObmFunctionId.PowerKeys, new byte[] { 130 })]
    [InlineData(ViperObmFunctionId.RazerKey, new byte[] { 216 })]
    [InlineData(ViperObmFunctionId.ScrollWheelMode, new byte[] { 4 })]
    [InlineData(ViperObmFunctionId.WindowsShortcutsKey, new byte[] { 99 })]
    [InlineData(ViperObmFunctionId.MacroTypeI, new byte[] { 0, 1, 2 })]
    [InlineData(ViperObmFunctionId.MacroTypeII, new byte[] { 0, 1 })]
    public void Product184ValidatorAcceptsTheBundleAlignedFunctions(
        ViperObmFunctionId function,
        byte[] data)
    {
        var request = ViperObmProtocol.CreateSetAssignmentRequest(
            new ViperObmAssignment(1, 1, ViperObmMappingMode.Normal, function, data));
        Assert.Equal((byte)function, request[RazerFeatureReport.ArgumentsOffset + 3]);
        Assert.Equal((byte)data.Length, request[RazerFeatureReport.ArgumentsOffset + 4]);
    }

    private static int Index(ViperButtonMappingFunction function) =>
        ViperMappingActions.Definitions
            .Select((definition, index) => (definition, index))
            .First(item => item.definition.Function == function)
            .index;

    private static int Index(ViperButtonMappingFunction function, byte value) =>
        ViperMappingActions.Definitions
            .Select((definition, index) => (definition, index))
            .First(item => item.definition.Function == function && item.definition.Value == value)
            .index;

    private static int Index(ViperButtonMappingFunction function, bool usesClutch) =>
        ViperMappingActions.Definitions
            .Select((definition, index) => (definition, index))
            .First(item => item.definition.Function == function &&
                item.definition.UsesDpiClutch == usesClutch)
            .index;

    private static int IndexByName(string nameKey) =>
        ViperMappingActions.Definitions
            .Select((definition, index) => (definition, index))
            .First(item => item.definition.NameKey == nameKey)
            .index;
}
