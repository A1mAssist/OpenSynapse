using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// The shared mapping row keeps the protocol-independent editor. A catalog supplies the
/// action vocabulary for one transport family; it never owns rows or read/write state.
/// </summary>
public interface IRazerMappingActionCatalog
{
    bool SupportsGroup(RazerHidMappingGroup group) => true;

    IReadOnlyList<string> OptionNames(
        RazerHidMappingGroup group,
        IReadOnlyList<RazerHidMacroOption>? macros = null);

    RazerHidMappingAction? CreateAction(
        RazerHidMappingGroup group,
        int optionIndex,
        byte modifierMask = 0,
        IReadOnlyList<RazerHidMacroOption>? macros = null,
        int turboRate = 7,
        int profileId = 1,
        int dpiClutchX = 800,
        int dpiClutchY = 800,
        RazerHidMacroPlayback macroPlayback = RazerHidMacroPlayback.Once,
        int macroRepeatCount = 1);

    string Describe(
        byte functionId,
        IReadOnlyList<byte> data,
        IReadOnlyList<RazerHidMacroOption>? macros = null);

    RazerHidDecodedMapping? Decode(byte functionId, IReadOnlyList<byte> data) =>
        RazerHidMappingActions.Decode(functionId, data);

    string DescribeButton(byte buttonId);
}

/// <summary>The middleware 0x02 vocabulary shared by HID mice and keyboards.</summary>
public class RazerHidMappingActionCatalog : IRazerMappingActionCatalog
{
    public static RazerHidMappingActionCatalog Instance { get; } = new();

    public IReadOnlyList<string> OptionNames(
        RazerHidMappingGroup group,
        IReadOnlyList<RazerHidMacroOption>? macros = null)
    {
        var names = RazerHidMappingActions.OptionNames(group, macros);
        return group == RazerHidMappingGroup.Macro
            ? names
            : names.Select(RazerMappingActionNames.Localize).ToArray();
    }

    public RazerHidMappingAction? CreateAction(
        RazerHidMappingGroup group,
        int optionIndex,
        byte modifierMask = 0,
        IReadOnlyList<RazerHidMacroOption>? macros = null,
        int turboRate = 7,
        int profileId = 1,
        int dpiClutchX = 800,
        int dpiClutchY = 800,
        RazerHidMacroPlayback macroPlayback = RazerHidMacroPlayback.Once,
        int macroRepeatCount = 1) =>
        RazerHidMappingActions.CreateAction(
            group, optionIndex, modifierMask, macros, turboRate, profileId,
            dpiClutchX, dpiClutchY, macroPlayback, macroRepeatCount);

    public string Describe(
        byte functionId,
        IReadOnlyList<byte> data,
        IReadOnlyList<RazerHidMacroOption>? macros = null) =>
        RazerHidMappingActions.Describe(functionId, data, macros);

    public virtual string DescribeButton(byte buttonId) =>
        RazerHidMappingActions.DescribeButton(buttonId);
}

/// <summary>
/// Keyboard button lists identify physical keys, not mouse buttons. Keep the shared
/// action names but avoid telling the user that key 4 is a mouse Back button.
/// </summary>
public class RazerKeyboardMappingActionCatalog : RazerHidMappingActionCatalog
{
    private readonly ushort _productId;

    public RazerKeyboardMappingActionCatalog(ushort productId = 0) => _productId = productId;

    public static new RazerKeyboardMappingActionCatalog Instance { get; } = new();

    public override string DescribeButton(byte buttonId) =>
        RazerKeyboardExtraKeyCatalog.DescribeResourceKey(_productId, buttonId) is { } key
            ? AppStrings.Text(key)
            // Firmware-key-id keyboards use 1..15 and 0x60.. for real keys, so the
            // reverse HID map names them instead of the mouse button table.
            : RazerHuntsmanV3ProKeys.UsesFirmwareKeyIds(_productId) &&
              RazerHuntsmanV3ProKeys.TryGetHidUsage(buttonId, out var hid) &&
              RazerHidKeyNames.Describe(hid) is { } label
                ? label
                : AppStrings.FormatText("RazerMappingKeyboardButton", buttonId);
}

/// <summary>
/// Keypads expose their main pad, thumb pad, wheel and D-pad as firmware ids.
/// Name the rows by what is printed on the device instead of "Button 44".
/// </summary>
public sealed class RazerKeypadMappingActionCatalog : RazerKeyboardMappingActionCatalog
{
    public static new RazerKeypadMappingActionCatalog Instance { get; } = new();

    private readonly ushort _productId;

    public RazerKeypadMappingActionCatalog(ushort productId = 0) : base(productId) => _productId = productId;

