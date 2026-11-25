namespace OpenSynapse.Core.Lighting;

/// <summary>Built-in scenes. Kept apart from StudioPresetCatalog on purpose.</summary>
public static class StudioSceneCatalog
{
    private static readonly StudioSceneRole[] AllRoles =
    [
        StudioSceneRole.Keyboard,
        StudioSceneRole.Mousepad,
        StudioSceneRole.Mouse,
        StudioSceneRole.Headset,
        StudioSceneRole.ChromaLink,
        StudioSceneRole.Argb,
        StudioSceneRole.SingleZone,
    ];

    private static readonly StudioRgb Black = new(0, 0, 0);

    public static IReadOnlyList<StudioScene> All { get; } =
    [
        Scene(
            "aurora-breathing",
            "LightingStudioSceneAurora",
            60,
            // Aurora curtains drift across the board; a wide glow keeps the pulse readable on small strips.
            Breathing("#00FF88", "#8A2BE2", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c02",
                durationMs: 28_000, opacity: 12, blend: StudioBlendMode.Screen),
            Preset("ambient-exp-polar-breath", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c01",
                "#00FF88", "#8A2BE2")),
        Scene(
            "heartbeat",
            "LightingStudioSceneHeartbeat",
            90,
            // Heart mask plus a 90 bpm pulse; breathing halves its cycle, so 2 * 666 ms.
            Breathing("#FF2B4D", "#5A0010", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c04",
                durationMs: 1_333, opacity: 22, blend: StudioBlendMode.Screen),
            Preset("ambient-love-f71b28", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c03",
                "#EF0926", "#54030D")),
        Scene(
            "neon-chase",
            "LightingStudioSceneNeonChase",
            120,
            // Border mode needs edges; a dim bed keeps single-zone and short strips from going dark.
            Solid("#22003A", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c0d", opacity: 30, blend: StudioBlendMode.Add,
                secondary: "#00FFF0"),
            Wave("#FF00E5", "#00FFF0", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c06",
                speed: 10, opacity: 30, blend: StudioBlendMode.Add, angle: 0, width: 40),
            // Border runner under the streaks.
            Preset("ambient-wave-cfeae9", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c05",
                "#FF00E5", "#00FFF0")),
        Scene(
            "deep-echo",
            "LightingStudioSceneDeepEcho",
            60,
            // No bundled preset carries this one: a navy bed, a slow wide swell, and a 12 s breath.
            Breathing("#00E5D0", "#003F5C", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c08",
                durationMs: 12_000, opacity: 20, blend: StudioBlendMode.Screen),
            Wave("#000B1F", "#00E5D0", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c07",
                speed: 1, opacity: 70, blend: StudioBlendMode.Screen, angle: 90, width: 60,
                stops: ["#000814", "#00405E", "#00A8A0", "#00E5D0", "#00405E", "#000814"]),
            Solid("#000814", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c0f", opacity: 100,
                blend: StudioBlendMode.Normal, secondary: "#00E5D0")),
        Scene(
            "lava-pulse",
            "LightingStudioSceneLavaPulse",
            100,
            // The ember field breathes with a slow fire pulse.
            Breathing("#FF2A00", "#FFB000", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c0a",
                durationMs: 1_200, opacity: 18, blend: StudioBlendMode.Screen),
            Preset("ambient-scene-0f7dc8", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c09",
                "#FF2A00", "#FFB000")),
        Scene(
            "prism-sweep",
            "LightingStudioScenePrismSweep",
            80,
            Solid("#2A0A3C", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c0e", opacity: 26, blend: StudioBlendMode.Add,
                secondary: "#B8E0FF"),
            Wave("#000000", "#000000", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c0c",
                speed: 12, opacity: 35, blend: StudioBlendMode.Add, angle: 35, width: 12,
                stops: ["#FF004D", "#FFB300", "#3CFF6E", "#00E5FF", "#7A5CFF", "#FF4FD8"]),
            // Rotating polygons under the rainbow band.
            Preset("ambient-exp-prism-hourglass", "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c0b",
                "#FFB3C7", "#7A5CFF")),
    ];

    public static StudioScene? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(scene => string.Equals(scene.Id, id, StringComparison.OrdinalIgnoreCase));

    private static StudioScene Scene(string id, string nameKey, int bpm, params StudioSceneLayer[] layers) =>
        new(id, nameKey, bpm, layers);

    private static StudioSceneLayer Preset(string presetId, string timebaseId, string primary, string secondary) =>
        Layer(StudioEffectKind.Preset, StudioRgb.FromHex(primary), StudioRgb.FromHex(secondary), timebaseId,
            presetId: presetId);

    private static StudioSceneLayer Solid(string primary, string timebaseId, double opacity,
        StudioBlendMode blend, string? secondary = null) =>
        Layer(StudioEffectKind.Solid, StudioRgb.FromHex(primary),
            secondary is null ? Black : StudioRgb.FromHex(secondary), timebaseId, opacity: opacity, blend: blend);

    private static StudioSceneLayer Breathing(string primary, string secondary, string timebaseId,
        int durationMs, double opacity, StudioBlendMode blend) =>
        Layer(StudioEffectKind.Breathing, StudioRgb.FromHex(primary), StudioRgb.FromHex(secondary), timebaseId,
            durationMs: durationMs, opacity: opacity, blend: blend);

    private static StudioSceneLayer Wave(string primary, string secondary, string timebaseId,
        double speed, double opacity, StudioBlendMode blend, int angle, double width, string[]? stops = null) =>
        Layer(StudioEffectKind.Wave, StudioRgb.FromHex(primary), StudioRgb.FromHex(secondary), timebaseId,
            speed: speed, opacity: opacity, blend: blend, angle: angle, width: width,
            stops: stops?.Select(StudioRgb.FromHex).ToArray());

    private static StudioSceneLayer Layer(
        StudioEffectKind effect,
        StudioRgb primary,
        StudioRgb secondary,
        string timebaseId,
        int durationMs = 0,
        double speed = 1,
        double opacity = 100,
        StudioBlendMode blend = StudioBlendMode.Normal,
        int angle = 0,
        double width = 50,
        StudioRgb[]? stops = null,
        string? presetId = null) =>
        new(
            AllRoles,
            effect,
            primary,
            secondary,
            durationMs,
            speed,
            opacity,
            blend,
            Guid.Parse(timebaseId),
            presetId,
            stops,
            angle,
            width);
}
