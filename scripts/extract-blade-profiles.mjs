#!/usr/bin/env node
// Copies Razer middleware device metadata into the built-in Blade manifests.
// The bundles under artifacts/razer-mw-full-cdn are the source of truth; this
// script never invents a value that the bundle does not declare.
//
// Usage:
//   node scripts/extract-blade-profiles.mjs [--root <repo>] [--out <dir>] [--check]
//
// Output is canonical (2-space indent, LF, fixed key order), so a second run
// produces no diff.

import fs from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { fileURLToPath } from 'node:url';

const options = {};
const args = process.argv.slice(2);
for (let index = 0; index < args.length; index++) {
  const arg = args[index];
  if (arg === '--check') options.check = true;
  else if (['--out', '--root'].includes(arg) && args[index + 1] && !args[index + 1].startsWith('--')) {
    options[arg.slice(2)] = path.resolve(args[++index]);
  } else throw new Error(`Invalid argument: ${arg}`);
}
const repoRoot = options.root ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const manifestDir = path.join(repoRoot, 'src', 'OpenSynapse.Windows', 'Devices', 'Manifests');
const outDir = options.out ?? manifestDir;
const bundleRoot = path.join(repoRoot, 'artifacts', 'razer-mw-full-cdn', 'bundles');

// Wire dialect comes from the audited bundle handlers plus the local OpenRazer
// device table. 2019-2021 Blades with a 6x16 matrix use the 023A-family
// 03:0A/03:0B path; the 2022+ Blade rows use the selector-prefixed 0F:02
// firmware path. Keep Unknown where the manifest has no effect contract.
const lightingDialects = {
  '023A': 'LedMatrixBlade',
  '0234': 'LedMatrixBlade',
  '0239': 'FirmwareEffect',
  '023B': 'FirmwareEffect',
  '024B': 'LedMatrixBlade',
  '024C': 'LedMatrixBlade',
  '024D': 'LedMatrixBlade',
  '0246': 'FirmwareEffect',
  '0252': 'FirmwareEffect',
  '0253': 'LedMatrixBlade',
  '0256': 'LedMatrixBlade',
  '0259': 'FirmwareEffect',
  '0268': 'FirmwareEffect',
  '026D': 'LedMatrixBlade',
  '026E': 'LedMatrixBlade',
  '026F': 'FirmwareEffect',
  '0270': 'LedMatrixBlade',
  '0276': 'LedMatrixBlade',
  '0279': 'LedMatrixBlade',
  '027A': 'FirmwareEffect',
  '028A': 'FirmwareEffect',
  '028B': 'FirmwareEffect',
  '028C': 'FirmwareEffect',
  '029F': 'FirmwareEffect',
  '029E': 'FirmwareEffect',
  '02A0': 'FirmwareEffect',
  '02E0': 'LedMatrix2026',
  '02E1': 'LedMatrix2026',
  '02C6': 'LedMatrix710',
  '029D': 'LedMatrix710',
  '02B6': 'LedMatrix710',
  '02B7': 'LedMatrix710',
  '02B8': 'LedMatrix710',
  '02C5': 'LedMatrix710',
  '02C7': 'LedMatrix710',
};

const lightingMatrices = {
  '023A': [6, 16],
  '0234': [6, 16],
  '024B': [6, 16],
  '024C': [6, 16],
  '024D': [6, 16],
  '0253': [6, 16],
  '0256': [6, 16],
  '026D': [6, 16],
  '026E': [6, 16],
  '0270': [6, 16],
  '0276': [6, 16],
  '0279': [6, 16],
};

function fail(context, message) {
  throw new Error(`${context}: ${message}`);
}

function readJson(file, context) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (error) {
    fail(context, `cannot read ${file}: ${error.message}`);
  }
}

function integerList(value, field, context) {
  const values = Array.isArray(value) ? value : [value];
  if (values.length === 0 || !values.every(Number.isInteger)) {
    fail(context, `${field} must be an integer or an array of integers`);
  }
  return values;
}

function productName(bundle) {
  if (typeof bundle.deviceName === 'string') {
    return bundle.deviceName.trim();
  }

  // Three older bundles omit deviceName; their own English product translation
  // still names the machine.
  for (const translation of Object.values(bundle.productTranslations ?? {})) {
    if (typeof translation?.en?.PRODUCT === 'string') {
      return translation.en.PRODUCT;
    }
  }
  return null;
}

function rzDeviceType(dir, pid) {
  const mainFiles = fs.readdirSync(dir).filter(name => /^main\..*\.js$/.test(name));
  const values = new Set();
  for (const name of mainFiles) {
    const source = fs.readFileSync(path.join(dir, name), 'utf8');
    for (const match of source.matchAll(/useFeature\("rzDeviceType","([^"]*)"\)/g)) {
      values.add(match[1]);
    }
  }

  if (values.size > 1) {
    fail(pid, `conflicting rzDeviceType values in ${dir}: ${[...values].join(', ')}`);
  }
  return values.size === 0 ? null : [...values][0];
}

