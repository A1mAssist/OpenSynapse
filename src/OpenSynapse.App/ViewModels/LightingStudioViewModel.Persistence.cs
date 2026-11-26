using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App.ViewModels;

public sealed partial class LightingStudioViewModel
{
    /// <summary>True when the last Apply left a snapshot for the next launch.</summary>
    internal static bool HasAppliedProject => File.Exists(AppliedProjectPath());

    /// <summary>
    /// Loads what was on the devices when the app last exited. Kept separate from
    /// the manual save slot so a draft save never changes what startup replays.
    /// </summary>
    internal bool LoadAppliedProject()
    {
        var path = AppliedProjectPath();
        if (!File.Exists(path)) return false;
        try
        {
            ApplyProject(StudioProjectCodec.Load(path));
            return true;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusText = AppStrings.FormatText("LightingStudioStatusLoadFailed", exception.Message);
            return false;
        }
    }

    /// <summary>A failed snapshot only costs the next replay; the effect is already live.</summary>
    private void SaveAppliedProject()
    {
        try
        {
            StudioProjectCodec.Save(AppliedProjectPath(), AppliedProjectSnapshot());
        }
        catch (Exception exception) when (IsExpected(exception))
        {
        }
    }

    /// <summary>A scene is applied without touching the user's project file.</summary>
    private StudioProject AppliedProjectSnapshot()
    {
        if (_activeScene is not { } scene)
        {
            var snapshot = StudioProjectCodec.Deserialize(StudioProjectCodec.Serialize(_project));
            if (snapshot.SelectedProfile is { } selected) selected.OutputScope = _appliedScope.Clone();
            return snapshot;
        }
        var project = new StudioProject { Name = AppStrings.Text(scene.NameKey) };
        var profile = StudioSceneRenderer.BuildProfile(
            scene, _project.SelectedProfile, AppStrings.Text(scene.NameKey));
        profile.OutputScope = _appliedScope.Clone();
        project.Profiles.Add(profile);
        project.SelectedProfileId = profile.Id;
        return project;
    }

    private static void ClearAppliedProject()
    {
        try
        {
            File.Delete(AppliedProjectPath());
        }
        catch (Exception exception) when (IsExpected(exception))
        {
        }
    }

    private static string AppliedProjectPath() =>
        AppDataPaths.Combine("lighting-studio", "applied.json");
}
