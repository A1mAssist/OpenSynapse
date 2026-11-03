using System.Buffers.Binary;
using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The NVAPI request block must stay byte-identical to the one Razer's bladeNative.dll
/// submits: version 0x11C94, one pstate, one clock, the clock domain and a kHz delta.
/// </summary>
public sealed class GpuClockOffsetTests
{
    [Fact]
    public void CoreRequestMatchesTheRazerBlock()
    {
        var parameters = NvApiGpuClockOffset.CreateParameters(clockDomain: 0, deltaMegahertz: 150);

        Assert.Equal(0x1CF8, parameters.Length);
        Assert.Equal(0x11C94u, ReadUInt32(parameters, 0x00));
        Assert.Equal(1u, ReadUInt32(parameters, 0x08));
        Assert.Equal(1u, ReadUInt32(parameters, 0x0C));
        Assert.Equal(0u, ReadUInt32(parameters, 0x14));
        Assert.Equal(0u, ReadUInt32(parameters, 0x1C));
        Assert.Equal(150_000u, ReadUInt32(parameters, 0x28));
    }

    [Fact]
    public void MemoryRequestUsesDomainFour()
    {
        var parameters = NvApiGpuClockOffset.CreateParameters(clockDomain: 4, deltaMegahertz: 1000);

        Assert.Equal(4u, ReadUInt32(parameters, 0x1C));
        Assert.Equal(1_000_000u, ReadUInt32(parameters, 0x28));
    }

    [Theory]
    [InlineData(-900, -500)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(150, 150)]
    [InlineData(250, 200)]
    public void CoreOffsetsAreClampedToTheLaptopEnvelope(int input, int expected) =>
        Assert.Equal(expected, NvApiGpuClockOffset.ClampCore(input));

    [Theory]
    [InlineData(-900, -500)]
    [InlineData(0, 0)]
    [InlineData(2000, 2000)]
    [InlineData(2500, 2000)]
    public void MemoryOffsetsAreClampedToTheSupportedEnvelope(int input, int expected) =>
        Assert.Equal(expected, NvApiGpuClockOffset.ClampMemory(input));

    [Fact]
    public void ClockOffsetOperationsRouteToTheirActions()
    {
        var status = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.GpuClockOffsetStatusOperation,
            Enabled: null);
        Assert.Equal(
            DedicatedGpuModeServiceAction.GpuClockOffsetStatus,
            DedicatedGpuModeServiceRequestParser.Parse(status));

