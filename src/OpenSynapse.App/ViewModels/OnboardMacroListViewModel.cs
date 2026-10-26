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
/// Shared onboard macro card: reads the slots stored on the device and writes one of the
/// local macros into a slot. The card is only visible for products that declare onboard
/// macro support.
/// </summary>
public sealed class OnboardMacroListViewModel : INotifyPropertyChanged
{
    private bool _isLoading;
    private string _statusText = string.Empty;
    private int _selectedLocalMacroIndex = -1;
    private double _targetMacroId = 1;

    public OnboardMacroListViewModel(bool supported)
    {
        Visibility = supported
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<OnboardMacroRowViewModel> Rows { get; } = [];

    public ObservableCollection<string> LocalMacroNames { get; } = [];

    public Microsoft.UI.Xaml.Visibility Visibility { get; }

    /// <summary>
    /// Local macro library supplied by the window, so this card does not own a second
    /// copy of the macro store.
    /// </summary>
    public Func<IReadOnlyList<MacroDefinition>>? LocalMacroProvider { get; set; }

    public bool CanLoad => !_isLoading;

    public bool CanWrite => !_isLoading && _selectedLocalMacroIndex >= 0;

    public int SelectedLocalMacroIndex
    {
        get => _selectedLocalMacroIndex;
        set
        {
            if (_selectedLocalMacroIndex == value)
            {
                return;
            }

            _selectedLocalMacroIndex = value;
            Raise(nameof(SelectedLocalMacroIndex));
            Raise(nameof(CanWrite));
        }
    }

    public double TargetMacroId
    {
        get => _targetMacroId;
        set
        {
            var clamped = Math.Clamp(value, 1, byte.MaxValue);
            if (_targetMacroId == clamped)
            {
                return;
            }

            _targetMacroId = clamped;
            Raise(nameof(TargetMacroId));
        }
    }

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
            Raise(nameof(StatusText));
        }
    }

    public void RefreshLocalMacros()
    {
        var previous = SelectedLocalMacroIndex;
        LocalMacroNames.Clear();
        if (LocalMacroProvider?.Invoke() is { } macros)
        {
            foreach (var macro in macros)
            {
                LocalMacroNames.Add(string.IsNullOrWhiteSpace(macro.Name) ? macro.Id : macro.Name);
            }
        }

        SelectedLocalMacroIndex = previous >= 0 && previous < LocalMacroNames.Count
            ? previous
            : LocalMacroNames.Count > 0 ? 0 : -1;
        Raise(nameof(CanWrite));
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
        Raise(nameof(CanLoad));
        Raise(nameof(CanWrite));
        try
        {
            RefreshLocalMacros();
            await ReloadAsync(serviceFactory(), cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusText = AppStrings.Text("RazerOnboardMacroUnavailable");
        }
        finally
        {
            _isLoading = false;
            Raise(nameof(CanLoad));
            Raise(nameof(CanWrite));
        }
    }

    public async Task WriteSelectedAsync(
        Func<RazerOnboardMacroService> serviceFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceFactory);
        if (_isLoading)
        {
            return;
        }

        var macros = LocalMacroProvider?.Invoke() ?? [];
        if (SelectedLocalMacroIndex < 0 || SelectedLocalMacroIndex >= macros.Count)
        {
            StatusText = AppStrings.Text("RazerOnboardMacroWriteFailed");
            return;
        }

        var macro = macros[SelectedLocalMacroIndex];
        if (macro.Steps.Any(step => step.Kind is not (MacroStepKind.Input or MacroStepKind.Delay)))
        {
            StatusText = AppStrings.Text("RazerOnboardMacroWriteUnsupported");
            return;
        }

        _isLoading = true;
        Raise(nameof(CanLoad));
        Raise(nameof(CanWrite));
        try
        {
            var items = OnboardMacroTranslation.FromSteps(macro.Steps);
            var service = serviceFactory();
            await service.WriteAsync((byte)Math.Round(TargetMacroId), items, cancellationToken).ConfigureAwait(true);
            await ReloadAsync(service, cancellationToken).ConfigureAwait(true);
            StatusText = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.Text("RazerOnboardMacroWriteDone"),
                (byte)Math.Round(TargetMacroId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusText = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.Text("RazerOnboardMacroWriteFailedWithReason"),
                exception.Message);
        }
        finally
        {
            _isLoading = false;
            Raise(nameof(CanLoad));
            Raise(nameof(CanWrite));
        }
    }

    private async Task ReloadAsync(RazerOnboardMacroService service, CancellationToken cancellationToken)
    {
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

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception;
}

