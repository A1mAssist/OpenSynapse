const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const ffi = require('../../artifacts/synapse-asar-4.0.698/node_modules/ffi-napi-rz');

const resolveMappingEngineDll = () => {
  if (process.env.OPENSYNAPSE_MAPPING_ENGINE_DLL) return process.env.OPENSYNAPSE_MAPPING_ENGINE_DLL;
  const root = path.join(process.env.ProgramFiles || 'C:/Program Files', 'Razer', 'RazerAppEngine');
  const versions = fs.existsSync(root)
    ? fs.readdirSync(root, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && entry.name.startsWith('app-'))
      .map((entry) => entry.name)
      .sort()
      .reverse()
    : [];
  for (const version of versions) {
    const candidate = path.join(root, version, 'CommonDLL', 'mapping_engine.dll');
    if (fs.existsSync(candidate)) return candidate;
  }
  throw new Error('Set OPENSYNAPSE_MAPPING_ENGINE_DLL or install Razer AppEngine.');
};

const dllPath = resolveMappingEngineDll();
const lib = ffi.Library(dllPath, {
  mappingEngineInitialize: ['void', ['pointer']],
  addUsbDevice: ['void', ['string', 'pointer', 'pointer']],
  removeUsbDevice: ['void', ['string', 'pointer']],
  localStorageSetItem: ['void', ['string', 'string', 'pointer']],
  enableMapping: ['void', ['pointer']],
  registerInputNotification: ['void', ['string', 'pointer']],
  unregisterInputNotification: ['void', ['string', 'pointer']],
  setInputNotificationCallback: ['void', ['string', 'pointer', 'pointer']],
});

const containerId = '{00000000-0000-0000-FFFF-FFFFFFFFFFFF}';
const device = JSON.stringify({
  vendorId: 5426,
  containerId,
  productId: 710,
  guid: crypto.randomUUID(),
});
const logPath = process.env.OPENSYNAPSE_MAPPING_LOG ||
  path.join(process.env.LOCALAPPDATA || process.cwd(), 'OpenSynapse', 'mapping-engine-razerkey-m345.jsonl');
const waitMs = Number(process.env.OPENSYNAPSE_MAPPING_WAIT_MS || 60000);
const storageLog = process.env.OPENSYNAPSE_RAZER_STORAGE_LOG ||
  path.join(process.env.LOCALAPPDATA || '', 'Razer', 'RazerAppEngine', 'User Data', 'Logs',
    'products_710_ui {00000000-0000-0000-FFFF-FFFFFFFFFFFF}4.log');
const storageMarker = 'device local storage data ';
if (!Number.isFinite(waitMs) || waitMs <= 0) {
  throw new Error('OPENSYNAPSE_MAPPING_WAIT_MS must be a positive number.');
}

fs.mkdirSync(path.dirname(logPath), { recursive: true });
fs.writeFileSync(logPath, '');

const record = (kind, value) => {
  const entry = { at: new Date().toISOString(), kind, value };
  fs.appendFileSync(logPath, `${JSON.stringify(entry)}\n`);
  console.log(JSON.stringify(entry));
};
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const callResult = (label, call) => new Promise((resolve) => {
  let settled = false;
  const done = (value) => {
    if (settled) return;
    settled = true;
    record(label, value);
    resolve(value);
  };
  const callback = ffi.Callback('void', ['bool', 'string', 'string'],
    (ok, reason, info) => done({ ok, reason, info }));
  try {
    call(callback);
  } catch (error) {
    done({ ok: false, error: error.message });
  }
  setTimeout(() => done({ ok: false, error: 'timeout' }), 5000);
});
const callSimpleResult = (label, call) => new Promise((resolve) => {
  let settled = false;
  const done = (value) => {
    if (settled) return;
    settled = true;
    record(label, value);
    resolve(value);
  };
  const callback = ffi.Callback('void', ['bool', 'string'],
    (ok, reason) => done({ ok, reason }));
  try {
    call(callback);
  } catch (error) {
    done({ ok: false, error: error.message });
  }
  setTimeout(() => done({ ok: false, error: 'timeout' }), 5000);
});
const requireSuccess = async (label, call) => {
  const result = await callResult(label, call);
  if (!result.ok) throw new Error(`${label} failed: ${result.reason || result.error || 'unknown error'}`);
};

