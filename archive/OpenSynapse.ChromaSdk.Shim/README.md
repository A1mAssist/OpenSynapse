# OpenSynapse Chroma SDK shim (archived)

This was an in-process `RzChromaSDK64.dll` replacement that forwarded Chroma
SDK calls to OpenSynapse over a named pipe. It is retired and must not be
shipped, installed, or started by the product.

The production path is now:

1. Razer's official `RzChromaSDK64.dll` in `C:\Windows\System32`.
2. `OpenSynapse.ChromaBroker`, started as LocalSystem by the
   `OpenSynapseGpuMode` service registered at install time.
3. The app receives decoded frames on `OpenSynapseChromaBroker`.

Kept here for ABI/protocol reference and for understanding old beta installs.
`Reference/ChromaSdkShimInstaller.cs` documents the old
`RzChromaSDK64.dll.opensynapse-backup` layout; production now restores that
backup once through `LegacyChromaShimMigration` and deletes the packaged shim.

`Tests/` contains the original shim tests for reference. They are not part of
the main test project and are not run by CI.

The source project is not in `OpenSynapse.slnx`. If it is ever needed for
investigation, build it explicitly:

```powershell
dotnet publish archive\OpenSynapse.ChromaSdk.Shim\Source\OpenSynapse.ChromaSdk.Shim.csproj -c Release
```
