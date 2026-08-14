namespace OpenSynapse.Core.Sensors;

public interface IPerformanceMonitor
{
    ValueTask<PerformanceSnapshot> SampleAsync(CancellationToken cancellationToken = default);
}
