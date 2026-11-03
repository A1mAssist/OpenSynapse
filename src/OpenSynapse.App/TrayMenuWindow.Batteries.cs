using Microsoft.UI.Xaml.Automation;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class TrayMenuWindow
{
    private async Task RefreshBatteryAsync()
    {
        if (BatteryReader is null || _batteryValues.Count == 0)
        {
            return;
        }

        _batteryRead?.Cancel();
        var cancellation = new CancellationTokenSource();
        _batteryRead = cancellation;
        IReadOnlyList<PeripheralBattery>? results = null;
        try
        {
            results = await BatteryReader(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("tray-battery", exception.ToString());
        }

        MenuRoot.DispatcherQueue.TryEnqueue(() =>
        {
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_batteryRead, cancellation) || !_menuOpen)
            {
                return;
            }

            var byKey = results?.ToLookup(device => device.Key, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < _batteryValues.Count; index++)
            {
                var matches = byKey?[_batteryKeys[index]].ToArray();
                var percent = matches is { Length: 1 } ? matches[0].Percent : null;
                var text = percent is int value ? $"{value}%" : "\u2014";
                _batteryValues[index].Text = text;
                AutomationProperties.SetName(_batteryRows[index], $"{_batteryNames[index].Text} {text}");
            }
        });
    }
}
