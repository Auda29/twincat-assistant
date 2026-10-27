import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, writeFile, readFile, realpath, mkdir, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { prepareNativeProject, readNativeMcpSettings } from '../src/native-mcp.mjs';
import { initializeState, inspectState } from '../src/state.mjs';

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
test('native MCP project locations isolate two PLCs and retain provider policy and source files', async () => {
  const f = await fixture();
  try {
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
