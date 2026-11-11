using System.Net.Sockets;
using System.Security.Principal;
using OpenSynapse.App.Runtime;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App;

public partial class App
{
    private ChromaRestHost? _chromaRestHost;
    private ChromaBrokerHost? _chromaBrokerHost;
    private Task? _chromaBrokerStartTask;
    private bool _chromaBrokerLaunched;
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
        if (!OpenSynapse.Core.Storage.LiteModePolicy.ShouldStartChromaRest(
                _behaviorSettings.LiteModeEnabled))
        {
            return;
        }

        _chromaDeviceRouter ??= CreateChromaDeviceRouter(viewModel);
        StartChromaBroker(viewModel);
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

    private void StartChromaBroker(MainViewModel viewModel)
    {
        if (!_behaviorSettings.ExperimentalChromaRestEnabled || _chromaBrokerHost is not null)
        {
            return;
        }
        if (!ChromaBrokerLauncher.IsAvailable)
        {
            _diagnosticLog.TryWrite("chroma-broker", "broker executable is not packaged; skipped.");
            return;
        }
        if (!ChromaBrokerLauncher.IsOfficialDllInstalled)
        {
            _diagnosticLog.TryWrite("chroma-broker", "official RzChromaSDK64.dll is not installed; broker skipped.");
            return;
        }

        ChromaBrokerHost? host = null;
        try
        {
            var applier = new ChromaSdkEffectApplier(viewModel, _bladeLightingController, _chromaDeviceRouter);
            host = new ChromaBrokerHost(
                applier.ApplyAsync,
                () => Volatile.Read(ref _closing) != 0 || !_behaviorSettings.RestoreLightingAfterChromaSession
                    ? Task.CompletedTask
                    : RestoreChromaAsync(viewModel),
                () => _behaviorSettings.RestoreLightingAfterChromaSession,
                log: message => _diagnosticLog.TryWrite("chroma-broker", message));
            host.StartAsync();
            _chromaBrokerHost = host;
            if (host.IsServingElsewhere)
            {
                _diagnosticLog.TryWrite(
                    "chroma-broker",
                    "another OpenSynapse instance owns the broker pipe; leaving it in charge.");
                return;
            }
            _diagnosticLog.TryWrite(
                "chroma-broker",
                "listening; asking the OpenSynapseService to start the official-DLL broker.");
            _chromaBrokerStartTask = StartChromaBrokerServiceAsync();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _chromaBrokerHost = null;
            if (host is not null)
            {
                _ = host.DisposeAsync();
            }
            _diagnosticLog.TryWrite("chroma-broker", $"unavailable: {exception.Message}");
        }
    }

    private async Task StartChromaBrokerServiceAsync()
    {
        try
        {
            var started = await OpenSynapseServiceClient
                .TryStartChromaBrokerAsync(Environment.ProcessId)
                .ConfigureAwait(false);
            if (started)
            {
                _chromaBrokerLaunched = true;
                _diagnosticLog.TryWrite("chroma-broker", "OpenSynapseService started the broker.");
                return;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _diagnosticLog.TryWrite("chroma-broker", $"service start failed: {exception.Message}");
        }

        if (OpenSynapseServiceInstaller.IsMachineInstallPath(Environment.ProcessPath))
        {
            _diagnosticLog.TryWrite(
                "chroma-broker",
                "OpenSynapseService is unavailable for this machine install; Chroma SDK is disabled until the service is registered.");
            return;
        }

        StartChromaBrokerElevated();
    }

    private void StartChromaBrokerElevated()
    {
        var userSid = WindowsIdentity.GetCurrent().User?.Value;
        if (ChromaBrokerLauncher.TryStartElevated(Environment.ProcessId, userSid, out var error))
        {
            _chromaBrokerLaunched = true;
            _diagnosticLog.TryWrite(
                "chroma-broker",
                "OpenSynapseService was unavailable; the broker started with administrator approval.");
            return;
        }

        _diagnosticLog.TryWrite("chroma-broker", $"the broker could not be started: {error}");
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

        var brokerHost = Interlocked.Exchange(ref _chromaBrokerHost, null);
        if (brokerHost is not null)
        {
            try
            {
                await brokerHost.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("chroma-broker", $"stop failed: {exception}");
            }
        }

        var startTask = Interlocked.Exchange(ref _chromaBrokerStartTask, null);
        if (startTask is not null)
        {
            await startTask.ConfigureAwait(false);
        }

        if (!_chromaBrokerLaunched)
        {
            return;
        }

        _chromaBrokerLaunched = false;
        var stopped = await OpenSynapseServiceClient
            .TryStopChromaBrokerAsync()
            .ConfigureAwait(false);
        if (!stopped)
        {
            _diagnosticLog.TryWrite("chroma-broker", "broker service stop request failed.");
        }
    }
}
