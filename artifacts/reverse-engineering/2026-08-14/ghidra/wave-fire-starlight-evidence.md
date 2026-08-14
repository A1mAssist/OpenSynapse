# RzLightingEngineApi effect evidence (static)

Target: `RzLightingEngineApi_v4.0.55.0.dll`, SHA-256 `CD9FC2A61FF920B9B73E1F5E27632020E2F8586F443E426A875AD3B042B714FA`.

This file records only facts recoverable from the local Ghidra analysis. It does not claim visual or HID parity.

## Starlight

Evidence: `FUN_1800601a0`/`FUN_180060280` in `lighting-effect-constructors-starlight.md`, and `FUN_180060790`, `FUN_180060900`, `FUN_180060950`, `FUN_180060dc0`, `FUN_180060de0` in `lighting-starlight-helpers.md`.

- The constructor installs `CStarlightEffect::vftable`, allocates a `0x30`-byte intrusive-tree sentinel at effect offset `0x198` (`param[0x33]`), sets the sentinel's three links to itself, and writes `0x0101` at sentinel offset `0x18`.
- Effect offset `0x1a0` (`param[0x34]`) starts at zero. The destructor walks the tree, destroys each node-owned frame buffer at node `+0x20`, then frees the nodes and sentinel.
- A generated node is `0x28` bytes. `FUN_180060dc0(node,effect)` stores the parent effect at node `+0x00`, clears node `+0x20`, and initializes node `+0x14` to one.
- `FUN_180060de0` copies effect state beginning at `effect + 0x3c`, stores a node color/seed at `+0x08`, a frame count at `+0x0c`, allocates `frameCount * 4` bytes at `+0x20`, and calls the shared color-stop renderer. It applies an intensity scale `param_4 / DAT_1801a9a70` before rendering.
- `FUN_180060900` emits one node frame at output index `node[+0x08]`, reads the frame from `node[+0x20] + node[+0x10] * 4`, advances `+0x10` modulo `+0x0c`, and decrements the node cycle counter at `+0x14` on wrap. A node with zero cycles is disabled.
- `FUN_180060790` emits all active nodes, and on effect frame index zero calls `FUN_180060950` to remove exhausted nodes and probabilistically create new nodes. The effect frame index at `+0x188` advances modulo `+0x18c`.
- `FUN_180060950` uses the same LCG as the other randomized effects (`state = state * 0x343fd + 0x269ec3`, with the DLL's 64-bit wrapping sequence), chooses free output indices from `0 .. rows*cols-1`, chooses node durations from the current frame count plus a minimum, and inserts nodes into an ordered tree. The exact source JSON field names and default spawn distribution are not recoverable from this decompilation alone because the copied configuration has unresolved stack-field types.

**Proven:** Starlight is a multi-node, independently timed effect with ordered placement and randomized spawn. **Unknown:** exact LedData defaults, spawn rate, intensity parameter, and Blade physical output parity.

## Wave timing and mapping

Evidence: `FUN_180065c90`, `FUN_1800666b0`, `FUN_180065f50`, and `FUN_180066160` in `effect-method-decompilations.md` and `lighting-starlight-helpers.md`.

`FUN_180065c90` performs the following calculations after the common effect setup:

```text
rate = max(10, state[0xa8])
angleRadians = (state[0xb0] + 0x10e) * PI / 360
distance = abs(sin(angleRadians) * state[0x30])
         + abs(cos(angleRadians) * state[0x34])
distance = 1 when the sum is zero

step = DEFAULT_STEP / state[0x38]                    when state[0xac] == 0
step = (((state[0xac] << 2) / DAT_1801a9a70) * distance) / state[0x38]
       otherwise
activeFrames = int(distance / step)
rateFrames = int(activeFrames * rate / 100)
pauseFrames = state[0x98] / (1000 / state[0x38])
totalFrames = pauseFrames + max(activeFrames, state[0x180])
```

The DLL stores `step` at `+0x190`, active frame count at `+0x1bc`, rate-scaled movement at `+0x1a4`, pause frames at `+0x1a8`, total sequence length at `+0x184`, and total-with-active at `+0x1a0`. `FUN_1800666b0` selects the normal projected path (`FUN_180065f50`) unless flags `0x400` or `0x800` select the packed perimeter path (`FUN_180066160`), then advances the combined pause/active index modulo `pauseFrames + activeFrames`.

`FUN_180065f50` maps a matrix coordinate to a precomputed color frame with a projected phase. It uses the sine/cosine signs for wrap direction, scales the row and column fractions by `state[0x1bc]`, adds the current frame and a two-cycle offset, and reduces modulo `state[0x184]`. This proves explicit angle, speed, pause, and wrap state; it does not prove the request values used for a specific Blade preset.

**Proven:** formula and state fields. **Unknown:** `DEFAULT_STEP`, `DAT_1801a9a70` numeric meaning, LedData geometry values, and the active Synapse Wave request. Do not replace the current Wave renderer or label it 1:1 from this evidence alone.

## Fire: 7 x 23 work field to Blade 6 x 17 output

Evidence: `FUN_1800625d0` and `FUN_1800633b0` in `effect-method-decompilations.md`.

- Fire allocates a work field of `frameCount * 0xa1` cells; `0xa1 = 7 * 23`.
- The output buffer remains `state[0x18] * state[0x1c]` cells per frame. Fire's effect-specific region dimensions are `state[0x30]` (rows) and `state[0x34]` (columns), with output origin in `state[0x24]`/`state[0x20]`.
- When `state[0x30] < 7` and `state[0x34] < 23`, the final conversion takes the direct crop path. For Blade's configured `6 x 17` region, each output frame copies exactly 17 cells from each of six source rows, in reverse row order:

```text
for outputRow = 0..5:
    sourceRow = 6 - outputRow       // source rows 6,5,4,3,2,1
    output[outputRow, 0..16] = work[sourceRow, 0..16]
```

- Source row zero is intentionally omitted by this branch. The output destination uses the normal matrix stride (`state[0x1c]`) and the configured origin; this is not a flat 102-cell truncation.
- The alternate branch handles regions at least 7 rows or 23 columns and is not used for the Blade 6 x 17 case.

**Proven:** the 7 x 23 cellular work grid and the 6 x 17 crop orientation/column range. **Unknown:** the active Fire rate/seed inputs from LedData and the exact color-stop values in a specific Blade request. The production Fire renderer remains unchanged until those are validated.

