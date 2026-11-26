using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerApBleHeadsetRowViewModel(RazerBleDevice device)
    : INotifyPropertyChanged, IDeviceBatteryRow
{
    private int? _batteryPercent;

    public string BatteryText => _batteryPercent is { } value ? $"{value}%" : string.Empty;
    public bool IsBatteryAvailable => _batteryPercent is not null;
    public event PropertyChangedEventHandler? PropertyChanged;

    public RazerBleDevice Device { get; } = device;
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? "Razer Audio BLE" : Device.Name;
    public string Identity => Device.ProductId == 0 ? "BLE" : $"BLE · 1532:{Device.ProductId:X4}";
    public string IconGlyph => "\uE7F6";
    public DeviceCategory HardwareCategory => DeviceCategory.Headset;
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    public int? BatteryPercent => _batteryPercent;

    internal void SetBatteryPercent(int? percent)
    {
        if (percent is null || percent == _batteryPercent)
        {
            return;
        }

        _batteryPercent = percent;
        PropertyChanged?.Invoke(this, new(nameof(IsBatteryAvailable)));
        PropertyChanged?.Invoke(this, new(nameof(BatteryText)));
        PropertyChanged?.Invoke(this, new(nameof(BatteryPercent)));
    }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed class RazerApBleHeadsetViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    // AP-BLE speaker wire values; some families expose only a subset.
    private static readonly (byte Value, string Label)[] EqPresets =
    [
        (0, "Default"),
        (1, "Game"),
        (2, "Music"),
        (3, "Movie"),
        (4, "Apex Legends"),
        (5, "CS:GO"),
        (6, "Valorant"),
        (7, "Fortnite"),
        (8, "Call of Duty"),
        // APBLEPresetEq.custom_speaker case 9 has rawValue 0x1F (31).
        (31, "Custom"),
    ];

    private static readonly (RazerApBleChromaEffect Value, string Label)[] ChromaEffects =
    [
        (RazerApBleChromaEffect.Off, "Off"),
        (RazerApBleChromaEffect.Static, "Static"),
        (RazerApBleChromaEffect.Breathing, "Breathing"),
        (RazerApBleChromaEffect.Spectrum, "Spectrum"),
        (RazerApBleChromaEffect.Wave, "Wave"),
    ];

    private static readonly int[] AutoPowerOffMinutes = [0, 5, 15, 30, 45, 60];

    private readonly RazerBleDevice _device;
    private RazerApBleHeadsetService? _service;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string _batteryText = "--";
    private int? _batteryPercent;
    private string _firmwareText = "—";
    private string _serialText = "—";
    private string _connectionText = "—";
    private string _chargingText = "—";
    private int _ancModeIndex;
    private double _ancLevel = 5;
    private double _volume;
    private double _sidetone;
    private bool _gameMode;
    private bool _thxSpatial;
    private bool _multipoint;
    private bool _dnd;
    private bool _hasAnc;
    private bool _hasVolume;
    private bool _hasSidetone;
    private bool _hasEq;
    private bool _hasGameMode;
    private bool _hasThx;
    private bool _hasMultipoint;
    private bool _hasDnd;
    private bool _hasAutoPowerOff;
    private bool _hasChroma;
    private int _selectedEqPresetIndex;
    private readonly OpenSynapse.Core.Audio.EqualizerReadback _eqReadback = new();
    private double _chromaBrightness = 100;
    private Color _chromaPrimaryColor = Color.FromArgb(255, 255, 0, 255);
    private int _selectedChromaEffectIndex;
    private int _selectedAutoPowerOffIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public RazerApBleHeadsetViewModel(RazerBleDevice device)
    {
        _device = device;
    }

    public RazerBleDevice Device => _device;
    public string Name => string.IsNullOrWhiteSpace(_device.Name) ? "Razer Audio BLE" : _device.Name;
    public string Identity => _device.ProductId == 0
        ? "BLE"
        : $"BLE · 1532:{_device.ProductId:X4}";
    public string BatteryText => _batteryText;
    public int? BatteryPercent => _batteryPercent;
    public string FirmwareText => _firmwareText;
    public string SerialText => _serialText;
    public string ConnectionText => _connectionText;
    public string ChargingText => _chargingText;
    public string ErrorText => _errorText;
    public string OperationStatusText => _operationStatusText;
    public bool HasError => !string.IsNullOrWhiteSpace(_errorText);
    public bool IsBusy => _isBusy;
    public bool CanRefresh => !_isBusy;
    public bool CanEdit => !_isBusy && _service is not null;

    public Visibility AncVisibility => Visible(_hasAnc);
    public Visibility VolumeVisibility => Visible(_hasVolume);
    public Visibility SidetoneVisibility => Visible(_hasSidetone);
    public Visibility EqVisibility => Visible(_hasEq);
    public Visibility GameModeVisibility => Visible(_hasGameMode);
    public Visibility ThxVisibility => Visible(_hasThx);
    public Visibility MultipointVisibility => Visible(_hasMultipoint);
    public Visibility DndVisibility => Visible(_hasDnd);
    public Visibility AutoPowerOffVisibility => Visible(_hasAutoPowerOff);
    public Visibility ChromaVisibility => Visible(_hasChroma);

    public IReadOnlyList<string> AncOptions => ["Off", "Ambient", "On"];
    public IReadOnlyList<string> EqPresetOptions => EqPresets.Select(item => item.Label).ToArray();
    public IReadOnlyList<string> ChromaEffectOptions => ChromaEffects.Select(item => item.Label).ToArray();
    public IReadOnlyList<string> AutoPowerOffOptions =>
        AutoPowerOffMinutes.Select(minutes => minutes == 0 ? "Off" : $"{minutes} min").ToArray();

    public int AncModeIndex
    {
        get => _ancModeIndex;
        set => SetField(ref _ancModeIndex, Math.Clamp(value, 0, 2));
    }

    public double AncLevel
    {
        get => _ancLevel;
        set
        {
            if (SetField(ref _ancLevel, Math.Clamp(value, 1, 10)))
            {
                OnPropertyChanged(nameof(AncLevelText));
            }
        }
    }

    public string AncLevelText => $"{(int)Math.Round(AncLevel)} / 10";

    public double Volume
    {
        get => _volume;
        set
        {
            if (SetField(ref _volume, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(VolumeText));
            }
        }
    }

    public string VolumeText => $"{(int)Math.Round(Volume)}%";

    public double Sidetone
    {
        get => _sidetone;
        set
        {
            if (SetField(ref _sidetone, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(SidetoneText));
            }
        }
    }

    public string SidetoneText => $"{(int)Math.Round(Sidetone)}%";

    public bool GameMode
    {
        get => _gameMode;
        set => SetField(ref _gameMode, value);
    }

    public bool ThxSpatial
    {
        get => _thxSpatial;
        set => SetField(ref _thxSpatial, value);
    }

    public bool Multipoint
    {
        get => _multipoint;
        set => SetField(ref _multipoint, value);
    }

    public bool Dnd
    {
        get => _dnd;
        set => SetField(ref _dnd, value);
    }

    public int SelectedEqPresetIndex
    {
        get => _selectedEqPresetIndex;
        set => SetField(ref _selectedEqPresetIndex, Math.Clamp(value, 0, EqPresets.Length - 1));
    }

    public double ChromaBrightness
    {
        get => _chromaBrightness;
        set
        {
            if (SetField(ref _chromaBrightness, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(ChromaBrightnessText));
            }
        }
    }

    public string ChromaBrightnessText => $"{(int)Math.Round(ChromaBrightness)}%";

    public Color ChromaPrimaryColor
    {
        get => _chromaPrimaryColor;
        set => SetField(ref _chromaPrimaryColor, value);
    }

    public int SelectedChromaEffectIndex
    {
        get => _selectedChromaEffectIndex;
        set => SetField(ref _selectedChromaEffectIndex, Math.Clamp(value, 0, ChromaEffects.Length - 1));
    }

    public int SelectedAutoPowerOffIndex
    {
        get => _selectedAutoPowerOffIndex;
        set => SetField(
            ref _selectedAutoPowerOffIndex,
            Math.Clamp(value, 0, AutoPowerOffMinutes.Length - 1));
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _eqReadback.Invalidate();
        SetBusy();
        try
        {
            _service ??= await RazerApBleHeadsetService.ConnectAsync(
                _device.Id, message => { }, cancellationToken).ConfigureAwait(true);
            var state = await _service.ReadStateAsync(cancellationToken).ConfigureAwait(true);
            Apply(state);
            SetOperation(string.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetError(exception.Message);
        }
        finally
        {
            _isBusy = false;
            SetBusy();
        }
    }

    public Task ApplyAncAsync(CancellationToken cancellationToken = default)
    {
        var (mode, level) = AncModeIndex switch
        {
            0 => ((byte)0, (byte)0),
            1 => ((byte)0x50, (byte)0),
            _ => ((byte)1, (byte)Math.Clamp((int)Math.Round(AncLevel), 1, 10)),
        };
        return WriteAsync(service => service.SetAncAsync(mode, level, cancellationToken), cancellationToken);
    }

    public Task ApplyVolumeAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(
            service => service.SetVolumeAsync((int)Math.Round(Volume), cancellationToken),
            cancellationToken);

    public Task ApplySidetoneAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(
            service => service.SetSidetoneAsync((int)Math.Round(Sidetone), cancellationToken),
            cancellationToken);

    public Task ApplyGameModeAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(service => service.SetGameModeAsync(GameMode, cancellationToken), cancellationToken);

    public Task ApplyThxAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(service => service.SetThxAsync(ThxSpatial, cancellationToken), cancellationToken);

    public Task ApplyMultipointAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(service => service.SetMultipointAsync(Multipoint, cancellationToken), cancellationToken);

    public Task ApplyDndAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(service => service.SetDndAsync(Dnd, cancellationToken), cancellationToken);

    public Task ApplyEqPresetAsync(CancellationToken cancellationToken = default)
    {
        var index = SelectedEqPresetIndex;
        return WriteAsync(async service =>
        {
            await service.SetEqPresetAsync(EqPresets[index].Value, cancellationToken: cancellationToken);
            _eqReadback.Accept(index.ToString(), []);
        }, cancellationToken);
    }

    internal async Task PollEqualizerAsync(Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        if (!isCurrent() || _isBusy || !_hasEq || _service is not { } service ||
            _eqReadback.Begin(SelectedEqPresetIndex.ToString(), []) is not { } revision) return;
        try
        {
            var preset = await service.ReadSpeakerEqPresetAsync(cancellationToken);
            var index = Array.FindIndex(EqPresets, item => item.Value == preset);
            if (index < 0 || !isCurrent() || _isBusy || !ReferenceEquals(service, _service) ||
                !_eqReadback.CanApply(revision, SelectedEqPresetIndex.ToString(), [])) return;
            SelectedEqPresetIndex = index;
            _eqReadback.Accept(index.ToString(), []);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the last good preset when a wireless read fails.
        }
    }

    public Task ApplyChromaBrightnessAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(
            service => service.SetChromaBrightnessAsync(
                (int)Math.Round(ChromaBrightness), cancellationToken),
            cancellationToken);

    public Task ApplyChromaEffectAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(
            service => service.SetChromaEffectAsync(
                new RazerApBleEffectRequest(
                    ChromaEffects[SelectedChromaEffectIndex].Value,
                    0,
                    0,
                    ChromaEffects[SelectedChromaEffectIndex].Value
                        is RazerApBleChromaEffect.Static or RazerApBleChromaEffect.Breathing
                        ? [ChromaPrimaryColor.R, ChromaPrimaryColor.G, ChromaPrimaryColor.B]
                        : []),
                cancellationToken),
            cancellationToken);

    public Task ApplyAutoPowerOffAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(
            service => service.SetAutoPowerOffAsync(
                AutoPowerOffMinutes[SelectedAutoPowerOffIndex] * 60, cancellationToken),
            cancellationToken);

    private async Task WriteAsync(
        Func<RazerApBleHeadsetService, Task> write,
        CancellationToken cancellationToken)
    {
        if (_service is null || _isBusy)
        {
            return;
        }

        _isBusy = true;
        _eqReadback.Invalidate();
        SetBusy();
        try
        {
            await write(_service).ConfigureAwait(true);
            SetOperation("已应用");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetError(exception.Message);
        }
        finally
        {
            _isBusy = false;
            SetBusy();
        }
    }

    private void Apply(RazerApBleState state)
    {
        _hasAnc = state.AncMode is not null;
        _hasVolume = state.Volume is not null;
        _hasSidetone = state.Sidetone is not null;
        _hasEq = state.EqPreset is not null;
        _hasGameMode = state.GameMode is not null;
        _hasThx = state.ThxSpatial is not null;
        _hasMultipoint = state.Multipoint is not null;
        _hasDnd = state.Dnd is not null;
        _hasAutoPowerOff = state.AutoPowerOffSeconds is not null;
        _hasChroma = state.ChromaBrightness is not null || state.ChromaEffect is not null;
        _batteryPercent = state.BatteryPercent;
        _batteryText = state.BatteryPercent is { } battery ? $"{battery}%" : "--";
        _firmwareText = state.Firmware ?? "—";
        _serialText = state.Serial ?? "—";
        _connectionText = state.ConnectionStatus is { } connection
            ? FormatConnection(connection)
            : "—";
        _chargingText = state.Charging switch
        {
            true => "Charging",
            false => "Not charging",
            null => "—",
        };
        if (state.AncMode is { } mode)
        {
            _ancModeIndex = mode switch
            {
                0x50 => 1,
                0 => state.AncLevel is > 0 ? 1 : 0,
                _ => 2,
            };
        }
        if (state.AncLevel is { } level) _ancLevel = (byte)Math.Max(1, (int)level);
        if (state.Volume is { } volume) _volume = volume;
        if (state.Sidetone is { } sidetone) _sidetone = sidetone;
        if (state.GameMode is { } gameMode) _gameMode = gameMode;
        if (state.ThxSpatial is { } thx) _thxSpatial = thx;
        if (state.Multipoint is { } multipoint) _multipoint = multipoint;
        if (state.Dnd is { } dnd) _dnd = dnd;
        if (state.EqPreset is { } preset)
        {
            var index = Array.FindIndex(EqPresets, item => item.Value == preset);
            if (index >= 0) _selectedEqPresetIndex = index;
            if (index >= 0) _eqReadback.Accept(index.ToString(), []);
        }
        if (state.ChromaBrightness is { } brightness) _chromaBrightness = brightness;
        if (state.ChromaEffect is { } effect)
        {
            var index = Array.FindIndex(ChromaEffects, item => item.Value == effect);
            if (index >= 0) _selectedChromaEffectIndex = index;
        }
        if (state.AutoPowerOffSeconds is { } seconds)
        {
            var index = Array.IndexOf(AutoPowerOffMinutes, seconds / 60);
            if (index >= 0) _selectedAutoPowerOffIndex = index;
        }

        _errorText = state.Errors.Count == 0 ? string.Empty : string.Join(" · ", state.Errors);
        OnPropertyChanged(string.Empty);
    }

    private void SetBusy()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanEdit));
    }

    private void SetOperation(string text) => SetField(ref _operationStatusText, text, nameof(OperationStatusText));

    private void SetError(string text) => SetField(ref _errorText, text, nameof(ErrorText));

    private static string FormatConnection(ushort status)
    {
        var transport = (status >> 8) switch
        {
            1 => "2.4 GHz",
            2 => "Bluetooth",
            3 => "Bluetooth (mobile)",
            _ => null,
        };
        var state = (status & 0xFF) != 0 ? "Connected" : "Disconnected";
        return transport is null ? state : $"{state} · {transport}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(property);
        if (property == nameof(ErrorText)) OnPropertyChanged(nameof(HasError));
        return true;
    }

    private void OnPropertyChanged(string? property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    private static Visibility Visible(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        _service?.Dispose();
        _service = null;
    }
}
