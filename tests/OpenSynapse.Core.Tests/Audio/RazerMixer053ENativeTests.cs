using System.Buffers.Binary;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerMixer053ENativeTests
{
    [Fact]
    public void DllCandidatesPreferSynapseThenApplicationDirectory()
    {
        var app = Path.Combine(Path.GetTempPath(), "OpenSynapseApp");
        var local = Path.Combine(Path.GetTempPath(), "OpenSynapseLocal");

        var candidates = RazerMixer053ENative.BuildCandidatePaths(app, local);

        Assert.Equal(
            Path.Combine(local, "Razer", "Synapse3", "Apps", "Synapse", RazerMixer053ENative.DllFileName),
            candidates[0]);
        Assert.Equal(
            Path.Combine(app, RazerMixer053ENative.DllFileName),
            candidates[1]);
        Assert.Contains(
            Path.Combine(
                local,
                "Razer",
                "RazerAppEngine",
                "User Data",
                "Apps",
                "Synapse",
                RazerMixer053ENative.DllFileName),
            candidates);
    }

    [Fact]
    public void FindDllReturnsTheFirstExistingCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"os-053e-{Guid.NewGuid():N}");
        var app = Path.Combine(root, "app");
        var local = Path.Combine(root, "local");
        var synapseDll = Path.Combine(local, "Razer", "Synapse3", "Apps", "Synapse", RazerMixer053ENative.DllFileName);
        var appDll = Path.Combine(app, RazerMixer053ENative.DllFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(synapseDll)!);
        Directory.CreateDirectory(app);
        File.WriteAllBytes(synapseDll, []);
        File.WriteAllBytes(appDll, []);

        try
        {
            Assert.Equal(synapseDll, RazerMixer053ENative.FindDll(app, local));

            File.Delete(synapseDll);
            Assert.Equal(appDll, RazerMixer053ENative.FindDll(app, local));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingDllReturnsAnUnavailableProviderWithoutThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"os-053e-missing-{Guid.NewGuid():N}");
        try
        {
            var native = RazerMixer053ENative.Load(
                appDirectory: root,
                localAppData: root,
                diagnostic: null);

            Assert.False(native.IsAvailable);
            Assert.Null(native.DllPath);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DeviceJsonParsesTheRzNativeShape()
    {
        const string json = """{"ID" : 42, "JackType" : 1001, "DataFlow" : 0, "DeviceState" : 1}""";

        Assert.True(RazerMixer053ENative.TryParseDevice(json, out var device));
        Assert.Equal(42, device.Id);
        Assert.Equal(1001, device.JackType);
        Assert.Equal(0, device.DataFlow);
        Assert.Equal(1, device.DeviceState);
        Assert.Equal(json, device.Json);

        Assert.False(RazerMixer053ENative.TryParseDevice("""{"status" : "error"}""", out _));
        Assert.False(RazerMixer053ENative.TryParseDevice(null, out _));
    }

    [Fact]
    public void PeakBytesConvertTwoLittleEndianFloats()
    {
        var data = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(0, 4), 0.25f);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4, 4), 0.5f);

        Assert.True(RazerMixer053ENative.TryConvertPeak(data, out var peak));
        Assert.Equal(0.25f, peak.Left);
        Assert.Equal(0.5f, peak.Right);
        Assert.False(RazerMixer053ENative.TryConvertPeak(data.AsSpan(0, 7), out _));
    }

    [Fact]
    public void PeakJsonRejectsErrorsAndShortPayloads()
    {
        const string valid = """{"status" : "success", "data" : [0, 0, 128, 62, 0, 0, 0, 63]}""";

        Assert.True(RazerMixer053ENative.TryParsePeakJson(valid, out var peak));
        Assert.Equal(0.25f, peak.Left);
        Assert.Equal(0.5f, peak.Right);

        Assert.False(RazerMixer053ENative.TryParsePeakJson("""{"status" : "error", "data" : []}""", out _));
        Assert.False(RazerMixer053ENative.TryParsePeakJson("""{"data" : [0, 0, 128]}""", out _));
        Assert.False(RazerMixer053ENative.TryParsePeakJson(null, out _));
    }
}
