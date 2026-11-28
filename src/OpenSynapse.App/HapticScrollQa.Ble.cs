#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views.DevicePanels;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App;

internal static partial class HapticScrollQa
{
    internal static async Task<FrameworkElement> CreateBlePanel()
    {
        var transport = new BleTransport();
        var service = new RazerBleDeviceService(new RazerBleSession(transport), "Naga BLE QA", productId: 0x00A9);
        service.MappingProfileId = 4;
        var document = ProfileDocument.CreateDefault();
        var rejectSave = false;
        var saves = 0;
        var model = RazerBleDeviceViewModel.CreateHapticQa(service, document, _ =>
        { saves++; var result = !rejectSave; rejectSave = false; return Task.FromResult(result); });
        await model.LoadHapticStageAsync();
        var root = new StackPanel { Spacing = 12 };
        var status = new TextBlock { Text = "READY BLE 00A9 profile=4 stages=3", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "HapticBleQaStatus");
        root.Children.Add(status);
        root.Children.Add(new BleHapticScrollPanel { DataContext = model });
        byte previous = 1;
        int? savedPrevious = null;
        Button("HapticBleQaInspect", "Inspect BLE stage and saved profile", () =>
        {
            status.Text = $"stage={transport.Active}; writes={transport.Writes}; saves={saves}; " +
                $"saved={Saved()?.HapticLevel}; profile={Saved()?.HapticProfileId}; lastWireProfile={transport.LastProfile}; error={model.ErrorText}";
            return Task.CompletedTask;
        });
        Button("HapticBleQaFailSave", "Reject next BLE profile save", () =>
        { previous = transport.Active; savedPrevious = Saved()?.HapticLevel; rejectSave = true; status.Text = "ARMED: change BLE stage"; return Task.CompletedTask; });
        Button("HapticBleQaCheckRollback", "Check BLE recovery", () =>
        {
            status.Text = transport.Active == previous && Saved()?.HapticLevel == savedPrevious && model.HasError
                ? "PASS BLE stage and saved profile restored" : "FAIL BLE recovery " + model.ErrorText;
            return Task.CompletedTask;
        });
        Button("HapticBleQaReplay", "Replay saved BLE stage after external change", async () =>
        {
            transport.Active = 1;
            await model.ApplyPersistedProfileAsync(PowerSourceKind.Unknown);
            await model.LoadHapticStageAsync();
            status.Text = transport.Active == Saved()?.HapticLevel && transport.LastProfile == 4
                ? "PASS BLE saved stage replayed to profile 4" : "FAIL BLE profile replay";
        });
        Localized.RefreshTree(root);
        return root;

        MouseProfileSettings? Saved() => document.Devices.GetValueOrDefault(model.ProfileDeviceKey)?.Viper;
        void Button(string id, string title, Func<Task> action)
        {
            var button = new Button { Content = title, Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += async (_, _) => { try { await action(); } catch (Exception error) { status.Text = "FAIL " + error; } };
            root.Children.Add(button);
        }
    }

    private sealed class BleTransport : IRazerBleTransport
    {
        public byte Active { get; set; } = 1;
        public byte LastProfile { get; private set; }
        public int Writes { get; private set; }
        public Task<byte[]?> ExchangeAsync(ReadOnlyMemory<byte> frame, TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = frame.ToArray();
            if (data[4] != 0x15) throw new InvalidOperationException("BLE QA only accepts Hapticore commands.");
            LastProfile = data[6];
            if (data[5] == 0) { Active = data[8]; Writes++; }
            var response = new byte[21]; response[0] = data[0]; response[1] = 1; response[4] = 0x15;
            response[5] = data[5]; response[7] = 2; response[20] = data[5] == 0x81 ? (byte)3 : Active;
            return Task.FromResult<byte[]?>(response);
        }
        public void Dispose() { }
    }
}
#endif
