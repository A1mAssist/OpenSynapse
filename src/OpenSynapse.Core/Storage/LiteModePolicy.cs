namespace OpenSynapse.Core.Storage;

public static class LiteModePolicy
{
    public static IReadOnlyList<string> VisibleNavigationPages(bool liteMode) => liteMode
        ? ["devices", "studio", "macros", "profiles", "diagnostics", "settings"]
        : ["overview", "devices", "studio", "macros", "profiles", "diagnostics", "settings"];

    public static bool ShouldCreateBladeLighting(bool liteMode) => !liteMode;
    public static bool ShouldStartPerformanceSampling(bool liteMode) => !liteMode;
    public static bool ShouldStartChromaRest(bool liteMode) => !liteMode;
    public static int PageCacheLimit(bool liteMode) => liteMode ? 1 : 2;
    public static TimeSpan DeviceReadoutInterval(bool liteMode) =>
        TimeSpan.FromSeconds(liteMode ? 3 : 1);
}
