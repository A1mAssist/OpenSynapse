using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App.ViewModels;

public sealed class PhilipsHueLightRowViewModel : INotifyPropertyChanged
{
    private bool _isOn;
    private double _brightness;

    public PhilipsHueLightRowViewModel(PhilipsHueLight light)
    {
        Id = light.Id;
        Name = light.Name;
        _isOn = light.IsOn;
        _brightness = light.Brightness ?? 0;
        SupportsBrightness = light.Brightness is not null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public string Name { get; }
    public bool SupportsBrightness { get; }

    public bool IsOn
    {
        get => _isOn;
        set => SetField(ref _isOn, value);
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (SetField(ref _brightness, value)) OnPropertyChanged(nameof(BrightnessText));
        }
    }

    public string BrightnessText => $"{Math.Round(Brightness):0}%";
    public bool CanEdit => !IsBusy;

    public bool IsBusy { get; private set; }

    public void SetBusy(bool busy)
    {
        IsBusy = busy;
        OnPropertyChanged(nameof(CanEdit));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Philips Hue bridge pairing and light control. The middleware exposes no wire protocol for
/// the bridge, so this uses the documented CLIP v2 API against the address the user enters.
/// </summary>
public sealed class PhilipsHueViewModel : INotifyPropertyChanged
{
    private readonly Action<string>? _diagnostic;
    private string _bridgeHost = string.Empty;
    private string? _appKey;
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;
    private bool _isBusy;
    private PhilipsHueLightRowViewModel? _selectedColorLight;
    private global::Windows.UI.Color _hueColor = global::Windows.UI.Color.FromArgb(255, 255, 255, 255);

    public PhilipsHueViewModel(Action<string>? diagnostic = null)
    {
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>MainWindow writes the bridge address and application key back to settings.</summary>
    public Action<string, string?>? Persist { get; set; }

    public ObservableCollection<PhilipsHueLightRowViewModel> Lights { get; } = new();

    public string BridgeHost
    {
        get => _bridgeHost;
        set => SetField(ref _bridgeHost, value);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_appKey);

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetField(ref _errorText, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value)) OnPropertyChanged(nameof(CanEdit));
        }
    }

    public bool CanEdit => !IsBusy;

    public void Configure(string? host, string? appKey)
    {
        BridgeHost = host ?? string.Empty;
        _appKey = string.IsNullOrWhiteSpace(appKey) ? null : appKey;
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(CanApplyColor));
    }

    public async Task PairAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            using var client = new PhilipsHueClient(BridgeHost);
            var result = await client.PairAsync(cancellationToken);
            switch (result.Outcome)
            {
                case PhilipsHuePairingOutcome.Paired:
                    _appKey = result.AppKey;
                    OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(CanApplyColor));
                    Persist?.Invoke(client.Host, _appKey);
                    BridgeHost = client.Host;
                    StatusText = AppStrings.Text("HuePaired");
                    ErrorText = string.Empty;
                    break;
                case PhilipsHuePairingOutcome.LinkButtonNotPressed:
                    ErrorText = AppStrings.Text("HueLinkButtonHint");
                    break;
                default:
                    ErrorText = result.Error ?? AppStrings.Text("HuePairFailed");
                    break;
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            EndOperation();
        }

        if (IsConfigured)
        {
            await LoadLightsAsync(cancellationToken);
        }
    }

    public async Task LoadLightsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            ErrorText = AppStrings.Text("HuePairFirst");
            return;
        }
        if (!BeginOperation()) return;
        try
        {
            using var client = new PhilipsHueClient(BridgeHost, _appKey);
            var lights = await client.GetLightsAsync(cancellationToken);
            Lights.Clear();
            foreach (var light in lights)
            {
                Lights.Add(new PhilipsHueLightRowViewModel(light));
            }
            StatusText = AppStrings.FormatText("HueLightCount", Lights.Count);
            ErrorText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    public PhilipsHueLightRowViewModel? SelectedColorLight
    {
        get => _selectedColorLight;
        set
        {
            if (SetField(ref _selectedColorLight, value)) OnPropertyChanged(nameof(CanApplyColor));
        }
    }

    public global::Windows.UI.Color HueColor
    {
        get => _hueColor;
        set => SetField(ref _hueColor, value);
    }

    public bool CanApplyColor => IsConfigured && SelectedColorLight is not null;

    public async Task ApplyColorAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || SelectedColorLight is not { } light) return;
        var (x, y) = PhilipsHueColor.FromRgb(HueColor.R, HueColor.G, HueColor.B);
        if (!BeginOperation()) return;
        try
        {
            using var client = new PhilipsHueClient(BridgeHost, _appKey);
            await client.SetLightAsync(
                light.Id,
                new PhilipsHueLightUpdate(Color: (x, y)),
                cancellationToken);
            StatusText = AppStrings.Text("BlackSharkSaved");
            ErrorText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }
    public async Task ApplyLightAsync(
        PhilipsHueLightRowViewModel light,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || light.IsBusy) return;
        light.SetBusy(true);
        try
        {
            using var client = new PhilipsHueClient(BridgeHost, _appKey);
            await client.SetLightAsync(
                light.Id,
                new PhilipsHueLightUpdate(
                    On: light.IsOn,
                    Brightness: light.SupportsBrightness ? light.Brightness : null),
                cancellationToken);
            StatusText = AppStrings.Text("BlackSharkSaved");
            ErrorText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            light.SetBusy(false);
        }
    }

    private bool BeginOperation()
    {
        if (IsBusy) return false;
        ErrorText = string.Empty;
        IsBusy = true;
        return true;
    }

    private void EndOperation() => IsBusy = false;

    private static bool IsExpectedException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or InvalidOperationException or
            ArgumentException or UriFormatException or System.Text.Json.JsonException;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
