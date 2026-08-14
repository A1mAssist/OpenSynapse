# OpenSynapse Foundation and Protocol Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the working application to OpenSynapse, preserve every currently verified device control, and create a read-only evidence pipeline that can safely turn source-backed Razer commands into device-specific implementation inputs.

**Architecture:** Keep the existing WinUI 3/Core/Windows project split and reuse `RazerFeatureReport`, `IRazerFeatureTransport`, SetupAPI discovery, and current telemetry code. Add one small console probe whose commands come only from a compiled read-only whitelist; it records requests and responses without accepting arbitrary class, command, or argument bytes. This plan deliberately stops before exposing Blade performance, fan, battery, Viper stage, or mapping controls that lack local hardware evidence.

**Tech Stack:** Windows 11 x64, PowerShell 7, .NET 10, WinUI 3, Windows App SDK `1.8.260710003`, SetupAPI, `hid.dll`, `System.Text.Json`, xUnit `2.9.3`.

## Global Constraints

- Supported hardware remains exactly `1532:02C6` and `1532:00B8`.
- The verified control collection remains `UsagePage 0001 / Usage 0002 / FeatureReportByteLength 91`.
- Hardware writes require a successful matching read and immediate readback; this plan adds no new hardware write path.
- The protocol probe contains a compiled GET-only whitelist and exposes no arbitrary transaction ID, class, command ID, data size, or argument CLI options.
- Source-backed responses are evidence, not permission to expose a production write control.
- Advanced lighting editor, macros, and Viper calibration remain outside this plan.
- Do not install a Windows service, kernel driver, USB capture driver, database, plugin loader, or dependency package.
- The workspace currently has no `.git` directory. Do not initialize Git without user authorization; use the verification checkpoints in each task instead of commit steps.
- Preserve changes already present in the workspace. In particular, audit the source-backed Blade performance, fan, and charge-limit code added after the original baseline; do not silently delete or promote it.

---

## File Map

### Renamed active artifacts

- Rename: `RazerLite.slnx` -> `OpenSynapse.slnx`
- Rename: `src/RazerLite.Core` -> `src/OpenSynapse.Core`
- Rename: `src/RazerLite.Windows` -> `src/OpenSynapse.Windows`
- Rename: `src/RazerLite.App` -> `src/OpenSynapse.App`
- Rename: `tests/RazerLite.Core.Tests` -> `tests/OpenSynapse.Core.Tests`
- Rename the four `.csproj` files to match their new directories.
- Modify active C#, XAML, project files, manifest, solution, README, environment-variable names, namespaces, assembly names, and executable name from `RazerLite` to `OpenSynapse`.
- Modify historical design and plan documents only by adding a superseded banner; preserve their historical body.

### New protocol-evidence files

- Create: `tools/OpenSynapse.ProtocolProbe/OpenSynapse.ProtocolProbe.csproj` - dependency-free read-only probe executable.
- Create: `tools/OpenSynapse.ProtocolProbe/ProbeCommand.cs` - whitelist command and evidence-level records.
- Create: `tools/OpenSynapse.ProtocolProbe/ProbeCatalog.cs` - exact verified and source-backed GET commands.
- Create: `tools/OpenSynapse.ProtocolProbe/Program.cs` - discovery, query, redacted JSON recording, and strict CLI parsing.
- Modify: `OpenSynapse.slnx` - include the probe project under `/tools/`.
- Modify: `tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj` - reference the probe project.
- Create: `tests/OpenSynapse.Core.Tests/ProbeCatalogTests.cs` - prove the whitelist cannot contain SET commands or unrelated PIDs.
- Create: `docs/protocol/capability-ledger.md` - human-reviewed capability and evidence record.
- Create: `docs/protocol/probe-schema.md` - JSON field definitions and promotion rules.
- Modify: `README.md` - current verified features, probe usage, hardware-test safety, and naming.

---

### Task 1: Freeze the baseline before renaming

**Files:**
- Inspect: `OpenSynapse` workspace, excluding `bin` and `obj`
- Inspect: `tests/RazerLite.Core.Tests/DeviceIdParserTests.cs`
- Inspect: `tests/RazerLite.Core.Tests/RazerDeviceTelemetryReaderTests.cs`
- Inspect: `README.md`

**Interfaces:**
- Consumes: the current solution and tests exactly as found on 2026-08-11.
- Produces: a recorded baseline of `25` passing tests, `2` skipped opt-in hardware tests, and a solution build with `0` warnings and `0` errors.

