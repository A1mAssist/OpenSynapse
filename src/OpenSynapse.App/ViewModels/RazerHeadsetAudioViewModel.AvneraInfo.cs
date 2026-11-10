using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private bool _hasSerial;
    private bool _hasEdition;
    private bool _hasLink;
    private bool _hasDock;
    private string _serialText = "--";
    private string _editionText = "--";
    private string _linkStatusText = "--";
    private string _dockFirmwareText = "--";
    private string _dockSerialText = "--";
    private string _dockEditionText = "--";

    public string SerialText { get => _serialText; private set => SetField(ref _serialText, value); }
    public string EditionText { get => _editionText; private set => SetField(ref _editionText, value); }
    public string LinkStatusText { get => _linkStatusText; private set => SetField(ref _linkStatusText, value); }
    public string DockFirmwareText { get => _dockFirmwareText; private set => SetField(ref _dockFirmwareText, value); }
    public string DockSerialText { get => _dockSerialText; private set => SetField(ref _dockSerialText, value); }
    public string DockEditionText { get => _dockEditionText; private set => SetField(ref _dockEditionText, value); }
    public Visibility AvneraInfoVisibility => _hasSerial || _hasEdition || _hasLink
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility DockInfoVisibility => _hasDock ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyAvneraInfo(RazerHeadsetAudioState state)
    {
        if (state.SerialNumber is { Length: > 0 } serial)
        {
            SerialText = serial;
            _hasSerial = true;
        }
        if (state.Edition is byte edition)
        {
            EditionText = edition.ToString();
            _hasEdition = true;
        }
        if (state.LinkStatus is byte link)
        {
            LinkStatusText = LinkStatusName(link);
            _hasLink = true;
        }
        if (state.DockFirmware is { } dockFirmware)
        {
            DockFirmwareText = dockFirmware.ToString();
            _hasDock = true;
        }
        if (state.DockSerialNumber is { Length: > 0 } dockSerial)
        {
            DockSerialText = dockSerial;
            _hasDock = true;
        }
        if (state.DockEdition is byte dockEdition)
        {
            DockEditionText = dockEdition.ToString();
            _hasDock = true;
        }
        if (state.ChromaEffect is RazerChromaEffect effect)
        {
            var effectIndex = Array.IndexOf(LightingEffects, effect);
            if (effectIndex >= 0)
            {
                SelectedLightingEffectIndex = effectIndex;
            }
            _hasLighting = true;
        }
        if (state.ChromaBrightness is byte brightness)
        {
            Brightness = brightness;
            _hasLighting = true;
        }
        if (state.ChromaColors is { Count: >= 3 } colors)
        {
            PrimaryColor = Color.FromArgb(255, colors[0], colors[1], colors[2]);
        }
        if (_hasLighting)
        {
            OnPropertyChanged(nameof(MatrixLightingVisibility));
        }
        OnPropertyChanged(nameof(AvneraInfoVisibility));
        OnPropertyChanged(nameof(DockInfoVisibility));
    }

    private static string LinkStatusName(byte status) => status switch
    {
        1 => "Active",
        2 => "Pair",
        3 => "Searching",
        5 => "ReSync",
        6 => "Initial scan",
        8 => "Pair cancelled",
        9 => "Active search",
        11 => "Active pair",
        _ => $"0x{status:X2}",
    };
}
