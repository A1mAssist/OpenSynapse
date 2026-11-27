using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Core.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerHidDeviceRowViewModel : INotifyPropertyChanged, IDeviceBatteryRow
{
    private int? _batteryPercent;
    private bool? _isCharging;
    private readonly RazerHidDeviceConnection _connection;
    private RazerHidDeviceViewModel? _detail;
    private readonly Brush _statusBrush;
    private readonly Brush _partialStatusBrush;

    public RazerHidDeviceRowViewModel(
        RazerHidDeviceConnection connection,
        RazerHidDeviceRowViewModel? previous = null)
    {
        _connection = connection;
        BatteryReadConnection = connection;
        if (previous is not null &&
            connection.Definition.ProductId == previous.Connection.Definition.ProductId &&
            StringComparer.OrdinalIgnoreCase.Equals(connection.InstanceId, previous.Connection.InstanceId))
        {
            _batteryPercent = previous.BatteryPercent;
            _isCharging = previous.IsCharging;
            // Keep the last verified read endpoint while the receiver's mouse is asleep.
            if (!connection.IsReady && previous.CanReadBattery)
                BatteryReadConnection = previous.BatteryReadConnection;
        }
        _statusBrush = new SolidColorBrush(connection.EndpointState == RazerHidEndpointState.Resolved
            ? Color.FromArgb(255, 68, 214, 44)
            : Color.FromArgb(255, 240, 185, 90));
        _partialStatusBrush = new SolidColorBrush(Color.FromArgb(255, 240, 185, 90));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Brush SelectorBackground { get; private set; } = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    public RazerHidDeviceConnection Connection => _connection;
    internal RazerHidDeviceConnection BatteryReadConnection { get; }
    internal bool CanReadBattery => BatteryReadConnection.IsReady &&
        BatteryReadConnection.Capabilities.Contains(RazerHidBackendCapability.BatteryRead);
    internal RazerHidDeviceViewModel? Detail => _detail;
    public DeviceCategory HardwareCategory => _connection.Definition.Category;
    public string Name => _connection.Definition.DisplayName;
    public string Category => _connection.Definition.Category switch
    {
        DeviceCategory.Mouse => AppStrings.Text("Text_4B32CEE8"),
        DeviceCategory.Laptop => AppStrings.Text("Text_66E7127F"),
        DeviceCategory.Keyboard => AppStrings.Text("Text_7D4E2D8B"),
        DeviceCategory.MouseMat => AppStrings.Text("Text_A3A74479"),
        DeviceCategory.Monitor => AppStrings.Text("Text_2E486BCB"),
        DeviceCategory.Accessory => AppStrings.Text("Text_71E692AA"),
        _ => AppStrings.Text("Text_CAF15352"),
    };
    public string Identity => $"VID_1532 / PID_{_connection.Definition.ProductId:X4}";
    public bool CanPairNewDevice =>
        _connection.IsReady && RazerDualDongleCatalog.CanHost(_connection.Definition.ProductId);
    public string IconGlyph => _connection.Definition.Category switch
    {
        DeviceCategory.Laptop => "\uE7F8",
        DeviceCategory.Mouse => "\uE962",
        DeviceCategory.Keyboard => "\uE765",
        DeviceCategory.MouseMat => "\uE7F4",
        DeviceCategory.Monitor => "\uE7F4",
        _ => "\uE772",
    };
    public string Status => _detail?.StatusText ?? _connection.EndpointState switch
    {
        RazerHidEndpointState.Resolved when _connection.Capabilities.Count > 0 =>
            AppStrings.FormatText("ProtocolAvailableCount", _connection.Capabilities.Count, _connection.Capabilities.Count),
        RazerHidEndpointState.Resolved => AppStrings.Text("Text_C097B416"),
        RazerHidEndpointState.RecognizedButUnresolved => AppStrings.Text("Text_242E08F4"),
        _ => AppStrings.Text("Text_D3632B96"),
    };
    public Brush StatusBrush => _detail is { ProtocolTotalCount: > 0 } detail &&
        detail.ProtocolAvailableCount < detail.ProtocolTotalCount
            ? _partialStatusBrush
            : _statusBrush;
    public string Error => string.IsNullOrWhiteSpace(_connection.Error)
        ? string.Empty
        : _connection.EndpointState == RazerHidEndpointState.RecognizedButUnresolved
            ? AppStrings.FormatText("RazerHidDeviceNotReady", Category)
            : AppStrings.Text("RazerHidProtocolRescanRequired");
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsBatteryAvailable => _batteryPercent is not null;
    public string BatteryText => _batteryPercent is { } value ? $"{value}%" : string.Empty;
    public int? BatteryPercent => _batteryPercent;
    public bool? IsCharging => _isCharging;

    /// <summary>A failed read keeps the last value so the card does not blink on a timeout.</summary>
    internal void SetBatteryPercent(int? percent)
    {
        if (percent is null || percent == _batteryPercent)
        {
            return;
        }

        _batteryPercent = percent;
        _detail?.ApplyBatteryState(percent, null);
        PropertyChanged?.Invoke(this, new(nameof(IsBatteryAvailable)));
        PropertyChanged?.Invoke(this, new(nameof(BatteryText)));
        PropertyChanged?.Invoke(this, new(nameof(BatteryPercent)));
    }

    /// <summary>A failed read keeps the last state so the charging bolt does not blink.</summary>
    internal void SetCharging(bool? charging)
    {
        if (charging is null || charging == _isCharging)
        {
            return;
        }

        _isCharging = charging;
        _detail?.ApplyBatteryState(null, charging);
        PropertyChanged?.Invoke(this, new(nameof(IsCharging)));
    }

    public void AttachDetail(RazerHidDeviceViewModel detail)
    {
        if (_detail is not null) _detail.PropertyChanged -= DetailPropertyChanged;
        _detail = detail;
        _detail.PropertyChanged += DetailPropertyChanged;
        SetBatteryPercent(detail.BatteryPercent);
        SetCharging(detail.IsCharging);
        detail.ApplyBatteryState(_batteryPercent, _isCharging);
        PropertyChanged?.Invoke(this, new(nameof(Status)));
        PropertyChanged?.Invoke(this, new(nameof(StatusBrush)));
    }

    private void DetailPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RazerHidDeviceViewModel.BatteryText) or
            nameof(RazerHidDeviceViewModel.ChargingText))
        {
            SetBatteryPercent(_detail?.BatteryPercent);
            SetCharging(_detail?.IsCharging);
            _detail?.ApplyBatteryState(_batteryPercent, _isCharging);
        }
        if (args.PropertyName is nameof(RazerHidDeviceViewModel.StatusText) or
            nameof(RazerHidDeviceViewModel.ProtocolAvailableCount))
        {
            PropertyChanged?.Invoke(this, new(nameof(Status)));
            PropertyChanged?.Invoke(this, new(nameof(StatusBrush)));
        }
    }

    public void SetSelectorBackground(Brush background)
    {
        SelectorBackground = background;
        PropertyChanged?.Invoke(this, new(nameof(SelectorBackground)));
    }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