- [ ] **Step 1: Confirm the workspace is not a Git repository**

Run:

```powershell
Test-Path -LiteralPath '.git'
```

Expected: `False`. Do not run `git commit` steps in this plan.

- [ ] **Step 2: Run the non-hardware tests**

Run:

```powershell
dotnet test '.\tests\RazerLite.Core.Tests\RazerLite.Core.Tests.csproj' --no-restore
```

Expected: `25` passed, `2` skipped, `0` failed. The skipped tests are the opt-in Blade/Viper hardware smoke tests.

- [ ] **Step 3: Build the full solution**

Run:

```powershell
dotnet build '.\RazerLite.slnx' --no-restore
```

Expected: build succeeds with `0` warnings and `0` errors.

- [ ] **Step 4: Record the late-arriving source-backed code without promoting it**

Run:

```powershell
rg -n 'BladePerformance|BladeFan|ChargeLimit|0x0D|0x92|0x12' '.\src' '.\tests'
```

Expected: the report lists the existing Blade performance/fan/charge-limit parser and fake-transport tests. Record these entries as `SourceBacked` in Task 6 unless an opt-in hardware run produces matching local evidence.

**Verification checkpoint:** No file has changed, and the exact build/test baseline is known.

---

### Task 2: Rename all active artifacts to OpenSynapse

**Files:**
- Rename and modify all active artifacts listed in the File Map.
- Modify: `2026-08-11-razer-lite-design.md`
- Modify: `docs/superpowers/plans/2026-08-11-razer-lite-implementation.md`
- Modify: `docs/superpowers/plans/2026-08-11-razer-lite-stabilization.md`

**Interfaces:**
- Consumes: the baseline solution and namespaces beginning with `RazerLite`.
- Produces: `OpenSynapse.slnx`, `OpenSynapse.App.exe`, `OpenSynapse.Core`, `OpenSynapse.Windows`, `OpenSynapse.App`, `OpenSynapse.Core.Tests`, and `OPENSYNAPSE_HARDWARE_TEST`.

- [ ] **Step 1: Rename project files, directories, and solution with explicit paths**

Run from the repository root:

```powershell
Move-Item -LiteralPath '.\src\RazerLite.Core\RazerLite.Core.csproj' -Destination '.\src\RazerLite.Core\OpenSynapse.Core.csproj'
Move-Item -LiteralPath '.\src\RazerLite.Windows\RazerLite.Windows.csproj' -Destination '.\src\RazerLite.Windows\OpenSynapse.Windows.csproj'
Move-Item -LiteralPath '.\src\RazerLite.App\RazerLite.App.csproj' -Destination '.\src\RazerLite.App\OpenSynapse.App.csproj'
Move-Item -LiteralPath '.\tests\RazerLite.Core.Tests\RazerLite.Core.Tests.csproj' -Destination '.\tests\RazerLite.Core.Tests\OpenSynapse.Core.Tests.csproj'
Move-Item -LiteralPath '.\src\RazerLite.Core' -Destination '.\src\OpenSynapse.Core'
Move-Item -LiteralPath '.\src\RazerLite.Windows' -Destination '.\src\OpenSynapse.Windows'
Move-Item -LiteralPath '.\src\RazerLite.App' -Destination '.\src\OpenSynapse.App'
Move-Item -LiteralPath '.\tests\RazerLite.Core.Tests' -Destination '.\tests\OpenSynapse.Core.Tests'
Move-Item -LiteralPath '.\RazerLite.slnx' -Destination '.\OpenSynapse.slnx'
```

Expected: every source/test directory and project file has an `OpenSynapse` name; historical Markdown filenames remain unchanged.

- [ ] **Step 2: Apply the mechanical identifier replacement only to active files**

Run this bulk mechanical rewrite; it intentionally excludes historical specifications and plans:

```powershell
$activeFiles = @(
    Get-Item -LiteralPath '.\OpenSynapse.slnx', '.\README.md'
    Get-ChildItem -LiteralPath '.\src', '.\tests' -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
)
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
foreach ($file in $activeFiles) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $updated = $text.Replace('RAZERLITE_', 'OPENSYNAPSE_').Replace('RazerLite', 'OpenSynapse')
    if ($updated -ne $text) {
        [System.IO.File]::WriteAllText($file.FullName, $updated, $utf8NoBom)
    }
}
```

