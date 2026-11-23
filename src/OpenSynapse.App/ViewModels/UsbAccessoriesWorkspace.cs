using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>Discovery, selected device lifetime and UI state for USB accessory backends.</summary>
public sealed partial class UsbAccessoriesWorkspace : INotifyPropertyChanged, IAsyncDisposable
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
    private int _headsetSelectionVersion;
    private bool _headsetsDisposed;
    private RazerHeadsetAudioConnection? _preferredHeadsetConnection;
    private readonly ConcurrentDictionary<string, HeadsetAudioSession> _headsetSessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (RazerHeadsetAudioConnection Connection, bool? Connected)>
        _headsetPowerConnections = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _headsetRefreshGate = new(1, 1);
    private RazerHeadsetAudioConnection[] _headsetConnections = [];
    private long _lastHeadsetPowerCheck;
    private readonly Dictionary<string, long> _carolMaintenanceAt = new(StringComparer.OrdinalIgnoreCase);
    internal Action<string>? ForgetAccessoryDiagnostic { get; set; }

    public ObservableCollection<RazerHeadsetAudioDeviceRowViewModel> HeadsetAudioDevices { get; } = new();
    internal IReadOnlyList<RazerHeadsetAudioConnection> HeadsetAudioConnections =>
        Volatile.Read(ref _headsetConnections);

    public RazerHeadsetAudioViewModel? SelectedHeadsetAudio
    {
        get => _selectedHeadsetAudio;
        private set => SetField(ref _selectedHeadsetAudio, value);
    }

    /// <summary>
    /// The selected studio-capable endpoint, or the first one before a page opens.
    /// </summary>
    internal RazerHeadsetAudioConnection? SelectedHeadsetAudioConnection =>
        _selectedHeadsetAudioDevice?.Connection is { } selected && SupportsStudio(selected)
            ? selected : HeadsetAudioConnections.FirstOrDefault(SupportsStudio);

    private bool SupportsStudio(RazerHeadsetAudioConnection connection) =>
        connection.HasControlInterface &&
        (connection.Transport == RazerHeadsetAudioTransport.Jolie64 ||
         connection.Transport == RazerHeadsetAudioTransport.KrakenBase37 &&
         _razerHid.FindKrakenConnection(connection) is { IsReady: true });

    internal async Task RefreshHeadsetAudioAsync(CancellationToken cancellationToken)
    {
        if (_headsetsDisposed) return;
        await _headsetRefreshGate.WaitAsync(cancellationToken);
        try
        {
            if (_headsetsDisposed) return;
            await RefreshHeadsetAudioCoreAsync(cancellationToken);
        }
        finally
        {
            _headsetRefreshGate.Release();
        }
    }

    private async Task RefreshHeadsetAudioCoreAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RazerHeadsetAudioConnection> connections;
        try
        {
            connections = await RazerHeadsetAudioService.FindAllAsync(
                message => _diagnosticLog.TryWrite("headset-audio", message), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("headset-audio", $"discovery failed: {exception}");
            return;
        }

        if (_headsetsDisposed) return;

        var present = connections.Select(item => item.DevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _headsetPowerConnections.Keys.Where(path => !present.Contains(path)).ToArray())
        {
            _headsetPowerConnections.Remove(path);
        }
        foreach (var row in HeadsetAudioDevices.Where(row => !present.Contains(row.Connection.DevicePath)).ToArray())
        {
            await RemoveHeadsetAsync(row.Connection);
        }

        foreach (var connection in connections)
        {
            try
            {
                // A powered-off radio keeps its USB receiver enumerated.
                if (connection.HasControlInterface && connection.SupportsWirelessPowerSignal)
                {
                    var connected = await TryReadHeadsetPowerAsync(connection, cancellationToken);
                    _headsetPowerConnections[connection.DevicePath] = (connection, connected);
                    if (connected == false)
                    {
                        await RemoveHeadsetAsync(connection);
                        continue;
                    }
                }
                else
                {
                    _headsetPowerConnections.Remove(connection.DevicePath);
                }
                await UpsertHeadsetAsync(connection, cancellationToken);
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception))
            {
                _diagnosticLog.TryWrite("headset-audio",
                    $"probe pid={connection.ProductId:X4} failed: {exception}");
            }
        }
    }

    private async Task UpsertHeadsetAsync(RazerHeadsetAudioConnection connection, CancellationToken cancellationToken)
    {
        if (_headsetsDisposed) return;
        var row = HeadsetAudioDevices.FirstOrDefault(item =>
            StringComparer.OrdinalIgnoreCase.Equals(item.Connection.DevicePath, connection.DevicePath));
        if (row is not null && row.Connection != connection)
        {
            await RemoveHeadsetAsync(row.Connection);
            row = null;
        }
        if (_headsetsDisposed) return;
        if (row is null)
        {
            _headsetSessions[connection.DevicePath] = new HeadsetAudioSession(new RazerHeadsetAudioViewModel(
                connection, message => _diagnosticLog.TryWrite("headset-audio", message),
                _razerHid.CreateKrakenLighting(connection), _getProfile, _saveProfile));
            row = new RazerHeadsetAudioDeviceRowViewModel(connection);
            HeadsetAudioDevices.Add(row);
            Volatile.Write(ref _headsetConnections, HeadsetAudioDevices.Select(item => item.Connection).ToArray());
            _refreshDeviceUi?.Invoke();
        }
        else if (row.ProbeState is RazerHeadsetProbeState.Ready or RazerHeadsetProbeState.Partial)
        {
            return;
        }
        await ProbeHeadsetRowAsync(row, connection, cancellationToken);
        if (SelectedHeadsetAudio is null && _preferredHeadsetConnection is { } preferred &&
            SameHeadset(preferred, connection) && HeadsetAudioDevices.Contains(row))
        {
            await SelectHeadsetAudioAsync(row, cancellationToken);
        }
    }

    internal void TurnOffHeadsetLightingForShutdown(TimeSpan timeout)
    {
        var sessions = _headsetSessions.Values.ToArray();
        foreach (var session in sessions) session.CancelOperations();
        var task = Task.Run(() => Task.WhenAll(sessions.Select(async session =>
        {
            var connection = session.ViewModel.Connection;
            try
            {
                using var deadline = new CancellationTokenSource(timeout);
                // WM_ENDSESSION blocks the UI thread; never wait for a UI-owned session operation here.
                if (await session.ViewModel.TurnOffLightingForShutdownAsync(deadline.Token, fadeOut: false))
                {
                    _diagnosticLog.TryWrite(
                        "lighting-shutdown",
                        $"headset {connection.ProductId:X4} off request sent.");
                }
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite(
                    "lighting-shutdown",
                    $"headset {connection.ProductId:X4} off failed: {exception.Message}");
            }
        })));
        try
        {
            if (!task.Wait(timeout))
            {
                _diagnosticLog.TryWrite(
                    "lighting-shutdown",
                    $"headset off timed out after {timeout.TotalSeconds:0.#}s.");
            }
        }
        catch (AggregateException exception)
        {
            _diagnosticLog.TryWrite(
                "lighting-shutdown",
                $"headset off failed: {exception.GetBaseException().Message}");
        }
    }

    private async Task ProbeHeadsetRowAsync(
        RazerHeadsetAudioDeviceRowViewModel row,
        RazerHeadsetAudioConnection connection,
        CancellationToken cancellationToken)
    {
        if (_headsetSessions.TryGetValue(connection.DevicePath, out var session) &&
            await session.RunAsync((vm, token) => vm.LoadAsync(token), cancellationToken) &&
            HeadsetAudioDevices.Contains(row))
        {
            var probe = session.ViewModel;
            row.SetProbeResult(
                probe.ProbeState, probe.CapabilityText, probe.ErrorText, probe.BatteryPercent);
        }
    }

    private async Task RemoveHeadsetAsync(RazerHeadsetAudioConnection connection)
    {
        _carolMaintenanceAt.Remove(connection.DevicePath);
        var row = HeadsetAudioDevices.FirstOrDefault(item =>
            StringComparer.OrdinalIgnoreCase.Equals(item.Connection.DevicePath, connection.DevicePath));
        if (row is not null)
        {
            HeadsetAudioDevices.Remove(row);
            Volatile.Write(ref _headsetConnections, HeadsetAudioDevices.Select(item => item.Connection).ToArray());
        }
        if (StringComparer.OrdinalIgnoreCase.Equals(
            _selectedHeadsetAudioDevice?.Connection.DevicePath, connection.DevicePath))
        {
            _headsetSelectionVersion++;
            _selectedHeadsetAudioDevice = null;
            SelectedHeadsetAudio = null;
        }
        if (_headsetSessions.TryRemove(connection.DevicePath, out var session)) await session.StopAsync();
        ForgetAccessoryDiagnostic?.Invoke("headset-audio:" + connection.DevicePath);
        if (row is not null) _refreshDeviceUi?.Invoke();
    }

    internal async Task PollHeadsetPowerAsync(CancellationToken cancellationToken)
    {
        if (_headsetsDisposed) return;
        if (Environment.TickCount64 - _lastHeadsetPowerCheck < 5000) return;
        if (SelectedHeadsetAudio is { IsBusy: true }) return;
        if (!await _headsetRefreshGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            _lastHeadsetPowerCheck = Environment.TickCount64;
            foreach (var (connection, previous) in _headsetPowerConnections.Values.ToArray())
            {
                try
                {
                    var connected = await TryReadHeadsetPowerAsync(connection, cancellationToken);
                    if (connected is null) continue;
                    if (connected == false)
                    {
                        await RemoveHeadsetAsync(connection);
                    }
                    else if (previous != true)
                    {
                        await _razerHid.ResetSpecialLightingSessionAsync(connection.DevicePath, cancellationToken);
                        await UpsertHeadsetAsync(connection, cancellationToken);
                    }
                    _headsetPowerConnections[connection.DevicePath] = (connection, connected);
                    if (connected == true) await MaintainCarolPowerAsync(connection, cancellationToken);
                }
                catch (Exception exception) when (IsExpectedRuntimeException(exception))
                {
                    _diagnosticLog.TryWrite("headset-audio",
                        $"power refresh pid={connection.ProductId:X4} failed: {exception.Message}");
                }
            }
            await RefreshSelectedHeadsetBatteryAsync(cancellationToken);
        }
        finally
        {
            _headsetRefreshGate.Release();
        }
    }

    internal async Task RefreshSelectedHeadsetBatteryAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedHeadsetAudio is not { } selected ||
            _selectedHeadsetAudioDevice is not { } row)
        {
            return;
        }

        if (!_headsetSessions.TryGetValue(selected.Connection.DevicePath, out var session)) return;
        await session.RunAsync((vm, token) => vm.RefreshBatteryStateAsync(token), cancellationToken);
        if (!ReferenceEquals(SelectedHeadsetAudio, selected) ||
            !ReferenceEquals(_selectedHeadsetAudioDevice, row)) return;
        row.SetProbeResult(row.ProbeState, row.CapabilityText, row.Error, selected.BatteryPercent);
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
            case RazerHeadsetAudioTransport.Protocol25Feature65 when RazerCarolAudioProtocol.IsSupported(connection.ProductId):
                return await OpenCarolService(connection).TryReadCarolLinkStatusAsync(cancellationToken).ConfigureAwait(false);
            case RazerHeadsetAudioTransport.NxpSerial:
                await using (var nxp = new RazerNxpHeadsetService(connection.DevicePath, Diagnostic))
                {
                    return await nxp.TryReadLinkStatusAsync(cancellationToken).ConfigureAwait(false);
                }
            case RazerHeadsetAudioTransport.AvneraFeature65:
                await using (var avnera = new RazerAvneraHeadsetService(
                    connection.DevicePath, Diagnostic, connection.FeatureReportByteLength))
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

    private RazerHeadsetAudioService OpenCarolService(RazerHeadsetAudioConnection connection) =>
        new(new RazerFeatureTransport(), connection.DevicePath, TimeSpan.FromMilliseconds(20),
            message => _diagnosticLog.TryWrite("headset-audio", message), connection.Transport, connection.ProductId);

    private async Task MaintainCarolPowerAsync(RazerHeadsetAudioConnection connection, CancellationToken cancellationToken)
    {
        if (!RazerCarolAudioProtocol.IsSupported(connection.ProductId) ||
            !_headsetSessions.TryGetValue(connection.DevicePath, out var session) ||
            _carolMaintenanceAt.TryGetValue(connection.DevicePath, out var last) && Environment.TickCount64 - last < 10_000)
            return;
        _carolMaintenanceAt[connection.DevicePath] = Environment.TickCount64;
        try
        {
            await session.RunAsync((_, token) => OpenCarolService(connection).MaintainCarolPowerAsync(token), cancellationToken);
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("headset-audio", $"Carol power maintenance failed: {exception.Message}");
        }
    }

    public async Task SelectHeadsetAudioAsync(
        RazerHeadsetAudioDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_headsetsDisposed || !HeadsetAudioDevices.Contains(row) ||
            !_headsetSessions.TryGetValue(row.Connection.DevicePath, out var session)) return;
        _preferredHeadsetConnection = row.Connection;
        if (_selectedHeadsetAudioDevice is not null &&
            SelectedHeadsetAudio is not null &&
            StringComparer.OrdinalIgnoreCase.Equals(
                _selectedHeadsetAudioDevice.Connection.DevicePath, row.Connection.DevicePath))
        {
            return;
        }

        var selectionVersion = ++_headsetSelectionVersion;
        _selectedHeadsetAudioDevice = row;
        var selected = session.ViewModel;
        SelectedHeadsetAudio = selected;
        if (row.ProbeState is not (RazerHeadsetProbeState.Ready or RazerHeadsetProbeState.Partial))
            await session.RunAsync((vm, token) => vm.LoadAsync(token), cancellationToken);
        // A device refresh can clear the selection while LoadAsync runs.
        if (selectionVersion == _headsetSelectionVersion && ReferenceEquals(SelectedHeadsetAudio, selected))
        {
            row.SetProbeResult(
                selected.ProbeState,
                selected.CapabilityText,
                selected.ErrorText,
                selected.BatteryPercent);
        }
    }

    internal async Task RefreshSelectedHeadsetAudioAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedHeadsetAudio is not { } selected) return;
        if (!_headsetSessions.TryGetValue(selected.Connection.DevicePath, out var session)) return;
        await session.RunAsync((vm, token) => vm.LoadAsync(token), cancellationToken);
        if (ReferenceEquals(SelectedHeadsetAudio, selected) &&
            _selectedHeadsetAudioDevice is { } row)
        {
            row.SetProbeResult(
                selected.ProbeState, selected.CapabilityText, selected.ErrorText, selected.BatteryPercent);
        }
    }

    internal Task RunHeadsetOperationAsync(RazerHeadsetAudioViewModel target,
        Func<RazerHeadsetAudioViewModel, CancellationToken, Task> action, CancellationToken cancellationToken) =>
        _headsetSessions.TryGetValue(target.Connection.DevicePath, out var session) &&
        ReferenceEquals(session.ViewModel, target)
            ? session.RunAsync(action, cancellationToken) : Task.CompletedTask;

    private RazerKeyboardDeviceRowViewModel? _selectedKeyboardDevice;
    private RazerKeyboardViewModel? _selectedKeyboard;
    private RazerKeyboardConnection[] _keyboardConnections = [];

    public ObservableCollection<RazerKeyboardDeviceRowViewModel> KeyboardDevices { get; } = new();

    /// <summary>
    /// A background-safe snapshot for services that must not read the bound
    /// ObservableCollection while the device page is being rebuilt.
    /// </summary>
    internal IReadOnlyList<RazerKeyboardConnection> KeyboardConnections =>
        Volatile.Read(ref _keyboardConnections);

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

        Volatile.Write(ref _keyboardConnections, connections.ToArray());
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
    public async ValueTask DisposeAsync()
    {
        _headsetsDisposed = true;
        _headsetSelectionVersion++;
        _selectedHeadsetAudioDevice = null;
        SelectedHeadsetAudio = null;
        var sessions = _headsetSessions.Values.ToArray();
        _headsetSessions.Clear();
        Volatile.Write(ref _headsetConnections, []);
        await Task.WhenAll(sessions.Select(session => session.StopAsync()));
    }

    private static bool SameHeadset(RazerHeadsetAudioConnection left, RazerHeadsetAudioConnection right) =>
        left.ProductId == right.ProductId &&
        (Guid.TryParse(left.ContainerId, out var leftId) && Guid.TryParse(right.ContainerId, out var rightId)
            ? leftId == rightId
            : !string.IsNullOrWhiteSpace(left.PhysicalDeviceKey) && !string.IsNullOrWhiteSpace(right.PhysicalDeviceKey)
                ? StringComparer.OrdinalIgnoreCase.Equals(left.PhysicalDeviceKey, right.PhysicalDeviceKey)
                : StringComparer.OrdinalIgnoreCase.Equals(left.DevicePath, right.DevicePath));

    private static bool IsExpectedRuntimeException(Exception exception) =>
        RuntimeErrors.IsExpected(exception) || exception is System.Runtime.InteropServices.COMException;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
