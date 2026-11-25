using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Controls;

public sealed partial class DownloadProgressPanel : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(DownloadProgressState),
        typeof(DownloadProgressPanel),
        new PropertyMetadata(null));

    public DownloadProgressPanel()
    {
        InitializeComponent();
    }

    public DownloadProgressState? State
    {
        get => (DownloadProgressState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }
}