        var set = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.SetGpuClockOffsetOperation,
            Enabled: null,
            CoreOffsetMhz: 100,
            MemoryOffsetMhz: 500);
        Assert.Equal(
            DedicatedGpuModeServiceAction.SetGpuClockOffset,
            DedicatedGpuModeServiceRequestParser.Parse(set));
    }

    [Fact]
    public void AClockOffsetWriteWithoutBothValuesIsRejected()
    {
        var request = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.SetGpuClockOffsetOperation,
            Enabled: null,
            CoreOffsetMhz: 100,
            MemoryOffsetMhz: null);

        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(request));
    }

    [Fact]
    public void V1LayoutMatchesTheLocalStructureDefinitions()
    {
        Assert.Equal(0x2C, NvApiGpuClockOffset.ClockEntrySize);
        Assert.Equal(0x1C8, NvApiGpuClockOffset.PstateSize);
        Assert.Equal(0x1C94, NvApiGpuClockOffset.PstatesInfoV1Size);
        Assert.Equal((uint)NvApiGpuClockOffset.PstatesInfoV1Size,
            NvApiGpuClockOffset.ParametersVersion & 0xFFFF);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 15)]
    public void OneGetterFindsP0AndBothDomainsRegardlessOfOrder(bool reversed, int p0Index)
    {
        var calls = 0;
        var result = NvApiGpuClockOffset.TryRead(buffer =>
        {
            calls++;
            Assert.Equal(0x11C94u, ReadUInt32(buffer, 0));
            Assert.All(buffer.Skip(4), value => Assert.Equal((byte)0, value));
            Snapshot(100_000, 800_000, reversed, p0Index).CopyTo(buffer, 0);
            return 0;
        }, out var core, out var memory, out var error);

        Assert.True(result, error);
        Assert.Equal(1, calls);
        Assert.Equal(100, core);
        Assert.Equal(800, memory);
    }

    [Theory]
    [InlineData(0x08, 0u)]
    [InlineData(0x08, 17u)]
    [InlineData(0x08, uint.MaxValue)]
    [InlineData(0x0C, 0u)]
    [InlineData(0x0C, 9u)]
    [InlineData(0x0C, uint.MaxValue)]
    [InlineData(0x10, 5u)]
    [InlineData(0x10, uint.MaxValue)]
    public void InvalidCountsAreRejectedBeforeArrayAccess(int offset, uint count)
    {
        var buffer = Snapshot();
        Write(buffer, offset, count);
        AssertInvalid(buffer);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x1C93)]
    public void TruncatedV1StructuresAreRejected(int length) =>
        AssertInvalid(Snapshot()[..length]);

    [Theory]
    [InlineData(0u)]
    [InlineData(0x21C94u)]
    [InlineData(0x11CF8u)]
    public void UnexpectedVersionsAreRejected(uint version)
    {
        var buffer = Snapshot();
        Write(buffer, 0, version);
        AssertInvalid(buffer);
    }

    [Theory]
    [InlineData(0x04)]
    [InlineData(0x18)]
    [InlineData(0x24)]
    [InlineData(0x50)]
    public void NonEditableTableStateOrDomainIsUnavailable(int flagOffset)
    {
        var buffer = Snapshot();
        Write(buffer, flagOffset, 0);
        AssertInvalid(buffer);
    }

    [Theory]
    [InlineData(0x14, 1u)] // No P0.
    [InlineData(0x1C, 8u)] // No graphics domain.
    [InlineData(0x48, 8u)] // No memory domain.
    [InlineData(0x48, 0u)] // Duplicate graphics domain.
    [InlineData(0x20, 2u)] // Unknown clock type.
    public void MissingAmbiguousOrMalformedDomainsAreRejected(int offset, uint value)
    {
        var buffer = Snapshot();
        Write(buffer, offset, value);
        AssertInvalid(buffer);
    }

    [Fact]
    public void DuplicateP0IsRejected()
    {
        var buffer = Snapshot(p0Index: 1);
        Write(buffer, 0x14, 0);
        AssertInvalid(buffer);
    }

    [Fact]
    public void OtherValidClockDomainsDoNotReplaceTheRequestedDomains()
    {
        var buffer = Snapshot();
        Write(buffer, 0x0C, 3);
        Write(buffer, 0x74, 8);
        Assert.True(NvApiGpuClockOffset.TryParseSnapshot(buffer, out var state, out var error), error);
        Assert.Equal(100_000, state.Core.DeltaKhz);
        Assert.Equal(800_000, state.Memory.DeltaKhz);
    }

    [Theory]
    [InlineData(0x2C, 201_000)] // Inverted graphics range.
    [InlineData(0x2C, 101_000)] // Graphics delta below driver minimum.
    [InlineData(0x5C, 799_000)] // Memory delta above driver maximum.
    [InlineData(0x28, 201_000)] // Outside laptop graphics envelope.
    [InlineData(0x28, -501_000)]
    [InlineData(0x54, 2_001_000)] // Outside memory envelope.
    [InlineData(0x54, -501_000)]
    [InlineData(0x28, 100_500)] // Cannot round a trusted restore baseline.
    [InlineData(0x54, 800_500)]
    public void InvalidRangesOrUnrepresentableOffsetsAreNotSyntheticZero(int offset, int value)
    {
        var buffer = Snapshot();
        Write(buffer, offset, unchecked((uint)value));
        AssertInvalid(buffer);
        Assert.False(NvApiGpuClockOffset.TryRead(target =>
        {
            buffer.CopyTo(target, 0);
            return 0;
        }, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(0x1C, 0)]
    [InlineData(0x1C, 100_000)]
    [InlineData(0x48, 0)]
    [InlineData(0x48, 800_000)]
    public void EditableButCollapsedDomainRangesAreUnavailable(int clock, int value)
    {
        var buffer = Snapshot();
        Write(buffer, clock + 12, (uint)value);
        Write(buffer, clock + 16, (uint)value);
        Write(buffer, clock + 20, (uint)value);
        AssertInvalid(buffer);
        var writes = 0;
        Assert.False(NvApiGpuClockOffset.TryApply(100, 800, target =>
        {
            buffer.CopyTo(target, 0);
            return 0;
        }, _ => { writes++; return 0; }, _ => { }, out var error));
        Assert.NotNull(error);
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData(150, 800, 0)]
    [InlineData(100, 1000, 4)]
    public void ChangingOneDomainDoesNotOverwriteTheOther(int core, int memory, int changedDomain)
    {
        var driver = new FakeDriver();
        Assert.True(Apply(driver, core, memory, out var error), error);
        Assert.Equal([(changedDomain, changedDomain == 0 ? core * 1000 : memory * 1000)], driver.Writes);
        Assert.Equal(core * 1000, driver.CoreKhz);
        Assert.Equal(memory * 1000, driver.MemoryKhz);
        Assert.Equal(2, driver.Reads);
    }

    [Fact]
    public void BothDomainsRequireMatchingReadbackForSuccess()
    {
        var driver = new FakeDriver();
        Assert.True(Apply(driver, 150, 1000, out var error), error);
        Assert.Null(error);
        Assert.Equal([(0, 150_000), (4, 1_000_000)], driver.Writes);
        Assert.Equal(2, driver.Reads);
    }

    [Fact]
    public void AnUnchangedTrustedSnapshotNeedsNoWrite()
    {
        var driver = new FakeDriver();
        Assert.True(Apply(driver, 100, 800, out var error), error);
        Assert.Empty(driver.Writes);
        Assert.Equal(1, driver.Reads);
    }

    [Theory]
    [InlineData(0x2C, 101_000)]
    [InlineData(0x30, 149_000)]
    [InlineData(0x5C, 999_000)]
    public void DriverRangesRejectRequestsBeforeAnyWrite(int offset, int limit)
    {
        var writes = 0;
        var buffer = Snapshot();
        // A valid old graphics offset must still be admitted by the narrower range.
        if (offset == 0x2C) Write(buffer, 0x28, 120_000);
        Write(buffer, offset, unchecked((uint)limit));
        var core = offset == 0x2C ? 100 : 150;
        Assert.False(NvApiGpuClockOffset.TryApply(core, 1000,
            target => { buffer.CopyTo(target, 0); return 0; },
            _ => { writes++; return 0; }, _ => { }, out var error));
        Assert.Contains("driver's writable delta ranges", error);
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnreadableOrMalformedBaselinePreventsAllWrites(bool malformed)
    {
        var writes = 0;
        Assert.False(NvApiGpuClockOffset.TryApply(150, 1000, target =>
        {
            if (!malformed) return -1;
            Snapshot().CopyTo(target, 0);
            Write(target, 0x0C, 9);
            return 0;
        }, _ => { writes++; return 0; }, _ => { }, out var error));
        Assert.NotNull(error);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void SecondDomainFailureRestoresBothOldValues()
    {
        var driver = new FakeDriver();
        driver.WriteStatus = (domain, value) => domain == 4 && value == 1_000_000 ? -1 : 0;
        Assert.False(Apply(driver, 150, 1000, out var error));
        Assert.Contains("Original offsets restored and verified", error);
        Assert.Equal(100_000, driver.CoreKhz);
        Assert.Equal(800_000, driver.MemoryKhz);
        Assert.Equal(33, driver.Writes.Count);
        Assert.Equal([(0, 100_000), (4, 800_000)], driver.Writes.TakeLast(2));
    }

    [Fact]
    public void EvenAFailedFirstCallMayHaveChangedHardwareAndIsRestored()
    {
        var driver = new FakeDriver();
        driver.WriteStatus = (domain, value) =>
        {
            if (domain != 0 || value != 150_000) return 0;
            driver.CoreKhz = value;
            return -1;
        };
        Assert.False(Apply(driver, 150, 1000, out var error));
        Assert.Contains("restored and verified", error);
        Assert.Equal(100_000, driver.CoreKhz);
        Assert.Contains((4, 800_000), driver.Writes);
    }

    [Fact]
    public void RollbackFailureIsExplicitAndDoesNotSkipTheOtherDomain()
    {
        var driver = new FakeDriver();
        driver.WriteStatus = (domain, value) =>
            domain == 4 && value == 1_000_000 || domain == 0 && value == 100_000 ? -1 : 0;
        Assert.False(Apply(driver, 150, 1000, out var error));
        Assert.Contains("Rollback failed", error);
        Assert.Contains("core=NvAPI_GPU_SetPstates20", error);
        Assert.Equal(150_000, driver.CoreKhz);
        Assert.Equal((4, 800_000), driver.Writes[^1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void AcceptedButClampedWritesFailAndRestoreTheOldSnapshot(int clampedDomain)
    {
        var driver = new FakeDriver();
        driver.WriteStatus = (domain, value) =>
        {
            if (domain == clampedDomain && (value == 150_000 || value == 1_000_000))
            {
                if (domain == 0) driver.CoreKhz = 140_000;
                else driver.MemoryKhz = 900_000;
                return 1; // FakeDriver converts this to accepted without applying value.
            }
            return 0;
        };
        Assert.False(Apply(driver, 150, 1000, out var error));
        Assert.Contains("readback mismatch", error);
        Assert.Contains("restored and verified", error);
        Assert.Equal(100_000, driver.CoreKhz);
        Assert.Equal(800_000, driver.MemoryKhz);
        Assert.Equal(3, driver.Reads);
    }

    [Fact]
    public void FailedReadbackAfterAcceptedWritesStillRestores()
    {
        var driver = new FakeDriver { ReadStatus = read => read == 2 ? -3 : 0 };
        Assert.False(Apply(driver, 150, 1000, out var error));
        Assert.Contains("Offset readback failed", error);
        Assert.Contains("restored and verified", error);
        Assert.Equal(100_000, driver.CoreKhz);
        Assert.Equal(800_000, driver.MemoryKhz);
    }

    [Fact]
    public void RollbackWithoutReadableVerificationIsReportedAsFailed()
    {
        var driver = new FakeDriver { ReadStatus = read => read > 1 ? -3 : 0 };
        Assert.False(Apply(driver, 150, 1000, out var error));
        Assert.Contains("Rollback failed", error);
        Assert.Contains("readback=NvAPI_GPU_GetPstates20", error);
        Assert.Equal([(0, 100_000), (4, 800_000)], driver.Writes.TakeLast(2));
    }

    [Fact]
    public void RollbackAcceptedButIgnoredIsNotReportedAsRestored()
    {
        var driver = new FakeDriver();
        driver.WriteStatus = (domain, value) =>
            domain == 4 && value == 1_000_000 ? -1 : domain == 0 && value == 100_000 ? 1 : 0;
        Assert.False(Apply(driver, 150, 1000, out var error));
        Assert.Contains("Rollback failed", error);
        Assert.Contains("reported 150000/800000", error);
    }

    [Fact]
    public void RetryRecreatesTheSetterBlockAndKeepsTheExistingDelay()
    {
        var driver = new FakeDriver();
        var attempts = 0;
        var delays = new List<int>();
        Assert.True(NvApiGpuClockOffset.TryApply(150, 800, driver.Get, buffer =>
        {
            Assert.Equal(1u, ReadUInt32(buffer, 0x08));
            if (++attempts > 2) return driver.Set(buffer);
            Write(buffer, 0x08, 17);
            return -1;
        }, delays.Add, out var error), error);
        Assert.Equal([90, 90], delays);
        Assert.Equal(3, attempts);
    }

    private static bool Apply(FakeDriver driver, int core, int memory, out string? error) =>
        NvApiGpuClockOffset.TryApply(core, memory, driver.Get, driver.Set, _ => { }, out error);

    private static void AssertInvalid(byte[] buffer)
    {
        Assert.False(NvApiGpuClockOffset.TryParseSnapshot(buffer, out _, out var error));
        Assert.NotNull(error);
    }

    // Independent fixture layout from the local NvAPIWrapper V1 structure declarations:
    // 20-byte header; 456-byte states; 44-byte clocks; editable bit 0 at each level.
    private static byte[] Snapshot(int coreKhz = 100_000, int memoryKhz = 800_000,
        bool reversed = false, int p0Index = 0)
    {
        var buffer = new byte[0x1CF8];
        Write(buffer, 0, 0x11C94);
        Write(buffer, 4, 1);
        Write(buffer, 8, (uint)(p0Index + 1));
        Write(buffer, 12, 2);
        for (var index = 0; index < p0Index; index++) Write(buffer, 20 + index * 456, (uint)(index + 1));
        var p0 = 20 + p0Index * 456;
        Write(buffer, p0 + 4, 1);
        Clock(p0 + 8 + (reversed ? 44 : 0), 0, coreKhz, 200_000);
        Clock(p0 + 8 + (reversed ? 0 : 44), 4, memoryKhz, 2_000_000);
        return buffer;

        void Clock(int offset, uint domain, int delta, int maximum)
        {
            Write(buffer, offset, domain);
            Write(buffer, offset + 8, 1);
            Write(buffer, offset + 12, unchecked((uint)delta));
            Write(buffer, offset + 16, unchecked((uint)-500_000));
            Write(buffer, offset + 20, (uint)maximum);
        }
    }

    private sealed class FakeDriver
    {
        internal int CoreKhz = 100_000;
        internal int MemoryKhz = 800_000;
        internal int Reads;
        internal List<(int Domain, int Delta)> Writes { get; } = [];
        internal Func<int, int>? ReadStatus;
        internal Func<int, int, int>? WriteStatus;

        internal int Get(byte[] buffer)
        {
            var status = ReadStatus?.Invoke(++Reads) ?? 0;
            if (ReadStatus is null) Reads++;
            if (status != 0) return status;
            Snapshot(CoreKhz, MemoryKhz).CopyTo(buffer, 0);
            return 0;
        }

        internal int Set(byte[] buffer)
        {
            var domain = (int)ReadUInt32(buffer, 0x1C);
            var value = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0x28, 4));
            Writes.Add((domain, value));
            var status = WriteStatus?.Invoke(domain, value) ?? 0;
            if (status < 0) return status;
            if (status == 0)
            {
                if (domain == 0) CoreKhz = value;
                else MemoryKhz = value;
            }
            return 0;
        }
    }

    private static void Write(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, sizeof(uint)));
}
