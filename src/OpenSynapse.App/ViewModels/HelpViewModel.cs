using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Help;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class HelpCategoryListViewModel(
    string categoryId,
    string title,
    string description,
    IReadOnlyList<HelpTopicLinkViewModel> topics,
    string topicCountText,
    string viewAllText)
{
    public string CategoryId { get; } = categoryId;
    public string Title { get; } = title;
    public string Description { get; } = description;
    public IReadOnlyList<HelpTopicLinkViewModel> Topics { get; } = topics;
    public string TopicCountText { get; } = topicCountText;
    public string ViewAllText { get; } = viewAllText;
    public string AutomationId { get; } = $"HelpCategoryRow:{categoryId}";
    public string ViewAllAutomationId { get; } = $"HelpCategoryViewAll:{categoryId}";
}

public sealed class HelpCommonTaskViewModel(string topicId, string title)
{
    public string TopicId { get; } = topicId;
    public string Title { get; } = title;
    public string AutomationId { get; } = $"HelpCommonTask:{topicId}";
}

public sealed class HelpTopicLinkViewModel(string topicId, string title, string summary)
{
    public string TopicId { get; } = topicId;
    public string Title { get; } = title;
    public string Summary { get; } = summary;
    public string AutomationId { get; } = $"HelpTopicLink:{topicId}";
}

public sealed class HelpCategoryLinkViewModel(string categoryId, string title)
{
    public string CategoryId { get; } = categoryId;
    public string Title { get; } = title;
    public string AutomationId { get; } = $"HelpCategoryLink:{categoryId}";
}

public sealed class HelpSearchGroupViewModel(
    string categoryTitle,
    IReadOnlyList<HelpSearchResultViewModel> results)
{
    public string CategoryTitle { get; } = categoryTitle;
    public IReadOnlyList<HelpSearchResultViewModel> Results { get; } = results;
}

public sealed class HelpSearchResultViewModel(
    string topicId,
    string categoryTitle,
    string title,
    string summary)
{
    public string TopicId { get; } = topicId;
    public string Heading { get; } = $"{categoryTitle} / {title}";
    public string Summary { get; } = summary;
    public string AutomationId { get; } = $"HelpSearchResult:{topicId}";
}

public sealed class HelpDirectoryCategoryViewModel(
    string title,
    IReadOnlyList<HelpDirectoryItemViewModel> topics)
{
    public string Title { get; } = title;
    public IReadOnlyList<HelpDirectoryItemViewModel> Topics { get; } = topics;
}

public sealed class HelpDirectoryItemViewModel(string topicId, string title, bool isCurrent)
{
    public string TopicId { get; } = topicId;
    public string Title { get; } = title;
    public Visibility CurrentVisibility { get; } = isCurrent ? Visibility.Visible : Visibility.Collapsed;
    public string AutomationId { get; } = $"HelpDirectoryTopic:{topicId}";
}

