using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerHeadsetAudioDeviceRowViewModel : INotifyPropertyChanged, IDeviceBatteryRow
{
    private static readonly Brush ConnectedBrush = new SolidColorBrush(Color.FromArgb(255, 68, 214, 44));
    private static readonly Brush PartialBrush = new SolidColorBrush(Color.FromArgb(255, 255, 185, 0));
    private static readonly Brush FailedBrush = new SolidColorBrush(Color.FromArgb(255, 232, 80, 80));
    private string _capabilityText = AppStrings.Text("BlackSharkStatusConnected");
    private string _status = AppStrings.Text("BlackSharkStatusConnected");
    private Brush _statusBrush = ConnectedBrush;
    private string _error = string.Empty;
    private int? _batteryPercent;

    public RazerHeadsetAudioDeviceRowViewModel(RazerHeadsetAudioConnection connection)
    {
        Connection = connection;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RazerHeadsetAudioConnection Connection { get; }
    public DeviceCategory HardwareCategory => RazerHeadsetAudioService.GetCategory(Connection.ProductId);
    public string Name => Connection.Name;
    public string Category => AppStrings.Text(HardwareCategory switch
    {
        DeviceCategory.Speaker => "RazerSpeakerCategory",
        DeviceCategory.Microphone => "RazerMicrophoneCategory",
        _ => "BlackSharkHeadsetCategory",
    });
    public string Identity => $"VID_1532 / PID_{Connection.ProductId:X4}";
    public string IconGlyph => "\uE7F6";
    public string Status => _status;
    public string CapabilityText => _capabilityText;
    public Brush StatusBrush => _statusBrush;
    public string Error => _error;
    public bool IsBatteryAvailable => _batteryPercent is not null;
    public string BatteryText => _batteryPercent is { } value ? $"{value}%" : string.Empty;
    public int? BatteryPercent => _batteryPercent;
    internal RazerHeadsetProbeState ProbeState { get; private set; }

    internal void SetProbeResult(
        RazerHeadsetProbeState state,
        string capabilityText,
        string error,
        int? batteryPercent = null)
    {
        ProbeState = state;
        // A probe that timed out must not blank a value the previous probe read.
        if (batteryPercent is not null)
        {
            _batteryPercent = batteryPercent;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBatteryAvailable)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BatteryText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BatteryPercent)));
        }
        _capabilityText = capabilityText;
        _error = error;
        (_status, _statusBrush) = state switch
        {
            RazerHeadsetProbeState.Failed =>
                (AppStrings.Text("BlackSharkCapabilityReadFailed"), FailedBrush),
            RazerHeadsetProbeState.Partial =>
                (AppStrings.Text("BlackSharkStatusPartial"), PartialBrush),
            RazerHeadsetProbeState.NotAvailable =>
                (AppStrings.Text("BlackSharkComponentRequired"), PartialBrush),
            RazerHeadsetProbeState.ControlInterfaceMissing =>
                (AppStrings.Text("RazerHeadsetControlInterfaceMissing"), PartialBrush),
            _ => (AppStrings.Text("BlackSharkStatusConnected"), ConnectedBrush),
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CapabilityText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Error)));
    }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
