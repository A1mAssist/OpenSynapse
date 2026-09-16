namespace OpenSynapse.Core.Sensors;

public interface IPerformanceMonitor
{
    ValueTask<PerformanceSnapshot> SampleAsync(CancellationToken cancellationToken = default);

    async ValueTask<(double? CpuTemperatureCelsius, double? GpuTemperatureCelsius)> SampleTemperaturesAsync(
        bool includeCpu,
        bool includeGpu,
        CancellationToken cancellationToken = default)
    {
        var sample = await SampleAsync(cancellationToken).ConfigureAwait(false);
        return (
            includeCpu ? sample.CpuTemperatureCelsius : null,
            includeGpu ? sample.GpuTemperatureCelsius : null);
    }
}
