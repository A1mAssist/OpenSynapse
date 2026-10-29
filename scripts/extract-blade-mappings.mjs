#!/usr/bin/env node
// Rebuilds Native/Razer/Mappings/Product<PID>Mapping.json from the local Razer
// middleware snapshot. Each Blade bundle declares its factory Fn/M-key graph in
// DEFAULTMAPPINGS; this script converts that graph into the press/release form
// BladeMappingInputRuntime consumes.
//
// Usage:
//   node scripts/extract-blade-mappings.mjs [--out <dir>] [--dry-run]
//
// The conversion is table driven: every KEY_* name, group and assignment must be
// listed below. An unknown value aborts the run instead of guessing a key code.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const dryRun = args.includes('--dry-run');
const outIndex = args.indexOf('--out');
const outDir = outIndex >= 0
  ? path.resolve(args[outIndex + 1])
  : path.join(repoRoot, 'src', 'OpenSynapse.App', 'Native', 'Razer', 'Mappings');
const bundleRoot = path.join(repoRoot, 'artifacts', 'razer-mw-full-cdn', 'bundles');
const capabilityTable = path.join(
  repoRoot, 'src', 'OpenSynapse.Core', 'Devices', 'BladeModelCapabilities.cs');

// Set-1 scan codes. Extended keys carry the E0 prefix, which our graph expresses
// as flag 2/3 instead of 0/1.
const keys = {
  KEY_LEFT_SHIFT: [0x2A, false],
  KEY_LEFT_CONTROL: [0x1D, false],
  KEY_LEFT_ALT: [0x38, false],
  KEY_LEFT_GUI: [0x5B, true],
  KEY_RIGHT_GUI: [0x5C, true],
  KEY_APPLICATION: [0x5D, true],
  KEY_PRINT_SCREEN: [0x37, true],
  KEY_INSERT: [0x52, true],
  KEY_DELETE: [0x53, true],
  KEY_HOME: [0x47, true],
  KEY_END: [0x4F, true],
  KEY_PAGE_UP: [0x49, true],
  KEY_PAGE_DOWN: [0x51, true],
  KEY_UP_ARROW: [0x48, true],
  KEY_DOWN_ARROW: [0x50, true],
  KEY_LEFT_ARROW: [0x4B, true],
  KEY_RIGHT_ARROW: [0x4D, true],
  KEY_NUMPAD_1: [0x4F, false],
  KEY_NUMPAD_2: [0x50, false],
  KEY_NUMPAD_3: [0x51, false],
  KEY_NUMPAD_4: [0x4B, false],
  KEY_NUMPAD_6: [0x4D, false],
  KEY_NUMPAD_7: [0x47, false],
  KEY_NUMPAD_8: [0x48, false],
  KEY_NUMPAD_9: [0x49, false],
  KEY_F23: [0x6E, false],
  KEY_A: [0x1E, false], KEY_B: [0x30, false], KEY_C: [0x2E, false], KEY_D: [0x20, false],
  KEY_E: [0x12, false], KEY_F: [0x21, false], KEY_G: [0x22, false], KEY_H: [0x23, false],
  KEY_I: [0x17, false], KEY_J: [0x24, false], KEY_K: [0x25, false], KEY_L: [0x26, false],
  KEY_M: [0x32, false], KEY_N: [0x31, false], KEY_O: [0x18, false], KEY_P: [0x19, false],
  KEY_Q: [0x10, false], KEY_R: [0x13, false], KEY_S: [0x1F, false], KEY_T: [0x14, false],
  KEY_U: [0x16, false], KEY_V: [0x2F, false], KEY_W: [0x11, false], KEY_X: [0x2D, false],
  KEY_Y: [0x15, false], KEY_Z: [0x2C, false],
};
for (let index = 0; index < 12; index++) {
  keys[`KEY_F${index + 1}`] = index < 10 ? [0x3B + index, false] : [0x57 + index - 10, false];
}

// keyInput HID usages are decimal, not hex.
const hidUsages = { 75: 'KEY_PAGE_UP', 78: 'KEY_PAGE_DOWN' };

const turboIds = {
  VolumeDown: '8a472b5b-04b1-4e88-9620-3bf0ff8ffce5',
  VolumeUp: 'cb32ace3-869a-4803-9ca6-7cfb6026ac62',
  ProjectionSettings: '3945ba01-5aca-499b-85fc-3d1176e384a1',
};

