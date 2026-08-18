using OpenSynapse.ProtocolProbe;

namespace OpenSynapse.Core.Tests;

public sealed class ProbeCatalogTests
{
    public static IEnumerable<object[]> NewSourceBackedGets =>
        new object[][]
        {
            new object[] { "blade.max-fan-speed-mode", (ushort)0x02C6, (byte)0x1F, (byte)0x01, (byte)0x07, (byte)0x8F, "00", 2, true },
            new object[] { "blade.logo-power", (ushort)0x02C6, (byte)0xFF, (byte)0x03, (byte)0x03, (byte)0x80, "010400", 2, false },
            new object[] { "blade.logo-mode", (ushort)0x02C6, (byte)0xFF, (byte)0x03, (byte)0x03, (byte)0x82, "010400", 2, false },
            new object[] { "blade.fan-id-list", (ushort)0x02C6, (byte)0x1F, (byte)0x50, (byte)0x0D, (byte)0x80, "", 2, false },
            new object[] { "blade.current-fan-speed-cpu", (ushort)0x02C6, (byte)0x1F, (byte)0x03, (byte)0x0D, (byte)0x88, "0101", 2, false },
            new object[] { "blade.current-fan-speed-gpu", (ushort)0x02C6, (byte)0x1F, (byte)0x03, (byte)0x0D, (byte)0x88, "0102", 2, false },
            new object[] { "blade.advanced-fan-cpu", (ushort)0x02C6, (byte)0x1F, (byte)0x03, (byte)0x0D, (byte)0x87, "0101", 2, false },
            new object[] { "blade.advanced-fan-gpu", (ushort)0x02C6, (byte)0x1F, (byte)0x03, (byte)0x0D, (byte)0x87, "0102", 2, false },
            new object[] { "blade.native-display-mode", (ushort)0x02C6, (byte)0x1F, (byte)0x01, (byte)0x0D, (byte)0x8E, "00", 2, true },
            new object[] { "blade.sku-hardware-configuration", (ushort)0x02C6, (byte)0x1F, (byte)0x01, (byte)0x0D, (byte)0x8F, "00", 2, true },
            new object[] { "blade.game-mode", (ushort)0x02C6, (byte)0x00, (byte)0x04, (byte)0x00, (byte)0x88, "", 2, false },
            new object[] { "blade.startup-animation", (ushort)0x02C6, (byte)0x1F, (byte)0x01, (byte)0x0F, (byte)0x98, "00", 2, false },
            new object[] { "viper.low-battery-threshold", (ushort)0x00B8, (byte)0x1F, (byte)0x01, (byte)0x07, (byte)0x81, "", 60, false },
            new object[] { "viper.dpi-stages", (ushort)0x00B8, (byte)0x1F, (byte)0x26, (byte)0x04, (byte)0x86, "01", 60, false },
        };

    public static IEnumerable<object?[]> ValidOptions =>
        new object?[][]
        {
            new object?[] { Array.Empty<string>(), false, null },
            new object?[] { new[] { "--include-source-backed" }, true, null },
            new object?[] { new[] { "--output", "probe.json" }, false, "probe.json" },
            new object?[] { new[] { "--include-source-backed", "--output", "probe.json" }, true, "probe.json" },
        };

    [Fact]
    public void DefaultCatalogContainsOnlyLocallyVerifiedReads()
    {
        var commands = ProbeCatalog.Get(includeSourceBacked: false);

        Assert.NotEmpty(commands);
        Assert.All(commands, command => Assert.Equal(ProbeEvidenceLevel.Verified, command.Evidence));
    }

    [Theory]
    [InlineData("blade.thermal-zone-1")]
    [InlineData("blade.thermal-zone-2")]
    [InlineData("blade.charge-limit")]
    [InlineData("blade.cpu-boost")]
    [InlineData("blade.gpu-boost")]
    public void DefaultCatalogIncludesBladeReadsWithHardwareRestoreEvidence(string name)
    {
        var command = Assert.Single(
            ProbeCatalog.Get(includeSourceBacked: false), command => command.Name == name);

        Assert.Equal(ProbeEvidenceLevel.Verified, command.Evidence);
    }

