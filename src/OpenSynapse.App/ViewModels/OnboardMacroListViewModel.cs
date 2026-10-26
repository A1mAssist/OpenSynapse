using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed class OnboardMacroRowViewModel
{
    public OnboardMacroRowViewModel(RazerOnboardMacro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        Title = $"{AppStrings.Text("RazerOnboardMacroRowPrefix")} {macro.Id}";
        Detail = string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.Text("RazerOnboardMacroRowDetail"),
            macro.Items.Count,
            macro.DataSize);
        Summary = Describe(macro.Items);
    }

    public string Title { get; }
    public string Detail { get; }
    public string Summary { get; }

    private static string Describe(IReadOnlyList<OnboardMacroItem> items)
    {
        if (items.Count == 0)
        {
            return AppStrings.Text("RazerOnboardMacroEmptyStream");
        }

        var parts = items.Take(16).Select(Describe).ToArray();
        var text = string.Join(" · ", parts);
        return items.Count > parts.Length ? text + " …" : text;
    }

    private static string Describe(OnboardMacroItem item) => item.Kind switch
    {
        OnboardMacroItemKind.Key =>
            $"{DescribeKey(item.Scancode)}{(item.Released ? "↑" : "↓")}",
        OnboardMacroItemKind.MouseButton =>
            $"{AppStrings.Text("RazerOnboardMacroMouseButton")} {item.MouseButton}{(item.MousePressed ? "↓" : "↑")}",
        OnboardMacroItemKind.Wheel =>
            $"{AppStrings.Text("RazerOnboardMacroWheel")} {(item.WheelDelta > 0 ? "+" : string.Empty)}{item.WheelDelta}",
        OnboardMacroItemKind.Delay => $"{item.DelayMilliseconds} ms",
        OnboardMacroItemKind.Cursor => $"({item.X}, {item.Y})",
        _ => "?",
    };

    private static string DescribeKey(int scancode)
    {
        var input = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            ScanCode = scancode & 0xFF,
            Extended = scancode > 0xFF,
            VirtualKey = (int)MapVirtualKey((uint)(scancode & 0xFF), MapVkScanToVk),
        };
        return MacroInputFormatter.Describe(input);
    }

    private const uint MapVkScanToVk = 1;

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}

/// <summary>
/// Shared read-only list of the macros stored in a device's onboard memory. The card is
/// only visible for products that declare onboard macro support.
/// </summary>
public sealed class OnboardMacroListViewModel : INotifyPropertyChanged
{
    private bool _isLoading;
    private string _statusText = string.Empty;

    public OnboardMacroListViewModel(bool supported)
    {
        Visibility = supported
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<OnboardMacroRowViewModel> Rows { get; } = [];

    public Microsoft.UI.Xaml.Visibility Visibility { get; }

    public bool CanLoad => !_isLoading;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    public async Task LoadAsync(
        Func<RazerOnboardMacroService> serviceFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceFactory);
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanLoad)));
        try
        {
            var service = serviceFactory();
            if (await service.ProbeStorageAsync(cancellationToken).ConfigureAwait(true) is null)
            {
                StatusText = AppStrings.Text("RazerOnboardMacroUnavailable");
                return;
            }

            var macros = await service.ReadAllAsync(cancellationToken).ConfigureAwait(true);
            Rows.Clear();
            foreach (var macro in macros)
            {
                Rows.Add(new OnboardMacroRowViewModel(macro));
            }

            StatusText = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.Text("RazerOnboardMacroLoaded"),
                Rows.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            InvalidOperationException or NotSupportedException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            StatusText = AppStrings.Text("RazerOnboardMacroUnavailable");
        }
        finally
        {
            _isLoading = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanLoad)));
        }
    }
}
