import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, writeFile, readFile, realpath, mkdir, rm, rename, unlink, cp } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { prepareNativeProject, readNativeMcpSettings } from '../src/native-mcp.mjs';
import { initializeState, inspectState } from '../src/state.mjs';
import { configureNativeModel } from '../src/native-chat-model.mjs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { withProjectLock, projectKey } from '../src/project-workspace.mjs';

async function fixture() {
  const root = await mkdtemp(join(tmpdir(), 'tc-native-mcp-'));
  const state = join(root, 'state');
  const executable = join(root, 'knowledge.exe'), entrypoint = join(root, 'engineering.mjs');
  await writeFile(executable, 'synthetic executable metadata'); await writeFile(entrypoint, '');
  const mcpConfig = join(root, 'mcp.json');
  await writeFile(mcpConfig, JSON.stringify({ knowledge: { executable }, engineering: { entrypoint } }));
  const solution = join(root, 'Test.sln'); await writeFile(solution, 'synthetic');
  const oldEngineering = join(root,'old-engineering.json');
  await writeFile(oldEngineering,JSON.stringify({readOnly:true,writeAllowlist:[],engineering:{enabled:true,backend:'configuredProjectFiles',projectFiles:[]}}));
  await initializeState(state, { provider: 'openai', sourceCodeDisclosureAccepted: true, engineering:{entrypoint,config:oldEngineering} });
  return { root, state, mcpConfig, solution };
}

test('busy project setup reports a fixed code without adopting or deleting the existing lock', async () => {
  const f = await fixture();
  try {
    const project = join(f.root, 'PLC.plcproj'); await writeFile(project, '<Project/>');
    const request = { state: f.state, mcpConfig: f.mcpConfig, solution: f.solution, project };
    const prepared = await prepareNativeProject(request);
    const canonical = async path => process.platform === 'win32' ? (await realpath(path)).toLowerCase() : await realpath(path);
    const key = projectKey(await canonical(f.solution), await canonical(project));
    const parent = join(f.state, 'projects'), lock = join(parent, key + '.lock');
    const policy = await readFile(join(prepared.workspace, 'opencode.json'));
    await withProjectLock(parent, key, async () => {
      await assert.rejects(prepareNativeProject(request), { code: 'PROJECT_WORKSPACE_LOCKED' });
      const result = spawnSync(process.execPath, [fileURLToPath(new URL('../src/native-mcp.mjs', import.meta.url))], {
        input: JSON.stringify(request), encoding: 'utf8', windowsHide: true, timeout: 60000,
      });
      assert.equal(result.status, 1);
      assert.deepEqual(JSON.parse(result.stdout), { configured: false, failure: 'project_locked' });
      assert.equal(await readFile(lock, 'utf8'), '', 'the existing lock is neither adopted nor removed');
      assert.deepEqual(await readFile(join(prepared.workspace, 'opencode.json')), policy);
    });
    await assert.rejects(readFile(lock), { code: 'ENOENT' });
    assert.equal((await prepareNativeProject(request)).workspace, prepared.workspace);
  } finally { await rm(f.root, { recursive: true, force: true }); }
});

