// Pure filesystem/process fixtures: never launches OpenSynapse or touches devices.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const scripts = path.dirname(fileURLToPath(import.meta.url));
const write = (file, value) => {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, value);
};
const json = (file, value) => write(file, JSON.stringify(value));
function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'opensynapse-governance-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  return root;
}
function run(command, args, env = {}) {
  const result = spawnSync(command, args, { encoding: 'utf8', windowsHide: true, env: { ...process.env, ...env } });
  assert.ifError(result.error);
  assert.equal(result.signal, null, result.stderr);
  return { code: result.status, output: result.stdout + result.stderr };
}
const node = (script, args) => run(process.execPath, [path.join(scripts, script), ...args]);
const ps = (script, args, env) => run('pwsh', ['-NoProfile', '-File', script, ...args], env);

function structure(t, files, budget) {
  const root = fixture(t);
  for (const [name, text] of Object.entries(files)) write(path.join(root, 'src', name), text);
  json(path.join(root, '.structure-budget.json'), { newFileLineLimit: 600, newFilePublicMemberLimit: 100, files: [], groups: [], ...budget });
  return ps(path.join(scripts, 'check-structure.ps1'), ['-Root', root]);
}
test('structure rejects a new 601-line file despite a matching group', t => {
  const result = structure(t, { 'Example.New.cs': 'line\n'.repeat(601) }, { groups: [{ pattern: 'src/Example*.cs', maxLines: 5000 }] });
  assert.equal(result.code, 1, result.output);
  assert.match(result.output, /601 > 600/);
});
test('structure enforces aggregate group line and public budgets', t => {
  const result = structure(t, { 'Example.A.cs': '    public int A;\n    public int B;\n', 'Example.B.cs': '    public int C;\n    public int D;\n' }, { groups: [{ pattern: 'src/Example*.cs', maxLines: 3, maxPublicMembers: 3 }] });
  assert.equal(result.code, 1, result.output);
  assert.equal((result.output.match(/4 > 3/g) ?? []).length, 2);
});
test('a saved line budget does not bypass the default public budget', t => {
  const result = structure(t, { 'Example.cs': '    public int A;\n'.repeat(101) }, { files: [{ path: 'src/Example.cs', maxLines: 200 }] });
  assert.equal(result.code, 1, result.output);
  assert.match(result.output, /101 > 100/);
  const upper = structure(t, { 'Example.CS': '    public int A;\n'.repeat(101) }, {});
  assert.equal(upper.code, 1, upper.output);
  assert.match(upper.output, /101 > 100/);
});
test('structure honors saved default limits', t => {
  const result = structure(t, { 'Example.cs': 'line\n'.repeat(6) }, { newFileLineLimit: 5 });
  assert.equal(result.code, 1, result.output);
  assert.match(result.output, /6 > 5/);
});
test('structure accepts bounded files and rejects stale groups', t => {
  assert.equal(structure(t, { 'Example.cs': 'line\n' }, {}).code, 0);
  assert.equal(structure(t, { 'Example.cs': 'line\n' }, { groups: [{ pattern: 'src/Missing*.cs', maxLines: 100 }] }).code, 1);
});

