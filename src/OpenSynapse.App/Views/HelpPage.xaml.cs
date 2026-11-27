using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class HelpPage : UserControl
{
    private HelpViewModel? _viewModel;

    public HelpPage()
    {
        InitializeComponent();
        DataContextChanged += HelpPageDataContextChanged;
        Loaded += (_, _) => AttachViewModel(DataContext as HelpViewModel);
        Unloaded += (_, _) => DetachViewModel();
        ArticleView.PageNavigationRequested += page => PageNavigationRequested?.Invoke(page);
        ArticleView.ContentNavigationCompleted += FocusContent;
    }

    internal event Action<string>? PageNavigationRequested;

    private HelpViewModel ViewModel => (HelpViewModel)DataContext;

    internal void OnNavigatedTo(string? topicId)
    {
        if (!string.IsNullOrWhiteSpace(topicId))
        {
            ViewModel.OpenArticle(topicId);
        }
        else
        {
            ViewModel.ShowHome();
        }

        ResetScroll();
    }

    internal void ShowHome()
    {
        ViewModel.ShowHome();
        ResetScroll();
        FocusContent();
    }

    internal void OpenTopic(string topicId)
    {
        ViewModel.OpenArticle(topicId);
        ResetScroll();
        FocusContent();
    }

    internal void FocusSearch()
    {
        HelpSearchBox.Focus(FocusState.Programmatic);
        HelpSearchBox.SelectAll();
    }

    private void HelpPageDataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args) =>
        AttachViewModel(args.NewValue as HelpViewModel);

    private void AttachViewModel(HelpViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        DetachViewModel();
        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelPropertyChanged;
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        _viewModel = null;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(HelpViewModel.HomeVisibility) or
            nameof(HelpViewModel.CategoryVisibility) or
            nameof(HelpViewModel.ArticleVisibility) or
            nameof(HelpViewModel.SearchVisibility) or
            nameof(HelpViewModel.NoResultsVisibility) or
            nameof(HelpViewModel.Article))
        {
            ResetScroll();
        }

        if (args.PropertyName == nameof(HelpViewModel.Article))
        {
            FocusContent();
        }
    }

    private void SearchAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        FocusSearch();
        args.Handled = true;
    }

    private void EscapeAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.ClearSearch())
        {
            return;
        }

        ResetScroll();
        args.Handled = true;
    }

    private void BackHistoryClick(object sender, RoutedEventArgs e)
    {
        ViewModel.GoBack();
        ResetScroll();
        FocusContent();
    }

    private void ForwardHistoryClick(object sender, RoutedEventArgs e)
    {
        ViewModel.GoForward();
        ResetScroll();
        FocusContent();
    }

    private void TopicClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string topicId })
        {
            OpenTopic(topicId);
        }
    }

    private void CategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string categoryId })
        {
            ViewModel.OpenCategory(categoryId);
            ResetScroll();
            FocusContent();
        }
    }

    private void HomeClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowHome();
        ResetScroll();
        FocusContent();
    }

    private void FocusContent() =>
        HelpScrollViewer.Focus(FocusState.Programmatic);

    private void ResetScroll() =>
        HelpScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
}
