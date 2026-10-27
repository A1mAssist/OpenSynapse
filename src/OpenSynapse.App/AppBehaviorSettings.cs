using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App;

internal sealed class AppBehaviorSettings
{
    internal static readonly BladePerformanceMode[] SupportedPerformanceCycleModes =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Hyperboost,
    ];

    private static readonly string SettingsPath = AppDataPaths.Combine("behavior.json");

    public bool ModeChangeNotificationsEnabled { get; set; } = true;
    public bool LowBatteryNotificationsEnabled { get; set; } = true;
    public bool TrayBatteryLevelsEnabled { get; set; } = true;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public Dictionary<string, int> LowBatteryNotifiedLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ChromaBlockedSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ExperimentalChromaRestEnabled { get; set; } = true;
    public bool RestoreLightingAfterChromaSession { get; set; } = true;
    public HashSet<BladePerformanceMode> PerformanceCycleModes { get; set; } =
        [.. SupportedPerformanceCycleModes];
    public bool PadlessHyperBoostAccepted { get; set; }
    public HashSet<int>? RefreshRateCycleHertz { get; set; }
    public string? HueBridgeHost { get; set; }
    public string? HueAppKey { get; set; }

    public static AppBehaviorSettings Load()
    {
        try
        {
            var settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppBehaviorSettings>(File.ReadAllText(SettingsPath)) ?? new()
                : new();
            settings.LowBatteryNotifiedLevels = (settings.LowBatteryNotifiedLevels ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && item.Value is 10 or 20)
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Min(item => item.Value), StringComparer.OrdinalIgnoreCase);
            settings.ChromaBlockedSources = (settings.ChromaBlockedSources ?? [])
                .Where(title => !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= 64)
                .Select(title => title.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            settings.PerformanceCycleModes = settings.PerformanceCycleModes?
                .Where(SupportedPerformanceCycleModes.Contains)
                .ToHashSet() ?? [];
            if (settings.PerformanceCycleModes.Count == 0)
            {
                settings.PerformanceCycleModes = [.. SupportedPerformanceCycleModes];
            }
            settings.RefreshRateCycleHertz = settings.RefreshRateCycleHertz?
                .Where(hertz => hertz > 0)
                .ToHashSet();
            if (settings.RefreshRateCycleHertz?.Count == 0)
            {
                settings.RefreshRateCycleHertz = null;
            }
            return settings;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            JsonException or System.Security.SecurityException)
        {
            return new();
        }
    }

    public void Save()
    {
        if (PerformanceCycleModes.Count == 0 ||
            PerformanceCycleModes.Any(mode => !SupportedPerformanceCycleModes.Contains(mode)))
        {
            throw new InvalidOperationException("At least one supported performance mode is required.");
        }
        if (RefreshRateCycleHertz is { Count: 0 } ||
            RefreshRateCycleHertz?.Any(hertz => hertz <= 0) == true)
        {
            throw new InvalidOperationException("At least one valid refresh rate is required.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
    }
}