function profile(t, pids = ['023A']) {
  const root = fixture(t);
  const manifests = path.join(root, 'src/OpenSynapse.Windows/Devices/Manifests');
  for (const pid of pids) {
    json(path.join(manifests, `blade-${pid.toLowerCase()}.json`), { productIds: [pid], capabilities: {} });
    const bundle = path.join(root, 'artifacts/razer-mw-full-cdn/bundles', pid);
    json(path.join(bundle, 'manifest.json'), { deviceName: `Blade ${pid}`, editionId: [0], layoutId: [1] });
    write(path.join(bundle, 'main.fixture.js'), 'useFeature("rzDeviceType","rzDevice25LedMatrixBlade")');
  }
  return { root, manifests };
}
test('profiles --out is reproducible and --check does not write', t => {
  const { root, manifests } = profile(t);
  const source = fs.readFileSync(path.join(manifests, 'blade-023a.json'), 'utf8');
  const out = path.join(root, 'output');
  const args = ['--root', root, '--out', out];
  assert.equal(node('extract-blade-profiles.mjs', args).code, 0);
  const file = path.join(out, 'blade-023a.json');
  const generated = fs.readFileSync(file, 'utf8');
  assert.equal(JSON.parse(generated).device.name, 'Blade 023A');
  assert.equal(node('extract-blade-profiles.mjs', args).code, 0);
  assert.equal(fs.readFileSync(file, 'utf8'), generated);
  assert.equal(node('extract-blade-profiles.mjs', [...args, '--check']).code, 0);
  write(file, generated.replaceAll('\n', '\r\n'));
  assert.equal(node('extract-blade-profiles.mjs', [...args, '--check']).code, 0);
  write(file, 'outdated');
  assert.equal(node('extract-blade-profiles.mjs', [...args, '--check']).code, 1);
  assert.equal(fs.readFileSync(file, 'utf8'), 'outdated');
  assert.equal(fs.readFileSync(path.join(manifests, 'blade-023a.json'), 'utf8'), source);
});
test('profiles validates all later PIDs before overwriting an earlier manifest', t => {
  const { root, manifests } = profile(t);
  const earlier = path.join(manifests, 'blade-023a.json');
  const before = fs.readFileSync(earlier, 'utf8');
  json(path.join(manifests, 'blade-ffff.json'), { productIds: ['FFFF'] });
  assert.equal(node('extract-blade-profiles.mjs', ['--root', root]).code, 1);
  assert.equal(fs.readFileSync(earlier, 'utf8'), before);
});
test('profiles conflicting bundle values leave outputs untouched', t => {
  const { root, manifests } = profile(t);
  const file = path.join(manifests, 'blade-023a.json');
  const before = fs.readFileSync(file, 'utf8');
  write(path.join(root, 'artifacts/razer-mw-full-cdn/bundles/023A/main.conflict.js'), 'useFeature("rzDeviceType","different")');
  assert.equal(node('extract-blade-profiles.mjs', ['--root', root]).code, 1);
  assert.equal(fs.readFileSync(file, 'utf8'), before);
});

function mapping(t, latePid = false) {
  const root = fixture(t);
  write(path.join(root, 'src/OpenSynapse.Core/Devices/BladeModelCapabilities.cs'), `var map = new() { [0x02C6] = new(), ${latePid ? '[0xFFFF] = new(),' : ''} };`);
  write(path.join(root, 'artifacts/razer-mw-full-cdn/bundles/02C6/main.fixture.js'), 'DEFAULTMAPPINGS:()=>m,DeviceInfo:()=>d; m=[{inputID:"KEY_A",outputType:"disableGroup"}];d={productId:710};');
  const out = path.join(root, 'output');
  return { root, out };
}
test('mapping 710 mismatch is rejected before any output writes', t => {
  const { root, out } = mapping(t);
  const file = path.join(out, 'Product710Mapping.json');
  json(file, { defaultMappings: { appEngine: { mappings: [] } } });
  const before = fs.readFileSync(file, 'utf8');
  const result = node('extract-blade-mappings.mjs', ['--root', root, '--out', out]);
  assert.equal(result.code, 1, result.output);
  assert.match(result.output, /differs/i);
  assert.equal(fs.readFileSync(file, 'utf8'), before);
});
test('mapping late PID failure leaves an existing output untouched', t => {
  const { root, out } = mapping(t, true);
  const file = path.join(out, 'Product710Mapping.json');
  write(file, 'sentinel');
  assert.equal(node('extract-blade-mappings.mjs', ['--root', root, '--out', out]).code, 1);
  assert.equal(fs.readFileSync(file, 'utf8'), 'sentinel');
});
test('mapping valid regeneration is deterministic; dry-run never writes', t => {
  const { root, out } = mapping(t);
  const args = ['--root', root, '--out', out];
  assert.equal(node('extract-blade-mappings.mjs', [...args, '--dry-run']).code, 0);
  assert.equal(fs.existsSync(out), false);
  assert.equal(node('extract-blade-mappings.mjs', args).code, 0);
  const before = fs.readFileSync(path.join(out, 'Product710Mapping.json'), 'utf8');
  assert.equal(node('extract-blade-mappings.mjs', args).code, 0);
  assert.equal(fs.readFileSync(path.join(out, 'Product710Mapping.json'), 'utf8'), before);
});

