using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>Discovery, selected device lifetime and UI state for USB accessory backends.</summary>
public sealed class UsbAccessoriesWorkspace : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly RazerHidWorkspace _razerHid;
    private readonly MacroWorkspace _macros;
    private readonly Func<ProfileDocument>? _getProfile;
    private readonly Func<CancellationToken, Task<bool>>? _saveProfile;
    private readonly Action? _refreshDeviceUi;

    internal UsbAccessoriesWorkspace(
        LocalDiagnosticLog log,
        RazerHidWorkspace razerHid,
        MacroWorkspace macros,
        Func<ProfileDocument>? getActiveProfile = null,
        Func<CancellationToken, Task<bool>>? saveProfileAsync = null,
        Action? refreshDeviceUi = null)
    {
        _diagnosticLog = log;
        _razerHid = razerHid;
        _macros = macros;
        _getProfile = getActiveProfile;
        _saveProfile = saveProfileAsync;
        _refreshDeviceUi = refreshDeviceUi;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private RazerHeadsetAudioDeviceRowViewModel? _selectedHeadsetAudioDevice;
    private RazerHeadsetAudioViewModel? _selectedHeadsetAudio;
    private RazerHeadsetAudioConnection? _headsetPowerConnection;
    private bool? _headsetPowerConnected;
    private long _lastHeadsetPowerCheck;
    internal Action<string>? ForgetAccessoryDiagnostic { get; set; }

    public ObservableCollection<RazerHeadsetAudioDeviceRowViewModel> HeadsetAudioDevices { get; } = new();

    public RazerHeadsetAudioViewModel? SelectedHeadsetAudio
    {
        get => _selectedHeadsetAudio;
        private set => SetField(ref _selectedHeadsetAudio, value);
    }

    internal async Task RefreshHeadsetAudioAsync(CancellationToken cancellationToken)
    {
        RazerHeadsetAudioConnection? connection;
        try
        {
            connection = await RazerHeadsetAudioService.FindAsync(
                message => _diagnosticLog.TryWrite("headset-audio", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("headset-audio", $"discovery failed: {exception}");
            return;
        }

        if (connection is null)
        {
            _headsetPowerConnection = null;
            _headsetPowerConnected = null;
            if (HeadsetAudioDevices.Count == 0 && SelectedHeadsetAudio is null) return;
            HeadsetAudioDevices.Clear();
            _selectedHeadsetAudioDevice = null;
            var stale = SelectedHeadsetAudio;
            SelectedHeadsetAudio = null;
            if (stale is not null) await stale.DisposeAsync();
            return;
        }

        // These headsets keep their HID endpoint enumerated after the device is powered
        // off, so the radio link is the only reliable power signal. A failed read falls
        // back to the old behaviour instead of hiding a live device.
        if (connection.SupportsWirelessPowerSignal)
        {
            var connected = await TryReadHeadsetPowerAsync(connection, cancellationToken);
            _headsetPowerConnection = connection;
            _headsetPowerConnected = connected;
            if (connected == false)
            {
                await HideHeadsetAsync(connection);
                return;
            }
            if (connected is null)
            {
                _diagnosticLog.TryWrite("headset-audio",
                    "wireless connection state unavailable; keeping the headset card visible");
            }
        }
        else
        {
            _headsetPowerConnection = null;
            _headsetPowerConnected = null;
        }

        if (HeadsetAudioDevices.Count == 1 &&
            StringComparer.OrdinalIgnoreCase.Equals(
                HeadsetAudioDevices[0].Connection.DevicePath, connection.DevicePath) &&
            HeadsetAudioDevices[0].Connection.ProductId == connection.ProductId &&
            HeadsetAudioDevices[0].ProbeState is RazerHeadsetProbeState.Ready or RazerHeadsetProbeState.Partial)
        {
            return;
        }

        if (HeadsetAudioDevices.Count == 1 &&
            StringComparer.OrdinalIgnoreCase.Equals(
                HeadsetAudioDevices[0].Connection.DevicePath, connection.DevicePath) &&
            HeadsetAudioDevices[0].Connection.ProductId == connection.ProductId)
        {
            await ProbeHeadsetRowAsync(HeadsetAudioDevices[0], connection, cancellationToken);
        }
        else
        {
            HeadsetAudioDevices.Clear();
            var row = new RazerHeadsetAudioDeviceRowViewModel(connection);
            HeadsetAudioDevices.Add(row);
            await ProbeHeadsetRowAsync(row, connection, cancellationToken);
            _refreshDeviceUi?.Invoke();
        }

        if (_selectedHeadsetAudioDevice is null ||
            !StringComparer.OrdinalIgnoreCase.Equals(
                _selectedHeadsetAudioDevice.Connection.DevicePath, connection.DevicePath))
        {
            _selectedHeadsetAudioDevice = null;
            var stale = SelectedHeadsetAudio;
            SelectedHeadsetAudio = null;
            if (stale is not null) await stale.DisposeAsync();
        }

    }

    private async Task ProbeHeadsetRowAsync(
        RazerHeadsetAudioDeviceRowViewModel row,
        RazerHeadsetAudioConnection connection,
        CancellationToken cancellationToken)
    {
        var probe = new RazerHeadsetAudioViewModel(
            connection,
            message => _diagnosticLog.TryWrite("headset-audio", message),
            _razerHid.CreateKrakenLighting(connection.ProductId),
            _getProfile,
            _saveProfile);
        try
        {
            await probe.LoadAsync(cancellationToken);
            row.SetProbeResult(
                probe.ProbeState, probe.CapabilityText, probe.ErrorText, probe.BatteryPercent);
        }
        finally
        {
            await probe.DisposeAsync();
        }
    }

    private async Task HideHeadsetAsync(RazerHeadsetAudioConnection connection)
    {
        _headsetPowerConnection = connection;
        _headsetPowerConnected = false;
        _lastHeadsetPowerCheck = Environment.TickCount64;

        if (HeadsetAudioDevices.Count == 0 && SelectedHeadsetAudio is null)
        {
            ForgetAccessoryDiagnostic?.Invoke("headset-audio:" + connection.DevicePath);
            return;
        }

        HeadsetAudioDevices.Clear();
        _selectedHeadsetAudioDevice = null;
        var stale = SelectedHeadsetAudio;
        SelectedHeadsetAudio = null;
        if (stale is not null) await stale.DisposeAsync();

        ForgetAccessoryDiagnostic?.Invoke("headset-audio:" + connection.DevicePath);
        _refreshDeviceUi?.Invoke();
    }

    internal async Task PollHeadsetPowerAsync(CancellationToken cancellationToken)
    {
        if (_headsetPowerConnection is not { } connection) return;
        if (Environment.TickCount64 - _lastHeadsetPowerCheck < 5000) return;
        if (SelectedHeadsetAudio is { IsBusy: true }) return;

        _lastHeadsetPowerCheck = Environment.TickCount64;
        var connected = await TryReadHeadsetPowerAsync(connection, cancellationToken);
        if (connected is null) return;

        if (connected == false)
        {
            await HideHeadsetAsync(connection);
            return;
        }

        if (_headsetPowerConnected != true)
        {
            _headsetPowerConnected = true;
            await RefreshHeadsetAudioAsync(cancellationToken);
        }
    }

    private async Task<bool?> TryReadHeadsetPowerAsync(
        RazerHeadsetAudioConnection connection,
        CancellationToken cancellationToken)
    {
        if (!connection.SupportsWirelessPowerSignal)
        {
            return null;
        }

        try
        {
            return await ReadWirelessPowerAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("headset-audio", $"power check failed: {exception.Message}");
            return null;
        }
    }

    private async Task<bool?> ReadWirelessPowerAsync(
        RazerHeadsetAudioConnection connection,
        CancellationToken cancellationToken)
    {
        void Diagnostic(string message) => _diagnosticLog.TryWrite("headset-audio", message);

        switch (connection.Transport)
        {
            case RazerHeadsetAudioTransport.AvneraFeature65:
                await using (var avnera = new RazerAvneraHeadsetService(connection.DevicePath, Diagnostic))
                {
                    return await avnera.TryReadLinkStatusAsync(cancellationToken).ConfigureAwait(false);
                }
            case RazerHeadsetAudioTransport.Valkyrie64:
            case RazerHeadsetAudioTransport.Billie64:
                await using (var valkyrie = new RazerValkyrieHeadsetService(
                    connection.ProductId, connection.DevicePath, Diagnostic))
                {
                    return await valkyrie.TryReadWirelessConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
            default:
                await using (var mxic = new RazerMxicHeadsetService(
                    connection.ProductId, connection.DevicePath, Diagnostic))
                {
                    return await mxic.TryReadWirelessConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
        }
    }

    public async Task SelectHeadsetAudioAsync(
        RazerHeadsetAudioDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedHeadsetAudioDevice is not null &&
            SelectedHeadsetAudio is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedHeadsetAudioDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            return;
        }

        _selectedHeadsetAudioDevice = row;
        var lighting = _razerHid.CreateKrakenLighting(row.Connection.ProductId);

        if (SelectedHeadsetAudio is { } previous)
        {
            await previous.DisposeAsync();
        }

        SelectedHeadsetAudio = new RazerHeadsetAudioViewModel(
            row.Connection,
            message => _diagnosticLog.TryWrite("headset-audio", message),
            lighting,
            _getProfile,
            _saveProfile);
        await SelectedHeadsetAudio.LoadAsync(cancellationToken);
        row.SetProbeResult(
            SelectedHeadsetAudio.ProbeState,
            SelectedHeadsetAudio.CapabilityText,
            SelectedHeadsetAudio.ErrorText,
            SelectedHeadsetAudio.BatteryPercent);
    }

    internal async Task RefreshSelectedHeadsetAudioAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedHeadsetAudio is not { } selected) return;
        await selected.LoadAsync(cancellationToken);
        if (_selectedHeadsetAudioDevice is { } row)
        {
            row.SetProbeResult(
                selected.ProbeState, selected.CapabilityText, selected.ErrorText, selected.BatteryPercent);
        }
    }

    private RazerKeyboardDeviceRowViewModel? _selectedKeyboardDevice;
    private RazerKeyboardViewModel? _selectedKeyboard;

    public ObservableCollection<RazerKeyboardDeviceRowViewModel> KeyboardDevices { get; } = new();

    public RazerKeyboardViewModel? SelectedKeyboard
    {
        get => _selectedKeyboard;
        private set => SetField(ref _selectedKeyboard, value);
    }

    internal async Task RefreshKeyboardsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RazerKeyboardConnection> connections;
        try
        {
            connections = await RazerKeyboardService.FindAllAsync(
                message => _diagnosticLog.TryWrite("keyboard", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("keyboard", $"discovery failed: {exception}");
            return;
        }

        if (connections.Count == 0)
        {
            if (KeyboardDevices.Count == 0 && SelectedKeyboard is null) return;
            KeyboardDevices.Clear();
            _selectedKeyboardDevice = null;
            SelectedKeyboard = null;
            return;
        }

        if (KeyboardDevices.Count == connections.Count &&
            KeyboardDevices.Zip(connections).All(pair =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    pair.First.Connection.DevicePath, pair.Second.DevicePath) &&
                pair.First.Connection.ProductId == pair.Second.ProductId))
        {
            return;
        }

        var selectedPath = _selectedKeyboardDevice?.Connection.DevicePath;
        KeyboardDevices.Clear();
        foreach (var connection in connections)
        {
            KeyboardDevices.Add(new RazerKeyboardDeviceRowViewModel(
                connection,
                message => _diagnosticLog.TryWrite("keyboard", message),
                _getProfile,
                _saveProfile));
        }

        foreach (var row in KeyboardDevices)
        {
            await row.EnsureLoadedAsync(cancellationToken);
        }

        _selectedKeyboardDevice = KeyboardDevices.FirstOrDefault(row =>
            StringComparer.OrdinalIgnoreCase.Equals(row.Connection.DevicePath, selectedPath));
        if (_selectedKeyboardDevice is null)
        {
            SelectedKeyboard = null;
        }
    }

    public async Task SelectKeyboardAsync(
        RazerKeyboardDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedKeyboardDevice is not null &&
            SelectedKeyboard is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedKeyboardDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            return;
        }

        _selectedKeyboardDevice = row;
        SelectedKeyboard = row.Detail;
        SelectedKeyboard.Onboard.LocalMacroProvider =
            () => _macros.Items.Select(macro => macro.Definition).ToArray();
        SelectedKeyboard.SoftwareMacroBinder = _macros.TryApplyMacroBindingAsync;
        SelectedKeyboard.SoftwareMacroClearer = _macros.TryClearMacroTriggerAsync;
        await row.EnsureLoadedAsync(cancellationToken);
    }

    private RazerGamepadDeviceRowViewModel? _selectedGamepadDevice;
    private RazerGamepadViewModel? _selectedGamepad;

    public ObservableCollection<RazerGamepadDeviceRowViewModel> GamepadDevices { get; } = new();

    public RazerGamepadViewModel? SelectedGamepad
    {
        get => _selectedGamepad;
        private set => SetField(ref _selectedGamepad, value);
    }

    internal async Task RefreshGamepadsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RazerGamepadConnection> connections;
        try
        {
            connections = await RazerGamepadService.FindAllAsync(
                message => _diagnosticLog.TryWrite("gamepad", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("gamepad", $"discovery failed: {exception}");
            return;
        }

        if (connections.Count == 0)
        {
            if (GamepadDevices.Count == 0 && SelectedGamepad is null) return;
            GamepadDevices.Clear();
            _selectedGamepadDevice = null;
            SelectedGamepad = null;
            return;
        }

        if (GamepadDevices.Count == connections.Count &&
            connections.Zip(GamepadDevices).All(pair =>
                pair.First.ProductId == pair.Second.Connection.ProductId &&
                StringComparer.OrdinalIgnoreCase.Equals(
                    pair.First.DevicePath, pair.Second.Connection.DevicePath)))
        {
            return;
        }

        var previousPath = _selectedGamepadDevice?.Connection.DevicePath;
        GamepadDevices.Clear();
        foreach (var connection in connections)
        {
            GamepadDevices.Add(new RazerGamepadDeviceRowViewModel(connection));
        }

        _selectedGamepadDevice = null;
        SelectedGamepad = null;
        var row = GamepadDevices.FirstOrDefault(candidate => previousPath is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(candidate.Connection.DevicePath, previousPath))
            ?? GamepadDevices[0];
        await SelectGamepadAsync(row, cancellationToken);
    }

    public async Task SelectGamepadAsync(
        RazerGamepadDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedGamepadDevice is not null &&
            SelectedGamepad is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedGamepadDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            return;
        }

        _selectedGamepadDevice = row;
        SelectedGamepad = new RazerGamepadViewModel(
            row.Connection,
            message => _diagnosticLog.TryWrite("gamepad", message),
            _getProfile,
            _saveProfile);
        await SelectedGamepad.LoadAsync(cancellationToken);
        row.SetCapabilityText(SelectedGamepad.CapabilityText);
    }

    private RazerChromaDeviceRowViewModel? _selectedChromaDevice;
    private RazerChromaViewModel? _selectedChroma;

    public ObservableCollection<RazerChromaDeviceRowViewModel> ChromaDevices { get; } = new();

    public RazerChromaViewModel? SelectedChroma
    {
        get => _selectedChroma;
        private set => SetField(ref _selectedChroma, value);
    }

    internal async Task RefreshChromaAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RazerChromaDeviceConnection> connections;
        try
        {
            connections = await RazerChromaStandardService.FindAllAsync(
                message => _diagnosticLog.TryWrite("chroma", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("chroma", $"discovery failed: {exception}");
            return;
        }

        if (connections.Count == 0)
        {
            if (ChromaDevices.Count == 0 && SelectedChroma is null) return;
            ChromaDevices.Clear();
            _selectedChromaDevice = null;
            SelectedChroma = null;
            return;
        }

        if (ChromaDevices.Count == connections.Count &&
            ChromaDevices.Zip(connections).All(pair =>
                StringComparer.OrdinalIgnoreCase.Equals(
                    pair.First.Connection.DevicePath, pair.Second.DevicePath) &&
                pair.First.Connection.ProductId == pair.Second.ProductId))
        {
            return;
        }

        var selectedPath = _selectedChromaDevice?.Connection.DevicePath;
        ChromaDevices.Clear();
        foreach (var connection in connections)
        {
            ChromaDevices.Add(new RazerChromaDeviceRowViewModel(connection));
        }

        _selectedChromaDevice = ChromaDevices.FirstOrDefault(row =>
            StringComparer.OrdinalIgnoreCase.Equals(row.Connection.DevicePath, selectedPath));
        if (_selectedChromaDevice is null)
        {
            SelectedChroma = null;
        }
    }

    public async Task SelectChromaAsync(
        RazerChromaDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_selectedChromaDevice is not null &&
            SelectedChroma is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedChromaDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            return;
        }

        _selectedChromaDevice = row;
        SelectedChroma = new RazerChromaViewModel(
            row.Connection,
            message => _diagnosticLog.TryWrite("chroma", message),
            _razerHid.SpecialLightingService,
            _getProfile,
            _saveProfile);
        await SelectedChroma.LoadAsync(cancellationToken);
    }
    public ValueTask DisposeAsync()
    {
        if (SelectedHeadsetAudio is { } headset)
        {
            // Clear the selection before the await so its PropertyChanged fires on the
            // caller's thread; clearing it afterwards raised it on the thread pool and
            // WinUI rejected the binding update with RPC_E_WRONG_THREAD during exit.
            SelectedHeadsetAudio = null;
            return headset.DisposeAsync();
        }

        return ValueTask.CompletedTask;
    }

    private static bool IsExpectedRuntimeException(Exception exception) =>
        RuntimeErrors.IsExpected(exception) || exception is System.Runtime.InteropServices.COMException;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
