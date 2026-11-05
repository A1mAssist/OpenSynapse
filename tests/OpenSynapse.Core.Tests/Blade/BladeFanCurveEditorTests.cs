using OpenSynapse.Core.Devices;
using Xunit;

public sealed class BladeFanCurveEditorTests
{
    [Fact]
    public void NewEditorStartsFromTheDefaultCurve()
    {
        var editor = new BladeFanCurveEditorViewModel();

        Assert.Equal(BladeFanCurve.CreateDefault().CpuPoints.Count, editor.Points.Count);
        Assert.NotNull(editor.BuildCurve());
    }

    [Fact]
    public void EditingTemperatureOutOfOrderProducesAValidCurve()
    {
        var editor = new BladeFanCurveEditorViewModel();
        editor.Points[0].TemperatureCelsius = 90;
        editor.Points[^1].TemperatureCelsius = 55;

        // BuildCurve must not throw: tiers get ordered and de-duplicated first.
        var curve = editor.BuildCurve();

        var temperatures = curve.CpuPoints.Select(point => point.TemperatureCelsius).ToArray();
        Assert.Equal(temperatures.Order(), temperatures);
        Assert.Equal(temperatures.Distinct().Count(), temperatures.Length);
    }

    [Fact]
    public void FanSpeedsAreClampedToTheProtocolRange()
    {
        var editor = new BladeFanCurveEditorViewModel();

        editor.Points[0].CpuRpm = 99999;
        editor.Points[0].GpuRpm = -50;

        Assert.Equal(BladeFanCurve.MaximumPointFanSpeedRpm, editor.Points[0].CpuRpm);
        Assert.Equal(BladeFanCurve.MinimumPointFanSpeedRpm, editor.Points[0].GpuRpm);
    }

    [Fact]
    public void RemovingTiersStopsAtOne()
    {
        var editor = new BladeFanCurveEditorViewModel(BladeFanCurve.CreateDefault());
        editor.SelectedIndex = 0;
        while (editor.Points.Count > 1)
        {
            editor.RemoveSelected();
        }

        editor.RemoveSelected();
        Assert.Single(editor.Points);
    }

    [Fact]
    public void AddingTiersStopsAtTheEditorLimit()
    {
        var editor = new BladeFanCurveEditorViewModel();
        for (var index = 0; index < 40; index++)
        {
            editor.AddPoint();
        }

        Assert.False(editor.CanAddPoint);
        Assert.Equal(editor.Points.Count, editor.BuildCurve().CpuPoints.Count);
    }

    [Fact]
    public void NumberBoxSurfaceKeepsTheLastValueWhenTheFieldIsEmpty()
    {
        var point = new BladeFanCurvePointViewModel(new BladeFanCurvePoint(70, 3000, 3100));

        // NumberBox reports NaN while its field is empty or holds text.
        point.TemperatureInput = double.NaN;
        point.CpuRpmInput = double.NaN;
        point.GpuRpmInput = double.NaN;

        Assert.Equal(70, point.TemperatureCelsius);
        Assert.Equal(3000, point.CpuRpm);
        Assert.Equal(3100, point.GpuRpm);
    }

    [Fact]
    public void NumberBoxSurfaceRoundsAndClamps()
    {
        var point = new BladeFanCurvePointViewModel(new BladeFanCurvePoint(70, 3000, 3100));

        point.CpuRpmInput = 3210.6;
        point.TemperatureInput = 999;

        Assert.Equal(3211, point.CpuRpm);
        Assert.Equal(BladeFanCurvePointViewModel.MaximumTemperatureCelsius, point.TemperatureCelsius);
    }

    [Fact]
    public void ResetRestoresTheDefaultCurve()
    {
        var editor = new BladeFanCurveEditorViewModel();
        editor.Points[0].CpuRpm = 4900;

        editor.ResetToDefault();

        Assert.Equal(BladeFanCurve.CreateDefault().CpuPoints[0].CpuFanSpeedRpm, editor.Points[0].CpuRpm);
    }

    [Fact]
    public void EditorLoadsAndBuildsSeparateCpuAndGpuCurves()
    {
        BladeFanCurvePoint[] cpuPoints =
        [
            new(52, 3000, 2300),
            new(80, 4800, 2300),
        ];
        BladeFanCurvePoint[] gpuPoints =
        [
            new(44, 2300, 3600),
            new(90, 2300, 5200),
        ];
        var editor = new BladeFanCurveEditorViewModel(new BladeFanCurve(
            BladeFanCurveTemperatureMode.Independent,
            cpuPoints,
            gpuPoints));

        Assert.Equal(52, editor.CpuPoints[0].TemperatureCelsius);
        Assert.Equal(44, editor.GpuPoints[0].TemperatureCelsius);

        var curve = editor.BuildCurve();

        Assert.Equal(BladeFanCurveTemperatureMode.Independent, curve.TemperatureMode);
        Assert.Equal(cpuPoints, curve.CpuPoints);
        Assert.Equal(gpuPoints, curve.GpuPoints);
    }

    [Fact]
    public void PointEditsOnlyTouchTheSelectedCurve()
    {
        var editor = new BladeFanCurveEditorViewModel();
        var cpuCount = editor.CpuPoints.Count;
        var gpuCount = editor.GpuPoints.Count;

        editor.SelectCurve(BladeFanCurveSelection.Gpu);
        editor.AddPoint();

        Assert.Equal(cpuCount, editor.CpuPoints.Count);
        Assert.Equal(gpuCount + 1, editor.GpuPoints.Count);

        editor.CpuPoints[0].CpuRpm = 4900;
        editor.ResetToDefault();

        Assert.Equal(4900, editor.CpuPoints[0].CpuRpm);
        Assert.Equal(gpuCount, editor.GpuPoints.Count);
    }
}
