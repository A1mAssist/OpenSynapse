namespace OpenSynapse.Core.Lighting;

/// <summary>All render module factories, grouped by the input they consume.</summary>
public static class RenderModules
{
    public static IReadOnlyList<IRenderModuleFactory> Ambient => AmbientRenderModules.All;
    public static IReadOnlyList<IRenderModuleFactory> Music => MusicRenderModules.All;
    public static IReadOnlyList<IRenderModuleFactory> Key => KeyRenderModules.All;
    public static IReadOnlyList<IRenderModuleFactory> System => SystemRenderModules.All;

    public static IReadOnlyList<IRenderModuleFactory> All { get; } = Array.AsReadOnly(
        Ambient.Concat(Music).Concat(Key).Concat(System).ToArray());
}
