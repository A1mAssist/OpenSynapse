using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Core.Storage;

public sealed class AppBehaviorSettings
{
    internal static readonly BladePerformanceMode[] SupportedPerformanceCycleModes =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Gaming,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Silent,
        BladePerformanceMode.BatterySaver,
        BladePerformanceMode.Hyperboost,
    ];

    private static readonly string SettingsPath = AppDataPaths.Combine("behavior.json");
    private static readonly object FileGate = new();
    private string? _blockedSavePath;
    private StudioScreenRect _ambientCaptureRegion = StudioScreenRect.FullScreen;
    [JsonIgnore] public string? LoadError { get; private set; }
    [JsonIgnore] public string? RecoveryFilePath { get; private set; }

    public bool ModeChangeNotificationsEnabled { get; set; } = true;
    public bool BladePerformanceCompatibilityEnabled { get; set; }
    public bool MacroPlaybackNotificationsEnabled { get; set; } = true;
    public bool LowBatteryNotificationsEnabled { get; set; } = true;
    public bool TrayBatteryLevelsEnabled { get; set; } = true;
    public bool TaskbarStatusEnabled { get; set; }
    public bool TaskbarStatusClickOpensApp { get; set; }
    /// <summary>
    /// Null until the user first toggles startup (or an existing Run entry is
    /// adopted); a plain bool would make a fresh install look "disabled".
    /// </summary>
    public bool? StartWithWindows { get; set; }
    public bool StartSilent { get; set; }
    public List<TaskbarBatteryPreference> TaskbarBatteryDisplay { get; set; } = [];
    public bool TurnOffPeripheralLightingOnShutdown { get; set; }
    public bool TurnOffPeripheralLightingOnDisplayOff { get; set; }
    public bool LiteModeEnabled { get; set; }
    public bool PerformanceOverlayEnabled { get; set; }
    public bool PerformanceOverlayDetailed { get; set; }
    public bool PerformanceOverlayAlwaysOnTop { get; set; } = true;
    public int? PerformanceOverlayX { get; set; }
    public int? PerformanceOverlayY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int NavigationPaneWidth { get; set; } = 224;
    public Dictionary<string, int> LowBatteryNotifiedLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ChromaBlockedSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ExperimentalChromaRestEnabled { get; set; } = true;
    public bool RestoreLightingAfterChromaSession { get; set; } = true;
    public HashSet<BladePerformanceMode> PerformanceCycleModes { get; set; } =
        [.. SupportedPerformanceCycleModes];
    public bool PadlessHyperBoostAccepted { get; set; }
    public bool AdvancedFeaturesEnabled { get; set; }
    public StudioScreenRect AmbientCaptureRegion
    {
        get => _ambientCaptureRegion;
        set => _ambientCaptureRegion = value.Clamp();
    }
    public HashSet<int>? RefreshRateCycleHertz { get; set; }
    public string? HueBridgeHost { get; set; }
    public string? HueAppKey { get; set; }

    public static AppBehaviorSettings Load() => Load(SettingsPath);

    public static AppBehaviorSettings Load(string filePath)
    {
        filePath = Path.GetFullPath(filePath);
        lock (FileGate) return LoadCore(filePath);
    }

    private static AppBehaviorSettings LoadCore(string filePath)
    {
        try
        {
            var settings = File.Exists(filePath)
                ? JsonSerializer.Deserialize<AppBehaviorSettings>(File.ReadAllText(filePath)) ?? throw new JsonException("Settings are null.")
                : new();
            settings.LowBatteryNotifiedLevels = (settings.LowBatteryNotifiedLevels ?? [])
                .Select(item => (Key: NormalizeBatteryKey(item.Key), item.Value))
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
            settings.TaskbarBatteryDisplay = (settings.TaskbarBatteryDisplay ?? [])
                .OfType<TaskbarBatteryPreference>()
                .Select(item => new TaskbarBatteryPreference
                {
                    Key = (item.Key ?? string.Empty).Trim(),
                    Name = (item.Name ?? string.Empty).Trim(),
                })
                .Where(item => item.Key.Length > 0)
                .Select(item => item.Name.Length <= 128
                    ? item
                    : new TaskbarBatteryPreference { Key = item.Key, Name = item.Name[..128] })
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(TaskbarStatusPolicy.MaximumBatteryCount)
                .ToList();
            return settings;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            JsonException or System.Security.SecurityException)
        {
            var fallback = new AppBehaviorSettings { LoadError = exception.Message, _blockedSavePath = filePath };
            if (exception is JsonException)
            {
                var recoveryPath = $"{filePath}.corrupt-{Guid.NewGuid():N}";
                try
                {
                    File.Copy(filePath, recoveryPath);
                    fallback.RecoveryFilePath = recoveryPath;
                    fallback._blockedSavePath = null;
                }
                catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    fallback.LoadError += $" Recovery failed: {recoveryError.Message}";
                }
            }
            return fallback;
        }
    }

    // Keys persisted before the rename carry the old prefix.
    private static string NormalizeBatteryKey(string key) =>
        key.StartsWith("openrazer:", StringComparison.OrdinalIgnoreCase)
            ? "razer-hid:" + key["openrazer:".Length..]
            : key;

    public void Save() => Save(SettingsPath);
    public void Save(string filePath) => Save(filePath, null);

    internal void Save(string filePath, Action<string>? beforeCommit)
    {
        filePath = Path.GetFullPath(filePath);
        if (StringComparer.OrdinalIgnoreCase.Equals(filePath, _blockedSavePath))
            throw new IOException("Unreadable behavior settings were not backed up; reload or recover them before saving.");
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

        lock (FileGate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var temporaryPath = $"{filePath}.tmp-{Guid.NewGuid():N}";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, this);
                    stream.Flush(flushToDisk: true);
                }
                beforeCommit?.Invoke(temporaryPath);
                if (File.Exists(filePath)) File.Replace(temporaryPath, filePath, null, ignoreMetadataErrors: true);
                else File.Move(temporaryPath, filePath);
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
            }
        }
    }
}
