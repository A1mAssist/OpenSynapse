const ffi = require('../../artifacts/synapse-asar-4.0.698/node_modules/ffi-napi-rz');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

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
const api = {
  mappingEngineInitialize: ['void', ['pointer']],
  mappingEngineShutdown: ['void', ['pointer']],
  addUsbDevice: ['void', ['string', 'pointer', 'pointer']],
  removeUsbDevice: ['void', ['string', 'pointer']],
  registerInputNotification: ['void', ['string', 'pointer']],
  unregisterInputNotification: ['void', ['string', 'pointer']],
  setInputNotificationCallback: ['void', ['string', 'pointer', 'pointer']],
  localStorageSetItem: ['void', ['string', 'string', 'pointer']],
  enableMapping: ['void', ['pointer']],
  disableMapping: ['void', ['pointer']],
  registerHardwareEvent: ['void', ['string', 'pointer']],
  unregisterHardwareEvent: ['void', ['string', 'pointer']],
  setHardwareEventCallback: ['void', ['string', 'pointer', 'pointer']],
  registerUnsupportedMapping: ['void', ['string', 'pointer']],
  unregisterUnsupportedMapping: ['void', ['string', 'pointer']],
  setUnsupportedMappingCallback: ['void', ['string', 'pointer', 'pointer']],
};

const lib = ffi.Library(dllPath, api);
const containerId = '{00000000-0000-0000-FFFF-FFFFFFFFFFFF}';
const device = JSON.stringify({
  vendorId: 5426,
  containerId,
  productId: 710,
  guid: crypto.randomUUID(),
});
const events = [];
const logPath = process.env.OPENSYNAPSE_MAPPING_LOG ||
  path.join(process.env.LOCALAPPDATA || process.cwd(), 'OpenSynapse', 'mapping-engine-input-01.jsonl');
fs.mkdirSync(path.dirname(logPath), { recursive: true });
const record = (kind, value) => {
  const entry = { at: new Date().toISOString(), kind, value };
  events.push(entry);
  if (logPath) fs.appendFileSync(logPath, `${JSON.stringify(entry)}\n`);
  console.log(JSON.stringify(entry));
};
const resultCallback = ffi.Callback('void', ['bool', 'string', 'string'], (ok, reason, info) =>
  record('result', { ok, reason, info }));
const deviceEventCallback = ffi.Callback('void', ['string'], (value) => record('deviceEvent', value));
const inputCallback = ffi.Callback('void', ['string', 'int', 'string', 'ulonglong'], (info, type, input, tick) =>
  record('input', { info, type, input, tick: String(tick) }));
const hardwareCallback = ffi.Callback('void', ['string', 'int', 'string', 'ulonglong'], (info, type, data, tick) =>
  record('hardware', { info, type, data, tick: String(tick) }));
const unsupportedMappingCallback = ffi.Callback(
  'void',
  ['string', 'int', 'string', 'string', 'ulonglong'],
  (info, type, input, output, tick) =>
    record('unsupportedMapping', { info, type, input, output, tick: String(tick) }));
const initCallback = ffi.Callback('void', [], () => record('initialized', true));
const shutdownCallback = ffi.Callback('void', [], () => record('shutdown', true));

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const callSimpleResult = (label, call) => new Promise((resolve) => {
  let settled = false;
  const done = (value) => {
    if (!settled) { settled = true; record(label, value); resolve(value); }
  };
  const callback = ffi.Callback('void', ['bool', 'string'], (ok, reason) => done({ ok, reason }));
  try { call(callback); } catch (error) { done({ ok: false, error: error.message }); }
  setTimeout(() => done({ ok: false, error: 'timeout' }), 5000);
});
const callResult = (label, call) => new Promise((resolve) => {
  let settled = false;
  const done = (value) => {
    if (!settled) { settled = true; record(label, value); resolve(value); }
  };
  const callback = ffi.Callback('void', ['bool', 'string', 'string'], (ok, reason, info) => done({ ok, reason, info }));
  try { call(callback); } catch (error) { done({ ok: false, error: error.message }); }
  setTimeout(() => done({ ok: false, error: 'timeout' }), 5000);
});

(async () => {
  try {
    lib.mappingEngineInitialize(initCallback);
    await delay(500);
    await callResult('addUsbDevice', (callback) => lib.addUsbDevice(device, deviceEventCallback, callback));
    await delay(1000);
    const storageLog = process.env.OPENSYNAPSE_RAZER_STORAGE_LOG ||
      path.join(process.env.LOCALAPPDATA || '', 'Razer', 'RazerAppEngine', 'User Data', 'Logs',
        'products_710_ui {00000000-0000-0000-FFFF-FFFFFFFFFFFF}4.log');
    const marker = 'device local storage data ';
    const storageLine = fs.readFileSync(storageLog, 'utf8').split(/\r?\n/).reverse().find((line) => line.includes(marker));
    if (!storageLine) throw new Error('Product 710 local storage data was not found.');
    const storageValue = storageLine.slice(storageLine.indexOf(marker) + marker.length);
    await callSimpleResult('localStorageSetItem', (callback) =>
      lib.localStorageSetItem(`synapse_710_${containerId}`, storageValue, callback));
    await callSimpleResult('enableMapping', (callback) => lib.enableMapping(callback));
    await callResult('registerInputNotification', (callback) => lib.registerInputNotification(device, callback));
    await callResult('setInputNotificationCallback', (callback) => lib.setInputNotificationCallback(device, inputCallback, callback));
    await callResult('registerUnsupportedMapping', (callback) => lib.registerUnsupportedMapping(device, callback));
    await callResult('setUnsupportedMappingCallback', (callback) =>
      lib.setUnsupportedMappingCallback(device, unsupportedMappingCallback, callback));
    await callResult('registerHardwareEvent', (callback) => lib.registerHardwareEvent(device, callback));
    await callResult('setHardwareEventCallback', (callback) => lib.setHardwareEventCallback(device, hardwareCallback, callback));
    record('ready', 'Press M3, M4, and M5 once each. Waiting 60 seconds.');
    await delay(60000);
  } finally {
    try { await callResult('unregisterInputNotification', (callback) => lib.unregisterInputNotification(device, callback)); } catch {}
    try { await callResult('unregisterUnsupportedMapping', (callback) => lib.unregisterUnsupportedMapping(device, callback)); } catch {}
    try { await callResult('unregisterHardwareEvent', (callback) => lib.unregisterHardwareEvent(device, callback)); } catch {}
    try { await callResult('removeUsbDevice', (callback) => lib.removeUsbDevice(device, callback)); } catch {}
    try { await callSimpleResult('disableMapping', (callback) => lib.disableMapping(callback)); } catch {}
    try { lib.mappingEngineShutdown(shutdownCallback); } catch (error) { record('shutdownError', error.message); }
    await delay(500);
  }
  record('summary', events);
})();
