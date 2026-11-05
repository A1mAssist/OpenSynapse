using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeLightingRuntimeDialectTests
{
    private const ushort MatrixBladePid = 0x0234;

    [Fact]
    public async Task ExplicitEffectRejectionDowngradesTheBladeDialectForTheSession()
    {
        BladeLightingRuntimeDialect.Reset();
        try
        {
            var transport = new RejectingEffectTransport(timeout: false);
            await using var controller = new BladeLightingController(transport);
            var device = CreateDevice(MatrixBladePid, "blade-671");
            var effect = new BladeLightingEffect(
                BladeLightingMode.Static,
                new RazerRgb(0x11, 0x22, 0x33));

            await Assert.ThrowsAsync<RazerCommandRejectedException>(() =>
                controller.ApplyAsync([device], effect));
            Assert.Equal(
                BladeLightingDialect.Unknown,
                BladeLightingRuntimeDialect.Get(MatrixBladePid));

            var effectWrites = transport.Requests.Count(IsEffectWrite);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                controller.ApplyAsync([device], effect));
            Assert.Equal(effectWrites, transport.Requests.Count(IsEffectWrite));
        }
        finally
        {
            BladeLightingRuntimeDialect.Reset();
        }
    }

    [Fact]
    public async Task EffectTimeoutDoesNotDowngradeTheBladeDialect()
    {
        BladeLightingRuntimeDialect.Reset();
        try
        {
            var transport = new RejectingEffectTransport(timeout: true);
            await using var controller = new BladeLightingController(transport);
            var device = CreateDevice(MatrixBladePid, "blade-671");

            await Assert.ThrowsAsync<RazerDeviceTimeoutException>(() =>
                controller.ApplyAsync(
                    [device],
                    new BladeLightingEffect(BladeLightingMode.Static)));

            Assert.Equal(
                BladeLightingDialect.LedMatrixBlade,
                BladeLightingRuntimeDialect.Get(MatrixBladePid));
        }
        finally
        {
            BladeLightingRuntimeDialect.Reset();
        }
    }

    [Fact]
    public async Task FirmwareEffectRejectionSendsTheSixByteBladeFrameAndDowngrades()
    {
        const ushort firmwareEffectPid = 0x029F;
        BladeLightingRuntimeDialect.Reset();
        try
        {
            var transport = new FirmwareEffectTransport();
            var reader = new BladeDisplayTelemetryReader(
                new RazerDeviceResolver(RazerDeviceRegistry.BuiltIn),
                new RazerCapabilityClient(transport),
                new BladeValidationState { KeyboardEffectPath = $"blade-{firmwareEffectPid:x4}" });

            await Assert.ThrowsAsync<RazerCommandRejectedException>(() =>
                reader.SetBlade2023KeyboardEffectAsync(
                    [CreateDevice(firmwareEffectPid, "blade-671")],
                    BladeLighting2023Effect.Breathing).AsTask());

            Assert.Equal((byte)0x06, transport.EffectSetDataSize);
            Assert.Equal(
                BladeLightingDialect.Unknown,
                BladeLightingRuntimeDialect.Get(firmwareEffectPid));
        }
        finally
        {
            BladeLightingRuntimeDialect.Reset();
        }
    }

    private static bool IsEffectWrite((byte CommandClass, byte CommandId) request) =>
        request.CommandClass == 0x03 && request.CommandId == 0x0A;

    private static DeviceDescriptor CreateDevice(ushort productId, string protocolFamily) =>
        new(
            $"blade-{productId:x4}",
            "Blade",
            0x1532,
            productId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            protocolFamily);

    private sealed class FirmwareEffectTransport : IRazerFeatureTransport
    {
        internal byte? EffectSetDataSize { get; private set; }

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
            if (commandClass == 0x0F && commandId == 0x02)
            {
                EffectSetDataSize = dataSize;
                throw new RazerCommandRejectedException("Injected firmware-effect rejection.");
            }

            var response = new byte[RazerFeatureReport.Length];
            response[1] = 0x02;
            response[2] = transactionId;
            response[6] = dataSize;
            response[7] = commandClass;
            response[8] = commandId;
            arguments.Span.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
            if (commandClass == 0x03 && commandId == 0x83)
            {
                response[RazerFeatureReport.ArgumentsOffset + 2] = 0x80;
            }

            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }

    private sealed class RejectingEffectTransport(bool timeout) : IRazerFeatureTransport
    {
        internal ConcurrentQueue<(byte CommandClass, byte CommandId)> Requests { get; } = [];

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
            Requests.Enqueue((commandClass, commandId));
            if (commandClass == 0x03 && commandId == 0x0A)
            {
                if (timeout)
                {
                    throw new RazerDeviceTimeoutException("Injected effect timeout.");
                }

                throw new RazerCommandRejectedException("Injected effect rejection.");
            }

            var response = new byte[RazerFeatureReport.Length];
            response[1] = 0x02;
            response[2] = transactionId;
            response[6] = dataSize;
            response[7] = commandClass;
            response[8] = commandId;
            arguments.Span.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}
