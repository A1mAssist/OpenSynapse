# Remove CPU Voltage Design

## Goal

Remove CPU voltage from OpenSynapse because the ordinary-user Windows telemetry path cannot provide a trustworthy value.

## Design

- Remove the voltage metric from the CPU dashboard and change the metric grid from five equal columns to four.
- Remove CPU voltage from `PerformanceSnapshot`, `CpuHardwareSample`, and `MainViewModel` rather than retaining a permanently null compatibility field.
- Keep CPU temperature, package power, load, and clock behavior unchanged.
- Do not add PawnIO, an elevated helper, a replacement voltage source, or a migration layer.

## Verification

- Search production and test sources for remaining CPU voltage symbols and visible labels.
- Build the complete solution with zero warnings and errors.
- Run the complete test project; hardware-mutating tests may remain skipped by their existing opt-in gates.