// The middleware emits some media keys as a self-contained tap and others as a
// held key; the 710 graph captured from hardware keeps that split, so we do too.
const mediaTaps = { MuteVolume: [0x20, true], Play: [0x22, true] };
const mediaHolds = { PrevTrack: [0x10, true], NextTrack: [0x19, true] };

function fail(pid, message) {
  throw new Error(`${pid}: ${message}`);
}

function scancode(pid, name) {
  const value = keys[name];
  if (!value) fail(pid, `unmapped key ${name}`);
  return value;
}

function pair(inputFor, press, release) {
  return [
    { input: inputFor(0), output: press },
    { input: inputFor(1), output: release },
  ];
}

function tap([scanCode, extended]) {
  return {
    type: 'multi',
    outputs: [
      { type: 'keyboard', scancode: scanCode, flag: extended ? 2 : 0 },
      { type: 'delay', ms: 10 },
      { type: 'keyboard', scancode: scanCode, flag: extended ? 3 : 1 },
    ],
  };
}

// Press sends modifiers before the key; release lifts the key first. The 710
// graph (captured from hardware) uses the same order.
function chord(pid, keyName, modifiers) {
  const [scanCode, extended] = scancode(pid, keyName);
  if (modifiers.length === 0) {
    return [
      { type: 'keyboard', scancode: scanCode, flag: extended ? 2 : 0 },
      { type: 'keyboard', scancode: scanCode, flag: extended ? 3 : 1 },
    ];
  }
  const mods = modifiers.map(name => scancode(pid, name));
  const down = [
    ...mods.map(([code, ext]) => ({ type: 'keyboard', scancode: code, flag: ext ? 2 : 0 })),
    { type: 'keyboard', scancode: scanCode, flag: extended ? 2 : 0 },
  ];
  const up = [
    { type: 'keyboard', scancode: scanCode, flag: extended ? 3 : 1 },
    ...mods.map(([code, ext]) => ({ type: 'keyboard', scancode: code, flag: ext ? 3 : 1 })),
  ];
  return [
    { type: 'multi', outputs: down },
    { type: 'multi', outputs: up },
  ];
}

function groupAction(pid, body) {
  const assignment = pattern => new RegExp(`${pattern}:"([^"]+)"`).exec(body)?.[1];
  if (/bladeBatteryGroup/.test(body)) {
    return { press: { type: 'disable' }, release: { type: 'bladeBattery', id: 'toggle' } };
  }
  if (/trackpadGroup/.test(body)) {
    return { press: { type: 'disable' }, release: { type: 'bladeTrackpad', id: 'toggle' } };
  }
  if (/bladePerformanceGroup/.test(body)) {
    return { press: { type: 'disable' }, release: { type: 'bladePerformance', id: 'nextPerformanceMode' } };
  }
  if (/screenRefreshGroup/.test(body)) {
    return { press: { type: 'disable' }, release: { type: 'screenRefresh', id: 'nextRefreshRate' } };
  }
  if (/snapTapGroup/.test(body)) {
    return { press: { type: 'disable' }, release: { type: 'snapTap', id: 'toggle' } };
  }
  if (/gameModeGroup/.test(body) || /outputType:`\$\{[\w$]+\.lRc\}`/.test(body)) {
    return { press: { type: 'gameMode' }, release: { type: 'disabled' } };
  }
  if (/backlightGroup/.test(body)) {
    const name = assignment('backlightAssignment');
    return { press: { type: 'backlight', name, flag: 0 }, release: { type: 'backlight', name, flag: 1 } };
  }
  if (/multimediaGroup/.test(body)) {
    const name = assignment('multimediaAssignment');
    if (name === 'VolumeDown' || name === 'VolumeUp') {
      const guid = turboIds[name];
      return {
        press: { type: 'turbo', flag: 0, delay: 100, guid },
        release: { type: 'turbo', guid, flag: 1 },
      };
    }
    if (name === 'MuteMic') {
      return { press: { repeat: 1, type: 'audio', id: 'mic', mute: 2 }, release: { type: 'disabled' } };
    }
    if (mediaTaps[name]) {
      return { press: tap(mediaTaps[name]), release: { type: 'disabled' } };
    }
    if (mediaHolds[name]) {
      const [scanCode, extended] = mediaHolds[name];
      return {
        press: { type: 'keyboard', scancode: scanCode, flag: extended ? 2 : 0 },
        release: { type: 'keyboard', scancode: scanCode, flag: extended ? 3 : 1 },
      };
    }
    return fail(pid, `unmapped multimedia assignment ${name}`);
  }
  if (/win8ShortcutsGroup/.test(body)) {
    const name = assignment('windowsShortcutAssignment');
    if (name === 'ProjectionSettings') {
      return {
        press: { type: 'turbo', guid: turboIds.ProjectionSettings, flag: 0, repeat: 1 },
        release: { type: 'disabled' },
      };
    }
    if (name === 'DisplayBrightnessDown' || name === 'DisplayBrightnessUp') {
      return {
        press: { type: 'display', id: name === 'DisplayBrightnessDown' ? 'driverBrightnessDown' : 'driverBrightnessUp' },
        release: { type: 'display', id: 'driverBrightnessStop' },
      };
    }
    return fail(pid, `unmapped Windows shortcut ${name}`);
  }
  if (/keyboardGroup/.test(body)) {
    const keyName = /key:"(KEY_[A-Z0-9_]+)"/.exec(body)?.[1] ?? fail(pid, 'keyboard group without key');
    const modifiers = /modifiers:\[([^\]]*)\]/.exec(body)?.[1]
      ?.match(/KEY_[A-Z0-9_]+/g) ?? [];
    const [press, release] = chord(pid, keyName, modifiers);
    return { press, release };
  }
  return null;
}

