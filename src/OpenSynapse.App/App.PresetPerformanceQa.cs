#if DEBUG
using Microsoft.UI.Xaml;
using OpenSynapse.App.Views;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App;

public partial class App
{
    private Window? _presetPerformanceQaWindow;

    private bool TryLaunchPresetPerformanceQa()
    {
        var output = Environment.GetEnvironmentVariable("OPENSSYNAPSE_PRESET_PERFORMANCE_QA_OUT");
        if (string.IsNullOrWhiteSpace(output)) return false;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE")))
            throw new InvalidOperationException("Preset QA requires an isolated data instance.");
        var owner = new Window();
        var editor = new PresetEditorWindow(owner.AppWindow, 1, null, StudioPresetCatalog.Default.All.First(), null);
        _presetPerformanceQaWindow = editor;
        editor.EnablePerformanceQa(output);
        editor.Activate();
        return true;
    }
}
#endif