test('unchanged project setup does not take the provider writer lock or rewrite global policy', async () => {
  const f = await fixture();
  try {
    const project = join(f.root,'PLC.plcproj'); await writeFile(project,'<Project/>');
    const request = { state:f.state, mcpConfig:f.mcpConfig, solution:f.solution, project };
    const first = await prepareNativeProject(request);
    const { paths } = await inspectState(f.state);
    const before = await Promise.all([readFile(paths.configPath),readFile(paths.settingsPath)]);
    const lock = join(f.state,'model-consent.lock'); await writeFile(lock,'synthetic other writer');
    assert.equal((await prepareNativeProject(request)).workspace,first.workspace);
    assert.deepEqual(await Promise.all([readFile(paths.configPath),readFile(paths.settingsPath)]),before);
    assert.equal(await readFile(lock,'utf8'),'synthetic other writer');
  } finally { await rm(f.root,{recursive:true,force:true}); }
});
test('native MCP project locations isolate two PLCs and retain provider policy and source files', async () => {
  const f = await fixture();
  try {
    const interrupted = join(f.state, 'projects', 'f'.repeat(64)); await mkdir(interrupted, { recursive: true });
    await writeFile(join(interrupted, 'project.json'), '{');
    const projects = [];
    for (const name of ['PLC_A', 'PLC_B']) {
      const directory = join(f.root, name); await mkdir(directory);
      const project = join(directory, name + '.plcproj'); await writeFile(project, '<Project/>');
      projects.push({ ...await prepareNativeProject({ state: f.state, mcpConfig: f.mcpConfig, solution: f.solution, project }), project });
      assert.equal(await readFile(project, 'utf8'), '<Project/>');
      assert.deepEqual(await readFile(join(projects.at(-1).workspace, 'engineering.json'), 'utf8').then(JSON.parse), {
        targetAmsNetId: '127.0.0.1.1.1', readOnly: true, writeAllowlist: [],
        engineering: { enabled: true, backend: 'configuredProjectFiles', projectFiles: [{ path: process.platform==='win32' ? (await realpath(project)).toLowerCase() : await realpath(project), type: 'plc' }] },
      });
    }
    assert.notEqual(projects[0].workspace, projects[1].workspace);
    const again = await prepareNativeProject({ state: f.state, mcpConfig: f.mcpConfig, solution: f.solution, project: projects[0].project });
    assert.equal(again.workspace, projects[0].workspace);
    if (process.platform==='win32') {
      const sameProject = await prepareNativeProject({ state:f.state, mcpConfig:f.mcpConfig,
        solution:f.solution.toUpperCase(), project:projects[0].project.toUpperCase() });
      assert.equal(sameProject.workspace, again.workspace, 'Windows path casing must not change or invalidate a generated project policy');
    }
    const local = JSON.parse(await readFile(join(again.workspace, 'opencode.json'), 'utf8'));
    assert.deepEqual(Object.keys(local), ['mcp', 'agents'], 'location config must not override global provider policy');
    assert.equal(local.experimental, undefined);
    assert.deepEqual(local.agents['twincat-readonly'].permissions[0], { action:'*', resource:'*', effect:'deny' });
    assert.ok(!local.agents['twincat-readonly'].permissions.some(rule => rule.effect==='allow' && /write|edit|shell|build|ads/i.test(rule.action)));
    assert.deepEqual(Object.keys(local.mcp.servers).sort(), ['engineering', 'knowledge']);
    const status = await inspectState(f.state);
    assert.equal(status.settings.provider, 'openai');
    assert.equal(status.modelCallsEnabled, true);
    assert.ok(status.settings.knowledge);
    assert.equal(status.settings.engineering, undefined, 'general chats have no project engineering backend');
  } finally { await rm(f.root, { recursive: true, force: true }); }
});
test('native MCP setup refuses unknown options, wrong project types and changed generated policies', async () => {
  const f = await fixture();
  try {
    const project = join(f.root, 'PLC.plcproj'); await writeFile(project, '<Project/>');
    const request = { state: f.state, mcpConfig: f.mcpConfig, solution: f.solution, project };
    await assert.rejects(prepareNativeProject({ ...request, command: 'anything' }), /Invalid/);
    await assert.rejects(prepareNativeProject({ ...request, project: f.solution }), /PLC project/);
    const prepared = await prepareNativeProject(request);
    await writeFile(join(prepared.workspace, 'opencode.json'), '{"permissions":[]}');
    await assert.rejects(prepareNativeProject(request), /modified/);
    await writeFile(f.mcpConfig, JSON.stringify({ knowledge: { executable: project, apiKey: 'private' }, engineering: { entrypoint: project } }));
    await assert.rejects(readNativeMcpSettings(f.mcpConfig), /Invalid/);
  } finally { await rm(f.root, { recursive: true, force: true }); }
});

test('MCP settings and model changes preserve project identity and existing workspace data', async () => {
  const f = await fixture();
  try {
    const project = join(f.root, 'PLC.plcproj'); await writeFile(project, '<Project/>');
    const request = { state: f.state, mcpConfig: f.mcpConfig, solution: f.solution, project };
    const first = await prepareNativeProject(request);
    const preserved = join(first.workspace, 'existing-chat-reference');
    await writeFile(preserved, 'persisted session ID, no source code');
    const newEntry = join(f.root, 'engineering-updated.mjs'); await writeFile(newEntry, '');
    const settings = JSON.parse(await readFile(f.mcpConfig, 'utf8'));
    settings.engineering.entrypoint = newEntry;
    await writeFile(f.mcpConfig, JSON.stringify(settings));
    await configureNativeModel({ state: f.state, model: 'openai/synthetic-test', acceptSourceDisclosure: true });
    const second = await prepareNativeProject(request);
    assert.equal(second.key, first.key);
    assert.equal(second.workspace, first.workspace);
    assert.equal(await readFile(preserved, 'utf8'), 'persisted session ID, no source code');
    const local = JSON.parse(await readFile(join(second.workspace, 'opencode.json'), 'utf8'));
    assert.equal(local.mcp.servers.engineering.command[1], newEntry);
    assert.deepEqual(local.agents['twincat-readonly'].permissions[0], { action: '*', resource: '*', effect: 'deny' });
    assert.equal((await inspectState(f.state)).settings.provider, 'openai');
  } finally { await rm(f.root, { recursive: true, force: true }); }
});

