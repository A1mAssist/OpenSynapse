using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerGamepadFunctionEncoderTests
{
    [Fact]
    public void ButtonAndKeyFunctionsMatchModule4442()
    {
        AssertFunction(
            RazerGamepadFunctionEncoder.Button(RazerGamepadButtonId.Button4),
            RazerGamepadFunction.ButtonCode, 1, 4);

        AssertFunction(
            RazerGamepadFunctionEncoder.Key(
                0x04, RazerGamepadModifiers.LeftCtrl | RazerGamepadModifiers.LeftShift),
            RazerGamepadFunction.KeyCode, 2, 3, 0x04);

        AssertFunction(
            RazerGamepadFunctionEncoder.Key(
                0x04, RazerGamepadModifiers.LeftCtrl | RazerGamepadModifiers.LeftShift, keysPerSecond: 7),
            RazerGamepadFunction.TurboModeKey, 4, 3, 0x04, 0x00, 0x8E);

        AssertFunction(
            RazerGamepadFunctionEncoder.Key(224, nKeyRollOver: true),
            RazerGamepadFunction.KeyCode, 2, 1, 0);
    }

    [Fact]
    public void MacroFunctionsUseTheMacroIdAndPlaybackByte()
    {
        AssertFunction(
            RazerGamepadFunctionEncoder.Macro(
                0x1234, RazerGamepadMacroPlayback.NTimes, 3),
            RazerGamepadFunction.MacroTypeI, 3, 0x12, 0x34, 3);

        AssertFunction(
            RazerGamepadFunctionEncoder.Macro(
                0x1234, RazerGamepadMacroPlayback.ContinuousHeld),
            RazerGamepadFunction.MacroTypeII, 2, 0x12, 0x34);
    }

    [Fact]
    public void DpiProfileAndScrollUseTheirActionBytes()
    {
        AssertFunction(
            RazerGamepadFunctionEncoder.Dpi(RazerGamepadDpiAction.Clutch, 800, 800),
            RazerGamepadFunction.Dpi, 5, 5, 0x03, 0x20, 0x03, 0x20);

        AssertFunction(
            RazerGamepadFunctionEncoder.Profile(RazerGamepadProfileAction.Specific, 3),
            RazerGamepadFunction.Profile, 2, 3, 3);

        AssertFunction(
            RazerGamepadFunctionEncoder.Scroll(RazerGamepadScrollAction.CycleUp),
            RazerGamepadFunction.ScrollWheelMode, 1, 4);
    }

    [Fact]
    public void MediaPowerAndWindowsKeysUseTheSharedTables()
    {
        AssertFunction(
            RazerGamepadFunctionEncoder.Multimedia(RazerGamepadMediaKey.VolumeUp),
            RazerGamepadFunction.MediaKeys, 2, 0x00, 0xE9);
        AssertFunction(
            RazerGamepadFunctionEncoder.Power(RazerGamepadPowerKey.Sleep),
            RazerGamepadFunction.PowerKeys, 1, 130);
        AssertFunction(
            RazerGamepadFunctionEncoder.WindowsShortcut(RazerGamepadWindowsShortcut.Screenshot),
            RazerGamepadFunction.Win8ShortcutsKey, 1, 99);
    }

    [Fact]
    public void ControllerRazerKeyAndTurboButtonUseOneByte()
    {
        AssertFunction(
            RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.M1),
            RazerGamepadFunction.Controller, 1, 16);
        AssertFunction(
            RazerGamepadFunctionEncoder.RazerKey(RazerGamepadRazerKey.HyperShift),
            RazerGamepadFunction.RazerKey, 1, 89);
        AssertFunction(
            RazerGamepadFunctionEncoder.TurboButton(RazerGamepadButtonId.Button4),
            RazerGamepadFunction.TurboModeButton, 3, 4, 0x00, 0x8E);
    }

    [Fact]
    public void OffDoubleClickAndModeButtonAreComplete()
    {
        AssertFunction(
            RazerGamepadFunctionEncoder.Off(),
            RazerGamepadFunction.Off, 0);
        AssertFunction(
            RazerGamepadFunctionEncoder.DoubleClick(),
            RazerGamepadFunction.DoubleClick, 1, 1);
        AssertFunction(
            RazerGamepadFunctionEncoder.ModeButton(),
            RazerGamepadFunction.ModeButtonKey, 1, 1);
    }

    private static void AssertFunction(
        RazerGamepadFunctionData data,
        RazerGamepadFunction function,
        byte size,
        params byte[] value)
    {
        Assert.Equal(function, data.Function);
        Assert.Equal(size, data.Size);
        Assert.Equal(value, data.Data[..size]);
    }
}