public sealed class HelpStepViewModel(int number, string text, string? actionTarget, string actionLabel)
{
    public int Number { get; } = number;
    public string Text { get; } = text;
    public string? ActionTarget { get; } = actionTarget;
    public string ActionLabel { get; } = actionLabel;
    public Visibility ActionVisibility { get; } =
        string.IsNullOrWhiteSpace(actionTarget) ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class HelpFaqViewModel(string question, string answer)
{
    public string Question { get; } = question;
    public string Answer { get; } = answer;
}

public sealed class HelpBlockViewModel
{
    public HelpBlockKind Kind { get; init; }
    public string? Text { get; init; }
    public string? Title { get; init; }
    public HelpSpecimenKind Specimen { get; init; }
    public IReadOnlyList<HelpStepViewModel> Steps { get; init; } = [];
    public IReadOnlyList<HelpFaqViewModel> Faqs { get; init; } = [];
    public IReadOnlyList<HelpTopicLinkViewModel> Related { get; init; } = [];
    public Brush? AccentBrush { get; init; }
    public Brush? BackgroundBrush { get; init; }
    public Brush? BorderBrush { get; init; }
}

public sealed class HelpArticleViewModel(
    string topicId,
    string categoryId,
    string categoryTitle,
    string title,
    string summary,
    string audience,
    IReadOnlyList<HelpBlockViewModel> blocks,
    string? previousTopicId,
    string? previousTitle,
    string? nextTopicId,
    string? nextTitle,
    string previousLabel,
    string nextLabel)
{
    public string TopicId { get; } = topicId;
    public string CategoryId { get; } = categoryId;
    public string CategoryTitle { get; } = categoryTitle;
    public string Title { get; } = title;
    public string Summary { get; } = summary;
    public string Audience { get; } = audience;
    public IReadOnlyList<HelpBlockViewModel> Blocks { get; } = blocks;
    public string? PreviousTopicId { get; } = previousTopicId;
    public string? PreviousTitle { get; } = previousTitle;
    public string? NextTopicId { get; } = nextTopicId;
    public string? NextTitle { get; } = nextTitle;
    public string PreviousLabel { get; } = previousLabel;
    public string NextLabel { get; } = nextLabel;
    public Visibility PreviousVisibility { get; } =
        string.IsNullOrWhiteSpace(previousTopicId) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NextVisibility { get; } =
        string.IsNullOrWhiteSpace(nextTopicId) ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class HelpViewModel : INotifyPropertyChanged
{
    private static readonly Brush TipAccent = Brush(68, 214, 44);
    private static readonly Brush TipBackground = Brush(24, 68, 214, 44);
    private static readonly Brush CautionAccent = Brush(240, 185, 90);
    private static readonly Brush CautionBackground = Brush(24, 240, 185, 90);
    private static readonly Brush LimitAccent = Brush(240, 125, 121);
    private static readonly Brush LimitBackground = Brush(24, 240, 125, 121);

    private readonly HelpNavigationState _state = new();
    private readonly List<string> _recentTopicIds = [];
    private string _searchQuery = string.Empty;
    private IReadOnlyList<HelpCategoryListViewModel> _categories = [];
    private IReadOnlyList<HelpCommonTaskViewModel> _commonTasks = [];
    private IReadOnlyList<HelpTopicLinkViewModel> _recentTopics = [];
    private IReadOnlyList<HelpCategoryLinkViewModel> _noResultCategories = [];
    private IReadOnlyList<HelpSearchGroupViewModel> _searchGroups = [];
    private IReadOnlyList<HelpDirectoryCategoryViewModel> _directory = [];
    private IReadOnlyList<HelpTopicLinkViewModel> _categoryTopics = [];
    private HelpArticleViewModel? _article;
    private string _categoryTitle = string.Empty;
    private string _categoryDescription = string.Empty;

    public HelpViewModel()
    {
        SearchPlaceholder = string.Empty;
        HomeTitle = string.Empty;
        HomeDescription = string.Empty;
        CommonTasksTitle = string.Empty;
        AllTopicsTitle = string.Empty;
        DirectoryTitle = string.Empty;
        BackText = string.Empty;
        ForwardText = string.Empty;
        PreviousText = string.Empty;
        NextText = string.Empty;
        RelatedText = string.Empty;
        FaqText = string.Empty;
        HelpText = string.Empty;
        OpenInAppText = string.Empty;
        SearchResultsText = string.Empty;
        RecentText = string.Empty;
        NoResultsText = string.Empty;
        ViewAllText = string.Empty;
        RefreshLocalization();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SearchPlaceholder { get; private set; }
    public string HomeTitle { get; private set; }
    public string HomeDescription { get; private set; }
    public string CommonTasksTitle { get; private set; }
    public string AllTopicsTitle { get; private set; }
    public string DirectoryTitle { get; private set; }
    public string BackText { get; private set; }
    public string ForwardText { get; private set; }
    public string PreviousText { get; private set; }
    public string NextText { get; private set; }
    public string RelatedText { get; private set; }
    public string FaqText { get; private set; }
    public string HelpText { get; private set; }
    public string OpenInAppText { get; private set; }
    public string SearchResultsText { get; private set; }
    public string RecentText { get; private set; }
    public string NoResultsText { get; private set; }
    public string ViewAllText { get; private set; }

    public IReadOnlyList<HelpCategoryListViewModel> Categories
    {
        get => _categories;
        private set => SetProperty(ref _categories, value, nameof(Categories));
    }

    public IReadOnlyList<HelpCommonTaskViewModel> CommonTasks
    {
        get => _commonTasks;
        private set => SetProperty(ref _commonTasks, value, nameof(CommonTasks));
    }

    public IReadOnlyList<HelpTopicLinkViewModel> RecentTopics
    {
        get => _recentTopics;
        private set
        {
            if (SetProperty(ref _recentTopics, value, nameof(RecentTopics)))
            {
                Raise(nameof(RecentVisibility));
            }
        }
    }

    public IReadOnlyList<HelpCategoryLinkViewModel> NoResultCategories
    {
        get => _noResultCategories;
        private set => SetProperty(ref _noResultCategories, value, nameof(NoResultCategories));
    }

    public IReadOnlyList<HelpSearchGroupViewModel> SearchGroups
    {
        get => _searchGroups;
        private set => SetProperty(ref _searchGroups, value, nameof(SearchGroups));
    }

    public IReadOnlyList<HelpDirectoryCategoryViewModel> Directory
    {
        get => _directory;
        private set => SetProperty(ref _directory, value, nameof(Directory));
    }

    public IReadOnlyList<HelpTopicLinkViewModel> CategoryTopics
    {
        get => _categoryTopics;
        private set => SetProperty(ref _categoryTopics, value, nameof(CategoryTopics));
    }

    public HelpArticleViewModel? Article
    {
        get => _article;
        private set => SetProperty(ref _article, value, nameof(Article));
    }

    public string CategoryTitle
    {
        get => _categoryTitle;
        private set => SetProperty(ref _categoryTitle, value, nameof(CategoryTitle));
    }

    public string CategoryDescription
    {
        get => _categoryDescription;
        private set => SetProperty(ref _categoryDescription, value, nameof(CategoryDescription));
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            value ??= string.Empty;
            if (StringComparer.Ordinal.Equals(_searchQuery, value))
            {
                return;
            }

            SetProperty(ref _searchQuery, value, nameof(SearchQuery));
            ApplySearch(value);
        }
    }

    public Visibility HomeVisibility => StateVisibility(HelpViewMode.Home);
    public Visibility CategoryVisibility => StateVisibility(HelpViewMode.Category);
    public Visibility ArticleVisibility => StateVisibility(HelpViewMode.Article);
    public Visibility SearchVisibility =>
        _state.Mode == HelpViewMode.Search && SearchGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoResultsVisibility =>
        _state.Mode == HelpViewMode.Search && SearchGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RecentVisibility => RecentTopics.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool CanGoBack => _state.CanGoBack;
    public bool CanGoForward => _state.CanGoForward;
    public string FocusTitle => _state.Mode switch
    {
        HelpViewMode.Category => CategoryTitle,
        HelpViewMode.Article => Article?.Title ?? HomeTitle,
        HelpViewMode.Search => SearchResultsText,
        _ => HomeTitle,
    };

    public void ShowHome()
    {
        _state.ShowHome();
        SetSearchText(string.Empty);
        RefreshView();
    }

    public void OpenCategory(string categoryId)
    {
        if (!HelpCatalog.CategoriesById.TryGetValue(categoryId, out var category))
        {
            return;
        }

        _state.ShowCategory(categoryId);
        SetSearchText(string.Empty);
        CategoryTitle = AppStrings.Text(category.TitleKey);
        CategoryDescription = AppStrings.Text(category.DescriptionKey);
        CategoryTopics = HelpCatalog.TopicsInCategory(categoryId).Select(ToLink).ToArray();
        RefreshView();
    }

    public void OpenArticle(string topicId)
    {
        if (!HelpCatalog.TopicsById.ContainsKey(topicId))
        {
            ShowHome();
            return;
        }

        _state.ShowArticle(topicId);
        SetSearchText(string.Empty);
        Article = BuildArticle(topicId);
        AddRecent(topicId);
        Directory = BuildDirectory(topicId);
        RefreshView();
    }

    public bool ClearSearch()
    {
        if (!_state.ClearSearch())
        {
            if (!string.IsNullOrEmpty(SearchQuery))
            {
                SetSearchText(string.Empty);
            }
            return false;
        }

        SetSearchText(string.Empty);
        RefreshView();
        return true;
    }

    public void GoBack()
    {
        if (_state.GoBack())
        {
            RestoreFromHistory();
        }
    }

    public void GoForward()
    {
        if (_state.GoForward())
        {
            RestoreFromHistory();
        }
    }

    public void RefreshLocalization()
    {
        SearchPlaceholder = AppStrings.Text("Help_Chrome_SearchPlaceholder");
        HomeTitle = AppStrings.Text("Help_Chrome_HomeTitle");
        HomeDescription = AppStrings.Text("Help_Chrome_HomeDescription");
        CommonTasksTitle = AppStrings.Text("Help_Chrome_CommonTasks");
        AllTopicsTitle = AppStrings.Text("Help_Chrome_AllTopics");
        DirectoryTitle = AppStrings.Text("Help_Chrome_Directory");
        BackText = AppStrings.Text("Help_Chrome_Back");
        ForwardText = AppStrings.Text("Help_Chrome_Forward");
        PreviousText = AppStrings.Text("Help_Chrome_Previous");
        NextText = AppStrings.Text("Help_Chrome_Next");
        RelatedText = AppStrings.Text("Help_Chrome_Related");
        FaqText = AppStrings.Text("Help_Chrome_Faq");
        HelpText = AppStrings.Text("Help_Chrome_Help");
        OpenInAppText = AppStrings.Text("Help_Chrome_OpenInApp");
        SearchResultsText = AppStrings.Text("Help_Chrome_SearchResults");
        RecentText = AppStrings.Text("Help_Chrome_Recent");
        NoResultsText = AppStrings.Text("Help_Chrome_NoResults");
        ViewAllText = AppStrings.Text("Help_Chrome_ViewAll");

        Categories = HelpCatalog.Categories.Select(category =>
        {
            var topics = HelpCatalog.TopicsInCategory(category.Id);
            return new HelpCategoryListViewModel(
                category.Id,
                AppStrings.Text(category.TitleKey),
                AppStrings.Text(category.DescriptionKey),
                topics.Take(3).Select(ToLink).ToArray(),
                AppStrings.FormatText("Help_Chrome_TopicCount", topics.Count),
                ViewAllText);
        }).ToArray();
        CommonTasks = HelpCatalog.CommonTasks
            .Select(task => new HelpCommonTaskViewModel(task.TopicId, AppStrings.Text(task.TitleKey)))
            .ToArray();
        NoResultCategories = HelpCatalog.Categories.Take(3)
            .Select(category => new HelpCategoryLinkViewModel(category.Id, AppStrings.Text(category.TitleKey)))
            .ToArray();
        RefreshRecent();

        if (_state.Mode == HelpViewMode.Category && _state.CategoryId is { } categoryId)
        {
            CategoryTitle = AppStrings.Text(HelpCatalog.CategoriesById[categoryId].TitleKey);
            CategoryDescription = AppStrings.Text(HelpCatalog.CategoriesById[categoryId].DescriptionKey);
            CategoryTopics = HelpCatalog.TopicsInCategory(categoryId).Select(ToLink).ToArray();
        }
        if (_state.Mode == HelpViewMode.Article && _state.TopicId is { } topicId)
        {
            Article = BuildArticle(topicId);
            Directory = BuildDirectory(topicId);
        }
        if (_state.Mode == HelpViewMode.Search)
        {
            BuildSearch(_state.SearchQuery);
        }

        NotifyChrome();
        RefreshView();
    }

    private void ApplySearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ClearSearch();
            return;
        }

        _state.Search(query);
        BuildSearch(query);
        RefreshView();
    }