function splitTopLevel(text, open, close) {
  const items = [];
  let depth = 0, start = -1, quote = null, template = false;
  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    if (quote) {
      if (char === '\\') { index++; continue; }
      if (char === quote) quote = null;
      continue;
    }
    if (char === '"' || char === "'") { quote = char; continue; }
    if (char === '`') { template = !template; continue; }
    if (template) continue;
    if (char === open) { if (depth === 0) start = index; depth++; }
    else if (char === close) {
      depth--;
      if (depth === 0) items.push(text.slice(start, index + 1));
    }
  }
  return items;
}

function exportAlias(text, name) {
  const arrow = new RegExp(`${name}:\\(\\)=>([A-Za-z_$][\\w$]*)`).exec(text);
  if (arrow) return arrow[1];
  const fn = new RegExp(`${name}:function\\(\\)\\{return ([A-Za-z_$][\\w$]*)\\}`).exec(text);
  return fn ? fn[1] : null;
}

function arrayLiteral(text, anchor, alias) {
  const start = text.indexOf(`${alias}=[`, text.indexOf(anchor));
  if (start < 0) return null;
  const body = text.slice(start + alias.length + 1);
  const [literal] = splitTopLevel(body, '[', ']');
  return literal ? `[${literal.slice(1)}` : null;
}

function readMapping(pid) {
  const directory = path.join(bundleRoot, pid);
  const main = fs.readdirSync(directory).find(name => /^main\..*\.js$/.test(name));
  if (!main) fail(pid, 'no main bundle');
  const text = fs.readFileSync(path.join(directory, main), 'utf8');
  const alias = exportAlias(text, 'DEFAULTMAPPINGS') ?? fail(pid, 'no DEFAULTMAPPINGS export');
  const array = arrayLiteral(text, 'DEFAULTMAPPINGS', alias) ?? fail(pid, 'DEFAULTMAPPINGS array not found');
  const dkmAlias = exportAlias(text, 'DKMKEYS');
  const dkmArray = dkmAlias ? arrayLiteral(text, 'DKMKEYS', dkmAlias) : null;
  const dkmKeys = {};
  for (const match of dkmArray?.matchAll(/\{inputID:"([^"]+)",key:(\d+)\}/g) ?? []) {
    dkmKeys[match[1]] = Number(match[2]);
  }
  const deviceAlias = exportAlias(text, 'DeviceInfo') ?? fail(pid, 'no DeviceInfo export');
  const deviceStart = text.indexOf(`${deviceAlias}={productId:`, text.indexOf('DeviceInfo'));
  if (deviceStart < 0) fail(pid, 'DeviceInfo object not found');
  const device = splitTopLevel(text.slice(deviceStart), '{', '}')[0];
  return {
    productId: Number(/productId:(\d+)/.exec(device)[1]),
    entries: splitTopLevel(array, '{', '}'),
    dkmKeys,
  };
}