Expected replacements include namespaces, `x:Class`, `RootNamespace`, project references, solution paths, manifest identity, title, executable path, README commands, and the hardware-test environment variable.

- [ ] **Step 3: Add a superseded banner to each historical document**

Insert immediately below the first heading of the three old Markdown documents:

```markdown
> Superseded by `docs/superpowers/specs/2026-08-11-opensynapse-functional-parity-design.md`. Retained as historical context only.
```

Do not replace `RazerLite` in the historical body; the old name is part of the record.

- [ ] **Step 4: Verify the active rename is complete**

Run:

```powershell
$activeMatches = @(
    rg -n 'RazerLite|RAZERLITE_' '.\OpenSynapse.slnx' '.\README.md' '.\src' '.\tests' `
        -g '!**/bin/**' -g '!**/obj/**'
)
$activeMatches.Count
```

Expected: `0`.

- [ ] **Step 5: Restore, test, and build under the new names**

Run:

```powershell
dotnet restore '.\OpenSynapse.slnx'
dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore
dotnet build '.\OpenSynapse.slnx' --no-restore
dotnet build '.\src\OpenSynapse.App\OpenSynapse.App.csproj' -p:Platform=x64 --no-restore
```

Expected: `25` passed, `2` skipped, `0` failed; both builds complete with `0` warnings and `0` errors; output includes `OpenSynapse.App.exe`.

**Verification checkpoint:** The application behavior is unchanged, all active identifiers use OpenSynapse, and historical documents are visibly superseded.

---

### Task 3: Add a compile-time read-only probe catalog

**Files:**
- Create: `tools/OpenSynapse.ProtocolProbe/OpenSynapse.ProtocolProbe.csproj`
- Create: `tools/OpenSynapse.ProtocolProbe/ProbeCommand.cs`
- Create: `tools/OpenSynapse.ProtocolProbe/ProbeCatalog.cs`
- Modify: `OpenSynapse.slnx`
- Modify: `tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj`
- Create: `tests/OpenSynapse.Core.Tests/ProbeCatalogTests.cs`

**Interfaces:**
- Consumes: public `OpenSynapse.Windows.Protocols.IRazerFeatureTransport` and `RazerFeatureReport`.
- Produces: `ProbeCatalog.Get(bool includeSourceBacked) : IReadOnlyList<ProbeCommand>` and records that contain no SET command.

- [ ] **Step 1: Create the probe project**

Create `tools/OpenSynapse.ProtocolProbe/OpenSynapse.ProtocolProbe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenSynapse.Core\OpenSynapse.Core.csproj" />
    <ProjectReference Include="..\..\src\OpenSynapse.Windows\OpenSynapse.Windows.csproj" />
  </ItemGroup>
</Project>
```

Add this project under a `/tools/` folder in `OpenSynapse.slnx`, and add a project reference from `tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj` to the probe project.

The resulting `OpenSynapse.slnx` must be:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/OpenSynapse.App/OpenSynapse.App.csproj">
      <Platform Project="x64" />
    </Project>
    <Project Path="src/OpenSynapse.Core/OpenSynapse.Core.csproj" />
    <Project Path="src/OpenSynapse.Windows/OpenSynapse.Windows.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/OpenSynapse.Core.Tests/OpenSynapse.Core.Tests.csproj" />
  </Folder>
  <Folder Name="/tools/">
    <Project Path="tools/OpenSynapse.ProtocolProbe/OpenSynapse.ProtocolProbe.csproj" />
  </Folder>
</Solution>
```

Add this exact item to the test project reference group:

```xml
<ProjectReference Include="..\..\tools\OpenSynapse.ProtocolProbe\OpenSynapse.ProtocolProbe.csproj" />
```

- [ ] **Step 2: Write the failing whitelist tests**

Create `tests/OpenSynapse.Core.Tests/ProbeCatalogTests.cs`:

