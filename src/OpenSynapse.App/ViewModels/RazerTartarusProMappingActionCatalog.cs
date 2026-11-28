using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>Tartarus V2 and Pro assignments use the host, without an onboard action table.</summary>
public sealed class RazerTartarusProMappingActionCatalog : IRazerMappingActionCatalog
{
    public static RazerTartarusProMappingActionCatalog Instance { get; } = new();
    private readonly ushort _productId;

    public RazerTartarusProMappingActionCatalog(ushort productId = 0x0244) => _productId = productId;

    public bool SupportsGroup(RazerHidMappingGroup group) => group is
        RazerHidMappingGroup.Keep or RazerHidMappingGroup.Off or
        RazerHidMappingGroup.MouseButton or RazerHidMappingGroup.KeyboardKey or
        RazerHidMappingGroup.MediaKey or RazerHidMappingGroup.DoubleClick or
        RazerHidMappingGroup.WindowsShortcut or RazerHidMappingGroup.HyperShiftToggle or
        RazerHidMappingGroup.Macro;

    public IReadOnlyList<string> OptionNames(
        RazerHidMappingGroup group, IReadOnlyList<RazerHidMacroOption>? macros = null)
    {
        var names = RazerHidMappingActionCatalog.Instance.OptionNames(group, macros);
        return group is RazerHidMappingGroup.WindowsShortcut or RazerHidMappingGroup.Macro
            ? names
            : OptionIndices(group).Select(index => names[index]).ToArray();
    }

    public RazerHidMappingAction? CreateAction(
        RazerHidMappingGroup group, int optionIndex, byte modifierMask = 0,
        IReadOnlyList<RazerHidMacroOption>? macros = null, int turboRate = 7,
        int profileId = 1, int dpiClutchX = 800, int dpiClutchY = 800,
        RazerHidMacroPlayback macroPlayback = RazerHidMacroPlayback.Once, int macroRepeatCount = 1)
    {
        if (!SupportsGroup(group) || group == RazerHidMappingGroup.Macro)
        {
            return null;
        }

        var indices = OptionIndices(group);
        if (optionIndex < 0 || optionIndex >= indices.Length)
        {
            return null;
        }

        return RazerHidMappingActions.CreateAction(group, indices[optionIndex], modifierMask);
    }

    public RazerHidDecodedMapping? Decode(byte functionId, IReadOnlyList<byte> data)
    {
        if (!RazerKeypadInputCatalog.IsSupportedAction(functionId, data) ||
            RazerHidMappingActions.Decode(functionId, data) is not { } decoded ||
            !SupportsGroup(decoded.Group))
        {
            return null;
        }

        var optionIndex = Array.IndexOf(OptionIndices(decoded.Group), decoded.OptionIndex);
        return optionIndex < 0 ? null : decoded with { OptionIndex = optionIndex };
    }

    public string Describe(byte functionId, IReadOnlyList<byte> data,
        IReadOnlyList<RazerHidMacroOption>? macros = null) =>
        RazerHidMappingActions.Describe(functionId, data, macros);

    public string DescribeButton(byte buttonId)
    {
        if (RazerKeypadInputCatalog.IsLegacy(_productId))
        {
            if (buttonId < 0xE0)
                return AppStrings.FormatText("RazerMappingKeyboardButton",
                    _productId is 0x0201 or 0x0208 ? buttonId - 5 : buttonId);
            if (buttonId is 0xE4 or 0xE5)
                return AppStrings.Text(buttonId == 0xE4 ? "RazerMappingButtonScrollUp" : "RazerMappingButtonScrollDown");
            return RazerKeypadMappingActionCatalog.Instance.DescribeButton(buttonId);
        }
        var number = buttonId switch
        {
            >= 0x40 and <= 0x43 => buttonId - 0x3F,
            0x50 => 5,
            >= 0x44 and <= 0x47 => buttonId - 0x3E,
            0x51 => 10,
            >= 0x48 and <= 0x4B => buttonId - 0x3D,
            >= 0x52 and <= 0x56 => buttonId - 0x43,
            0x60 => 20,
            _ => 0,
        };
        return number > 0
            ? AppStrings.FormatText("RazerMappingKeyboardButton", number)
            : RazerKeypadMappingActionCatalog.Instance.DescribeButton(buttonId);
    }

    private static int[] OptionIndices(RazerHidMappingGroup group) =>
        Enumerable.Range(0, RazerHidMappingActions.OptionNames(group).Count)
            .Where(index => RazerHidMappingActions.CreateAction(group, index) is { } action &&
                RazerKeypadInputCatalog.IsSupportedAction(action.FunctionId, action.Data))
            .ToArray();
}
