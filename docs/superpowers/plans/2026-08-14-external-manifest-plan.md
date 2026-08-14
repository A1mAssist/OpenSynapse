# External Device Manifest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Load safe external same-family device manifests from LocalAppData so a reviewed new PID works without rebuilding OpenSynapse.

**Architecture:** Reuse the existing strict `RazerDeviceRegistry` parser and compiled family contracts. Add a bounded directory loader that merges valid non-conflicting external documents with embedded documents and returns diagnostics; construct one registry at App startup and inject it everywhere.

**Tech Stack:** .NET 10, System.Text.Json, WinUI 3, xUnit.

## Global Constraints

- External manifests cannot define raw reports or unknown protocol families.
- Embedded VID/PIDs and manifest IDs cannot be overridden.
- Read at most 64 `*.json` files, each at most 65,536 bytes.
- One invalid external file must not disable built-ins or other valid files.
- Diagnostics expose a filename, not full local paths.

---

### Task 1: Preserve manifest source and merge diagnostics

**Files:**
- Modify: `src/OpenSynapse.Windows/Devices/RazerDeviceManifest.cs`
- Modify: `src/OpenSynapse.Windows/Devices/RazerDeviceRegistry.cs`
- Test: `tests/OpenSynapse.Core.Tests/RazerDeviceRegistryTests.cs`

**Interfaces:**
- Produces: `RazerDeviceRegistryLoadResult(RazerDeviceRegistry Registry, IReadOnlyList<string> Errors)`.
- Produces: `RazerDeviceManifest.SourceName` containing embedded resource name or external filename only.

- [ ] **Step 1: Add failing source/merge tests**

Create an embedded Blade manifest and an external same-family manifest for PID `02C7`; assert both are found and source names are preserved. Add a duplicate `02C6` external manifest and assert it is rejected while the built-in remains available.

- [ ] **Step 2: Run the focused tests and confirm failure**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~RazerDeviceRegistryTests'
```

- [ ] **Step 3: Add a source-aware parse input**

```csharp
internal sealed record ManifestDocument(string SourceName, string Json, bool IsBuiltIn);
internal sealed record RazerDeviceRegistryLoadResult(
    RazerDeviceRegistry Registry,
    IReadOnlyList<string> Errors);
```

Change `Parse` to accept `ManifestDocument` and set `SourceName` on the validated immutable manifest. Keep `LoadJson(IEnumerable<string>)` as a test helper by assigning deterministic `memory-N.json` names.

- [ ] **Step 4: Merge external documents one at a time**

Start from every parsed built-in. For each external document in ordinal filename order, parse and validate it, then reject ID or VID/PID conflicts against the accumulated registry. Catch only expected JSON/schema/I/O failures and append `"filename.json：message"`; do not catch `OutOfMemoryException` or other process failures.

- [ ] **Step 5: Run registry tests**

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add 'src\OpenSynapse.Windows\Devices\RazerDeviceManifest.cs' 'src\OpenSynapse.Windows\Devices\RazerDeviceRegistry.cs' 'tests\OpenSynapse.Core.Tests\RazerDeviceRegistryTests.cs'
git commit -m 'feat: merge external device manifests safely'
```

### Task 2: Add the bounded LocalAppData loader

**Files:**
- Modify: `src/OpenSynapse.Windows/Devices/RazerDeviceRegistry.cs`
- Test: `tests/OpenSynapse.Core.Tests/RazerDeviceRegistryTests.cs`

**Interfaces:**
- Produces: `RazerDeviceRegistry.Load(string? externalDirectory = null)`.

- [ ] **Step 1: Add failing filesystem-boundary tests**

Use a test temp directory. Cover absent directory, valid file, malformed JSON plus valid sibling, unknown family, unknown field, missing capability, 65,537-byte file, and 65 files. Assert deterministic errors and that valid/built-in devices remain findable.

- [ ] **Step 2: Implement the bounded loader with BCL APIs**

```csharp
private const int MaximumExternalManifestFiles = 64;
private const long MaximumExternalManifestBytes = 64 * 1024;

internal static RazerDeviceRegistryLoadResult Load(string? externalDirectory = null)
{
    externalDirectory ??= Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSynapse", "devices");
    var errors = new List<string>();
    var external = new List<ManifestDocument>();
    if (Directory.Exists(externalDirectory))
    {
        var files = Directory.EnumerateFiles(externalDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length > MaximumExternalManifestFiles)
        {
            errors.Add($"外部 manifest 超过 {MaximumExternalManifestFiles} 个，已拒绝全部外部配置。");
        }
        else
        {
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                try
                {
                    if (new FileInfo(file).Length > MaximumExternalManifestBytes)
                    {
                        throw new InvalidOperationException("文件超过 65536 字节。");
                    }
                    external.Add(new ManifestDocument(name, File.ReadAllText(file), false));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    errors.Add($"{name}：{exception.Message}");
                }
            }
        }
    }
    return Merge(LoadBuiltInDocuments(), external, errors);
}
```

