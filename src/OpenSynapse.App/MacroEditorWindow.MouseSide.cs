using OpenSynapse.App.Views;

namespace OpenSynapse.App;

public sealed partial class MacroEditorWindow
{
    private void MacroTriggerFlyoutOpening(object sender, object e) =>
        MacroTriggerMenu.Populate(
            MacroMouseSideTriggerMenu,
            MacroNagaTriggerMenu,
            _viewModel.Macros,
            _lifetime.Token);
}