```csharp
using OpenSynapse.ProtocolProbe;

namespace OpenSynapse.Core.Tests;

public sealed class ProbeCatalogTests
{
    [Fact]
    public void DefaultCatalogContainsOnlyLocallyVerifiedReads()
    {
        var commands = ProbeCatalog.Get(includeSourceBacked: false);

        Assert.NotEmpty(commands);
        Assert.All(commands, command => Assert.Equal(ProbeEvidenceLevel.Verified, command.Evidence));
    }

    [Fact]
    public void CatalogContainsOnlySupportedPidsAndGetCommands()
    {
        var commands = ProbeCatalog.Get(includeSourceBacked: true);

        Assert.All(commands, command =>
        {
            Assert.Contains(command.ProductId, new ushort[] { 0x02C6, 0x00B8 });
            Assert.NotEqual(0, command.CommandId & 0x80);
            Assert.InRange(command.DataSize, (byte)0, (byte)80);
            Assert.True(command.Arguments.Length <= command.DataSize);
        });
    }

    [Fact]
    public void CommandNamesAreUniquePerDevice()
    {
        var commands = ProbeCatalog.Get(includeSourceBacked: true);
        var keys = commands.Select(command => (command.ProductId, command.Name)).ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
    }
}
```

- [ ] **Step 3: Run the focused tests and verify they fail**

Run:

```powershell
dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --filter 'FullyQualifiedName~ProbeCatalogTests'
```

Expected: compile failure because `OpenSynapse.ProtocolProbe`, `ProbeCatalog`, and its record types do not exist.

- [ ] **Step 4: Define the immutable command records**

Create `tools/OpenSynapse.ProtocolProbe/ProbeCommand.cs`:

```csharp
namespace OpenSynapse.ProtocolProbe;

public enum ProbeEvidenceLevel
{
    Verified,
    SourceBacked,
}

public sealed record ProbeCommand(
    ushort ProductId,
    string Name,
    ProbeEvidenceLevel Evidence,
    byte TransactionId,
    byte DataSize,
    byte CommandClass,
    byte CommandId,
    ReadOnlyMemory<byte> Arguments,
    int WaitMilliseconds,
    bool AllowRemainingPacketsMismatch = false);
```

- [ ] **Step 5: Implement the exact GET-only catalog**

Create `tools/OpenSynapse.ProtocolProbe/ProbeCatalog.cs`:

```csharp
namespace OpenSynapse.ProtocolProbe;

public static class ProbeCatalog
{
    private static readonly ProbeCommand[] Commands =
    {
        new(0x02C6, "blade.keyboard-brightness", ProbeEvidenceLevel.Verified,
            0xFF, 0x02, 0x0E, 0x84, new byte[] { 0x01, 0x00 }, 1),

        new(0x02C6, "blade.thermal-zone-1", ProbeEvidenceLevel.SourceBacked,
            0x1F, 0x04, 0x0D, 0x82, new byte[] { 0x00, 0x01, 0x00, 0x00 }, 2),
        new(0x02C6, "blade.thermal-zone-2", ProbeEvidenceLevel.SourceBacked,
            0x1F, 0x04, 0x0D, 0x82, new byte[] { 0x00, 0x02, 0x00, 0x00 }, 2),
        new(0x02C6, "blade.fan-target-zone-1", ProbeEvidenceLevel.SourceBacked,
            0x1F, 0x03, 0x0D, 0x81, new byte[] { 0x00, 0x01, 0x00 }, 2),
        new(0x02C6, "blade.fan-target-zone-2", ProbeEvidenceLevel.SourceBacked,
            0x1F, 0x03, 0x0D, 0x81, new byte[] { 0x00, 0x02, 0x00 }, 2),
        new(0x02C6, "blade.charge-limit", ProbeEvidenceLevel.SourceBacked,
            0x1F, 0x01, 0x07, 0x92, new byte[] { 0x00 }, 2, true),

        new(0x00B8, "viper.battery", ProbeEvidenceLevel.Verified,
            0x1F, 0x02, 0x07, 0x80, Array.Empty<byte>(), 60),
        new(0x00B8, "viper.polling-rate", ProbeEvidenceLevel.Verified,
            0x1F, 0x01, 0x00, 0x85, Array.Empty<byte>(), 60),
        new(0x00B8, "viper.current-dpi", ProbeEvidenceLevel.Verified,
            0x1F, 0x07, 0x04, 0x85, new byte[] { 0x00 }, 60),
        new(0x00B8, "viper.idle-timeout", ProbeEvidenceLevel.Verified,
            0x1F, 0x02, 0x07, 0x83, Array.Empty<byte>(), 60),
    };

    public static IReadOnlyList<ProbeCommand> Get(bool includeSourceBacked) =>
        Commands
            .Where(command => includeSourceBacked || command.Evidence == ProbeEvidenceLevel.Verified)
            .ToArray();
}
```

- [ ] **Step 6: Run the focused tests**

