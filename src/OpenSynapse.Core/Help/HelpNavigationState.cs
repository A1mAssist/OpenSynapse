namespace OpenSynapse.Core.Help;

public enum HelpViewMode
{
    Home,
    Category,
    Article,
    Search,
}

public sealed class HelpNavigationState
{
    private readonly Stack<HelpLocation> _back = [];
    private readonly Stack<HelpLocation> _forward = [];
    private HelpViewMode _returnMode = HelpViewMode.Home;
    private string? _returnCategoryId;
    private string? _returnTopicId;

    public HelpViewMode Mode { get; private set; } = HelpViewMode.Home;
    public string? CategoryId { get; private set; }
    public string? TopicId { get; private set; }
    public string SearchQuery { get; private set; } = string.Empty;
    public bool CanGoBack => Mode == HelpViewMode.Search || _back.Count > 0;
    public bool CanGoForward => Mode != HelpViewMode.Search && _forward.Count > 0;

    public void ShowHome() => NavigateTo(HelpViewMode.Home, null, null);

    public void ShowCategory(string categoryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        NavigateTo(HelpViewMode.Category, categoryId, null);
    }

    public void ShowArticle(string topicId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicId);
        NavigateTo(HelpViewMode.Article, null, topicId);
    }

    public bool Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ClearSearch();
        }

        if (Mode != HelpViewMode.Search)
        {
            _returnMode = Mode;
            _returnCategoryId = CategoryId;
            _returnTopicId = TopicId;
            _forward.Clear();
        }

        Mode = HelpViewMode.Search;
        CategoryId = null;
        TopicId = null;
        SearchQuery = query.Trim();
        return true;
    }

    public bool ClearSearch()
    {
        if (Mode != HelpViewMode.Search)
        {
            return false;
        }

        Mode = _returnMode;
        CategoryId = _returnCategoryId;
        TopicId = _returnTopicId;
        SearchQuery = string.Empty;
        _forward.Clear();
        return true;
    }

    public bool GoBack()
    {
        if (Mode == HelpViewMode.Search)
        {
            return ClearSearch();
        }

        if (_back.Count == 0)
        {
            return false;
        }

        _forward.Push(new HelpLocation(Mode, CategoryId, TopicId));
        Apply(_back.Pop());
        return true;
    }

    public bool GoForward()
    {
        if (Mode == HelpViewMode.Search || _forward.Count == 0)
        {
            return false;
        }

        _back.Push(new HelpLocation(Mode, CategoryId, TopicId));
        Apply(_forward.Pop());
        return true;
    }

    private void NavigateTo(HelpViewMode mode, string? categoryId, string? topicId)
    {
        var next = new HelpLocation(mode, categoryId, topicId);
        var current = CurrentContentLocation();
        if (Mode != HelpViewMode.Search && current == next)
        {
            return;
        }

        if (_back.Count == 0 || _back.Peek() != current)
        {
            _back.Push(current);
        }

        _forward.Clear();
        Apply(next);
    }

    private HelpLocation CurrentContentLocation() =>
        Mode == HelpViewMode.Search
            ? new HelpLocation(_returnMode, _returnCategoryId, _returnTopicId)
            : new HelpLocation(Mode, CategoryId, TopicId);

    private void Apply(HelpLocation location)
    {
        Mode = location.Mode;
        CategoryId = location.CategoryId;
        TopicId = location.TopicId;
        SearchQuery = string.Empty;
        _returnMode = Mode;
        _returnCategoryId = CategoryId;
        _returnTopicId = TopicId;
    }

    private readonly record struct HelpLocation(
        HelpViewMode Mode,
        string? CategoryId,
        string? TopicId);
}
