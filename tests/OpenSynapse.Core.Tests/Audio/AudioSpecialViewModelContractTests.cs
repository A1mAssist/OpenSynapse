using Xunit;

namespace OpenSynapse.Core.Tests;

// Source-level contracts only: the test project deliberately does not compile App sources.
// Real WinUI synchronization-context/timer acceptance remains a separate check.
public sealed class AudioSpecialViewModelContractTests
{
    [Fact]
    public void FirstBleConnectionAndReadShareOneBusyLifetime()
    {
        var source = Source("RazerBleDeviceViewModel.cs");
        var start = source.IndexOf("public async Task LoadAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task LoadLightingAsync", start, StringComparison.Ordinal);
        var load = source[start..end];
        Assert.Equal(1, load.Split("BeginOperation()").Length - 1);
        Assert.Contains("finally", load);
        Assert.Contains("EndOperation();", load);
        Assert.True(load.IndexOf("OpenAsync", StringComparison.Ordinal) < load.IndexOf("ReadStateAsync", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("RazerAwBleHeadsetViewModel.cs")]
    [InlineData("RazerAwBleHeadsetViewModel.Controls.cs")]
    [InlineData("RazerAwBleHeadsetViewModel.Info.cs")]
    [InlineData("RazerCoolingPadViewModel.cs")]
    [InlineData("RazerCoolingPadViewModel.Curve.cs")]
    [InlineData("RazerCoolingPadViewModel.FixedMode.cs")]
    [InlineData("RazerCoolingPadViewModel.Lighting.cs")]
    public void BoundStateAndTimerContinuationsKeepTheCallingContext(string file) =>
        Assert.DoesNotContain("ConfigureAwait(false)", Source(file));

    [Fact]
    public void ThxResponsesUseTheirOwnEndpointAndCannotClearAnotherRequestsBusyState()
    {
        var state = Source("ThxAudioViewModel.State.cs");
        Assert.Contains("var endpoint = state.Endpoint;", state);
        Assert.Contains("generation == _requestGeneration", state);
        Assert.Contains("status.State.Endpoint == endpoint", state);
        Assert.Contains("IsBusy = --_pendingRequests > 0;", state);
        var source = Source("ThxAudioViewModel.cs");
        Assert.Contains("++_requestGeneration;", source);
        Assert.Contains("_state = null;", source);
    }

    [Fact]
    public void HueHostChangesInvalidateCredentialsRegardlessOfDiscoverySelection()
    {
        var source = Source("PhilipsHueViewModel.cs");
        var change = source[source.IndexOf("var sameHost =", StringComparison.Ordinal)..];
        Assert.True(change.IndexOf("_appKey = null;", StringComparison.Ordinal) <
            change.IndexOf("if (_selectedBridge", StringComparison.Ordinal));
        Assert.Contains("SelectedColorLight = null;", change);
        Assert.Contains("PhilipsHueClient.NormalizeHost(left)", source);
        var pair = source[source.IndexOf("case PhilipsHuePairingOutcome.Paired:", StringComparison.Ordinal)..];
        Assert.True(pair.IndexOf("BridgeHost = client.Host;", StringComparison.Ordinal) <
            pair.IndexOf("_appKey = result.AppKey;", StringComparison.Ordinal));
    }

    [Fact]
    public void CoolingTerminalWritesDrainThePreviousCurveAndDisposedTicksCannotStartWrites()
    {
        foreach (var (file, write) in new[]
        {
            ("RazerCoolingPadViewModel.cs", "_service.SetSpeedRpmAsync"),
            ("RazerCoolingPadViewModel.FixedMode.cs", "_service.SetFixedModeAsync"),
        })
        {
            var source = Source(file);
            var stop = source.IndexOf("StopCurveAndSelectManual();", StringComparison.Ordinal);
            var drain = source.IndexOf("await _curveTask;", stop, StringComparison.Ordinal);
            var terminal = source.IndexOf(write, drain, StringComparison.Ordinal);
            Assert.True(stop >= 0 && drain > stop && terminal > drain);
        }
        var curve = Source("RazerCoolingPadViewModel.Curve.cs");
        var sample = curve.IndexOf("SampleTemperaturesAsync", StringComparison.Ordinal);
        var cancelled = curve.IndexOf("cancellationToken.ThrowIfCancellationRequested();", sample, StringComparison.Ordinal);
        var generation = curve.IndexOf("generation != _curveGeneration", sample, StringComparison.Ordinal);
        var speed = curve.IndexOf("_service.SetCurveSpeedAsync", sample, StringComparison.Ordinal);
        Assert.True(cancelled > sample && cancelled < speed && generation < speed);
        Assert.Contains("_curveCancellation?.Cancel();", curve);
        Assert.Contains("_disposed = true; StopCurveTimer();", Source("RazerCoolingPadViewModel.cs"));
    }

    [Fact]
    public void HeadsetAndBleCompositeWritesDoNotSwallowChildFailures()
    {
        var headset = Source("RazerHeadsetAudioViewModel.cs");
        var start = headset.IndexOf("public Task ApplyAdvancedAsync", StringComparison.Ordinal);
        var end = headset.IndexOf("public Task ApplyDndAsync", start, StringComparison.Ordinal);
        var composite = headset[start..end];
        Assert.DoesNotContain("await Apply", composite);
        Assert.Contains("if (_hasDnd) await service.SetAudioDndAsync", composite);
        Assert.Contains("service.SetCustomEqAsync(customPreset, bands, token)", headset);
        var ble = Source("RazerBleDeviceViewModel.Extra.cs");
        start = ble.IndexOf("public async Task ApplyScrollExtrasAsync", StringComparison.Ordinal);
        end = ble.IndexOf("public Task ApplyBatteryChemistryAsync", start, StringComparison.Ordinal);
        composite = ble[start..end];
        Assert.Equal(1, composite.Split("await RunWriteAsync(").Length - 1);
        Assert.Contains("if (TactileModeVisibility == Visibility.Visible)", composite);
        Assert.Contains("if (HapticStageVisibility == Visibility.Visible)", composite);
        Assert.Contains("_scrollApplyFailed || cancellationToken.IsCancellationRequested", composite);
    }

    private static string Source(string name) => File.ReadAllText(Path.Combine(RepoLayout.Root,
        "src", "OpenSynapse.App", "ViewModels", name));
}