const initCallback = ffi.Callback('void', [], () => record('initialized', true));
let resolveDriverReady;
const driverReady = new Promise((resolve) => { resolveDriverReady = resolve; });
const deviceEventCallback = ffi.Callback('void', ['string', 'int', 'string', 'ulonglong'],
  (info, type, event, tick) => {
    let parsedEvent;
    try { parsedEvent = JSON.parse(event); } catch (error) { parsedEvent = { parseError: error.message }; }
    record('deviceEvent', { info, type, event, parsedEvent, tick: String(tick) });
    if (parsedEvent.type === 'info' && parsedEvent.info === 'driver ready') resolveDriverReady(true);
    if (parsedEvent.type === 'error') resolveDriverReady(false);
  });
const inputNotificationCallback = ffi.Callback(
  'void',
  ['string', 'int', 'string', 'ulonglong'],
  (info, type, input, tick) => {
    let parsedInput;
    try {
      parsedInput = JSON.parse(input);
    } catch (error) {
      parsedInput = { parseError: error.message };
    }
    record('inputnotified', { info, type, input, parsedInput, tick: String(tick) });
  });

let stopping = false;
const stop = async (exitCode) => {
  if (stopping) return;
  stopping = true;
  await callResult('unregisterInputNotification', (callback) =>
    lib.unregisterInputNotification(device, callback));
  await callResult('removeUsbDevice', (callback) => lib.removeUsbDevice(device, callback));
  record('stopped', true);
  process.exit(exitCode);
};

process.once('SIGINT', () => void stop(130));
process.once('SIGTERM', () => void stop(143));

(async () => {
  try {
    lib.mappingEngineInitialize(initCallback);
    await delay(500);
    await requireSuccess('addUsbDevice', (callback) =>
      lib.addUsbDevice(device, deviceEventCallback, callback));
    if (!await Promise.race([driverReady, delay(10000).then(() => false)])) {
      throw new Error('Blade filter driver did not become ready.');
    }
    const storageLine = fs.readFileSync(storageLog, 'utf8').split(/\r?\n/).reverse()
      .find((line) => line.includes(storageMarker));
    if (!storageLine) throw new Error('Product 710 local storage data was not found.');
    const storageValue = storageLine.slice(storageLine.indexOf(storageMarker) + storageMarker.length);
    const storageData = JSON.parse(storageValue);
    if (storageData.productId !== 710 || storageData.reportIDs?.['4'] !== 'razerKeyReportID') {
      throw new Error('Product 710 local storage data does not declare Report ID 4 as RazerKey.');
    }
    const mapping = await callSimpleResult('enableMapping', (callback) => lib.enableMapping(callback));
    if (!mapping.ok && mapping.reason !== 'already enabled') {
      throw new Error(`enableMapping failed: ${mapping.reason || mapping.error || 'unknown error'}`);
    }
    await requireSuccess('registerInputNotification', (callback) =>
      lib.registerInputNotification(device, callback));
    await requireSuccess('setInputNotificationCallback', (callback) =>
      lib.setInputNotificationCallback(device, inputNotificationCallback, callback));
    const storageResult = await callSimpleResult('localStorageSetItem', (callback) =>
      lib.localStorageSetItem(`synapse_710_${containerId}`, storageValue, callback));
    if (!storageResult.ok) {
      throw new Error(`localStorageSetItem failed: ${storageResult.reason || storageResult.error || 'unknown error'}`);
    }
    await delay(500);
    record('ready', `Press the Blade keyboard M3, M4, and M5 once each. Waiting ${waitMs} ms.`);
    await delay(waitMs);
    await stop(0);
  } catch (error) {
    record('fatal', error.message);
    await stop(1);
  }
})();
