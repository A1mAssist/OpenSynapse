# Archive

Code in this folder is kept for reference and migration only. Nothing here is
included in `OpenSynapse.slnx`, packaged into the installer, or loaded by the
app.

| Folder | Why it is archived | Production replacement |
| --- | --- | --- |
| `OpenSynapse.ChromaSdk.Shim` | In-process Chroma DLL shim retired after the official-DLL broker became the supported path. | Official `RzChromaSDK64.dll` plus `OpenSynapse.ChromaBroker`, hosted by the `OpenSynapseGpuMode` service. |
