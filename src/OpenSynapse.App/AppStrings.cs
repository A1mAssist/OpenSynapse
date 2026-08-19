using Microsoft.Windows.ApplicationModel.Resources;

namespace OpenSynapse.App;

internal static class AppStrings
{
    private static ResourceLoader? _loader;
    private static bool _enabled;

    public static void Enable() => _enabled = true;

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

    public static string Format(string key, string fallback, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Load(key, fallback), args);

    private static string Load(string key, string fallback)
    {
        try
        {
            var value = (_loader ??= new ResourceLoader()).GetString(key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
