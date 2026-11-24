namespace OpenSynapse.Core.Lighting;

/// <summary>Reusable ambient renderers; preset files supply the look, not new code.</summary>
internal static class AmbientRenderModules
{
    public static IReadOnlyList<IRenderModuleFactory> All { get; } = Array.AsReadOnly<IRenderModuleFactory>(
    [
        new AmbientFieldModule(),
        new AmbientParticlesModule(),
        new AmbientMaskModule(),
        new AmbientTransitionModule(),
        new AmbientAutomatonModule(),
        new AmbientFlowModule(),
        new AmbientGeometryModule(),
        new AmbientLiquidModule(),
        new AmbientSignalModule(),
    ]);
}
