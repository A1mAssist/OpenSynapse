#if DEBUG
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App;

public partial class App
{
    private static async Task<IReadOnlyList<string>> CheckCalibrationLifecycleAsync(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE")))
            throw new InvalidOperationException("Lifecycle QA requires an isolated calibration store.");
        var transport = new CalibrationLifecycleQaTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var profile = ProfileDocument.CreateDefault();
        await using var workspace = new RazerHidWorkspace(service, null,
            () => PowerSourceKind.FullPowerAdapter, () => profile, () => new ProfileDefinition(),
            _ => Task.FromResult(true), new LocalDiagnosticLog(Path.Combine(outputDirectory, "lifecycle.log")),
            () => false, () => [], null, null, () => new MachineSettings(),
            _ => Task.FromResult(true), _ => Task.CompletedTask, () => { }, _ => { }, null);
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00BF)!;
        RazerHidDeviceConnection Device(string name) => new(definition, "qa-control:" + name, name,
            RazerHidEndpointState.Resolved, new HashSet<RazerHidBackendCapability>(),
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        var first = Device("lifecycle-a");
        var second = Device("lifecycle-b");
        foreach (var connection in new[] { first, second })
            RazerSurfaceCalibrationStore.Shared.Save(new(
                RazerHidDeviceViewModel.CalibrationIdentity(connection), RazerProximityCalibrationKind.Orca5Plus,
                new() { Mode = RazerSurfaceCalibrationMode.Adaptive, TrackingDistance = 2 }, DateTimeOffset.UtcNow));

        using var cancellation = new CancellationTokenSource();
        transport.BeforeNextRead = cancellation.Cancel;
        try
        {
            await workspace.RestoreCalibrationsAsync([first, second], cancellation.Token);
            throw new InvalidOperationException("The interrupted restoration did not propagate cancellation.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Require(transport.Calls.All(call => !call.Write), "Cancellation before reading does not write a device");

        transport.Calls.Clear();
        await workspace.RestoreCalibrationsAsync([first, second], CancellationToken.None);
        Require(transport.Calls.Count(call => call.Write && call.Path == first.DevicePath) == 2 &&
            transport.Calls.Count(call => call.Write && call.Path == second.DevicePath) == 2,
            "After cancellation, both the interrupted and not-yet-visited mouse are restored");

        transport.Calls.Clear();
        await workspace.RestoreCalibrationsAsync([first, second], CancellationToken.None);
        Require(transport.Calls.Count == 0, "Repeated discovery performs no calibration reads or writes");

        await workspace.RestoreCalibrationsAsync([second], CancellationToken.None);
        transport.Reset(first.DevicePath!);
        await workspace.RestoreCalibrationsAsync([first, second], CancellationToken.None);
        Require(transport.Calls.Count(call => call.Write) == 2 &&
            transport.Calls.All(call => call.Path == first.DevicePath),
            "Replugging one same-PID mouse restores only that exact endpoint");
        transport.Calls.Clear();
        transport.Reset(second.DevicePath!);
        await workspace.RestoreCalibrationsAsync([first, second], CancellationToken.None, verifySavedWireless: true);
        Require(transport.Calls.Count(call => call.Write && call.Path == second.DevicePath) == 2 &&
            transport.Calls.All(call => !call.Write || call.Path == second.DevicePath),
            "The background pass restores an unselected mouse that reset between heartbeat ticks");
        transport.Calls.Clear();
        await workspace.RestoreCalibrationsAsync([first, second], CancellationToken.None, verifySavedWireless: true);
        Require(transport.Calls.All(call => !call.Write), "Healthy saved wireless settings are only read, never rewritten");
        return
        [
            "Connection restoration survives cancellation before another mouse is visited",
            "Repeated discovery performs zero calibration I/O",
            "One same-PID replug restores only its exact endpoint",
            "Background verification restores an unselected reset mouse without discovery or a visible page",
            "Background verification never rewrites an already matching calibration",
        ];

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    private sealed class CalibrationLifecycleQaTransport : IRazerFeatureTransport
    {
        private readonly Dictionary<string, byte> _sensorStates = [];
        private readonly Dictionary<string, byte[]> _liftSettings = [];
        public List<(string Path, bool Write)> Calls { get; } = [];
        public Action? BeforeNextRead { get; set; }

        public void Reset(string path)
        {
            _sensorStates.Remove(path);
            _liftSettings.Remove(path);
        }

        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize, byte commandClass,
            byte commandId, ReadOnlyMemory<byte> arguments, TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            if (response[7] == 0 && response[8] == 0xBF)
            {
                Calls.Add((devicePath, false));
                response[9] = 1;
                response[10] = 1;
                response[11] = 0;
                response[12] = 0xBF;
                response[1] = 2;
                response[89] = RazerFeatureReport.CalculateCrc(response);
                return Task.FromResult(response);
            }
            if (response[7] != 0x0B) throw new InvalidOperationException("Unexpected non-calibration request.");
            var command = response[8];
            Calls.Add((devicePath, command < 0x80));
            if (command >= 0x80 && BeforeNextRead is { } action)
            {
                BeforeNextRead = null;
                action();
            }
            cancellationToken.ThrowIfCancellationRequested();
            switch (command)
            {
                case 0x03: _sensorStates[devicePath] = response[11]; break;
                case 0x0B: _liftSettings[devicePath] = [response[11], response[12]]; break;
                case 0x83: response[11] = _sensorStates.GetValueOrDefault(devicePath, (byte)1); break;
                case 0x8B:
                    var lift = _liftSettings.GetValueOrDefault(devicePath) ?? [1, 0];
                    response[11] = lift[0];
                    response[12] = lift[1];
                    break;
                default: throw new InvalidOperationException($"Unexpected calibration command {command:X2}.");
            }
            response[1] = 2;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}
#endif
