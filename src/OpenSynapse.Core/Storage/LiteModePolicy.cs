namespace OpenSynapse.Core.Storage;

public static class LiteModePolicy
{
    public static IReadOnlyList<string> VisibleNavigationPages(bool liteMode) => liteMode
        ? ["devices", "macros", "profiles", "diagnostics", "settings"]
        : ["overview", "devices", "studio", "macros", "profiles", "diagnostics", "settings"];

    public static bool ShouldCreateBladeLighting(bool liteMode) => !liteMode;
    public static bool ShouldStartPerformanceSampling(bool liteMode) => !liteMode;
    public static bool ShouldStartChromaRest(bool liteMode) => !liteMode;
}
