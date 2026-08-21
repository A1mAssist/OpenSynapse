using System.Globalization;

namespace OpenSynapse.App;

internal static class AppLanguageSettings
{
    public const string System = "system";
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSynapse",
        "language.txt");

    public static string Current { get; private set; } = System;
    public static string Effective => Current == System
        ? ResolveSystemLanguage([CultureInfo.CurrentUICulture.Name])
        : Current;

    public static void ApplySaved()
    {
        Apply(Read());
    }

    public static void Save(string language)
    {
        if (!IsSupported(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, language);
        Current = language;
    }

    private static void Apply(string language) => Current = language;

    private static string Read()
    {
        try
        {
            var language = File.Exists(SettingsPath)
                ? File.ReadAllText(SettingsPath).Trim()
                : System;
            return IsSupported(language) ? language : System;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or global::System.Security.SecurityException)
        {
            return System;
        }
    }

    private static bool IsSupported(string language) =>
        language is System or Chinese or English;

    internal static string ResolveSystemLanguage(IEnumerable<string> languages)
    {
        foreach (var language in languages)
        {
            if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return Chinese;
            }
            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return English;
            }
        }

        return English;
    }
}
