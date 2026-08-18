# External Device Manifests

OpenSynapse can discover a new PID when it uses a compiled protocol family that
is already supported. Put reviewed files in:

`%LocalAppData%\OpenSynapse\devices\`

The loader reads only top-level `*.json` files, at most 64 files and 65,536
bytes per file. Built-in manifests always win. A duplicate manifest ID or
VID/PID is rejected only for that file; the built-in device and other valid
files remain available. The diagnostic row names the file but never exposes a
HID path.

An external file may reuse only `blade-710` or `viper-184`. Copy the complete
capability contract from the built-in manifest and change identity fields only.
Command classes, command IDs, report sizes, arguments, delays, and capability
names are not an extension point.

Example for a reviewed Blade PID `02C7`:

```json
{
  "schemaVersion": 1,
  "id": "blade-710-02c7",
  "displayName": "Razer Blade 16 2025 (02C7)",
  "vendorId": "1532",
  "productIds": ["02C7"],
  "collection": {
    "usagePage": "0001",
    "usage": "0002",
    "featureReportLength": 91
  },
  "protocolFamily": "blade-710",
  "transport": { "waitMilliseconds": 2 },
  "capabilities": {
    "keyboard-brightness.get": { "transactionId": "FF", "dataSize": "02", "commandClass": "0E", "commandId": "84", "arguments": "0100", "waitMilliseconds": 1 },
    "keyboard-brightness.set": { "transactionId": "FF", "dataSize": "02", "commandClass": "0E", "commandId": "04", "arguments": "", "waitMilliseconds": 1 },
    "thermal-state.get": { "transactionId": "1F", "dataSize": "04", "commandClass": "0D", "commandId": "82", "arguments": "" },
    "thermal-state.set": { "transactionId": "1F", "dataSize": "04", "commandClass": "0D", "commandId": "02", "arguments": "" },
    "fan-target.get": { "transactionId": "1F", "dataSize": "03", "commandClass": "0D", "commandId": "81", "arguments": "" },
    "current-fan-rpm.get": { "transactionId": "1F", "dataSize": "03", "commandClass": "0D", "commandId": "88", "arguments": "" },
    "advanced-fan-mode.get": { "transactionId": "1F", "dataSize": "03", "commandClass": "0D", "commandId": "87", "arguments": "" },
    "boost.get": { "transactionId": "1F", "dataSize": "03", "commandClass": "0D", "commandId": "87", "arguments": "" },
    "boost.set": { "transactionId": "1F", "dataSize": "03", "commandClass": "0D", "commandId": "07", "arguments": "" },
    "charge-limit.get": { "transactionId": "1F", "dataSize": "01", "commandClass": "07", "commandId": "92", "arguments": "00", "allowRemainingPacketsMismatch": true },
    "charge-limit.set": { "transactionId": "1F", "dataSize": "01", "commandClass": "07", "commandId": "12", "arguments": "" },
    "max-fan.get": { "transactionId": "1F", "dataSize": "01", "commandClass": "07", "commandId": "8F", "arguments": "00", "allowRemainingPacketsMismatch": true },
    "max-fan.set": { "transactionId": "1F", "dataSize": "01", "commandClass": "07", "commandId": "0F", "arguments": "" },
    "logo-power.get": { "transactionId": "FF", "dataSize": "03", "commandClass": "03", "commandId": "80", "arguments": "010400" },
    "logo-power.set": { "transactionId": "FF", "dataSize": "03", "commandClass": "03", "commandId": "00", "arguments": "" },
    "logo-mode.get": { "transactionId": "FF", "dataSize": "03", "commandClass": "03", "commandId": "82", "arguments": "010400" },
    "logo-mode.set": { "transactionId": "FF", "dataSize": "03", "commandClass": "03", "commandId": "02", "arguments": "" }
  }
}
```

This is a protocol-family alias, not a way to add unsupported hardware. A new
packet format requires a typed parser/builder and a reviewed built-in family
contract.
