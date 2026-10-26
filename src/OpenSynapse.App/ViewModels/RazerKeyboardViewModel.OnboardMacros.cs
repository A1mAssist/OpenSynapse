using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class OnboardMacroRowViewModel
{
    public OnboardMacroRowViewModel(RazerOnboardMacro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        Title = $"{AppStrings.Text("RazerOnboardMacroRowPrefix")} {macro.Id}";
        Detail = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
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

public sealed partial class RazerKeyboardViewModel
{
    private readonly ObservableCollection<OnboardMacroRowViewModel> _onboardMacros = [];
    private string _onboardMacroStatusText = string.Empty;

    public ObservableCollection<OnboardMacroRowViewModel> OnboardMacros => _onboardMacros;

    public string OnboardMacroStatusText
    {
        get => _onboardMacroStatusText;
        private set => SetField(ref _onboardMacroStatusText, value);
    }

    public bool HasOnboardMacroSupport =>
        RazerKeyboardService.OnboardMacroProducts.Contains(_connection.ProductId);

    public Microsoft.UI.Xaml.Visibility OnboardMacroVisibility =>
        HasOnboardMacroSupport ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public async Task LoadOnboardMacrosAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            var service = OpenOnboardMacroService();
            var storage = await service.ProbeOnboardMacroStorageAsync(cancellationToken);
            if (storage is null)
            {
                OnboardMacroStatusText = AppStrings.Text("RazerOnboardMacroUnavailable");
                return;
            }

            var macros = await service.ReadOnboardMacrosAsync(cancellationToken);
            _onboardMacros.Clear();
            foreach (var macro in macros)
            {
                _onboardMacros.Add(new OnboardMacroRowViewModel(macro));
            }

            OnboardMacroStatusText = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                AppStrings.Text("RazerOnboardMacroLoaded"),
                _onboardMacros.Count);
            ErrorText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            OnboardMacroStatusText = AppStrings.Text("RazerOnboardMacroUnavailable");
            ErrorText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private RazerKeyboardService OpenOnboardMacroService() =>
        new(
            new RazerFeatureTransport(),
            _connection.DevicePath,
            TimeSpan.FromMilliseconds(20),
            _diagnostic);
}

