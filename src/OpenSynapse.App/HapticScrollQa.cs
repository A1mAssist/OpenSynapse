#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views.DevicePanels;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App;

internal static partial class HapticScrollQa
{
    internal static async Task<FrameworkElement> CreatePanel()
    {
        var transport = new HapticScrollQaTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00A7)!,
            "qa-haptic", "qa-haptic", RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.HapticScrollRead, RazerHidBackendCapability.HapticScrollWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        var profile = new ProfileDefinition();
        var saves = 0;
        var rejectSave = false;
        var model = new RazerHidDeviceViewModel(service, connection, getActiveProfile: () => profile,
            saveProfileAsync: _ => { saves++; var ok = !rejectSave; rejectSave = false; return Task.FromResult(ok); });
        await model.LoadHapticScrollAsync();
        var root = new StackPanel { Spacing = 12 };
        var status = new TextBlock { Text = "READY USB 00A7 fake transport", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "HapticQaStatus");
        root.Children.Add(status);
        var actions = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 2,
            ItemWidth = 270, ItemHeight = 40 };
        root.Children.Add(actions);
        var panel = new HapticScrollPanel { DataContext = model };
        root.Children.Add(panel);
        var baseline = transport.State.Clone();
        var profileBaseline = (HapticScrollProfile?)null;
        var armed = false;
        Button("HapticQaInspect", "Inspect device and saved profile", () =>
        {
            var saved = Saved();
            status.Text = $"writes={transport.Writes}; saves={saves}; stage={transport.State.ActiveStage}; " +
                $"enabled={string.Join(',', transport.State.StageTypes)}; upSteps={transport.State.Custom!.Metadata[5]}; " +
                $"upTension={transport.State.Custom.Metadata[6]}; savedMatches={saved?.ContentEquals(transport.State)}; error={model.ErrorText}";
            return Task.CompletedTask;
        });
        Button("HapticQaFailWrite", "Fail next third write after mutation", () => { Arm(); transport.FailAfterWrites = 3; return Task.CompletedTask; });
        Button("HapticQaFailReadback", "Corrupt next curve readback", () => { Arm(); transport.CorruptNextReadback = true; return Task.CompletedTask; });
        Button("HapticQaFailSave", "Reject next profile save", () => { Arm(); rejectSave = true; return Task.CompletedTask; });
        Button("HapticQaCheckRollback", "Check complete recovery", () =>
        {
            var saved = Saved();
            var profileMatches = profileBaseline is null ? saved is null : saved is not null && profileBaseline.ContentEquals(saved);
            status.Text = armed && baseline.ContentEquals(transport.State) && profileMatches && !string.IsNullOrEmpty(model.ErrorText)
                ? "PASS full metadata/generator/CW/CCW/velocity/stages and profile restored"
                : "FAIL recovery check: " + model.ErrorText;
            armed = false;
            return Task.CompletedTask;
        });
        Button("HapticQaRead", "Read current fake device", async () => { await model.LoadHapticScrollAsync(); status.Text = "READ " + model.ErrorText; });
        Button("HapticQaShowCurve", "Show editable curve", () =>
        {
            FindSteps(panel)?.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
            return Task.CompletedTask;
        });
        Localized.RefreshTree(root);
        return root;

        static FrameworkElement? FindSteps(DependencyObject element)
        {
            if (element is FrameworkElement field && AutomationProperties.GetAutomationId(field) == "HapticSteps") return field;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
                if (FindSteps(VisualTreeHelper.GetChild(element, index)) is { } found) return found;
            return null;
        }

        HapticScrollProfile? Saved() => profile.Devices.GetValueOrDefault(ProfileResolver.GetDeviceKey(connection.ToDescriptor()))?.Viper.HapticScroll;
        void Arm()
        {
            baseline = transport.State.Clone(); profileBaseline = Saved()?.Clone(); armed = true;
            status.Text = "ARMED: edit a curve value, then check recovery";
        }
        void Button(string id, string title, Func<Task> action)
        {
            var button = new Button { Content = title, Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) => { try { await action(); } catch (Exception error) { status.Text = "FAIL " + error; } };
            actions.Children.Add(button);
        }
    }
}
#endif