test('a 0.1.9 project workspace is adopted in place and survives later configuration changes', async () => {
  const f = await fixture();
  try {
    const project = join(f.root, 'PLC.plcproj'); await writeFile(project, '<Project/>');
    const request = { state: f.state, mcpConfig: f.mcpConfig, solution: f.solution, project };
    const first = await prepareNativeProject(request);
    const receipt = JSON.parse(await readFile(join(first.workspace, 'project.json'), 'utf8'));
    receipt.version = 1;
    const legacyKey = createHash('sha256').update(JSON.stringify(receipt)).digest('hex');
    const legacy = join(f.state, 'projects', legacyKey);
    await rename(first.workspace, legacy);
    await unlink(join(f.state, 'projects', first.key + '.json'));
    await writeFile(join(legacy, 'project.json'), JSON.stringify(receipt));
    const config = await readFile(join(legacy, 'opencode.json'), 'utf8');
    await writeFile(join(legacy, 'opencode.json'), config.replaceAll(JSON.stringify(first.workspace).slice(1, -1), JSON.stringify(legacy).slice(1, -1)));
    await writeFile(join(legacy, 'original-history-marker'), 'existing session');
    const adopted = await prepareNativeProject(request);
    assert.equal(adopted.workspace, legacy);
    assert.equal(await readFile(join(legacy, 'original-history-marker'), 'utf8'), 'existing session');
    assert.equal((await prepareNativeProject(request)).workspace, legacy);
  } finally { await rm(f.root, { recursive: true, force: true }); }
});

test('historical configuration folders remain valid chat locations only for their original PLC', async () => {
  const f = await fixture();
  try {
    const project = join(f.root, 'PLC.plcproj'); await writeFile(project, '<Project/>');
    const request = { state: f.state, mcpConfig: f.mcpConfig, solution: f.solution, project };
    const first = await prepareNativeProject(request);
    const receipt = JSON.parse(await readFile(join(first.workspace, 'project.json'), 'utf8'));
    receipt.version = 1;
    const key = createHash('sha256').update(JSON.stringify(receipt)).digest('hex');
    const legacy = join(f.state, 'projects', key);
    await cp(first.workspace, legacy, { recursive: true });
    await writeFile(join(legacy, 'project.json'), JSON.stringify(receipt));
    const config = await readFile(join(legacy, 'opencode.json'), 'utf8');
    await writeFile(join(legacy, 'opencode.json'), config.replaceAll(JSON.stringify(first.workspace).slice(1, -1), JSON.stringify(legacy).slice(1, -1)));
    const current = await prepareNativeProject(request);
    assert.deepEqual(new Set(current.workspaces), new Set([first.workspace, legacy]));
    assert.equal((await prepareNativeProject({ ...request, workspace: legacy })).workspace, legacy);
    assert.equal((await prepareNativeProject(request)).workspace, first.workspace, 'opening an old chat does not replace the stable project index');
    const foreign = join(f.root, 'Other.plcproj'); await writeFile(foreign, '<Project/>');
    await assert.rejects(prepareNativeProject({ ...request, project: foreign, workspace: legacy }), /another project/);
  } finally { await rm(f.root, { recursive: true, force: true }); }
});

test('an interrupted managed configuration update recovers without replacing project history', async () => {
  const f = await fixture();
  try {
    const project = join(f.root, 'PLC.plcproj'); await writeFile(project, '<Project/>');
    const request = { state: f.state, mcpConfig: f.mcpConfig, solution: f.solution, project };
    const first = await prepareNativeProject(request);
    const receiptFile = join(first.workspace, 'project.json');
    const before = JSON.parse(await readFile(receiptFile, 'utf8'));
    const nextEntry = join(f.root, 'updated.mjs'); await writeFile(nextEntry, '');
    const input = JSON.parse(await readFile(f.mcpConfig, 'utf8'));
    input.engineering.entrypoint = nextEntry; await writeFile(f.mcpConfig, JSON.stringify(input));
    await prepareNativeProject(request);
    const after = JSON.parse(await readFile(receiptFile, 'utf8'));
    // Simulate process death after committing config but before its receipt.
    await writeFile(receiptFile, JSON.stringify(before));
    const journal = join(first.workspace, '.configuration.json');
    await writeFile(journal, JSON.stringify({ version: 1, solution: before.solution, project: before.project,
      before: before.settings, after: after.settings }));
    assert.equal((await prepareNativeProject(request)).workspace, first.workspace);
    assert.deepEqual(JSON.parse(await readFile(receiptFile, 'utf8')), after);
    await assert.rejects(readFile(journal), { code: 'ENOENT' });
  } finally { await rm(f.root, { recursive: true, force: true }); }
});