function latestFirmware(bundle, pid) {
  const latest = bundle.latestFirmwareInfo;
  if (latest === undefined || latest === null) {
    return null;
  }
  if (typeof latest?.version !== 'string' || typeof latest?.url !== 'string') {
    fail(pid, 'latestFirmwareInfo is missing version or url');
  }
  const result = { version: latest.version, url: latest.url };
  if (latest.additional !== undefined) {
    if (!Array.isArray(latest.additional)) fail(pid, 'latestFirmwareInfo.additional must be an array');
    result.additional = latest.additional.map(component => {
      if (typeof component.type !== 'string' || typeof component.version !== 'string' ||
          (component.model !== undefined && typeof component.model !== 'string') ||
          (component.url !== undefined && typeof component.url !== 'string')) {
        fail(pid, 'additional component must declare type, version and an optional model');
      }
      return { type: component.type, version: component.version,
        ...(component.model === undefined ? {} : { model: component.model }),
        ...(component.url === undefined ? {} : { url: component.url }) };
    });
  }
  return result;
}

function driver(bundle, pid) {
  const matches = (bundle.resourceManifest?.resources ?? [])
    .filter(resource => /driver/i.test(resource.resourceName ?? ''));
  if (matches.length === 0) {
    return null;
  }
  if (matches.length > 1) {
    fail(pid, `multiple Driver resources: ${matches.map(item => item.resourceName).join(', ')}`);
  }
  const resource = matches[0];
  if (typeof resource.resourceVersion !== 'string' || typeof resource.url !== 'string') {
    fail(pid, `Driver resource ${resource.resourceName} is missing version or url`);
  }
  return { version: resource.resourceVersion, url: resource.url };
}

function extract(bundleDir, pid) {
  const context = `${pid} (${path.basename(bundleDir)})`;
  const file = path.join(bundleDir, 'manifest.json');
  if (!fs.existsSync(file)) {
    fail(context, `missing ${file}`);
  }

  const bundle = readJson(file, context);
  return {
    name: productName(bundle),
    shortName: typeof bundle.short_name === 'string' ? bundle.short_name.trim() : null,
    editionIds: integerList(bundle.editionId, 'editionId', context),
    layoutIds: integerList(bundle.layoutId, 'layoutId', context),
    rzDeviceType: rzDeviceType(bundleDir, context),
    firmware: {
      latest: latestFirmware(bundle, context),
      driver: driver(bundle, context),
    },
  };
}

const bundleDirs = new Map();
for (const name of fs.readdirSync(bundleRoot)) {
  const dir = path.join(bundleRoot, name);
  if (!fs.statSync(dir).isDirectory()) {
    continue;
  }
  const key = name.toLowerCase();
  bundleDirs.set(key, [...(bundleDirs.get(key) ?? []), dir]);
}

function profileFor(pid) {
  const dirs = bundleDirs.get(pid.toLowerCase());
  if (!dirs || dirs.length === 0) {
    fail(pid, `no bundle directory under ${bundleRoot}`);
  }

  const profiles = dirs.sort().map(dir => extract(dir, pid));
  for (const profile of profiles.slice(1)) {
    if (!isDeepStrictEqual(profiles[0], profile)) {
      fail(pid, `bundles disagree: ${dirs.map(dir => path.basename(dir)).join(', ')}`);
    }
  }
  return profiles[0];
}

const manifestFiles = fs.readdirSync(manifestDir)
  .filter(name => /^blade-.*\.json$/.test(name))
  .sort();

if (manifestFiles.length === 0) throw new Error('No Blade manifests found.');
const generated = [];
for (const name of manifestFiles) {
  const file = path.join(manifestDir, name);
  const manifest = readJson(file, name);
  const productIds = manifest.productIds;
  if (!Array.isArray(productIds) || productIds.length === 0 || !productIds.every(pid => typeof pid === 'string' && /^[0-9a-f]{4}$/i.test(pid))) {
    fail(name, 'productIds must be a non-empty array of four-digit hex PIDs');
  }

  const profiles = productIds.map(profileFor);
  for (const profile of profiles.slice(1)) {
    if (!isDeepStrictEqual(profiles[0], profile)) {
      fail(name, `productIds ${productIds.join(', ')} disagree`);
    }
  }

  const dialect = lightingDialects[productIds[0].toUpperCase()] ?? 'Unknown';
  for (const pid of productIds.slice(1)) {
    if ((lightingDialects[pid.toUpperCase()] ?? 'Unknown') !== dialect) {
      fail(name, `productIds ${productIds.join(', ')} map to different lighting dialects`);
    }
  }

  // Reinsert at the end so the generated block always has the same position.
  delete manifest.device;
  delete manifest.lightingDialect;
  if (dialect === 'LedMatrixBlade') {
    const matrix = lightingMatrices[productIds[0].toUpperCase()];
    if (!matrix) {
      fail(name, `LedMatrixBlade ${productIds[0]} has no matrix geometry`);
    }
    manifest.matrixRows = matrix[0];
    manifest.matrixColumns = matrix[1];
  }
  manifest.device = profiles[0];
  manifest.lightingDialect = dialect;

  const text = `${JSON.stringify(manifest, null, 2)}\n`;
  const target = path.join(outDir, name);
  if (!fs.existsSync(target) || fs.readFileSync(target, 'utf8').replace(/\r\n/g, '\n') !== text) generated.push({ target, text });
}

if (options.check) {
  for (const { target } of generated) console.error(`Out of date: ${target}`);
  if (generated.length) process.exitCode = 1;
} else {
  fs.mkdirSync(outDir, { recursive: true });
  for (const { target, text } of generated) fs.writeFileSync(target, text, 'utf8');
}
console.log(`${options.check ? 'Checked' : 'Updated'} ${generated.length} of ${manifestFiles.length} Blade manifests.`);
