using System.Net.Sockets;
using OpenSynapse.App.Runtime;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App;

public partial class App
{
    private ChromaRestHost? _chromaRestHost;
    private ChromaSdkBridgeHost? _chromaSdkBridge;
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
        StartChromaSdkBridge(viewModel);
        StartChromaRestHost(viewModel);
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
            var applier = new ChromaSdkEffectApplier(viewModel, _bladeLightingController);
            var bridge = new ChromaSdkBridgeHost(
                applier.ApplyAsync,
                () => Volatile.Read(ref _closing) != 0 || !_behaviorSettings.RestoreLightingAfterChromaSession
                    ? Task.CompletedTask
                    : RestoreLightingAfterChromaAsync(viewModel),
                () => _behaviorSettings.RestoreLightingAfterChromaSession,
                () => applier.SupportedDevices);
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
            _chromaRestHost = new ChromaRestHost(
                () => viewModel.CurrentDeviceDescriptors,
                _bladeLightingController,
                () => Volatile.Read(ref _closing) != 0 || !_behaviorSettings.RestoreLightingAfterChromaSession
                    ? Task.CompletedTask
                    : RestoreLightingAfterChromaAsync(viewModel),
                () => _behaviorSettings.RestoreLightingAfterChromaSession,
                viewModel.RazerHid.ApplyChromaFrameAsync,
                viewModel.Lighting.IsBladeChromaOverrideEnabled,
                _behaviorSettings.ChromaBlockedSources);
            _chromaRestHost.StartAsync();
            _diagnosticLog.TryWrite("chroma-rest", "Chroma REST host listening on 127.0.0.1:54235.");
        }
        catch (SocketException exception)
        {
            _chromaRestHost = null;
            _diagnosticLog.TryWrite("chroma-rest", $"Chroma REST host unavailable: {exception.Message}");
        }
    }

    private static async Task RestoreLightingAfterChromaAsync(MainViewModel viewModel)
    {
        await viewModel.Lighting.RestoreBladeLightingAfterExternalAsync().ConfigureAwait(false);
        await viewModel.RazerHid.RestoreLightingAfterExternalAsync().ConfigureAwait(false);
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