    public override string DescribeButton(byte buttonId) => _productId == 0x02EB
        ? buttonId == 0xDB ? AppStrings.Text("RazerMappingKeypadStickClick") : base.DescribeButton(buttonId)
        : buttonId switch
    {
        0x03 => AppStrings.Text("RazerMappingKeypadScrollClick"),
        0x09 => AppStrings.Text("RazerMappingButtonScrollUp"),
        0x0A => AppStrings.Text("RazerMappingButtonScrollDown"),
        0x2C => AppStrings.Text("RazerMappingKeypadDpadUp"),
        0x2D => AppStrings.Text("RazerMappingKeypadDpadDown"),
        0x2E => AppStrings.Text("RazerMappingKeypadDpadLeft"),
        0x2F => AppStrings.Text("RazerMappingKeypadDpadRight"),
        0xF0 => AppStrings.Text("RazerMappingKeypadDpadUp"),
        0xF1 => AppStrings.Text("RazerMappingKeypadDpadDown"),
        0xF2 => AppStrings.Text("RazerMappingKeypadDpadLeft"),
        0xF3 => AppStrings.Text("RazerMappingKeypadDpadRight"),
        0xF4 => AppStrings.Text("RazerMappingKeypadDpadCenter"),
        0xF5 => AppStrings.Text("RazerMappingKeypadDpadUpLeft"),
        0xF6 => AppStrings.Text("RazerMappingKeypadDpadUpRight"),
        0xF7 => AppStrings.Text("RazerMappingKeypadDpadDownLeft"),
        0xF8 => AppStrings.Text("RazerMappingKeypadDpadDownRight"),
        0xE0 => "Alt",
        0xE1 => AppStrings.Text("RazerMappingKeypadSpace"),
        0xE3 => AppStrings.Text("RazerMappingKeypadScrollClick"),
        0x3B => "Fn",
        0x3C => "Alt",
        0xD9 => "Profile Cycle",
        0xDA => "Keymap Cycle",
        0xDB => "Right Stick Button",
        >= 0x40 and <= 0x4B => AppStrings.FormatText("RazerMappingKeyboardButton", buttonId - 0x3F),
        >= 0x50 and <= 0x56 => AppStrings.FormatText("RazerMappingKeypadThumbKey", buttonId - 0x4F),
        0x60 => AppStrings.FormatText("RazerMappingKeypadThumbKey", 8),
        _ => base.DescribeButton(buttonId),
    };
}

/// <summary>
/// Names Naga firmware rows by their plate; output keyboard usages are unrelated.
/// </summary>
public sealed class RazerNagaMappingActionCatalog : RazerHidMappingActionCatalog
{
    private readonly ushort _productId;

    public RazerNagaMappingActionCatalog(ushort productId = 0) => _productId = productId;

    public static new RazerNagaMappingActionCatalog Instance { get; } = new();

    public override string DescribeButton(byte buttonId)
    {
        var panelSize = RazerNagaSideButtonResolver.GetPanelButtonCount(_productId, buttonId);
        if (panelSize > 0)
            return AppStrings.FormatText("RazerMappingNagaPanelButton", panelSize,
                panelSize == 12 ? buttonId - 63 : buttonId - 79);
        return RazerSpecialKeyMap.TryResolve(_productId, buttonId, out var keyCode)
            ? RazerPrivateKeyCatalog.Describe(_productId, keyCode)
            : base.DescribeButton(buttonId);
    }
}

/// <summary>
/// Product 184 accepts the same function ids but exposes a narrower, bundle-verified set
/// of mouse actions. This adapter keeps the shared row model while preserving that set.
/// </summary>
public sealed class RazerViperMappingActionCatalog : IRazerMappingActionCatalog
{
    public static RazerViperMappingActionCatalog Instance { get; } = new();

    private static readonly (ViperMappingActionDefinition Definition, int Index)[] Definitions =
        ViperMappingActions.Definitions
            .Select((definition, index) => (definition, index))
            .ToArray();

    public IReadOnlyList<string> OptionNames(
        RazerHidMappingGroup group,
        IReadOnlyList<RazerHidMacroOption>? macros = null)
    {
        var names = group switch
        {
            RazerHidMappingGroup.Keep => [],
            RazerHidMappingGroup.Macro => RazerHidMappingActions.OptionNames(group, macros),
            RazerHidMappingGroup.KeyboardKey or RazerHidMappingGroup.TurboKey =>
                RazerHidMappingActions.OptionNames(group),
            RazerHidMappingGroup.TurboButton => TurboButtonNames(),
            _ => ForGroup(group).Select(item => Name(item.Definition)).ToArray(),
        };
        return group == RazerHidMappingGroup.Macro
            ? names
            : names.Select(RazerMappingActionNames.Localize).ToArray();
    }

