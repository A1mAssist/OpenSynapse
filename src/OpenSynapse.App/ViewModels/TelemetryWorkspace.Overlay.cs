namespace OpenSynapse.App.ViewModels;

public sealed partial class TelemetryWorkspace
{
    public bool HasCpuUsage => _system.HasCpuUsage;
    public bool HasGpuUsage => _system.HasGpuUsage;
    public string GpuMemoryUsedText => _system.GpuMemoryUsedText;
    public double GpuMemoryPercent => _system.GpuMemoryPercent;
    public bool HasGpuMemory => _system.HasGpuMemory;
    public string MemoryUsedText => _system.MemoryUsedText;
    public bool HasMemory => _system.HasMemory;
}
