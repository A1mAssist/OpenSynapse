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

    string DescribeButton(byte buttonId);
}

/// <summary>The middleware 0x02 vocabulary shared by HID mice and keyboards.</summary>
public class RazerHidMappingActionCatalog : IRazerMappingActionCatalog
{
    public static RazerHidMappingActionCatalog Instance { get; } = new();

    public IReadOnlyList<string> OptionNames(
        RazerHidMappingGroup group,
        IReadOnlyList<RazerHidMacroOption>? macros = null) =>
        RazerHidMappingActions.OptionNames(group, macros);

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
public sealed class RazerKeyboardMappingActionCatalog : RazerHidMappingActionCatalog
{
    public static new RazerKeyboardMappingActionCatalog Instance { get; } = new();

    public override string DescribeButton(byte buttonId) =>
        AppStrings.FormatText("RazerMappingKeyboardButton", buttonId);
}

/// <summary>
/// Naga side plates report the keyboard number row, not the 1-12 names the
/// mapping rows should show.
/// </summary>
public sealed class RazerNagaMappingActionCatalog : RazerHidMappingActionCatalog
{
    public static new RazerNagaMappingActionCatalog Instance { get; } = new();

    public override string DescribeButton(byte buttonId) =>
        RazerNagaSideButtonResolver.Resolve(buttonId) is { } input
            ? AppStrings.FormatText("RazerMappingNagaSideButton", input.SideButton)
            : base.DescribeButton(buttonId);
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
        IReadOnlyList<RazerHidMacroOption>? macros = null) => group switch
    {
        RazerHidMappingGroup.Keep or RazerHidMappingGroup.Macro => [],
        RazerHidMappingGroup.KeyboardKey or RazerHidMappingGroup.TurboKey =>
            RazerHidMappingActions.OptionNames(group),
        RazerHidMappingGroup.TurboButton => TurboButtonNames(),
        _ => ForGroup(group).Select(item => Name(item.Definition)).ToArray(),
    };

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
        if (group is RazerHidMappingGroup.Keep or RazerHidMappingGroup.Macro)
        {
            return null;
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
        RazerHidMappingActions.DescribeButton(buttonId);

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
            _ => null,
        };

    private static string Name(ViperMappingActionDefinition definition) =>
        definition.Function == ViperButtonMappingFunction.MouseButton
            ? RazerHidMappingActions.DescribeButton(definition.Value)
            : AppStrings.Text(definition.NameKey);

    private static IReadOnlyList<string> TurboButtonNames() =>
        TurboButtons
            .Select(button => $"Turbo {RazerHidMappingActions.DescribeButton(button)}")
            .ToArray();

    private static ushort Interval(int keysPerSecond)
    {
        var rate = Math.Clamp(keysPerSecond, 1, 50);
        return (ushort)Math.Clamp(1000 / rate, 1, ushort.MaxValue);
    }

    private static byte High(int value) => (byte)(value >> 8);
    private static byte Low(int value) => (byte)value;
}
