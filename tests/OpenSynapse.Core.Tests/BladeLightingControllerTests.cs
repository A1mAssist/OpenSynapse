using System.Text.Json.Nodes;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeLightingControllerTests
{
    private static readonly DeviceDescriptor Blade = new(
        "blade", "Razer Blade 16", 0x1532, 0x02C6,
        DeviceAccessState.Available, DeviceCapabilityState.PendingValidation,
        91, 0x0001, 0x0002, "blade-710");

    [Fact]
    public async Task AppliesFirstCompleteFrameOnlyAfterBrightnessValidation()
    {
        var transport = new LightingTransport();
        await using var controller = new BladeLightingController(transport);
        var color = new RazerRgb(10, 20, 30);

        await controller.ApplyAsync([Blade], new BladeLightingEffect(BladeLightingMode.Static, color));

        Assert.Equal(1, transport.BrightnessReads);
        Assert.Equal(1, transport.DeviceModeWrites);
        Assert.Equal(1, transport.LightingEngineGateWrites);
        Assert.Equal(Enumerable.Range(1, BladeLightingProtocol.Rows).Select(value => (byte)value),
            transport.Rows.Take(BladeLightingProtocol.Rows).Select(row => row.TransactionId));
        Assert.True(transport.Rows.Count >= BladeLightingProtocol.Rows);
        Assert.All(transport.Rows.Take(BladeLightingProtocol.Rows), row => Assert.Equal(color, row.Color));
        Assert.True(transport.FirmwareEffectWrites >= 1);
    }

    [Fact]
    public async Task RejectsUnavailableOrUnsupportedDeviceBeforeWriting()
    {
        var transport = new LightingTransport();
        await using var controller = new BladeLightingController(transport);
        var unavailable = Blade with { Access = DeviceAccessState.BusyOrUnavailable };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.ApplyAsync([unavailable], BladeLightingEffect.Spectrum));

        Assert.Equal(0, transport.BrightnessReads);
        Assert.Empty(transport.Rows);
    }

    [Fact]
    public async Task RejectsInvalidWheelDirectionBeforeWriting()
    {
        var transport = new LightingTransport();
        await using var controller = new BladeLightingController(transport);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            controller.ApplyAsync(
                [Blade],
                new BladeLightingEffect(
                    BladeLightingMode.Wheel,
                    Direction: (BladeWaveDirection)99)));

        Assert.Empty(transport.Commands);
    }

    [Fact]
    public async Task UsesInjectedSameFamilyManifestForCompatiblePid()
    {
        var document = ReadBuiltInBladeManifest();
        document["id"] = "blade-compatible";
        document["productIds"]![0] = "02C7";
        document["capabilities"]!["keyboard-brightness.get"]!["transactionId"] = "2A";
        var registry = RazerDeviceRegistry.LoadJson([document.ToJsonString()]);
        var compatible = Blade with { ProductId = 0x02C7 };
        var transport = new LightingTransport();
        await using var controller = new BladeLightingController(transport, registry);

        await controller.ApplyAsync(
            [compatible],
            new BladeLightingEffect(BladeLightingMode.Static, new RazerRgb(1, 2, 3)));

        Assert.Equal(1, transport.BrightnessReads);
        Assert.Contains(transport.Commands, command =>
            (command.TransactionId, command.CommandClass, command.CommandId) == (0x2A, 0x0E, 0x84));
    }

    [Fact]
    public async Task GateFailureRestoresNormalMode()
    {
        var transport = new LightingTransport { FailLightingEngineGate = true };
        await using var controller = new BladeLightingController(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.ApplyAsync([Blade], BladeLightingEffect.Spectrum));

        Assert.Equal(2, transport.DeviceModeWrites);
        Assert.Equal(1, transport.LightingEngineGateWrites);
        Assert.Contains(transport.Commands, command =>
            (command.CommandClass, command.CommandId) == (0x00, 0x04) && command.TransactionId == 2);
        Assert.Empty(transport.Rows);
    }

    [Fact]
    public async Task StopRestoresPersistentMatrixFrame()
    {
        var transport = new LightingTransport();
        var restore = new RazerRgb(0x99, 0xDD, 0x72);
        await using var controller = new BladeLightingController(transport, restore);

        await controller.ApplyAsync(
            [Blade],
            new BladeLightingEffect(BladeLightingMode.Static, new RazerRgb(1, 2, 3)));
        await controller.StopAsync();

        Assert.True(transport.Rows.Count >= BladeLightingProtocol.Rows * 2);
        Assert.All(transport.Rows.TakeLast(BladeLightingProtocol.Rows), row => Assert.Equal(restore, row.Color));
    }

    [Fact]
    public async Task SharedModeLeasePreventsPrematureNormalRestore()
    {
        var transport = new LightingTransport();
        var coordinator = new BladeSoftwareModeCoordinator();
        var finalRestores = 0;
        var externalLease = await coordinator.AcquireAsync(
            Blade.Id,
            static _ => Task.CompletedTask,
            () =>
            {
                Interlocked.Increment(ref finalRestores);
                return Task.CompletedTask;
            },
            CancellationToken.None);
        await using var controller = new BladeLightingController(
            transport,
            RazerDeviceRegistry.BuiltIn,
            new RazerRgb(0x99, 0xDD, 0x72),
            coordinator);

        await controller.ApplyAsync(
            [Blade],
            new BladeLightingEffect(BladeLightingMode.Static, new RazerRgb(1, 2, 3)));
        await controller.StopAsync();

        Assert.Equal(1, transport.DeviceModeWrites);
        Assert.True(transport.Rows.Count >= BladeLightingProtocol.Rows * 2);

        await externalLease.ReleaseAsync(() =>
        {
            Interlocked.Increment(ref finalRestores);
            return Task.CompletedTask;
        });
        Assert.Equal(1, finalRestores);
    }

    [Fact]
    public async Task RuntimeCompletionReportsFailureAfterFirstFrame()
    {
        var transport = new LightingTransport { FailMatrixAtWrite = BladeLightingProtocol.Rows + 1 };
        var controller = new BladeLightingController(transport);

        try
        {
            await controller.ApplyAsync(
                [Blade],
                new BladeLightingEffect(BladeLightingMode.Static, new RazerRgb(1, 2, 3)));

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.RuntimeCompletion.WaitAsync(TimeSpan.FromSeconds(2)));

            Assert.Contains("Simulated matrix failure", failure.Message);
            Assert.True(transport.Rows.Count >= BladeLightingProtocol.Rows);
        }
        finally
        {
            try
            {
                await controller.DisposeAsync();
            }
            catch (InvalidOperationException)
            {
                // The runtime fault is the behavior under test; disposal re-awaits it.
            }
        }
    }

    [Fact]
    public async Task ReplacingEffectRestoresAndStopsThePreviousSessionFirst()
    {
        var transport = new LightingTransport();
        var restore = new RazerRgb(0x99, 0xDD, 0x72);
        await using var controller = new BladeLightingController(transport, restore);

        await controller.ApplyAsync(
            [Blade],
            new BladeLightingEffect(BladeLightingMode.Static, new RazerRgb(1, 2, 3)));
        var beforeReplacement = transport.Rows.Count;
        await controller.ApplyAsync(
            [Blade],
            new BladeLightingEffect(BladeLightingMode.Static, new RazerRgb(4, 5, 6)));

        var replacement = transport.Rows.Skip(beforeReplacement).ToArray();
        Assert.True(replacement.Length >= BladeLightingProtocol.Rows * 2);
        Assert.All(replacement.Take(BladeLightingProtocol.Rows), row => Assert.Equal(restore, row.Color));
        Assert.All(replacement.Skip(BladeLightingProtocol.Rows).Take(BladeLightingProtocol.Rows),
            row => Assert.Equal(new RazerRgb(4, 5, 6), row.Color));
    }

    [Fact]
    public async Task RestoreMatrixFailureStillSendsNormalMode()
    {
        var transport = new LightingTransport { FailMatrixAtWrite = 7 };
        await using var controller = new BladeLightingController(transport);
        await controller.ApplyAsync(
            [Blade],
            new BladeLightingEffect(BladeLightingMode.Static, new RazerRgb(1, 2, 3)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StopAsync());

        Assert.Equal(2, transport.DeviceModeWrites);
        Assert.Equal(1, transport.LightingEngineGateWrites);
    }

    private static JsonObject ReadBuiltInBladeManifest()
    {
        var assembly = typeof(RazerDeviceRegistry).Assembly;
        var resourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(".blade-710.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return JsonNode.Parse(stream)!.AsObject();
    }

    private sealed class LightingTransport : IRazerFeatureTransport
    {
        public int BrightnessReads { get; private set; }
        public int FirmwareEffectWrites { get; private set; }
        public int DeviceModeWrites { get; private set; }
        public int LightingEngineGateWrites { get; private set; }
        public bool FailLightingEngineGate { get; init; }
        public int? FailMatrixAtWrite { get; init; }
        public List<(byte TransactionId, byte Row, RazerRgb Color)> Rows { get; } = [];
        public List<(byte TransactionId, byte CommandClass, byte CommandId)> Commands { get; } = [];
        private int _matrixWrites;

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add((transactionId, commandClass, commandId));
            var responseArguments = arguments.ToArray();
            if ((commandClass, commandId) == (0x0E, 0x84))
            {
                BrightnessReads++;
                responseArguments = [0x01, 0xFF];
            }
            else if ((commandClass, commandId) == (0x03, 0x0A))
            {
                FirmwareEffectWrites++;
            }
            else if ((commandClass, commandId) == (0x00, 0x04))
            {
                DeviceModeWrites++;
            }
            else if ((commandClass, commandId) == (0x0F, 0x02) && arguments.Span[2] == 8)
            {
                LightingEngineGateWrites++;
                if (FailLightingEngineGate)
                {
                    throw new InvalidOperationException("Simulated lighting-engine gate failure.");
                }
            }
            else if ((commandClass, commandId) == (0x03, 0x0B))
            {
                _matrixWrites++;
                if (_matrixWrites == FailMatrixAtWrite)
                {
                    throw new InvalidOperationException("Simulated matrix failure.");
                }
                Rows.Add((transactionId, arguments.Span[1], new RazerRgb(
                    arguments.Span[4], arguments.Span[5], arguments.Span[6])));
            }

            var response = new byte[RazerFeatureReport.Length];
            response[1] = 0x02;
            response[2] = transactionId;
            response[6] = dataSize;
            response[7] = commandClass;
            response[8] = commandId;
            responseArguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}