function scan(t, entries) {
  const root = fixture(t);
  const bundles = path.join(root, 'bundles');
  const catalog = path.join(root, 'Catalog.cs');
  write(catalog, entries.map(({ pid, type, channel }) => `[0x${pid}] = new("${type}", RazerHeadsetChannel.${channel}),`).join('\n'));
  for (const { pid, bundleType } of entries) write(path.join(bundles, pid, 'main.fixture.js'), bundleType ? `useFeature("rzDeviceType","${bundleType}")` : '');
  const output = path.join(root, 'scan.csv');
  return { ...ps(path.join(scripts, 'scan-device-transport.ps1'), ['-BundleRoot', bundles, '-CatalogPath', catalog, '-OutputPath', output]), rows: fs.readFileSync(output, 'utf8') };
}
const scanEntry = (pid, bundleType = 'AudioMxicDevice', channel = 'Mxic64') => ({ pid, type: 'AudioMxicDevice', bundleType, channel });
test('transport mixed ok plus mismatch fails and keeps the report', t => {
  const result = scan(t, [scanEntry('1001'), scanEntry('1002', 'wrong')]);
  assert.equal(result.code, 1, result.output);
  assert.match(result.rows, /"ok"/);
  assert.match(result.rows, /"mismatch"/);
});
test('transport fully matching rows pass', t => {
  assert.equal(scan(t, [scanEntry('1001')]).code, 0);
  assert.equal(scan(t, [{ pid: '0A54', type: 'rzDevice25ValkyrieT3', bundleType: 'rzDevice25ValkyrieT3', channel: 'ValkyrieT3Feature64' }]).code, 0);
});
test('transport maps Billie and KrakenBase bundle types', t => {
  const result = scan(t, [
    { pid: '056E', type: 'AudioOdessaT2Wireless', bundleType: 'AudioOdessaT2Wireless', channel: 'Billie64' },
    { pid: '0520', type: 'AudioKrakenTE', bundleType: 'AudioKrakenTE', channel: 'KrakenBase37' },
  ]);
  assert.equal(result.code, 0, result.output);
});
test('transport unverified rows are not counted as validation', t => {
  assert.equal(scan(t, [scanEntry('1001', null)]).code, 1);
  assert.equal(scan(t, [scanEntry('1001', 'AudioMxicDevice', 'Unsupported')]).code, 1);
  const mixed = scan(t, [scanEntry('1001'), scanEntry('1002', null)]);
  assert.equal(mixed.code, 0, mixed.output);
  assert.ok(mixed.output.includes('unverified/unsupported: 1'));
});

