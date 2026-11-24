using Microsoft.UI.Xaml;
using OpenSynapse.App.Views;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private PresetEditorWindow? _presetEditorWindow;

    internal void OpenPresetEditorWindow(
        StudioEffectParams? parameters = null,
        StudioPresetConfig? preset = null)
    {
        if (_presetEditorWindow is not null)
        {
            _presetEditorWindow.BringToFront();
            return;
        }

        var path = preset is not null && !StudioPresetLibrary.IsBuiltInId(preset.Id)
            ? StudioPresetLibrary.PathForId(preset.Id)
            : null;
        PresetEditorWindow window;
        try
        {
            window = new PresetEditorWindow(
                AppWindow,
                RootLayout.XamlRoot?.RasterizationScale ?? 1d,
                _lightingStudioPage?.ViewModel,
                preset,
                path,
                saved =>
                {
                    if (parameters is not null)
                    {
                        parameters.Preset = saved.Id;
                        parameters.PresetDefinition = saved;
                        _lightingStudioPage?.ViewModel?.NotifyEdited();
                        _lightingStudioPage?.RefreshPresetInspector();
                        return;
                    }

                    _lightingStudioPage?.ViewModel?.AddPresetLayer(saved);
                });
        }
        catch (Exception exception)
        {
            // A bad preset must not take the whole app down; the caller can retry.
            CrashReportWriter.TryWrite("preset-editor-window", exception, null);
            throw;
        }

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_presetEditorWindow, window)) _presetEditorWindow = null;
        };
        _presetEditorWindow = window;
        window.Activate();
        window.BringToFront();
    }
}