    private void BuildSearch(string query)
    {
        var hits = HelpSearch.Search(query, AppStrings.Text);
        var groups = new List<HelpSearchGroupViewModel>();
        foreach (var category in HelpCatalog.Categories)
        {
            var results = hits
                .Where(hit => StringComparer.Ordinal.Equals(
                    HelpCatalog.TopicsById[hit.TopicId].CategoryId,
                    category.Id))
                .Select(hit =>
                {
                    var topic = HelpCatalog.TopicsById[hit.TopicId];
                    return new HelpSearchResultViewModel(
                        topic.Id,
                        AppStrings.Text(category.TitleKey),
                        AppStrings.Text(topic.TitleKey),
                        AppStrings.Text(topic.SummaryKey));
                })
                .ToArray();
            if (results.Length > 0)
            {
                groups.Add(new HelpSearchGroupViewModel(AppStrings.Text(category.TitleKey), results));
            }
        }

        SearchGroups = groups;
    }

    private HelpArticleViewModel BuildArticle(string topicId)
    {
        var topic = HelpCatalog.TopicsById[topicId];
        var category = HelpCatalog.CategoriesById[topic.CategoryId];
        var previous = HelpCatalog.Previous(topicId);
        var next = HelpCatalog.Next(topicId);
        return new HelpArticleViewModel(
            topicId,
            topic.CategoryId,
            AppStrings.Text(category.TitleKey),
            AppStrings.Text(topic.TitleKey),
            AppStrings.Text(topic.SummaryKey),
            AppStrings.FormatText(
                "Help_Chrome_AppliesTo",
                AppStrings.Text(HelpCatalog.AudienceKey(topic.Audience))),
            topic.Blocks.Select(ToBlock).ToArray(),
            previous?.Id,
            previous is null ? null : AppStrings.Text(previous.TitleKey),
            next?.Id,
            next is null ? null : AppStrings.Text(next.TitleKey),
            PreviousText,
            NextText);
    }

