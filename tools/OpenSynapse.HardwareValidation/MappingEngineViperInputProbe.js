const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const ffi = require('../../artifacts/synapse-asar-4.0.698/node_modules/ffi-napi-rz');

const dllPath = 'C:/Program Files/Razer/RazerAppEngine/app-4.0.698/CommonDLL/mapping_engine.dll';
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

const containerId = '{9E502CF7-160A-51EA-8250-14BD19EB4A4A}';
const productId = 184;
const device = JSON.stringify({ vendorId: 5426, containerId, productId, guid: crypto.randomUUID() });
const storageLog = 'C:/Users/A1mAssist/AppData/Local/Razer/RazerAppEngine/User Data/Logs/' +
  `products_184_mw ${containerId}1.log`;
const logPath = process.env.OPENSYNAPSE_MAPPING_LOG ||
  'D:/Workspaces/OpenSynapse/artifacts/protocol/2026-08-15/mapping-engine-viper-m345.jsonl';
const waitMs = Number(process.env.OPENSYNAPSE_MAPPING_WAIT_MS || 60000);
if (!Number.isFinite(waitMs) || waitMs <= 0) throw new Error('Invalid wait time.');

fs.mkdirSync(path.dirname(logPath), { recursive: true });
fs.writeFileSync(logPath, '');
const record = (kind, value) => {
  const entry = { at: new Date().toISOString(), kind, value };
  fs.appendFileSync(logPath, `${JSON.stringify(entry)}\n`);
  console.log(JSON.stringify(entry));
};
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const call = (label, args, simple = false) => new Promise((resolve) => {
  let settled = false;
  const done = (value) => {
    if (settled) return;
    settled = true;
    record(label, value);
    resolve(value);
  };
  const callback = ffi.Callback('void', simple ? ['bool', 'string'] : ['bool', 'string', 'string'],
    (ok, reason, info) => done({ ok, reason, info }));
  try { args(callback); } catch (error) { done({ ok: false, error: error.message }); }
  setTimeout(() => done({ ok: false, error: 'timeout' }), 5000);
});
const requireSuccess = async (label, args, simple = false) => {
  const result = await call(label, args, simple);
  if (!result.ok) throw new Error(`${label} failed: ${result.reason || result.error || 'unknown error'}`);
};

const storageLine = fs.readFileSync(storageLog, 'utf8').split(/\r?\n/).reverse()
  .find((line) => line.includes('"action":"localStorageSetItem"') &&
    line.includes(`"key":"synapse_${productId}_${containerId}"`));
if (!storageLine) throw new Error('Official Product 184 deviceData was not found.');
const argsStart = storageLine.indexOf('args: ') + 'args: '.length;
const argsEnd = storageLine.indexOf(' , r:', argsStart);
const storageValue = JSON.parse(storageLine.slice(argsStart, argsEnd)).payload.value;
const storageData = JSON.parse(storageValue);
if (storageData.productId !== productId || storageData.reportIDs?.['4'] !== 'razerKeyReportID') {
  throw new Error('Product 184 deviceData is invalid.');
}

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
const inputCallback = ffi.Callback('void', ['string', 'int', 'string', 'ulonglong'],
  (info, type, input, tick) => {
    let parsedInput;
    try { parsedInput = JSON.parse(input); } catch (error) { parsedInput = { parseError: error.message }; }
    record('inputnotified', { info, type, input, parsedInput, tick: String(tick) });
  });

let stopping = false;
const stop = async (exitCode) => {
  if (stopping) return;
  stopping = true;
  await call('unregisterInputNotification', (callback) => lib.unregisterInputNotification(device, callback));
  await call('removeUsbDevice', (callback) => lib.removeUsbDevice(device, callback));
  record('stopped', true);
  process.exit(exitCode);
};
process.once('SIGINT', () => void stop(130));
process.once('SIGTERM', () => void stop(143));

(async () => {
  try {
    lib.mappingEngineInitialize(initCallback);
    await delay(500);
    await requireSuccess('addUsbDevice', (callback) => lib.addUsbDevice(device, deviceEventCallback, callback));
    if (!await Promise.race([driverReady, delay(10000).then(() => false)])) {
      throw new Error('Viper filter driver did not become ready.');
    }
    await requireSuccess('localStorageSetItem', (callback) =>
      lib.localStorageSetItem(`synapse_${productId}_${containerId}`, storageValue, callback), true);
    await delay(500);
    const mapping = await call('enableMapping', (callback) => lib.enableMapping(callback), true);
    if (!mapping.ok && mapping.reason !== 'already enabled') {
      throw new Error(`enableMapping failed: ${mapping.reason || mapping.error || 'unknown error'}`);
    }
    await requireSuccess('registerInputNotification', (callback) =>
      lib.registerInputNotification(device, callback));
    await requireSuccess('setInputNotificationCallback', (callback) =>
      lib.setInputNotificationCallback(device, inputCallback, callback));
    record('ready', `Press Viper M3, M4, and M5 once each. Waiting ${waitMs} ms.`);
    await delay(waitMs);
    await stop(0);
  } catch (error) {
    record('fatal', error.message);
    await stop(1);
  }
})();
