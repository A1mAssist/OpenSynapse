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
        page.Content = root;
        root.Children.Add(new TextBlock { Text = "配置新建 · 隔离验收", FontSize = 26 });
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
        root.Children.Add(status);
        return page;
    }
}
#endif