    private HelpBlockViewModel ToBlock(HelpBlock block) => block switch
    {
        HelpParagraphBlock paragraph => new HelpBlockViewModel
        {
            Kind = HelpBlockKind.Paragraph,
            Text = AppStrings.Text(paragraph.TextKey),
        },
        HelpStepsBlock steps => new HelpBlockViewModel
        {
            Kind = HelpBlockKind.Steps,
            Steps = steps.Steps.Select((step, index) => new HelpStepViewModel(
                index + 1,
                AppStrings.Text(step.TextKey),
                step.Action?.Target,
                OpenInAppText)).ToArray(),
        },
        HelpSpecimenBlock specimen => new HelpBlockViewModel
        {
            Kind = HelpBlockKind.Specimen,
            Specimen = specimen.Specimen,
        },
        HelpCalloutBlock callout => new HelpBlockViewModel
        {
            Kind = HelpBlockKind.Callout,
            Text = AppStrings.Text(callout.TextKey),
            AccentBrush = CalloutAccent(callout.Callout),
            BackgroundBrush = CalloutBackground(callout.Callout),
            BorderBrush = CalloutAccent(callout.Callout),
        },
        HelpFaqBlock faq => new HelpBlockViewModel
        {
            Kind = HelpBlockKind.Faq,
            Title = FaqText,
            Faqs = faq.Items
                .Select(item => new HelpFaqViewModel(
                    AppStrings.Text(item.QuestionKey),
                    AppStrings.Text(item.AnswerKey)))
                .ToArray(),
        },
        HelpRelatedBlock related => new HelpBlockViewModel
        {
            Kind = HelpBlockKind.Related,
            Title = RelatedText,
            Related = related.TopicIds
                .Select(id => new HelpTopicLinkViewModel(
                    id,
                    AppStrings.Text(HelpCatalog.TopicsById[id].TitleKey),
                    AppStrings.Text(HelpCatalog.TopicsById[id].SummaryKey)))
                .ToArray(),
        },
        _ => new HelpBlockViewModel { Kind = HelpBlockKind.Paragraph, Text = string.Empty },
    };

