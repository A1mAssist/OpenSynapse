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

    public static IReadOnlyList<StudioScene> All { get; } =
    [
        Scene(
            "aurora-breathing",
            "LightingStudioSceneAurora",
            60,
            StudioEffectKind.Breathing,
            "#00FF88",
            "#00CFFF",
            14_000,
            1,
            "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c01"),
        Scene(
            "heartbeat",
            "LightingStudioSceneHeartbeat",
            90,
            StudioEffectKind.Breathing,
            "#FF2B4D",
            "#FF7A9C",
            60_000 / 90,
            1,
            "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c02"),
        Scene(
            "neon-chase",
            "LightingStudioSceneNeonChase",
            120,
            StudioEffectKind.Wave,
            "#FF00E5",
            "#00FFF0",
            2_500,
            1,
            "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c03"),
        Scene(
            "deep-echo",
            "LightingStudioSceneDeepEcho",
            60,
            StudioEffectKind.Tidal,
            "#001B44",
            "#00E5D0",
            3_000,
            1,
            "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c04"),
        Scene(
            "lava-pulse",
            "LightingStudioSceneLavaPulse",
            100,
            StudioEffectKind.Fire,
            "#FF2A00",
            "#FFD000",
            1_200,
            1,
            "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c05"),
        Scene(
            "prism-sweep",
            "LightingStudioScenePrismSweep",
            80,
            StudioEffectKind.Spectrum,
            "#FF0000",
            "#00FFF0",
            8_000,
            1,
            "b6e4c66e-0c1f-4a43-9a7b-0f1f0a8f5c06"),
    ];

    public static StudioScene? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(scene => string.Equals(scene.Id, id, StringComparison.OrdinalIgnoreCase));

    private static StudioScene Scene(
        string id,
        string nameKey,
        int bpm,
        StudioEffectKind effect,
        string primary,
        string secondary,
        int durationMs,
        double speed,
        string timebaseId) =>
        new(
            id,
            nameKey,
            bpm,
            [
                new StudioSceneLayer(
                    AllRoles,
                    effect,
                    StudioRgb.FromHex(primary),
                    StudioRgb.FromHex(secondary),
                    durationMs,
                    speed,
                    100,
                    StudioBlendMode.Normal,
                    Guid.Parse(timebaseId)),
            ]);
}
