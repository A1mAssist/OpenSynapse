using Microsoft.Windows.ApplicationModel.Resources;

namespace OpenSynapse.App;

internal static class AppStrings
{
    private static ResourceManager? _manager;
    private static ResourceMap? _resources;
    private static ResourceContext? _context;
    private static bool _enabled;

    public static void Enable() => _enabled = true;

    public static void Reset()
    {
        _manager = null;
        _resources = null;
        _context = null;
    }

    public static string Get(string source)
    {
        if (!_enabled)
        {
            return source;
        }

        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in source)
            {
                hash = (hash ^ character) * 16777619;
            }

            return Load($"Text_{hash:X8}", source);
        }
    }

    public static IReadOnlyList<string> Get(params string[] values) =>
        values.Select(Get).ToArray();

    public static string Text(string key) => Load(key, key);

    public static string FormatText(string key, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Load(key, key), args);

    internal static string? TryGet(string key)
    {
        if (!_enabled)
        {
            return null;
        }

        try
        {
            _manager ??= new ResourceManager();
            _resources ??= _manager.MainResourceMap.GetSubtree("Resources");
            _context ??= _manager.CreateResourceContext();
            _context.QualifierValues["Language"] = AppLanguageSettings.Effective;
            return _resources.TryGetValue(key, _context)?.ValueAsString;
        }
        catch
        {
            return null;
        }
    }

    private static string Load(string key, string fallback)
    {
        var value = TryGet(key);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }
}