    private IReadOnlyList<HelpDirectoryCategoryViewModel> BuildDirectory(string currentTopicId) =>
        HelpCatalog.Categories
            .Select(category => new HelpDirectoryCategoryViewModel(
                AppStrings.Text(category.TitleKey),
                HelpCatalog.TopicsInCategory(category.Id)
                    .Select(topic => new HelpDirectoryItemViewModel(
                        topic.Id,
                        AppStrings.Text(topic.TitleKey),
                        StringComparer.Ordinal.Equals(topic.Id, currentTopicId)))
                    .ToArray()))
            .ToArray();

    private void AddRecent(string topicId)
    {
        _recentTopicIds.Remove(topicId);
        _recentTopicIds.Insert(0, topicId);
        if (_recentTopicIds.Count > 4)
        {
            _recentTopicIds.RemoveRange(4, _recentTopicIds.Count - 4);
        }

        RefreshRecent();
    }

    private void RefreshRecent() =>
        RecentTopics = _recentTopicIds
            .Where(HelpCatalog.TopicsById.ContainsKey)
            .Select(topicId => ToLink(HelpCatalog.TopicsById[topicId]))
            .ToArray();

    private HelpTopicLinkViewModel ToLink(HelpTopic topic) =>
        new(topic.Id, AppStrings.Text(topic.TitleKey), AppStrings.Text(topic.SummaryKey));

