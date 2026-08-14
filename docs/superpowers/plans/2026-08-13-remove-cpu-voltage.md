# Remove CPU Voltage Implementation Plan

> **For agentic workers:** Remove the field end to end; do not retain a compatibility shim.

**Goal:** Remove the unavailable CPU voltage metric from OpenSynapse while preserving the four native CPU metrics.

**Architecture:** Delete the metric at the sensor, snapshot, view-model, and XAML boundaries. Existing consumers are updated at compile time so the build proves the contract is consistent.

**Tech Stack:** C# 14, .NET 10, WinUI 3, xUnit

## Global Constraints

- OpenSynapse remains `asInvoker` and must not load PawnIO or Razer CPUID components.
- CPU temperature, power, load, and clock behavior must not change.
- No compatibility field or replacement voltage provider is added.

---

### Task 1: Delete CPU Voltage End To End

**Files:**
- Modify: `src/OpenSynapse.Windows/Sensors/CpuHardwareMonitor.cs`
- Modify: `src/OpenSynapse.Windows/Sensors/WindowsPerformanceMonitor.cs`
- Modify: `src/OpenSynapse.Core/Sensors/PerformanceSnapshot.cs`
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`
- Modify: `src/OpenSynapse.App/MainWindow.xaml`
- Modify: `tests/OpenSynapse.Core.Tests/CpuHardwareMonitorTests.cs`
- Modify: callers constructing `PerformanceSnapshot`

**Interfaces:**
- Produces: `CpuHardwareSample(double? TemperatureCelsius, double? PowerWatts, int? ClockMegahertz)`
- Produces: `PerformanceSnapshot` without `CpuVoltageVolts`

- [ ] Remove voltage constructor arguments and properties from both telemetry records.
- [ ] Remove `CpuVoltageText`, its formatting/reset paths, and its XAML column.
- [ ] Change the CPU metric grid from five equal columns to four.
- [ ] Update all `PerformanceSnapshot` constructors and affected tests using compiler errors as the exhaustive caller list.
- [ ] Run `rg -n 'CpuVoltage|VoltageVolts|Text="电压"' src tests` and expect no matches.
- [ ] Run `dotnet build OpenSynapse.slnx --no-restore` and expect zero warnings and errors.
- [ ] Run `dotnet test tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj --no-restore` and expect no failures.
