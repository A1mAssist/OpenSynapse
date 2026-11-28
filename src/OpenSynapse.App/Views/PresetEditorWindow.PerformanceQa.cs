#if DEBUG
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace OpenSynapse.App.Views;

public sealed partial class PresetEditorWindow
{
    private int _qaPreviewRenders;

    internal void EnablePerformanceQa(string output)
    {
        RootLayout.Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            void Check(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
                checks.Add(message);
            }
            try
            {
                await Task.Delay(750);
                SetPreviewMode(1);
                await Task.Delay(150);
                Check(_previewTimer.IsEnabled && _qaPreviewRenders > 0, "playing renders the world preview");
                TogglePlayback();
                var paused = _qaPreviewRenders;
                var time = _seconds;
                await Task.Delay(250);
                Check(!_previewTimer.IsEnabled && _qaPreviewRenders == paused && _seconds == time, "paused preview does no periodic work");
                TimelineSlider.Value = 2;
                Check(_qaPreviewRenders > paused && _seconds == 2, "paused timeline edits redraw immediately");
                TogglePlayback();
                var presenter = (OverlappedPresenter)AppWindow.Presenter;
                presenter.Minimize();
                await Task.Delay(250);
                var hidden = _qaPreviewRenders;
                time = _seconds;
                await Task.Delay(250);
                Check(!_previewTimer.IsEnabled && _qaPreviewRenders == hidden && _seconds == time, "minimized preview stops work and time");
                presenter.Restore(activateWindow: false);
                await Task.Delay(250);
                Check(_previewTimer.IsEnabled && _qaPreviewRenders > hidden && _seconds > time, "restore resumes playing without a time jump");
                TogglePlayback();
                presenter.Minimize();
                await Task.Delay(100);
                presenter.Restore(activateWindow: false);
                await Task.Delay(150);
                Check(!_previewTimer.IsEnabled && !_playing, "restore preserves paused state");
                Title = "OpenSynapse Preset QA PASS";
                File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, checks }));
            }
            catch (Exception exception)
            {
                Title = "OpenSynapse Preset QA FAIL";
                File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, checks, error = exception.ToString() }));
            }
        };
    }
}
#endif