Run:

```powershell
dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --filter 'FullyQualifiedName~ProbeCatalogTests'
```

Expected: `3` passed and `0` failed.

**Verification checkpoint:** All probe commands are immutable compile-time GET commands for the two approved PIDs; no CLI input can construct a hardware command.

---

### Task 4: Implement redacted JSON probe output

**Files:**
- Create: `tools/OpenSynapse.ProtocolProbe/Program.cs`
- Test: `tests/OpenSynapse.Core.Tests/ProbeCatalogTests.cs`

**Interfaces:**
- Consumes: `WindowsHidDiscovery.DiscoverAsync`, `RazerFeatureTransport.QueryAsync`, `RazerFeatureReport.CreateRequest`, and `ProbeCatalog.Get`.
- Produces: CLI options `--include-source-backed` and `--output <path>`, plus JSON containing PID, command metadata, request/response hex, status, error, and UTC timestamp. It never records the full HID device path.

- [ ] **Step 1: Add CLI parsing tests**

Append to `ProbeCatalogTests.cs`:

```csharp
public static IEnumerable<object?[]> ValidOptions =>
    new object?[][]
    {
        new object?[] { Array.Empty<string>(), false, null },
        new object?[] { new[] { "--include-source-backed" }, true, null },
        new object?[] { new[] { "--output", "probe.json" }, false, "probe.json" },
        new object?[] { new[] { "--include-source-backed", "--output", "probe.json" }, true, "probe.json" },
    };

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
```

- [ ] **Step 2: Run the CLI tests and verify they fail**

Run:

```powershell
dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --filter 'FullyQualifiedName~ProbeCatalogTests'
```

Expected: compile failure because `ProbeOptions` does not exist.

- [ ] **Step 3: Implement the strict parser and probe executable**

Create `tools/OpenSynapse.ProtocolProbe/Program.cs`:

```csharp
using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.ProtocolProbe;

public sealed record ProbeOptions(bool IncludeSourceBacked, string? OutputPath)
{
    public static ProbeOptions Parse(string[] args)
    {
        var includeSourceBacked = false;
        string? outputPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--include-source-backed":
                    includeSourceBacked = true;
                    break;
                case "--output" when index + 1 < args.Length:
                    outputPath = args[++index];
                    break;
                default:
                    throw new ArgumentException($"Unsupported or incomplete option: {args[index]}");
            }
        }

        return new ProbeOptions(includeSourceBacked, outputPath);
    }
}

public sealed record ProbeResult(
    ushort ProductId,
    string Name,
    ProbeEvidenceLevel Evidence,
    string RequestHex,
    string? ResponseHex,
    byte? ResponseStatus,
    string? Error);

public sealed record ProbeDocument(
    DateTimeOffset CapturedAt,
    ushort UsagePage,
    ushort Usage,
    ushort FeatureReportByteLength,
    IReadOnlyList<ProbeResult> Results);

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        ProbeOptions options;
        try
        {
            options = ProbeOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 64;
        }

        var snapshot = await new WindowsHidDiscovery().DiscoverAsync();
        var devices = snapshot.Devices
            .Where(device =>
                device.Access == DeviceAccessState.Available &&
                device.FeatureReportByteLength == RazerFeatureReport.Length &&
                device.UsagePage == 0x0001 &&
                device.Usage == 0x0002)
            .ToDictionary(device => device.ProductId);

        var transport = new RazerFeatureTransport();
        var results = new List<ProbeResult>();
        foreach (var command in ProbeCatalog.Get(options.IncludeSourceBacked))
        {
            if (!devices.TryGetValue(command.ProductId, out var device))
            {
                continue;
            }

            var request = RazerFeatureReport.CreateRequest(
                command.TransactionId,
                command.DataSize,
                command.CommandClass,
                command.CommandId,
                command.Arguments.Span);

            try
            {
                var response = await transport.QueryAsync(
                    device.Id,
                    command.TransactionId,
                    command.DataSize,
                    command.CommandClass,
                    command.CommandId,
                    command.Arguments,
                    TimeSpan.FromMilliseconds(command.WaitMilliseconds),
                    CancellationToken.None,
                    command.AllowRemainingPacketsMismatch);
                results.Add(new ProbeResult(
                    command.ProductId,
                    command.Name,
                    command.Evidence,
                    Convert.ToHexString(request),
                    Convert.ToHexString(response),
                    response[1],
                    null));
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                results.Add(new ProbeResult(
                    command.ProductId,
                    command.Name,
                    command.Evidence,
                    Convert.ToHexString(request),
                    null,
                    null,
                    exception.Message));
            }
        }

        var document = new ProbeDocument(
            DateTimeOffset.UtcNow,
            0x0001,
            0x0002,
            RazerFeatureReport.Length,
            results);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);

        if (options.OutputPath is not null)
        {
            var fullPath = Path.GetFullPath(options.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return results.Count == 0 ? 2 : results.Any(result => result.Error is not null) ? 1 : 0;
    }
}
```