function convert(pid, mapping) {
  const mappings = [];
  const skipped = [];
  for (const body of mapping.entries) {
    const inputId = /inputID:"([^"]+)"/.exec(body)?.[1] ?? fail(pid, `entry without inputID: ${body}`);
    const isDkm = /inputType:"DKMInput"/.test(body);
    const hyperShift = /isHyperShift:!0/.test(body);
    const hid = /keyInput:\{pageID:"\d+",HID:"(\d+)"\}/.exec(body)?.[1];
    let inputFor;
    if (isDkm) {
      const key = mapping.dkmKeys[inputId];
      if (key === undefined) fail(pid, `${inputId} is not declared in DKMKEYS`);
      inputFor = flag => ({ type: 'razerKey', key, hypershift: hyperShift, flag });
    } else {
      const name = hid ? hidUsages[Number(hid)] ?? fail(pid, `unmapped HID usage ${hid}`) : inputId;
      const [scanCode, extended] = scancode(pid, name);
      inputFor = flag => ({
        type: 'keyboard',
        scancode: scanCode,
        hypershift: hyperShift,
        flag: extended ? flag + 2 : flag,
      });
    }

    if (/bladeVCLedStateGroup/.test(body)) {
      skipped.push(`${inputId} (bladeVCLedStateGroup)`);
      continue;
    }
    if (/outputType:"disableGroup"/.test(body)) {
      mappings.push(...pair(inputFor, { type: 'disabled' }, { type: 'disabled' }));
      continue;
    }
    const action = groupAction(pid, body);
    if (!action) fail(pid, `unmapped output ${body}`);
    mappings.push(...pair(inputFor, action.press, action.release));
  }

  // Both Fn keys report as vendor keys 10 and 11; Synapse maps them to the
  // HyperShift layer on every Blade, and the graph above depends on that layer.
  for (const key of [10, 11]) {
    for (const hyperShift of [false, true]) {
      const inputFor = flag => ({ type: 'razerKey', key, hypershift: hyperShift, flag });
      mappings.push({ input: inputFor(0), output: { type: 'hypershift', flag: 0 } });
      mappings.push({ input: inputFor(1), output: { type: 'hypershift', flag: 1 } });
    }
  }
  return { mappings, skipped };
}

function canonical(graph) {
  return JSON.stringify(graph.map(item => ({
    input: item.input,
    output: item.output,
  })).sort((left, right) => JSON.stringify(left).localeCompare(JSON.stringify(right))));
}

// BladeModelCapabilities is the single source of truth for which PIDs are
// Blades, so this script never keeps a second model list.
const bladeBundles = [...fs.readFileSync(capabilityTable, 'utf8')
  .matchAll(/\[0x([0-9A-F]{4})\]/g)].map(match => match[1]);

if (bladeBundles.length === 0) throw new Error('No Blade bundles found.');
if (!dryRun) fs.mkdirSync(outDir, { recursive: true });
const product710 = path.join(outDir, 'Product710Mapping.json');
const previous710 = !dryRun && fs.existsSync(product710)
  ? fs.readFileSync(product710, 'utf8')
  : null;

const summary = [];
for (const pid of bladeBundles.sort()) {
  const mapping = readMapping(pid);
  const { mappings, skipped } = convert(pid, mapping);
  const payload = { productId: mapping.productId, defaultMappings: { appEngine: { mappings } } };
  if (!dryRun) {
    fs.writeFileSync(
      path.join(outDir, `Product${mapping.productId}Mapping.json`),
      `${JSON.stringify(payload)}\n`);
  }
  summary.push({ pid, productId: mapping.productId, rules: mappings.length / 2, skipped });
}

for (const row of summary) {
  const skip = row.skipped.length ? ` skipped: ${row.skipped.join(', ')}` : '';
  console.log(`${row.pid} -> Product${row.productId}Mapping.json (${row.rules} rules)${skip}`);
}
console.log(`${summary.length} Blade mappings written to ${outDir}`);

// The Product 710 graph is validated on hardware; regeneration must leave it
// rule-for-rule unchanged or the converter drifted.
if (previous710) {
  const before = JSON.parse(previous710).defaultMappings.appEngine.mappings;
  const after = JSON.parse(fs.readFileSync(product710, 'utf8')).defaultMappings.appEngine.mappings;
  const same = canonical(before) === canonical(after);
  console.log(same
    ? 'Product 710 mapping matches the hardware-validated graph.'
    : 'Product 710 mapping DIFFERS from the hardware-validated graph.');
  if (!same) process.exitCode = 1;
}