    private void SetSearchText(string value)
    {
        if (StringComparer.Ordinal.Equals(_searchQuery, value))
        {
            return;
        }

        _searchQuery = value;
        Raise(nameof(SearchQuery));
    }

    private void RestoreFromHistory()
    {
        SetSearchText(string.Empty);
        if (_state.Mode == HelpViewMode.Category && _state.CategoryId is { } categoryId)
        {
            var category = HelpCatalog.CategoriesById[categoryId];
            CategoryTitle = AppStrings.Text(category.TitleKey);
            CategoryDescription = AppStrings.Text(category.DescriptionKey);
            CategoryTopics = HelpCatalog.TopicsInCategory(categoryId).Select(ToLink).ToArray();
        }

        if (_state.Mode == HelpViewMode.Article && _state.TopicId is { } topicId)
        {
            Article = BuildArticle(topicId);
            AddRecent(topicId);
            Directory = BuildDirectory(topicId);
        }

        RefreshView();
    }

    private void RefreshView()
    {
        Raise(nameof(HomeVisibility));
        Raise(nameof(CategoryVisibility));
        Raise(nameof(ArticleVisibility));
        Raise(nameof(SearchVisibility));
        Raise(nameof(NoResultsVisibility));
        Raise(nameof(FocusTitle));
        Raise(nameof(CanGoBack));
        Raise(nameof(CanGoForward));
    }

    private void NotifyChrome()
    {
        foreach (var name in new[]
                 {
                     nameof(SearchPlaceholder),
                     nameof(HomeTitle),
                     nameof(HomeDescription),
                     nameof(CommonTasksTitle),
                     nameof(AllTopicsTitle),
                     nameof(DirectoryTitle),
                     nameof(BackText),
                     nameof(ForwardText),
                     nameof(PreviousText),
                     nameof(NextText),
                     nameof(RelatedText),
                     nameof(FaqText),
                     nameof(HelpText),
                     nameof(OpenInAppText),
                     nameof(SearchResultsText),
                     nameof(RecentText),
                     nameof(NoResultsText),
                     nameof(ViewAllText),
                 })
        {
            Raise(name);
        }
    }

    private Visibility StateVisibility(HelpViewMode mode) =>
        _state.Mode == mode ? Visibility.Visible : Visibility.Collapsed;

    private static Brush CalloutAccent(HelpCalloutKind kind) => kind switch
    {
        HelpCalloutKind.Tip => TipAccent,
        HelpCalloutKind.Caution => CautionAccent,
        _ => LimitAccent,
    };

    private static Brush CalloutBackground(HelpCalloutKind kind) => kind switch
    {
        HelpCalloutKind.Tip => TipBackground,
        HelpCalloutKind.Caution => CautionBackground,
        _ => LimitBackground,
    };

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Color.FromArgb(255, red, green, blue));

    private static SolidColorBrush Brush(byte alpha, byte red, byte green, byte blue) =>
        new(Color.FromArgb(alpha, red, green, blue));

    private bool SetProperty<T>(
        ref T field,
        T value,
        string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
