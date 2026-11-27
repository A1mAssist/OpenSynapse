using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class HelpArticleView : UserControl
{
    internal event Action<string>? PageNavigationRequested;
    internal event Action? ContentNavigationCompleted;

    public HelpArticleView() => InitializeComponent();

    private HelpViewModel ViewModel => (HelpViewModel)DataContext;

    private void HomeClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowHome();
        ContentNavigationCompleted?.Invoke();
    }

    private void CategoryClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Article is { } article)
        {
            ViewModel.OpenCategory(article.CategoryId);
            ContentNavigationCompleted?.Invoke();
        }
    }

    private void StepActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string page })
        {
            PageNavigationRequested?.Invoke(page);
        }
    }

    private void DirectoryTopicClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string topicId })
        {
            return;
        }

        DirectoryButton.Flyout?.Hide();
        ViewModel.OpenArticle(topicId);
    }

    private void DirectoryOpened(object sender, object e)
    {
        if (sender is not Flyout { Content: DependencyObject content } ||
            ViewModel.Article is not { } article)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (FindTopicButton(content, article.TopicId) is { } target)
            {
                target.StartBringIntoView();
            }
        });
    }

    private static Button? FindTopicButton(DependencyObject root, string topicId)
    {
        if (root is Button { Tag: string id } button &&
            StringComparer.Ordinal.Equals(id, topicId))
        {
            return button;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindTopicButton(VisualTreeHelper.GetChild(root, index), topicId) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private void RelatedClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string topicId })
        {
            ViewModel.OpenArticle(topicId);
        }
    }

    private void PreviousClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string topicId })
        {
            ViewModel.OpenArticle(topicId);
        }
    }

    private void NextClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string topicId })
        {
            ViewModel.OpenArticle(topicId);
        }
    }
}
