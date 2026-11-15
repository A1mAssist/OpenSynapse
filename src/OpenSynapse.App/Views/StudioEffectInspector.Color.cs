using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.Views;

public sealed partial class StudioEffectInspector
{
    private FrameworkElement BuildColorEditor(StudioRgb initial, Action<StudioRgb> apply)
    {
        var picker = new ColorPicker
        {
            Color = ToColor(initial),
            Width = 320,
            IsAlphaEnabled = false,
            IsColorSpectrumVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            IsMoreButtonVisible = false,
        };
        AutomationProperties.SetAutomationId(picker, "StudioColorPicker");
        picker.ColorChanged += (_, args) =>
            apply(new StudioRgb(args.NewColor.R, args.NewColor.G, args.NewColor.B));
        return picker;
    }
}
