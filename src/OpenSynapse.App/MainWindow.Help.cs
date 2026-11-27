using Microsoft.UI.Xaml.Input;
using OpenSynapse.Core.Help;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private string? _pendingHelpTopic;

    private void HelpAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (IsPageCurrent("help"))
        {
            HelpPage.FocusSearch();
            return;
        }

        OpenHelpTopic(HelpContext.Resolve(_currentPage) ?? "getting-started.first-launch");
    }

    internal void OpenHelpTopic(string topicId)
    {
        if (!HelpCatalog.TopicsById.ContainsKey(topicId))
        {
            topicId = "getting-started.first-launch";
        }

        if (IsPageCurrent("help"))
        {
            HelpPage.OpenTopic(topicId);
            return;
        }

        _pendingHelpTopic = topicId;
        RequestNavigation("help");
    }
}
