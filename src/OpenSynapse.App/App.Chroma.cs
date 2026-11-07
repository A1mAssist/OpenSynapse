using System.Net.Sockets;
using OpenSynapse.App.Runtime;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App;

public partial class App
{
    private ChromaRestHost? _chromaRestHost;
    private ChromaSdkBridgeHost? _chromaSdkBridge;
    private ChromaDeviceRouter? _chromaDeviceRouter;
    internal bool IsChromaRestRunning => _chromaRestHost?.IsRunning == true;

    internal ChromaRestSnapshot GetChromaRestSnapshot() =>
        _chromaRestHost?.Snapshot ?? default;

    internal string ChromaRestStatus => _chromaRestHost?.ActiveSessionTitle is { } title
        ? $"127.0.0.1:54235 · {title}"
        : _chromaRestHost?.IsRunning == true
            ? "127.0.0.1:54235 · Ready"
            : "Disabled";

    internal async Task SetChromaRestEnabledAsync(bool enabled)
    {
        _behaviorSettings.ExperimentalChromaRestEnabled = enabled;
        if (_audioMuteViewModel is null)
        {
            return;
        }

        if (enabled)
        {
            StartChromaHosts(_audioMuteViewModel);
            return;
        }

        await DisposeChromaHostsAsync().ConfigureAwait(false);
    }

    private void StartChromaHosts(MainViewModel viewModel)
    {
        _chromaDeviceRouter ??= CreateChromaDeviceRouter(viewModel);
        StartChromaSdkBridge(viewModel);
        StartChromaRestHost(viewModel);
    }

    private ChromaDeviceRouter CreateChromaDeviceRouter(MainViewModel viewModel) =>
        new(
            viewModel.RazerHid.DeviceService,
            viewModel.RazerHid.SpecialLightingService,
            () => viewModel.RazerHid.CurrentConnections,
            () => viewModel.Accessories.KeyboardDevices
                .Select(row => row.Connection)
                .ToArray(),
            () => viewModel.Accessories.ChromaDevices
                .Select(row => row.Connection)
                .ToArray(),
            () => viewModel.RazerHid.KrakenConnections,
            message => _diagnosticLog.TryWrite("chroma", message));

    private async Task RestoreChromaAsync(MainViewModel viewModel)
    {
        await viewModel.RestoreLightingAfterExternalAsync().ConfigureAwait(false);
        if (_chromaDeviceRouter is not null)
        {
            await _chromaDeviceRouter.RestoreAsync().ConfigureAwait(false);
        }
    }

    private void StartChromaSdkBridge(MainViewModel viewModel)
    {
        if (!_behaviorSettings.ExperimentalChromaRestEnabled ||
            _chromaSdkBridge is not null)
        {
            return;
        }

        try
        {
            var applier = new ChromaSdkEffectApplier(viewModel, _bladeLightingController, _chromaDeviceRouter);
            var bridge = new ChromaSdkBridgeHost(
                applier.ApplyAsync,
                () => Volatile.Read(ref _closing) != 0 || !_behaviorSettings.RestoreLightingAfterChromaSession
                    ? Task.CompletedTask
                    : RestoreChromaAsync(viewModel),
                () => _behaviorSettings.RestoreLightingAfterChromaSession,
                () => applier.SupportedDevices,
                log: message => _diagnosticLog.TryWrite("chroma-sdk", message));
            bridge.StartAsync();
            _chromaSdkBridge = bridge;
            _diagnosticLog.TryWrite("chroma-sdk", "Chroma SDK bridge listening on \\\\.\\pipe\\OpenSynapseChromaSdk.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _chromaSdkBridge = null;
            _diagnosticLog.TryWrite("chroma-sdk", $"Chroma SDK bridge unavailable: {exception.Message}");
        }
    }

    private void StartChromaRestHost(MainViewModel viewModel)
    {
        if (!_behaviorSettings.ExperimentalChromaRestEnabled ||
            _chromaRestHost is not null ||
            _bladeLightingController is null)
        {
            return;
        }

        try
        {
            var host = new ChromaRestHost(
                () => viewModel.CurrentDeviceDescriptors,
                _bladeLightingController,
                () => Volatile.Read(ref _closing) != 0 || !_behaviorSettings.RestoreLightingAfterChromaSession
                    ? Task.CompletedTask
                    : RestoreChromaAsync(viewModel),
                () => _behaviorSettings.RestoreLightingAfterChromaSession,
                (kind, frame, rows, columns, token) =>
                    _chromaDeviceRouter?.ApplyAsync(kind, frame, rows, columns, token) ?? Task.FromResult(false),
                viewModel.Lighting.IsBladeChromaOverrideEnabled,
                _behaviorSettings.ChromaBlockedSources);
            _chromaRestHost = host;
            _ = StartChromaRestHostAsync(host);
        }
        catch (Exception exception)
        {
            _chromaRestHost = null;
            _diagnosticLog.TryWrite("chroma-rest", $"Chroma REST host unavailable: {exception.Message}");
        }
    }

    private async Task StartChromaRestHostAsync(ChromaRestHost host)
    {
        // A restart can briefly leave the previous instance holding the fixed port.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await host.StartAsync().ConfigureAwait(false);
                if (!ReferenceEquals(_chromaRestHost, host))
                {
                    await host.DisposeAsync().ConfigureAwait(false);
                    return;
                }
                _diagnosticLog.TryWrite("chroma-rest", "Chroma REST host listening on 127.0.0.1:54235.");
                return;
            }
            catch (SocketException exception) when (
                exception.SocketErrorCode == SocketError.AddressAlreadyInUse &&
                attempt < 10 &&
                ReferenceEquals(_chromaRestHost, host))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(_chromaRestHost, host))
                {
                    _chromaRestHost = null;
                    _diagnosticLog.TryWrite("chroma-rest", $"Chroma REST host unavailable: {exception.Message}");
                }
                return;
            }
        }
    }

    private async Task DisposeChromaHostsAsync()
    {
        var host = Interlocked.Exchange(ref _chromaRestHost, null);
        if (host is not null)
        {
            try
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("chroma-rest", $"stop failed: {exception}");
            }
        }

        var sdkBridge = Interlocked.Exchange(ref _chromaSdkBridge, null);
        if (sdkBridge is not null)
        {
            try
            {
                await sdkBridge.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("chroma-sdk", $"stop failed: {exception}");
            }
        }
    }
}
