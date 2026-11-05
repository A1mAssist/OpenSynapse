using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeAuditCurveTests
{
    [Theory]
    [InlineData(35)]
    [InlineData(40)]
    public void CpuLowerBoundIsStrictlyAboveTheCurveBaseline(int temperature)
    {
        var editor = new BladeFanCurveEditorViewModel();
        editor.CpuPoints[0].TemperatureInput = temperature;
        var curve = editor.BuildCurve();
        Assert.Equal(41, editor.CpuPoints[0].MinimumEditableTemperatureCelsius);
        Assert.All(curve.CpuPoints, point => Assert.True(point.TemperatureCelsius > curve.MinimumCpuTemperatureCelsius));
    }

    [Fact]
    public void DuplicateMaximumTemperaturesFitBelowTheUpperBound()
    {
        var editor = new BladeFanCurveEditorViewModel();
        foreach (var point in editor.CpuPoints.Concat(editor.GpuPoints)) point.TemperatureInput = 120;
        var curve = editor.BuildCurve();
        Assert.Equal(120, curve.CpuPoints[^1].TemperatureCelsius);
        Assert.Equal(curve.CpuPoints.Count, curve.CpuPoints.Select(point => point.TemperatureCelsius).Distinct().Count());
        Assert.All(curve.GpuPoints, point => Assert.InRange(point.TemperatureCelsius, 31, 120));
    }

    [Fact]
    public void CustomBaselinesSurviveAddResetAndBuildWithOnlyOneTemperatureAvailable()
    {
        var source = new BladeFanCurve(BladeFanCurveTemperatureMode.Both, [new(120, 3200, 3200)],
            [new(120, 3300, 3300)], 119, 119, 2500);
        var editor = new BladeFanCurveEditorViewModel(source);
        foreach (var selection in Enum.GetValues<BladeFanCurveSelection>())
        {
            editor.SelectedCurve = selection;
            Assert.False(editor.CanAddPoint);
            editor.AddPoint();
            editor.ResetToDefault();
            Assert.Single(editor.Points);
            Assert.Equal(120, editor.Points[0].TemperatureCelsius);
        }
        var result = editor.BuildCurve();
        Assert.Equal(119, result.MinimumCpuTemperatureCelsius);
        Assert.Equal(119, result.MinimumGpuTemperatureCelsius);
        Assert.Equal(2500, result.MinimumFanSpeedRpm);
    }

    [Fact]
    public void NumberBoxExtremesClampBeforeDoubleToIntConversion()
    {
        var point = new BladeFanCurvePointViewModel(new(70, 3000, 3100));
        point.TemperatureInput = double.MaxValue; point.CpuRpmInput = double.MaxValue;
        point.GpuRpmInput = -double.MaxValue;
        Assert.Equal(120, point.TemperatureCelsius);
        Assert.Equal(5400, point.CpuRpm);
        Assert.Equal(1900, point.GpuRpm);
        point.RpmInput = double.PositiveInfinity; point.TemperatureInput = double.NaN;
        Assert.Equal(5400, point.Rpm); Assert.Equal(120, point.TemperatureCelsius);
    }

    [Theory]
    [InlineData(0x0239, 5400)]
    [InlineData(0x0252, 5400)]
    [InlineData(0x0259, 5400)]
    [InlineData(0x029F, 5300)]
    public void EmptyAndSavedCurveEditorsRespectTheCurveDomainAndModelCeiling(ushort pid, int maximum)
    {
        var modelCeiling = BladeModelCapabilities.MaximumWritableFanTargetRpmFor(pid);
        var source = BladeFanCurve.CreateDefault();
        foreach (var curve in new[] { source, OpenSynapse.Core.Profiles.BladeFanCurveProfile.FromCurve(source).CreateCurve() })
        {
            var editor = new BladeFanCurveEditorViewModel(curve.ClampMaximumFanSpeed(modelCeiling));
            var restored = editor.BuildCurve();
            Assert.All(restored.CpuPoints.Concat(restored.GpuPoints), point =>
            { Assert.InRange(point.CpuFanSpeedRpm, 1900, maximum); Assert.InRange(point.GpuFanSpeedRpm, 1900, maximum); });
        }
    }

    [Fact]
    public void CurveBuildStaysInsideTheDeviceOperationErrorBoundary()
    {
        var source = File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/ViewModels/FanWorkspace.cs"));
        var method = source[source.IndexOf("private async Task ApplyBladeFanCurveCoreAsync", StringComparison.Ordinal)..];
        Assert.True(method.IndexOf("await _runDeviceOperation", StringComparison.Ordinal) <
            method.IndexOf("curve = createCurve()", StringComparison.Ordinal));
        Assert.Contains("catch (ArgumentException exception) { throw new InvalidOperationException", method);
    }

    [Fact]
    public void PlotUnloadDetachesEditorAndPointEventsAndReloadReattaches()
    {
        // Source lifecycle contract only; this does not instantiate WinUI or prove GC.
        var plot = File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/BladeFanCurvePlot.xaml.cs"));
        var unload = plot[plot.IndexOf("Unloaded +=", StringComparison.Ordinal)..plot.IndexOf("PlotCanvas.PointerPressed +=", StringComparison.Ordinal)];
        Assert.Contains("Attach(null)", unload);
        Assert.Contains("ReleasePointerCaptures()", unload);
        Assert.Contains("_editor.PropertyChanged -= EditorChanged", plot);
        Assert.Contains("point.PropertyChanged -= PointChanged", plot);
        Assert.Contains("Attach(IsLoaded ? DataContext", plot);
        Assert.Contains("Attach(DataContext as BladeFanCurveEditorViewModel)", plot);
        var window = File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App/BladeFanCurveWindow.xaml.cs"));
        Assert.Contains("Closed += (_, _) => RootLayout.DataContext = null", window);
    }
}