- [ ] **Step 4: Run all non-hardware tests and build the probe**

Run:

```powershell
dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj'
dotnet build '.\tools\OpenSynapse.ProtocolProbe\OpenSynapse.ProtocolProbe.csproj'
```

Expected: all non-hardware tests pass, two hardware tests remain skipped, and the probe builds with `0` warnings and `0` errors.

- [ ] **Step 5: Verify arbitrary command options are rejected without touching hardware**

Run:

```powershell
dotnet run --project '.\tools\OpenSynapse.ProtocolProbe\OpenSynapse.ProtocolProbe.csproj' -- --class 0D
```

Expected: exit code `64` and `Unsupported or incomplete option: --class`. Device discovery and HID queries do not run because option parsing happens first.

**Verification checkpoint:** The probe emits redacted JSON, cannot accept arbitrary hardware bytes, and returns distinct exit codes for bad CLI input, no device, query failure, and success.

---

### Task 5: Document capability evidence and promotion rules

**Files:**
- Create: `docs/protocol/capability-ledger.md`
- Create: `docs/protocol/probe-schema.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: the four evidence states from the approved design and the JSON output from Task 4.
- Produces: a reviewable ledger that is the only authority for promoting a source-backed command into a production control.

- [ ] **Step 1: Create the capability ledger with exact current statuses**

Create `docs/protocol/capability-ledger.md` with this table and rules:

```markdown
# OpenSynapse Protocol Capability Ledger

Only `Verified` entries may enable a production write control. `SourceBacked` entries may be queried only by the opt-in read-only probe. `Blocked` entries have no runnable command.

| Device | Capability | Status | Production write | Evidence required for promotion |
|---|---|---:|---:|---|
| Blade `02C6` | Keyboard brightness | Verified | Yes | Existing local write/readback/restore smoke test |
| Blade `02C6` | Firmware quick effects | SourceBacked | No | Exact effect report, success response, visual result, restore |
| Blade `02C6` | `6 x 17` custom matrix | SourceBacked | No | Fixed frame, per-zone visual check, stable repeated send |
| Blade `02C6` | Performance/fan state | SourceBacked | No | Matching two-zone GET responses on `02C6` |
| Blade `02C6` | Performance mode write | SourceBacked | No | Same-value write, two-zone readback, minimal change, restore |
| Blade `02C6` | Fixed/manual fan | Blocked | No | Current limits, safe range, process-exit and sleep recovery |
| Blade `02C6` | Charge limit | SourceBacked | No | GET decode, same-value SET, GET compare, minimal change, restore |
| Blade `02C6` | Keyboard/system/display controls | Blocked | No | Device-specific source or Synapse capture plus readback plan |
| Viper `00B8` | Battery | Verified | Read only | Existing local read result |
| Viper `00B8` | Current X/Y DPI | Verified | Yes | Existing local write/readback/restore smoke test |
| Viper `00B8` | 125/500/1000 Hz | Verified | Yes | Existing local write/readback/restore smoke test |
| Viper `00B8` | Idle timeout | Verified | Yes | Existing local write/readback/restore smoke test |
| Viper `00B8` | DPI stages/current stage | Blocked | No | Device-specific source or Synapse capture and unplug persistence |
| Viper `00B8` | Button mapping/Hypershift/profile | Blocked | No | Device-specific source or Synapse capture and no-Synapse behavior |
| Viper `00B8` | Low-battery policy/battery type | Blocked | No | Device-specific GET/SET/readback evidence |

Promotion requires a dated probe artifact, exact VID/PID and collection, source citation, parser test, same-value write when available, minimal-change write, readback or deterministic behavior check, and restoration of the original state.
```

- [ ] **Step 2: Define the probe JSON schema and redaction rules**

Create `docs/protocol/probe-schema.md`:

```markdown
# Protocol Probe Artifact