    [Theory]
    [InlineData("blade.cpu-boost", "000100")]
    [InlineData("blade.gpu-boost", "000200")]
    public void VerifiedBladeBoostGetsMatchHardwareValidatedProtocol(string name, string argumentHex)
    {
        var command = Assert.Single(
            ProbeCatalog.Get(includeSourceBacked: false), command => command.Name == name);

        Assert.Equal((ushort)0x02C6, command.ProductId);
        Assert.Equal(ProbeEvidenceLevel.Verified, command.Evidence);
        Assert.Equal((byte)0x1F, command.TransactionId);
        Assert.Equal((byte)0x03, command.DataSize);
        Assert.Equal((byte)0x0D, command.CommandClass);
        Assert.Equal((byte)0x87, command.CommandId);
        Assert.Equal(Convert.FromHexString(argumentHex), command.Arguments.ToArray());
        Assert.Equal(2, command.WaitMilliseconds);
        Assert.False(command.AllowRemainingPacketsMismatch);
    }

    [Fact]
    public void CatalogContainsOnlySupportedPidsAndGetCommands()
    {
        var commands = ProbeCatalog.Get(includeSourceBacked: true);

        Assert.All(commands, command =>
        {
            Assert.Contains(command.ProductId, new ushort[] { 0x02C6, 0x00B8 });
            if (command.TransactionId == 0)
            {
                Assert.Equal("blade.game-mode", command.Name);
            }
            Assert.NotEqual(0, command.CommandId & 0x80);
            Assert.InRange(command.DataSize, (byte)0, (byte)80);
            Assert.True(command.Arguments.Length <= command.DataSize);
            Assert.True(command.WaitMilliseconds > 0);
        });
    }

    [Fact]
    public void CommandNamesAreUniquePerDevice()
    {
        var commands = ProbeCatalog.Get(includeSourceBacked: true);
        var keys = commands.Select(command => (command.ProductId, command.Name)).ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(NewSourceBackedGets))]
    public void SourceBackedGetReportsMatchDeviceSpecificSources(
        string name,
        ushort productId,
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        string argumentHex,
        int waitMilliseconds,
        bool allowRemainingPacketsMismatch)
    {
        var command = Assert.Single(
            ProbeCatalog.Get(includeSourceBacked: true), command => command.Name == name);

        Assert.Equal(ProbeEvidenceLevel.SourceBacked, command.Evidence);
        Assert.Equal(productId, command.ProductId);
        Assert.Equal(transactionId, command.TransactionId);
        Assert.Equal(dataSize, command.DataSize);
        Assert.Equal(commandClass, command.CommandClass);
        Assert.Equal(commandId, command.CommandId);
        Assert.Equal(Convert.FromHexString(argumentHex), command.Arguments.ToArray());
        Assert.Equal(waitMilliseconds, command.WaitMilliseconds);
        Assert.Equal(allowRemainingPacketsMismatch, command.AllowRemainingPacketsMismatch);
    }

    [Theory]
    [MemberData(nameof(ValidOptions))]
    public void ParsesOnlySupportedOptions(string[] args, bool includeSourceBacked, string? outputPath)
    {
        var options = ProbeOptions.Parse(args);

        Assert.Equal(includeSourceBacked, options.IncludeSourceBacked);
        Assert.Equal(outputPath, options.OutputPath);
    }

    [Theory]
    [InlineData("--class")]
    [InlineData("--command")]
    [InlineData("--args")]
    [InlineData("--output")]
    public void RejectsOptionsThatCouldCreateArbitraryCommands(string option)
    {
        Assert.Throws<ArgumentException>(() => ProbeOptions.Parse(new[] { option }));
    }

    [Fact]
    public void RejectsOptionLookingOutputPath()
    {
        Assert.Throws<ArgumentException>(() => ProbeOptions.Parse(new[] { "--output", "--class" }));
    }
}