    public RazerHidMappingAction? CreateAction(
        RazerHidMappingGroup group,
        int optionIndex,
        byte modifierMask = 0,
        IReadOnlyList<RazerHidMacroOption>? macros = null,
        int turboRate = 7,
        int profileId = 1,
        int dpiClutchX = 800,
        int dpiClutchY = 800,
        RazerHidMacroPlayback macroPlayback = RazerHidMacroPlayback.Once,
        int macroRepeatCount = 1)
    {
        if (group == RazerHidMappingGroup.Keep)
        {
            return null;
        }

        if (group == RazerHidMappingGroup.Macro)
        {
            return RazerHidMappingActions.CreateAction(
                group, optionIndex, modifierMask, macros, turboRate, profileId,
                dpiClutchX, dpiClutchY, macroPlayback, macroRepeatCount);
        }

        if (group is RazerHidMappingGroup.KeyboardKey or RazerHidMappingGroup.TurboKey)
        {
            return RazerHidMappingActions.CreateAction(
                group, optionIndex, modifierMask, null, turboRate);
        }

        if (group == RazerHidMappingGroup.TurboButton)
        {
            if (optionIndex < 0 || optionIndex >= TurboButtons.Length)
            {
                return null;
            }

            var button = TurboButtons[optionIndex];
            var interval = Interval(turboRate);
            return new(
                $"Turbo {RazerHidMappingActions.DescribeButton(button)}",
                RazerHidMappingFunctions.TurboModeButton,
                [button, High(interval), Low(interval)]);
        }

        var matches = ForGroup(group).ToArray();
        if (optionIndex < 0 || optionIndex >= matches.Length)
        {
            return null;
        }

        var item = matches[optionIndex];
        var source = new ViperButtonAssignment(
            1, 1, ViperButtonMappingLayer.Normal, ViperButtonMappingFunction.Off, []);
        if (!ViperMappingActions.TryApply(
            source, item.Index, modifierMask, 4, dpiClutchX, dpiClutchY,
            turboRate, 1, out var result, profileId))
        {
            return null;
        }

        return new(Name(item.Definition), (byte)result.Function, result.FunctionData.ToArray());
    }

    public string Describe(
        byte functionId,
        IReadOnlyList<byte> data,
        IReadOnlyList<RazerHidMacroOption>? macros = null) =>
        RazerHidMappingActions.Describe(functionId, data, macros);

    public string DescribeButton(byte buttonId) =>
        RazerPrivateKeyCatalog.IsPrivate(0x00B8, buttonId)
            ? RazerPrivateKeyCatalog.Describe(0x00B8, buttonId)
            : RazerHidMappingActions.DescribeButton(buttonId);

    private static readonly byte[] TurboButtons = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    private static IEnumerable<(ViperMappingActionDefinition Definition, int Index)> ForGroup(
        RazerHidMappingGroup group) =>
        Definitions.Where(item => MapGroup(item.Definition) == group);

    private static RazerHidMappingGroup? MapGroup(ViperMappingActionDefinition definition) =>
        definition.Group switch
        {
            ViperMappingActionGroup.Mouse when definition.Function == ViperButtonMappingFunction.Off =>
                RazerHidMappingGroup.Off,
            ViperMappingActionGroup.Mouse when definition.Function == ViperButtonMappingFunction.MouseButton =>
                RazerHidMappingGroup.MouseButton,
            ViperMappingActionGroup.Mouse when definition.Function == ViperButtonMappingFunction.DoubleClick =>
                RazerHidMappingGroup.DoubleClick,
            ViperMappingActionGroup.Keyboard => RazerHidMappingGroup.KeyboardKey,
            ViperMappingActionGroup.Dpi when definition.UsesDpiClutch => RazerHidMappingGroup.DpiClutch,
            ViperMappingActionGroup.Dpi => RazerHidMappingGroup.Dpi,
            ViperMappingActionGroup.Profile when definition.UsesProfileId => RazerHidMappingGroup.ProfileSpecific,
            ViperMappingActionGroup.Profile => RazerHidMappingGroup.Profile,
            ViperMappingActionGroup.Media => RazerHidMappingGroup.MediaKey,
            ViperMappingActionGroup.Power => RazerHidMappingGroup.PowerKey,
            ViperMappingActionGroup.Scroll => RazerHidMappingGroup.ScrollWheelMode,
            ViperMappingActionGroup.RazerKey => RazerHidMappingGroup.RazerKey,
            ViperMappingActionGroup.WindowsShortcut => RazerHidMappingGroup.WindowsShortcut,
            ViperMappingActionGroup.HyperShift => RazerHidMappingGroup.HyperShiftToggle,
            ViperMappingActionGroup.Turbo when definition.Function == ViperButtonMappingFunction.KeyboardTurbo =>
                RazerHidMappingGroup.TurboKey,
            ViperMappingActionGroup.Turbo when definition.Function == ViperButtonMappingFunction.MouseTurbo =>
                RazerHidMappingGroup.TurboButton,
            ViperMappingActionGroup.Perfect180 => RazerHidMappingGroup.MouseButton,
            _ => null,
        };

    private static string Name(ViperMappingActionDefinition definition) =>
        definition.Function == ViperButtonMappingFunction.MouseButton
            ? RazerMappingActionNames.Localize(RazerHidMappingActions.DescribeButton(definition.Value))
            : AppStrings.Text(definition.NameKey);

    private static IReadOnlyList<string> TurboButtonNames() =>
        TurboButtons
            .Select(button => RazerMappingActionNames.Localize(
                $"Turbo {RazerHidMappingActions.DescribeButton(button)}"))
            .ToArray();

    private static ushort Interval(int keysPerSecond)
    {
        var rate = Math.Clamp(keysPerSecond, 1, 50);
        return (ushort)Math.Clamp(1000 / rate, 1, ushort.MaxValue);
    }

    private static byte High(int value) => (byte)(value >> 8);
    private static byte Low(int value) => (byte)value;
}
