#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App.Views;

public sealed partial class ProfilesPage
{
    internal static async Task<ProfilesPage> CreateProfileQaAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE")))
            throw new InvalidOperationException("Profile QA requires an isolated data instance.");
        var store = new ProfileStore(AppDataPaths.Combine("profile-create-qa.json"));
        var document = ProfileDocument.CreateDefault();
        var macros = new MacroWorkspace(() => document.Profiles[document.ActiveProfileName],
            _ => Task.FromResult(true), new LocalDiagnosticLog(), getDocument: () => document);
        var applied = 0;
        var workspace = new ProfilesWorkspace(store, () => document, value => document = value,
            () => document.Profiles[document.ActiveProfileName], _ => Task.FromResult(true), macros,
            (_, _) => { }, () => [BladePerformanceMode.Balanced], () => [], () => null, () => null,
            null, null, new SemaphoreSlim(1, 1), _ => { }, _ => { }, _ => { }, () => applied++);
        await workspace.LoadAsync(default);
        var page = new ProfilesPage();
        page.Loaded -= page.ProfilesPageLoaded;
        page.Unloaded += (_, _) => macros.Dispose();
        var root = new StackPanel { Spacing = 16, Background = (Brush)Application.Current.Resources["CanvasBrush"] };
        var picker = page.ActiveProfilePicker;
        var profileCard = ((StackPanel)page.PageScrollViewer.Content).Children.OfType<Border>().First();
        ((StackPanel)profileCard.Child).Children.OfType<Grid>().First().Children.Remove(picker);
        page.Content = root;
        root.Children.Add(new TextBlock { Text = "配置管理 · 隔离验收", FontSize = 26 });
        picker.DataContext = workspace;
        picker.IsEnabled = true;
        root.Children.Add(picker);
        var status = new TextBlock { Text = "READY", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "ProfileQaStatus");
        var create = new Button { Content = AppStrings.Text("Loc_9D032642E0/Content"),
            Style = (Style)Application.Current.Resources["PrimaryButtonStyle"] };
        AutomationProperties.SetAutomationId(create, "ProfileQaCreate");
        create.Click += async (_, _) =>
        {
            status.Text = "WORKING";
            var before = document.Profiles.Count;
            var previous = document.ActiveProfileName;
            var applyBefore = applied;
            await page.CreateProfileAsync(workspace, default);
            var saved = await store.LoadAsync();
            if (document.Profiles.Count == before + 1 && saved.Profiles.Count == before + 1 &&
                saved.ActiveProfileName == document.ActiveProfileName && applied == applyBefore + 1)
            {
                document.Profiles[document.ActiveProfileName].Global.Viper.DpiX = 2400;
                await store.SaveAsync(document);
                var reloaded = await store.LoadAsync();
                status.Text = reloaded.Profiles[document.ActiveProfileName].Global.Viper.DpiX == 2400 &&
                    reloaded.Profiles[previous].Global.Viper.DpiX != 2400
                    ? "PASS created, selected, edited and reloaded: " + document.ActiveProfileName
                    : "FAIL new profile settings were not independent";
            }
            else if (document.Profiles.Count == before && saved.Profiles.Count == before &&
                document.ActiveProfileName == previous && saved.ActiveProfileName == previous && applied == applyBefore)
                status.Text = "PASS unchanged: " + workspace.ProfileStatusText;
            else status.Text = "FAIL profile create or persistence";
        };
        root.Children.Add(create);
        AddOperation("ProfileQaClone", "Loc_212999B9CD/Content", clone: true);
        AddOperation("ProfileQaRename", "Loc_12964C8CFC/Content", clone: false);
        root.Children.Add(status);
        var refresh = new Button { Content = "刷新并检查当前配置" };
        AutomationProperties.SetAutomationId(refresh, "ProfileQaRefresh");
        refresh.Click += async (_, _) =>
        {
            var expected = picker.SelectedItem as string;
            workspace.RefreshProfileState();
            await Task.Delay(200);
            var saved = await store.LoadAsync();
            status.Text = expected == document.ActiveProfileName && expected == saved.ActiveProfileName &&
                expected == picker.SelectedItem as string
                ? "PASS selection persisted: " + expected
                : $"FAIL selection reverted: before={expected ?? "null"}, document={document.ActiveProfileName}, saved={saved.ActiveProfileName}, after={picker.SelectedItem ?? "null"}";
        };
        root.Children.Add(refresh);
        return page;

        void AddOperation(string id, string label, bool clone)
        {
            var button = new Button { Content = AppStrings.Text(label),
                Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) =>
            {
                status.Text = "WORKING";
                var before = document.Profiles.Count;
                var previous = document.ActiveProfileName;
                var dpi = document.Profiles[previous].Global.Viper.DpiX;
                var applyBefore = applied;
                if (clone) await page.CloneProfileAsync(workspace, default);
                else await page.RenameProfileAsync(workspace, default);
                var saved = await store.LoadAsync();
                var current = document.ActiveProfileName;
                if (current != previous && saved.ActiveProfileName == current &&
                    document.Profiles.Count == before + (clone ? 1 : 0) &&
                    saved.Profiles.Count == document.Profiles.Count && applied == applyBefore + 1 &&
                    saved.Profiles[current].Global.Viper.DpiX == dpi &&
                    saved.Profiles.ContainsKey(previous) == clone)
                {
                    document.Profiles[current].Global.Viper.DpiX = dpi + 100;
                    await store.SaveAsync(document);
                    var reloaded = await store.LoadAsync();
                    status.Text = reloaded.Profiles[current].Global.Viper.DpiX == dpi + 100 &&
                        (!clone || reloaded.Profiles[previous].Global.Viper.DpiX == dpi)
                        ? $"PASS {(clone ? "cloned" : "renamed")}, selected, edited and reloaded: {current}"
                        : "FAIL profile settings were not independent";
                }
                else if (document.Profiles.Count == before && saved.Profiles.Count == before &&
                    current == previous && saved.ActiveProfileName == previous && applied == applyBefore &&
                    saved.Profiles[previous].Global.Viper.DpiX == dpi)
                    status.Text = "PASS unchanged: " + workspace.ProfileStatusText;
                else status.Text = "FAIL profile operation or persistence";
            };
            root.Children.Add(button);
        }
    }
}
#endif
