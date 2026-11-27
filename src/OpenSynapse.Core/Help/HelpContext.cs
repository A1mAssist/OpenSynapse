namespace OpenSynapse.Core.Help;

public static class HelpContext
{
    public static IReadOnlyDictionary<string, string> PageTopics { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["overview"] = "getting-started.first-launch",
            ["devices"] = "getting-started.connect-device",
            ["studio"] = "lighting.browse-scenes",
            ["macros"] = "macros.record-edit",
            ["profiles"] = "profiles.device-rules",
            ["diagnostics"] = "diagnostics.logs",
            ["settings"] = "getting-started.settings",
        };

    public static string? Resolve(string pageKey) =>
        PageTopics.TryGetValue(pageKey, out var topicId) ? topicId : null;

    public static bool TryResolve(string pageKey, out string topicId) =>
        PageTopics.TryGetValue(pageKey, out topicId!);
}
