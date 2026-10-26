using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
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
/// Philips Hue bridge discovery, pairing and light control. Bridges are found by SSDP and
/// Philips' N-UPnP service; pairing and control use the documented CLIP v2 API.
/// </summary>
public sealed class PhilipsHueViewModel : INotifyPropertyChanged
{
    private readonly Action<string>? _diagnostic;
    private string _bridgeHost = string.Empty;
    private string? _appKey;
    private string _statusText = string.Empty;
    private string _bridgeSummary = string.Empty;
    private string _errorText = string.Empty;
    private bool _isBusy;
    private bool _hasScanned;
    private PhilipsHueLightRowViewModel? _selectedColorLight;
    private PhilipsHueBridge? _selectedBridge;
    private global::Windows.UI.Color _hueColor = global::Windows.UI.Color.FromArgb(255, 255, 255, 255);
    private static readonly Brush ReadyBrush = CreateStatusBrush(153, 221, 114);
    private static readonly Brush DiscoveredBrush = CreateStatusBrush(255, 181, 71);
    private static readonly Brush NotConnectedBrush = CreateStatusBrush(255, 107, 107);

    public PhilipsHueViewModel(Action<string>? diagnostic = null)
    {
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>MainWindow writes the bridge address and application key back to settings.</summary>
    public Action<string, string?>? Persist { get; set; }

    public ObservableCollection<PhilipsHueLightRowViewModel> Lights { get; } = new();
    public ObservableCollection<PhilipsHueBridge> Bridges { get; } = new();

    public string BridgeHost
    {
        get => _bridgeHost;
        set
        {
            if (!SetField(ref _bridgeHost, value)) return;
            if (_selectedBridge is { } selected &&
                !StringComparer.OrdinalIgnoreCase.Equals(selected.Host, value))
            {
                _selectedBridge = null;
                _appKey = null;
                Lights.Clear();
                OnPropertyChanged(nameof(SelectedBridge));
            }
            OnConnectionStateChanged();
        }
    }

    public PhilipsHueBridge? SelectedBridge
    {
        get => _selectedBridge;
        set
        {
            if (!SetField(ref _selectedBridge, value) || value is null) return;
            var changedHost = !StringComparer.OrdinalIgnoreCase.Equals(BridgeHost, value.Host);
            if (changedHost)
            {
                _appKey = null;
                Lights.Clear();
            }

            BridgeHost = value.Host;
            OnConnectionStateChanged();
            if (changedHost) Persist?.Invoke(BridgeHost, _appKey);
        }
    }

    public string BridgeSummary
    {
        get => _bridgeSummary;
        private set => SetField(ref _bridgeSummary, value);
    }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_appKey);
    public bool HasBridge => !string.IsNullOrWhiteSpace(BridgeHost);
    public bool IsReady => IsConfigured || HasBridge;
    public string DeviceStatusText => IsConfigured
        ? AppStrings.Text("HuePaired")
        : HasBridge
            ? AppStrings.Text("HueDiscovered")
            : AppStrings.Text("HueNotConnected");
    public string DeviceIdentity => SelectedBridge is { } bridge
        ? string.Join(" · ", new[] { bridge.Id, bridge.SoftwareVersion, bridge.Host }
            .Where(part => !string.IsNullOrWhiteSpace(part)))
        : BridgeHost;
    public Brush StatusBrush => IsConfigured
        ? ReadyBrush
        : HasBridge
            ? DiscoveredBrush
            : NotConnectedBrush;

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
        Bridges.Clear();
        if (!string.IsNullOrWhiteSpace(host))
        {
            Bridges.Add(new PhilipsHueBridge(host, null, null, null));
            _selectedBridge = Bridges[0];
            OnPropertyChanged(nameof(SelectedBridge));
        }
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(CanApplyColor));
        OnConnectionStateChanged();
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsConfigured && HasBridge)
        {
            await LoadLightsAsync(cancellationToken);
            return;
        }

        if (_hasScanned) return;
        await ScanAsync(cancellationToken);
    }

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        StatusText = AppStrings.Text("HueScanning");
        ErrorText = string.Empty;
        var found = 0;
        try
        {
            using var discovery = new PhilipsHueDiscovery(diagnostic: _diagnostic);
            var bridges = await discovery.DiscoverAsync(cancellationToken: cancellationToken);
            found = bridges.Count;
            Bridges.Clear();
            foreach (var bridge in bridges) Bridges.Add(bridge);

            if (bridges.Count == 0)
            {
                if (HasBridge)
                {
                    Bridges.Add(new PhilipsHueBridge(
                        BridgeHost,
                        _selectedBridge?.Id,
                        _selectedBridge?.Name,
                        _selectedBridge?.SoftwareVersion));
                    _selectedBridge = Bridges[0];
                    OnPropertyChanged(nameof(SelectedBridge));
                    OnConnectionStateChanged();
                }
                StatusText = AppStrings.Text("HueNoBridges");
                return;
            }

            SelectedBridge = bridges[0];
            StatusText = AppStrings.FormatText("HueFoundBridges", bridges.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            _hasScanned = true;
            EndOperation();
        }

        if (found > 0 && IsConfigured)
        {
            await LoadLightsAsync(cancellationToken);
        }
    }

    public async Task PairAsync(CancellationToken cancellationToken = default)
    {
        if (!HasBridge)
        {
            ErrorText = AppStrings.Text("HueBridgeRequired");
            return;
        }
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
                    OnConnectionStateChanged();
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
            var bridge = await client.GetBridgeAsync(cancellationToken);
            BridgeSummary = string.Join(" · ", new[] { bridge.Name, bridge.ModelId, bridge.SoftwareVersion }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
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

    private void OnConnectionStateChanged()
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(CanApplyColor));
        OnPropertyChanged(nameof(HasBridge));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(DeviceStatusText));
        OnPropertyChanged(nameof(DeviceIdentity));
        OnPropertyChanged(nameof(StatusBrush));
    }

    private static Brush CreateStatusBrush(byte red, byte green, byte blue) =>
        new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, red, green, blue));

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