The probe writes UTF-8 JSON with `CapturedAt`, collection identity, and `Results`. Each result contains `ProductId`, stable command `Name`, `Evidence`, exact 91-byte `RequestHex`, optional 91-byte `ResponseHex`, response status, and error text.

Artifacts must not contain the HID device path, Windows account name, machine name, serial number, Razer account data, or cloud tokens. Store local runs under `artifacts/protocol/YYYY-MM-DD/`; do not treat them as product configuration.

A successful response proves only that the GET command is accepted by the exact PID/collection. It does not prove that a related SET command, value range, persistence rule, or similarly numbered command is safe.
```

- [ ] **Step 3: Update README to match the approved scope and real status**

README must state all of the following explicitly:

```markdown
- Product name: OpenSynapse.
- Production writes currently verified: Blade brightness; Viper DPI, 125/500/1000 Hz, and idle timeout.
- Blade performance, fan, charge-limit, lighting effects, Viper stages, mappings, and power policy remain evidence-gated.
- The protocol probe is GET-only and defaults to locally verified queries.
- `--include-source-backed` adds only the compiled source-backed GET list.
- Hardware smoke tests require `OPENSYNAPSE_HARDWARE_TEST=1` and restore original values.
- Advanced lighting editor, macros, and Viper calibration are deferred.
```

Do not claim a feature merely because a parser or fake-transport test exists.

- [ ] **Step 4: Scan the active docs for contradictory claims**

Run:

```powershell
rg -n 'verified|已验证|remain disabled|未验证|SourceBacked|Blocked|performance|fan|charge' '.\README.md' '.\docs\protocol' '.\docs\superpowers\specs\2026-08-11-opensynapse-functional-parity-design.md'
```

Expected: current production support and evidence-gated capabilities agree across all three locations.

**Verification checkpoint:** There is one explicit promotion rule, and fake transport tests cannot be mistaken for hardware verification.

---

### Task 6: Run the read-only evidence pass on both devices

**Files:**
- Generate locally: `artifacts/protocol/2026-08-11/verified.json`
- Generate locally: `artifacts/protocol/2026-08-11/source-backed.json`
- Modify after human review: `docs/protocol/capability-ledger.md`

**Interfaces:**
- Consumes: the compiled whitelist and both connected target devices.
- Produces: dated, redacted evidence artifacts and reviewed status changes; it performs no SET operation.

- [ ] **Step 1: Close Synapse manually and confirm both control collections are available**

Run the application or existing discovery test and confirm both devices show:

```text
UsagePage: 0001
Usage: 0002
FeatureReportByteLength: 91
Access: Available
```

Do not terminate Synapse programmatically. If either device remains busy, stop this task and record the access error.

- [ ] **Step 2: Capture the verified GET baseline**

Run:

```powershell
dotnet run --project '.\tools\OpenSynapse.ProtocolProbe\OpenSynapse.ProtocolProbe.csproj' -- `
    --output '.\artifacts\protocol\2026-08-11\verified.json'
```

Expected: exit code `0`; results for Blade brightness and the four Viper reads have response status `2`, valid CRC, and no device path in the JSON.

- [ ] **Step 3: Capture the source-backed Blade GET responses**

Run:

```powershell
dotnet run --project '.\tools\OpenSynapse.ProtocolProbe\OpenSynapse.ProtocolProbe.csproj' -- `
    --include-source-backed `
    --output '.\artifacts\protocol\2026-08-11\source-backed.json'
$probeExitCode = $LASTEXITCODE
if ($probeExitCode -notin 0, 1, 2) {
    throw "Unexpected probe exit code: $probeExitCode"
}
"Probe exit code: $probeExitCode"
```

Expected: probe exit code `0`, `1`, or `2`; no SET report is sent. A result may succeed or contain an error; either outcome is evidence and must not be rewritten as success.

- [ ] **Step 4: Validate artifact redaction and report length**

Run:

```powershell
$documents = Get-ChildItem -LiteralPath '.\artifacts\protocol\2026-08-11' -Filter '*.json'
foreach ($document in $documents) {
    $json = Get-Content -Raw -LiteralPath $document.FullName | ConvertFrom-Json
    foreach ($result in $json.Results) {
        if ($result.RequestHex.Length -ne 182) { throw "Request length mismatch: $($result.Name)" }
        if ($result.ResponseHex -and $result.ResponseHex.Length -ne 182) { throw "Response length mismatch: $($result.Name)" }
    }
}
$sensitiveMatches = @(
    rg -n -i 'hid#|users\\|machineName|serialNumber|token' '.\artifacts\protocol\2026-08-11'
)
$sensitiveMatches.Count
```

