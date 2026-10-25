namespace OpenSynapse.App;

internal static class AppStrings
{
    public static string Text(string key) => key;

    public static string FormatText(string key, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, key, args);
}
