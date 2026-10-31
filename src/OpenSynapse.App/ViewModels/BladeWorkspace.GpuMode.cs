using OpenSynapse.Core.Displays;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    /// <summary>
    /// Only Auto has to survive a restart; the other three modes are read back from the
    /// hardware state. Global rather than per-profile: the GPU route is machine-wide.
    /// </summary>
    internal BladeGpuMode? StoredGpuMode => Profile.Global.Blade.GpuMode;

    internal async Task<bool> SaveGpuModeAsync(
        BladeGpuMode? mode,
        CancellationToken cancellationToken = default)
    {
        Profile.Global.Blade.GpuMode = mode;
        return await SaveProfileAsync(cancellationToken);
    }
}