Expected: all present report hex strings are `182` characters, and the final count is `0`.

- [ ] **Step 5: Review source-backed responses without sending writes**

For each successful Blade source-backed GET, record in the ledger:

```text
PID and collection
stable command name
transaction/class/command/data size
raw response arguments
decoded candidate value
agreement between both thermal zones
source citation
date and artifact path
```

Keep status `SourceBacked`. Promotion to `Verified` requires a separate write/readback/restore plan reviewed before execution.

**Verification checkpoint:** Both verified baseline and source-backed evidence are captured without arbitrary commands or writes.

---

### Task 7: Final Stage A verification and next-plan gates

**Files:**
- Verify: all active source, tests, README, protocol docs, and approved specification.
- Do not create Blade lighting, Viper parity, or Blade system-control implementation code in this task.

**Interfaces:**
- Consumes: OpenSynapse naming, passing tests, probe artifacts, and reviewed ledger.
- Produces: exact inputs for the next independent implementation plans.

- [ ] **Step 1: Run every non-hardware check**

Run:

```powershell
dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj'
dotnet build '.\OpenSynapse.slnx'
dotnet build '.\src\OpenSynapse.App\OpenSynapse.App.csproj' -p:Platform=x64
```

Expected: all non-hardware tests pass, two existing write/restore hardware tests remain skipped unless explicitly enabled, and both builds have `0` warnings and `0` errors.

- [ ] **Step 2: Run the existing opt-in hardware regression only after saving current values**

Run:

```powershell
$env:OPENSYNAPSE_HARDWARE_TEST = '1'
try {
    dotnet test '.\tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --filter 'Category=Hardware'
}
finally {
    Remove-Item Env:\OPENSYNAPSE_HARDWARE_TEST -ErrorAction SilentlyContinue
}
```

Expected: Blade brightness and Viper DPI/polling/idle tests change the smallest supported amount, read back, restore, and pass. Any restoration failure blocks further hardware work.

- [ ] **Step 3: Confirm no deferred feature or unverified write became user-facing**

Run:

```powershell
rg -n 'Lighting Studio|Chroma Studio|Macro|Calibration|Smart Tracking|SetBladePerformanceMode|SetBladeChargeLimit' '.\src\OpenSynapse.App'
```

Expected: no advanced editor, macro, Viper calibration, or unverified Blade performance/charge write control appears in the WinUI project.

- [ ] **Step 4: Create separate next plans only for evidence that exists**

Use these exact gates:

```text
Blade lighting plan:
  Requires locally accepted hardware-effect GET/SET facts or documented no-GET verification,
  a validated 6 x 17 matrix frame, and a measured stable send cadence.

Viper parity plan:
  Requires exact DPI-stage/profile/mapping/power commands for PID 00B8 and a restoration method.

Blade system-control plan:
  Requires exact 02C6 reads, safe ranges, readback, and recovery behavior for each independent setting.

Profiles/tray plan:
  May proceed independently because it uses Windows APIs and local JSON, but it may persist only Ready capabilities.
```

If a gate is missing, do not manufacture an implementation task for that subsystem. Keep it `Blocked` and continue with another independently testable plan.

**Verification checkpoint:** Stage A ends with a correctly named, still-working application and trustworthy protocol evidence. No unsupported feature has been promoted by inference.

---

## Plan Self-Review

- Spec coverage: S0 naming and S1 evidence are fully covered. S2-S6 remain separate plans because their private commands and safety boundaries are not yet known.
- Placeholder scan: the plan contains no arbitrary protocol-byte step and no unspecified implementation action. Evidence failures have explicit stop behavior.
- Type consistency: `ProbeCommand`, `ProbeEvidenceLevel`, `ProbeCatalog.Get`, `ProbeOptions`, `ProbeResult`, and `ProbeDocument` use the same names and signatures in tests and implementation.
- Safety review: the probe accepts only `--include-source-backed` and `--output`; all commands have GET command IDs with bit `0x80`; hardware smoke tests remain opt-in and restore original values.
- Simplicity review: no plugin system, generic device framework, service, database, new NuGet package, or capture driver is introduced.
