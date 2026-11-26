#if DEBUG
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App;

public partial class App
{
    private Window? _studioScopeQaWindow;

    // This entry point returns before production discovery, services, hooks or transports exist.
    private bool TryLaunchStudioScopeQa()
    {
        var output = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_SCOPE_QA_OUT");
        if (string.IsNullOrWhiteSpace(output)) return false;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE")))
            throw new InvalidOperationException("Studio QA requires an isolated data instance.");
        var controller = new StudioScopeQaController();
        var page = new LightingStudioPage();
        var devices = new List<StudioDeviceOption>
        {
            new StudioDeviceOption("Blade keyboard", null, "qa-blade", 0x029D, 6, 16,
                IsBlade: true, BladeFrameMode: StudioBladeFrameMode.Direct),
            new StudioDeviceOption("External keyboard", null, "qa-keyboard", 0x0282, 6, 22),
            new StudioDeviceOption("Laptop stand", null, "qa-stand", 0x0F08, 1, 8),
        };
        var restored = new List<string>();
        page.Initialize(controller, () => [], () => devices,
            restoreAfterExternal: ids =>
            {
                if (!page.DispatcherQueue.HasThreadAccess)
                    throw new InvalidOperationException("Card restore ran outside the UI dispatcher.");
                restored.AddRange(ids);
                return Task.CompletedTask;
            });
        _studioScopeQaWindow = new Window { Title = "OpenSynapse Studio QA", Content = page };
        _studioScopeQaWindow.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1800, 1200));
        page.Background = (Brush)Resources["CanvasBrush"];
        page.Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            void Check(bool condition, string label)
            {
                if (!condition) throw new InvalidOperationException(label);
                checks.Add(label);
            }
            try
            {
                var vm = page.ViewModel!;
                var layer = vm.SelectedLayer!;
                layer.Params.Effect = StudioEffectKind.Ambient;
                layer.Params.AmbientMapping = StudioAmbientMapping.PerDevice;
                vm.NotifyEdited();
                page.OnNavigatedTo();
                Check(!vm.IsEditing, "Browse is the default");
                await vm.ApplySelectedAsync(["qa-blade"]);
                Check(controller.Applies == 1 && restored.Count == 0, "Only selected Blade is acquired");
                vm.SelectedDevice = devices[1];
                Check(vm.GetOutputChoices().Single(choice => choice.IsSelected).InstanceId == "qa-blade",
                    "Changing focus preserves the frozen output target");
                await vm.ApplySelectedAsync(["qa-blade", "qa-keyboard"]);
                Check(controller.Applies == 1 && controller.Stops == 0,
                    "Retained Blade keeps its frame source without a restart");
                vm.SetOutputScope(new StudioOutputScope { Mode = StudioOutputScopeMode.Selected });
                Check(!vm.CanApply && controller.Stops == 0,
                    "Empty pending selection disables Apply without changing live ownership");
                vm.Undo();
                Check(vm.GetOutputChoices().Count(choice => choice.IsSelected) == 2,
                    "Undo restores the explicit output identities");
                await vm.ApplySelectedAsync(["qa-keyboard"]);
                Check(controller.Stops == 1 && restored.SequenceEqual(["qa-blade"]),
                    "Departing Blade alone restores its card lighting");
                await vm.StopAsync();
                Check(controller.Stops == 1 && restored.Count == 1, "Stop does not restore a never-owned device");
                await Task.Run(() => vm.RestoreOwnedTargetsAsync([]));
                Check(restored.Count == 1, "Worker-thread retries restore cards on the UI dispatcher");
                await vm.ApplySelectedAsync(["qa-blade"]);
                controller.FailNextStop = true;
                await vm.StopAsync();
                Check(controller.Stops == 2 && restored.Count == 1,
                    "Failed Blade stop does not restore while its frame source may still be running");
                await vm.StopAsync();
                Check(controller.Stops == 3 && restored.SequenceEqual(["qa-blade", "qa-blade"]),
                    "Retry stops the retained Blade source before restoring its card");
                var unavailable = new StudioDeviceOption("Unavailable keyboard", null, "qa-offline", 0x0282, 6, 22)
                {
                    IsOutputAvailable = false,
                };
                devices.Add(unavailable);
                vm.RefreshDevices();
                var unavailableScope = new StudioOutputScope { Mode = StudioOutputScopeMode.Selected };
                unavailableScope.Targets.Add(new StudioOutputTarget("qa-offline", unavailable.Name));
                vm.SetOutputScope(unavailableScope);
                Check(!vm.CanApply && !vm.GetOutputChoices().Single(choice => choice.IsSelected).IsOnline,
                    "An enumerated unavailable endpoint cannot enable Apply");
                devices.Remove(unavailable);
                vm.RefreshDevices();
                Check(vm.GetOutputChoices().Single(choice => choice.IsSelected).InstanceId == "qa-offline",
                    "An offline choice retains its exact identity after discovery removes it");
                await vm.ApplySelectedAsync(["qa-blade"]);
                vm.SetOutputScope(new StudioOutputScope { Mode = StudioOutputScopeMode.Selected });
                var applyCount = controller.Applies;
                await vm.SuspendForExternalAsync();
                Check(!vm.IsApplied && vm.HasSuspendedOutput && LightingStudioViewModel.HasAppliedProject,
                    "Temporary takeover preserves the applied project for recovery");
                await vm.SuspendForExternalAsync();
                await vm.ResumeAfterExternalAsync();
                Check(vm.IsApplied && !vm.HasSuspendedOutput && controller.Applies == applyCount + 1,
                    "External exit resumes without a manual Stop or Apply");
                Check(!vm.CanApply && vm.AppliedOutputSummary.Contains("Blade keyboard"),
                    "Resume uses the applied devices without applying a pending selection");
                await vm.SuspendForExternalAsync();
                await vm.StopAsync();
                await vm.ResumeAfterExternalAsync();
                Check(!vm.IsApplied && !vm.HasSuspendedOutput,
                    "Explicit Stop cancels pending automatic recovery");
                vm.SelectScene(StudioSceneCatalog.All[0]);
                await vm.ApplySelectedAsync(["qa-blade"]);
                var selectedScene = vm.SelectedSceneCard;
                await vm.SuspendForExternalAsync();
                await vm.ResumeAfterExternalAsync();
                Check(vm.IsApplied && ReferenceEquals(selectedScene, vm.SelectedSceneCard),
                    "Temporary game takeover preserves the selected scene");
                await vm.StopAsync();
                vm.SelectedDevice = devices[0];
                vm.SetOutputScope(new StudioOutputScope());
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { status = "passed", checks }));
                if (Environment.GetEnvironmentVariable("OPENSSYNAPSE_CHROMA_UI_QA") == "1")
                {
                    var settingsPage = new SettingsPage();
                    var settingsContent = (Microsoft.UI.Xaml.Controls.Panel)
                        ((Microsoft.UI.Xaml.Controls.ScrollViewer)settingsPage.Content).Content;
                    var card = settingsContent.Children.OfType<Microsoft.UI.Xaml.Controls.Border>()
                        .Single(element => element.Name == "ChromaRestCard");
                    settingsContent.Children.Remove(card);
                    Localized.RefreshTree(card);
                    _studioScopeQaWindow.Content = new Microsoft.UI.Xaml.Controls.Grid
                    {
                        Padding = new Thickness(24),
                        Background = (Brush)Resources["CanvasBrush"],
                        Children = { card },
                    };
                    _studioScopeQaWindow.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1100, 760));
                }
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { status = "failed", checks, error = error.ToString() }));
            }
        };
        _studioScopeQaWindow.Closed += (_, _) => page.ViewModel?.Dispose();
        _studioScopeQaWindow.Activate();
        return true;
    }

    private sealed class StudioScopeQaController : IBladeLightingController
    {
        public event Action<string, Exception>? RestoreFailed { add { } remove { } }
        public Task RuntimeCompletion => Task.CompletedTask;
        public int Applies { get; private set; }
        public int Stops { get; private set; }
        public bool FailNextStop { get; set; }
        public Task ApplyAsync(IReadOnlyList<DeviceDescriptor> devices, BladeLightingEffect effect,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync()
        {
            Stops++;
            if (FailNextStop)
            {
                FailNextStop = false;
                throw new InvalidOperationException("Simulated Blade stop failure.");
            }
            return Task.CompletedTask;
        }
        public Task SetDisplayAvailableAsync(bool available, IReadOnlyList<DeviceDescriptor>? devices = null) => Task.CompletedTask;
        public Task ApplyExternalAsync(IReadOnlyList<DeviceDescriptor> devices, ISoftwareLightingFrameSource source,
            bool restorePersistentEffect = true, CancellationToken cancellationToken = default)
        { Applies++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#endif
