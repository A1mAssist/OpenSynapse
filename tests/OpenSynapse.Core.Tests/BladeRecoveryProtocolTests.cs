using System.IO.MemoryMappedFiles;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeRecoveryProtocolTests
{
    private const string DevicePath = @"\\?\hid#vid_1532&pid_02c6&mi_00#test#{00000000-0000-0000-0000-000000000000}";
    private const string FilterPath = @"\\?\RZCONTROL#VID_1532&PID_02C6&MI_00#test#{E3BE005D-D130-4910-88FF-09AE02F680E9}";

    [Fact]
    public void MarkerRoundTripsAndRejectsUnknownFieldsAndVersions()
    {
        var marker = new BladeRecoveryMarker(2, 1234, DevicePath, FilterPath, DateTimeOffset.Parse("2026-08-20T00:00:00Z"));
        Assert.Equal(marker, BladeRecoveryProtocol.ParseMarker(BladeRecoveryProtocol.SerializeMarker(marker)));

        Assert.Throws<ArgumentException>(() => BladeRecoveryProtocol.ParseMarker(
            $$"""{"version":2,"ownerPid":1234,"devicePath":"{{DevicePath.Replace("\\", "\\\\")}}","filterDevicePath":"{{FilterPath.Replace("\\", "\\\\")}}","startedAtUtc":"2026-08-20T00:00:00Z","extra":1}"""));
        Assert.Throws<ArgumentException>(() => BladeRecoveryProtocol.ParseMarker(
            $$"""{"version":1,"ownerPid":1234,"devicePath":"{{DevicePath.Replace("\\", "\\\\")}}","filterDevicePath":"{{FilterPath.Replace("\\", "\\\\")}}","startedAtUtc":"2026-08-20T00:00:00Z"}"""));
    }

    [Fact]
    public void SharedStateRoundTripsAndUnknownVersionFailsClosed()
    {
        var name = BladeRecoveryProtocol.CreateObjectName("RecoveryKeys", Guid.NewGuid());
        using var owner = BladeRecoverySharedState.CreateOwner(name);
        using var writer = BladeRecoverySharedState.OpenExisting(name);
        writer.Write([new(0x1E, false), new(0x50, true), new(0x1E, false)]);

        Assert.True(owner.TryRead(out var keys));
        Assert.Equal([new BladeRecoverySyntheticKey(0x1E, false), new(0x50, true)], keys);

        using (var raw = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite))
        using (var view = raw.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite))
        {
            view.Write(4, 99u);
            view.Flush();
        }
        Assert.False(owner.TryRead(out keys));
        Assert.Empty(keys);
    }

    [Fact]
    public async Task DeviceRestorerSendsHandshakeThenNormalMode()
    {
        var session = new FakeSession();
        await BladeRecoveryDeviceRestorer.RestoreNormalModeAsync(new FakeTransport(session), DevicePath);

        var handshake = Assert.Single(session.Sent);
        Assert.Equal(0x02, handshake[0]);
        Assert.Equal(0x00, handshake[7]);
        Assert.Equal(0x81, handshake[8]);
        Assert.Equal((byte)0x00, session.QueryClass);
        Assert.Equal((byte)0x04, session.QueryCommand);
        Assert.Equal([0x00, 0x00], session.QueryArguments);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task CoordinatorUsesFailClosedRecoveryOrder()
    {
        var marker = new BladeRecoveryMarker(2, 1234, DevicePath, FilterPath, DateTimeOffset.Parse("2026-08-20T00:00:00Z"));
        var order = new List<string>();
        await BladeRecoveryCoordinator.RecoverAsync(marker, [new(0x1E, false)],
            path => order.Add($"filter:{path}"),
            path => { order.Add($"normal:{path}"); return Task.CompletedTask; },
            keys => order.Add($"keys:{keys.Count}"));
        Assert.Equal([$"filter:{FilterPath}", $"normal:{DevicePath}", "keys:1"], order);
    }

    [Fact]
    public void FilterRecoveryPlanDisablesRedirectBeforeClearingHooksAndNotifications()
    {
        var plan = BladeRecoveryFilterRestorer.CreateRecoveryPlan();
        Assert.Equal(26, plan.Count);
        Assert.Equal(0x8888301Cu, plan[0].Code);
        Assert.Equal([1, 0, 0, 0, 0], plan[0].Payload);
        Assert.All(plan.Skip(1).Take(23), operation => Assert.Equal(0x8888302Cu, operation.Code));
        Assert.Equal(0x88883038u, plan[24].Code);
        Assert.Equal(0x88883034u, plan[25].Code);
        Assert.Equal([0, 0, 0, 0], plan[24].Payload);
        Assert.Equal([0, 0, 0, 0], plan[25].Payload);
    }

    [Fact]
    public async Task PreviousDeadOwnerIsRecoveredBeforeMarkerCanBeReplaced()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-stale-{Guid.NewGuid():N}.json");
        var stale = new BladeRecoveryMarker(2, 1234, DevicePath, FilterPath, DateTimeOffset.Parse("2026-08-20T00:00:00Z"));
        await File.WriteAllTextAsync(path, BladeRecoveryProtocol.SerializeMarker(stale));
        var recovered = new List<BladeRecoveryMarker>();
        try
        {
            await BladeRecoveryClient.EnsurePreviousMarkerClearedAsync(path, TimeSpan.Zero,
                _ => false,
                marker => { recovered.Add(marker); return Task.CompletedTask; });
            Assert.Equal([stale], recovered);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task PreviousLiveOwnerBlocksReplacementWithoutRecovery()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-live-{Guid.NewGuid():N}.json");
        var stale = new BladeRecoveryMarker(2, 1234, DevicePath, FilterPath, DateTimeOffset.Parse("2026-08-20T00:00:00Z"));
        await File.WriteAllTextAsync(path, BladeRecoveryProtocol.SerializeMarker(stale));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BladeRecoveryClient.EnsurePreviousMarkerClearedAsync(path, TimeSpan.Zero,
                    _ => true,
                    _ => throw new InvalidOperationException("Recovery must not run for a live owner.")));
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task MarkerCreationNeverOverwritesAnExistingOwnershipClaim()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-owned-{Guid.NewGuid():N}.json");
        var existing = new BladeRecoveryMarker(
            2,
            1234,
            DevicePath,
            FilterPath,
            DateTimeOffset.Parse("2026-08-20T00:00:00Z"));
        var contender = existing with
        {
            OwnerPid = 5678,
            StartedAtUtc = DateTimeOffset.Parse("2026-08-20T00:01:00Z"),
        };
        await File.WriteAllTextAsync(path, BladeRecoveryProtocol.SerializeMarker(existing));
        try
        {
            await Assert.ThrowsAsync<IOException>(() =>
                BladeRecoveryProtocol.WriteMarkerAtomicAsync(path, contender));

            Assert.Equal(existing, await BladeRecoveryProtocol.ReadMarkerAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task FailedStaleRecoveryKeepsTheOriginalMarker()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-recovery-failed-{Guid.NewGuid():N}.json");
        var stale = new BladeRecoveryMarker(
            2,
            1234,
            DevicePath,
            FilterPath,
            DateTimeOffset.Parse("2026-08-20T00:00:00Z"));
        await File.WriteAllTextAsync(path, BladeRecoveryProtocol.SerializeMarker(stale));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BladeRecoveryClient.EnsurePreviousMarkerClearedAsync(
                    path,
                    TimeSpan.Zero,
                    _ => false,
                    _ => throw new InvalidOperationException("Recovery failed.")));

            Assert.Equal(stale, await BladeRecoveryProtocol.ReadMarkerAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task MarkerChangedDuringRecoveryIsNotDeletedOrAcceptedAsCleared()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-marker-race-{Guid.NewGuid():N}.json");
        var stale = new BladeRecoveryMarker(
            2,
            1234,
            DevicePath,
            FilterPath,
            DateTimeOffset.Parse("2026-08-20T00:00:00Z"));
        var replacement = stale with
        {
            OwnerPid = 5678,
            StartedAtUtc = DateTimeOffset.Parse("2026-08-20T00:01:00Z"),
        };
        await File.WriteAllTextAsync(path, BladeRecoveryProtocol.SerializeMarker(stale));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BladeRecoveryClient.EnsurePreviousMarkerClearedAsync(
                    path,
                    TimeSpan.Zero,
                    _ => false,
                    async _ => await File.WriteAllTextAsync(
                        path,
                        BladeRecoveryProtocol.SerializeMarker(replacement))));

            Assert.Equal(replacement, await BladeRecoveryProtocol.ReadMarkerAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task MarkerGateSerializesOwnershipTransitionsAndReleasesWithTheHandle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-marker-gate-{Guid.NewGuid():N}.json");
        var gatePath = $"{path}.lock";
        try
        {
            await using (await BladeRecoveryClient.AcquireMarkerGateAsync(
                             path,
                             TimeSpan.FromSeconds(1)))
            {
                await Assert.ThrowsAsync<TimeoutException>(() =>
                    BladeRecoveryClient.AcquireMarkerGateAsync(
                        path,
                        TimeSpan.FromMilliseconds(75)));
            }

            await using var reacquired = await BladeRecoveryClient.AcquireMarkerGateAsync(
                path,
                TimeSpan.FromSeconds(1));
            Assert.True(reacquired.CanRead);
            Assert.True(reacquired.CanWrite);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(gatePath)) File.Delete(gatePath);
        }
    }

    [Fact]
    public async Task GatedCompareAndDeletePreservesAChangedOwner()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-gated-delete-{Guid.NewGuid():N}.json");
        var gatePath = $"{path}.lock";
        var expected = new BladeRecoveryMarker(
            2,
            1234,
            DevicePath,
            FilterPath,
            DateTimeOffset.Parse("2026-08-20T00:00:00Z"));
        var replacement = expected with
        {
            OwnerPid = 5678,
            StartedAtUtc = DateTimeOffset.Parse("2026-08-20T00:01:00Z"),
        };
        await File.WriteAllTextAsync(path, BladeRecoveryProtocol.SerializeMarker(replacement));
        try
        {
            Assert.False(await BladeRecoveryClient.DeleteMatchingMarkerUnderGateAsync(path, expected));
            Assert.Equal(replacement, await BladeRecoveryProtocol.ReadMarkerAsync(path));
            Assert.True(await BladeRecoveryClient.DeleteMatchingMarkerUnderGateAsync(path, replacement));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(gatePath)) File.Delete(gatePath);
        }
    }

    private sealed class FakeTransport(FakeSession session) : IRazerFeatureTransport
    {
        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize, byte commandClass,
            byte commandId, ReadOnlyMemory<byte> arguments, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<IRazerFeatureSession> OpenSessionAsync(string devicePath, CancellationToken cancellationToken) =>
            Task.FromResult<IRazerFeatureSession>(session);
    }

    private sealed class FakeSession : IRazerFeatureSession
    {
        private byte _transaction;
        internal List<byte[]> Sent { get; } = [];
        internal byte QueryClass { get; private set; }
        internal byte QueryCommand { get; private set; }
        internal byte[] QueryArguments { get; private set; } = [];
        internal bool Disposed { get; private set; }

        public byte NextTransactionId() => _transaction++;
        public Task SendAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
        {
            Sent.Add(request.ToArray());
            return Task.CompletedTask;
        }

        public Task<byte[]> QueryAsync(byte transactionId, byte dataSize, byte commandClass, byte commandId,
            ReadOnlyMemory<byte> arguments, TimeSpan deviceWait, byte responseReportId,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            QueryClass = commandClass;
            QueryCommand = commandId;
            QueryArguments = arguments.ToArray();
            var response = RazerFeatureReport.CreateRequest(transactionId, dataSize, commandClass, commandId, arguments.Span);
            response[0] = responseReportId;
            response[1] = 0x02;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
