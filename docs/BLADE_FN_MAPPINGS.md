# Blade Fn/M-key mappings per model

`src/OpenSynapse.App/Native/Razer/Mappings/Product<PID>Mapping.json` is the key graph
`BladeFnRuntime` feeds into `BladeMappingInputRuntime`. There is one file per Blade in
`BladeModelCapabilities`; the runtime loads `Product{productId}Mapping.json` and skips the
filter runtime entirely when that file is absent, leaving the keyboard in firmware mode.

## Where the data comes from

Razer's middleware bundle for each PID ships the factory graph as `DEFAULTMAPPINGS`
(`main.*.js`) and the model's DKM key table as `DKMKEYS`. `scripts/extract-blade-mappings.mjs`
reads both and converts them into the press/release form this app consumes:

```
node scripts/extract-blade-mappings.mjs            # rewrite every mapping file
node scripts/extract-blade-mappings.mjs --dry-run  # parse and report only
```

The script takes the PID list from `BladeModelCapabilities`, so adding a Blade there and
re-running it is the whole procedure for a new model.

## Invariants the script enforces

- Every `KEY_*`, group and assignment used by the bundles is in the script's tables; an
  unknown value aborts the run instead of inventing a scan code.
- Vendor keys 10 and 11 (both Fn keys) always map to the HyperShift layer. Without them the
  `isHyperShift` rules never fire.
- Product 710 is compared rule-for-rule against the previously committed file, because that
  graph is the one validated on hardware.
- `bladeVCLedStateGroup` (Fn+V on 02C7) is skipped: the middleware has no user-visible
  setting behind it, so the key is left unhooked instead of swallowed.

## What the runtime does with it

- The filter driver hooks exactly the keyboard scancodes the graph consumes, so models with
  Fn+Delete or Fn+V get those hooks and models without them do not.
- Unmapped keyboard scancodes are re-emitted unchanged, so hooking a key never silences it.
- The same hook list goes into the recovery marker; `OpenSynapse.RecoveryHost` clears those
  hooks and disables interception if the app dies.

No model in this table has been exercised on hardware. The 710 graph is the only one with a
device-validated reference.