function qa(t, mode, existing = 0, allowRestart = false, stopFails = false) {
  const root = fixture(t);
  const runner = path.join(root, 'skill/scripts/winui-qa.ps1');
  write(runner, `param([string]$Scenario)
$c = Get-Content -LiteralPath $Scenario -Raw | ConvertFrom-Json
[IO.File]::WriteAllText($env:GOV_SEEN, [IO.File]::ReadAllText($Scenario))
if ($env:GOV_MODE -eq 'no-report') { exit 0 }
if ($env:GOV_MODE -eq 'runner-fails') { exit 7 }
$started = [DateTimeOffset]::UtcNow
if ($env:GOV_MODE -eq 'stale') { $started = $started.AddDays(-1) }
$r = @{status = $(if ($env:GOV_MODE -eq 'failed-report') { 'failed' } else { 'passed' }); scenario = $Scenario; exe = $c.exe; startedAt = $started.ToString('o'); finishedAt = $started.ToString('o')}
$r | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $c.outDir 'report.json')
`);
  const wrapper = path.join(root, 'wrapper.ps1');
  write(wrapper, `function global:Get-Process { param($Name, $ErrorAction)
  for ($i = 0; $i -lt [int]$env:GOV_EXISTING; $i++) { [pscustomobject]@{Id = 424242 + $i; Path = $env:GOV_APP} }
}
function global:Stop-Process { param($Id, [switch]$Force, $ErrorAction)
  Add-Content -LiteralPath $env:GOV_LOG -Value "stop:$Id"
  if ($env:GOV_STOP_FAIL -eq '1') { throw 'fixture stop failed' }
}
function global:Start-Process { param($FilePath, $ArgumentList, $WorkingDirectory, $WindowStyle)
  Add-Content -LiteralPath $env:GOV_LOG -Value "restart:$FilePath"
}
function global:Start-Sleep { param($Seconds) }
& $env:GOV_SCRIPT -AppPath $env:GOV_APP -SkillRoot $env:GOV_SKILL -OutDir $env:GOV_OUT -AllowRestart:($env:GOV_ALLOW -eq '1')
`);
  const app = path.join(root, 'dummy-never-executed.exe');
  write(app, 'not an executable');
  const log = path.join(root, 'actions.log');
  const seen = path.join(root, 'seen.json');
  const result = ps(wrapper, [], { GOV_SCRIPT: path.join(scripts, 'run-ui-qa.ps1'), GOV_APP: app, GOV_SKILL: path.join(root, 'skill'), GOV_OUT: path.join(root, 'reports'), GOV_MODE: mode, GOV_EXISTING: String(existing), GOV_ALLOW: allowRestart ? '1' : '0', GOV_STOP_FAIL: stopFails ? '1' : '0', GOV_LOG: log, GOV_SEEN: seen });
  return { ...result, actions: fs.existsSync(log) ? fs.readFileSync(log, 'utf8') : '', scenario: fs.existsSync(seen) ? JSON.parse(fs.readFileSync(seen, 'utf8')) : null };
}
test('QA refuses existing instances by default without stopping or invoking a runner', t => {
  const result = qa(t, 'passed', 1);
  assert.notEqual(result.code, 0);
  assert.equal(result.actions, '');
  assert.equal(result.scenario, null);
});
test('QA uses stable navigation IDs and accepts only this run report', t => {
  const result = qa(t, 'passed');
  assert.equal(result.code, 0, result.output);
  assert.deepEqual(result.scenario.steps.filter(x => x.action === 'select').map(x => x.automationId), ['OverviewNavigationItem', 'DevicesNavigationItem', 'MacrosNavigationItem', 'ProfilesNavigationItem', 'DiagnosticsNavigationItem', 'SettingsNavigationItem', 'OverviewNavigationItem']);
  for (const mode of ['stale', 'no-report', 'failed-report']) assert.notEqual(qa(t, mode).code, 0, mode);
});
test('QA restores an explicitly stopped instance after runner failure', t => {
  const result = qa(t, 'runner-fails', 1, true);
  assert.notEqual(result.code, 0);
  assert.match(result.actions, /stop:424242/);
  assert.match(result.actions, /restart:/);
});
test('QA failed stop or multiple instances never starts another instance', t => {
  const failed = qa(t, 'passed', 1, true, true);
  assert.notEqual(failed.code, 0);
  assert.doesNotMatch(failed.actions, /restart:/);
  assert.equal(failed.scenario, null);
  const multiple = qa(t, 'passed', 2, true);
  assert.notEqual(multiple.code, 0);
  assert.equal(multiple.actions, '');
});
