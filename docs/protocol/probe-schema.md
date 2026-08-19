# Protocol Probe Artifact

The probe writes UTF-8 JSON with `CapturedAt`, collection identity, and `Results`. Each result contains `ProductId`, stable command `Name`, `Evidence`, exact 91-byte `RequestHex`, optional 91-byte `ResponseHex`, response status, and error text.

Artifacts must not contain the HID device path, Windows account name, machine name, serial number, Razer account data, or cloud tokens. Store local runs under `artifacts/protocol/YYYY-MM-DD/`; do not treat them as product configuration. Hardware captures are local validation output and are intentionally ignored by the clean repository.

A successful response proves only that the GET command is accepted by the exact PID/collection. It does not prove that a related SET command, value range, persistence rule, or similarly numbered command is safe.

The executable accepts only the compiled GET catalog. `--include-source-backed` adds the source-backed GET entries; it does not add a command class, command ID, argument, or report-writing option. `--output <path>` writes the same redacted JSON to disk.

## Dynamic reverse-engineering captures

Raw USBPcap captures, exported transfer TSV, and normalized files containing full 91-byte reports stay outside the workspace under `%LocalAppData%\OpenSynapse\reverse-engineering\raw\`. They may contain descriptors or unrelated traffic and are never eligible for publication as-is.

Only an operator-reviewed, exact command-class/command-ID allowlist may produce a repository artifact. That artifact must retain only the command-specific fields required for the conclusion and must omit HID paths, USB serials, account and machine identifiers, process details, tokens, unrelated commands, and unrelated USB payloads.

A successful USB transfer or SET acknowledgement proves transport acceptance only. It is not a GET contract, state readback, visible behavior result, persistence result, or production-verification result.