`LoadBuiltInDocuments` and `Merge` are the source-aware helpers produced by Task 1. Do not add another configuration format or file watching.

- [ ] **Step 3: Run registry tests**

Expected: PASS with no writes outside the test temp directory.

- [ ] **Step 4: Commit**

```powershell
git add 'src\OpenSynapse.Windows\Devices\RazerDeviceRegistry.cs' 'tests\OpenSynapse.Core.Tests\RazerDeviceRegistryTests.cs'
git commit -m 'feat: load bounded external manifests'
```

### Task 3: Inject one registry through discovery, telemetry, and lighting

**Files:**
- Modify: `src/OpenSynapse.Windows/Devices/WindowsHidDiscovery.cs`
- Modify: `src/OpenSynapse.Windows/Devices/RazerDeviceTelemetryReader.cs`
- Modify: `src/OpenSynapse.Windows/Lighting/BladeLightingController.cs`
- Modify: `src/OpenSynapse.App/App.xaml.cs`
- Modify: `src/OpenSynapse.App/ViewModels/MainViewModel.cs`
- Test: `tests/OpenSynapse.Core.Tests/RazerDeviceRegistryTests.cs`

**Interfaces:**
- Consumes: `RazerDeviceRegistryLoadResult` from Tasks 1-2.
- Produces: one immutable registry shared by all device components.

- [ ] **Step 1: Add an integration-style same-family PID test**

Build a registry containing Blade PID `02C7`; pass it to discovery/controller/reader test constructors and assert the reader uses family `blade-710` without a hard-coded PID branch.

- [ ] **Step 2: Make existing internal constructors consistently accept the registry**

Keep public parameterless constructors for tools/tests that intentionally use built-ins. App startup must call `RazerDeviceRegistry.Load()`, then pass `result.Registry` to `WindowsHidDiscovery`, `RazerDeviceTelemetryReader`, and `BladeLightingController`.

- [ ] **Step 3: Surface loader errors through existing diagnostics**

Pass `result.Errors` into `MainViewModel` constructor as immutable startup diagnostics and include them in `RebuildDiagnostics`; do not add another logging subsystem.

- [ ] **Step 4: Run full non-hardware tests and Release build**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore
dotnet build 'OpenSynapse.slnx' -c Release --no-restore
```

Expected: all non-hardware tests PASS; 0 warnings/errors.

- [ ] **Step 5: Commit**

```powershell
git add 'src\OpenSynapse.Windows\Devices\WindowsHidDiscovery.cs' 'src\OpenSynapse.Windows\Devices\RazerDeviceTelemetryReader.cs' 'src\OpenSynapse.Windows\Lighting\BladeLightingController.cs' 'src\OpenSynapse.App\App.xaml.cs' 'src\OpenSynapse.App\ViewModels\MainViewModel.cs' 'tests\OpenSynapse.Core.Tests\RazerDeviceRegistryTests.cs'
git commit -m 'feat: use one runtime device registry'
```

### Task 4: Document and smoke-test a new PID manifest

**Files:**
- Create: `docs/device-manifest-guide.md`
- Modify: `README.md`
- Modify: `docs/frontend-handoff.md`

**Interfaces:**
- Produces: exact deployment and diagnostic instructions for external manifests.

- [ ] **Step 1: Write a `02C7` example that copies the complete Blade family contract**

Document `%LocalAppData%\OpenSynapse\devices`, the 64/64-KiB limits, conflict behavior, and why command bytes cannot be changed. Use a fenced JSON example copied from `blade-710.json` with only `id`, `displayName`, and `productIds` changed.

- [ ] **Step 2: Run a manifest-loader smoke test against the example**

```powershell
dotnet test 'tests\OpenSynapse.Core.Tests\OpenSynapse.Core.Tests.csproj' --no-restore --filter 'FullyQualifiedName~ExternalManifestGuideExampleIsValid'
```

Expected: PASS.

- [ ] **Step 3: Commit**

```powershell
git add 'docs\device-manifest-guide.md' 'README.md' 'docs\frontend-handoff.md' 'tests\OpenSynapse.Core.Tests\RazerDeviceRegistryTests.cs'
git commit -m 'docs: add external manifest guide'
```
