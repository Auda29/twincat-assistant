import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, mkdir, readFile, rm, writeFile, symlink, lstat } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { initializeState, inspectState, prepareChatWorkspace, runtimeEnvironment, readJsonFile } from '../src/state.mjs';

test('chat workspace prepares both fresh-profile roots and preserves existing chats and policy on repeat', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-chat-workspace-'));
  try {
    const root = join(parent, 'runtime');
    const paths = await initializeState(root, {});
    const policy = await readFile(paths.configPath);
    const settings = await readFile(paths.settingsPath);
    await prepareChatWorkspace(root);
    const legacy = join(paths.home, '.config', 'openchamber', 'chats');
    const managed = join(paths.openchamber, 'chats');
    assert.ok((await lstat(legacy)).isDirectory());
    assert.ok((await lstat(managed)).isDirectory());
    await writeFile(join(legacy, 'existing-chat.txt'), 'synthetic existing chat');
    await prepareChatWorkspace(root);
    assert.equal(await readFile(join(legacy, 'existing-chat.txt'), 'utf8'), 'synthetic existing chat');
    assert.deepEqual(await readFile(paths.configPath), policy);
    assert.deepEqual(await readFile(paths.settingsPath), settings);
    assert.equal((await inspectState(root)).modelCallsEnabled, false);
  } finally { await rm(parent, { recursive: true, force: true }); }
});

test('chat workspace refuses redirected parents and conflicting files without creating descendants', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-chat-redirect-'));
  try {
    const root = join(parent, 'runtime');
    const paths = await initializeState(root, {});
    const outside = join(parent, 'outside');
    await mkdir(outside);
    const config = join(paths.home, '.config');
    await symlink(outside, config, process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(prepareChatWorkspace(root), /redirected/);
    await assert.rejects(lstat(join(outside, 'openchamber')), { code: 'ENOENT' });
    await rm(config);
    await writeFile(config, 'existing file');
    await assert.rejects(prepareChatWorkspace(root), /regular directories/);
    assert.equal(await readFile(config, 'utf8'), 'existing file');
  } finally { await rm(parent, { recursive: true, force: true }); }
});

test('runtime environment isolates user state and never forwards unrelated credentials or config injections', () => {
  const env = runtimeEnvironment('/absolute/runtime', {}, { PATH: '/bin', HOME: '/real/home', GITHUB_TOKEN: 'private-test-value', OPENCODE_CONFIG_CONTENT: '{unsafe}', OPENAI_API_KEY: 'private-test-value' });
  assert.equal(env.PATH, '/bin');
  assert.ok(env.HOME.endsWith('runtime/home') || env.HOME.endsWith('runtime\\home'));
  assert.equal(env.GITHUB_TOKEN, undefined);
  assert.equal(env.OPENCODE_CONFIG_CONTENT, undefined);
  assert.equal(env.OPENAI_API_KEY, undefined);
  assert.equal(env.OPENCHAMBER_RELAY_HOST, 'off');
  const selected = runtimeEnvironment('/absolute/runtime', { model: 'openai/example', sourceCodeDisclosureAccepted: true }, { OPENAI_API_KEY: 'private-test-value', ANTHROPIC_API_KEY: 'not-selected' });
  assert.equal(selected.OPENAI_API_KEY, 'private-test-value');
  assert.equal(selected.ANTHROPIC_API_KEY, undefined);
});

test('bounded JSON reader rejects symlinks, directories, oversized and invalid UTF-8 files', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-json-'));
  try {
    const path = join(parent, 'settings.json');
    await writeFile(path, '{}');
    try {
      await symlink(path, join(parent, 'link.json'));
      await assert.rejects(readJsonFile(join(parent, 'link.json')), /regular|file/);
    } catch (error) {
      if (process.platform !== 'win32' || error.code !== 'EPERM') throw error;
      // Windows file symlinks need Developer Mode or elevation; junctions do not.
      await symlink(parent, join(parent, 'link-directory'), 'junction');
      await assert.rejects(readJsonFile(join(parent, 'link-directory')), /regular|file/);
    }
    await assert.rejects(readJsonFile(parent), /regular|file/);
    await writeFile(path, Buffer.alloc(65537));
    await assert.rejects(readJsonFile(path), /bounded|large/);
    await writeFile(path, Buffer.from([0xff]));
    await assert.rejects(readJsonFile(path));
  } finally { await rm(parent, { recursive: true, force: true }); }
});

test('diagnosis detects modified policy and never claims native or corpus readiness', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-diagnosis-'));
  try {
    const root = join(parent, 'runtime');
    const paths = await initializeState(root, {});
    const status = await inspectState(root);
    assert.equal(status?.configurationVerified, true);
    assert.equal(status.nativeEngineering, 'unavailable');
    assert.equal(status.corpus, 'not_configured');
    assert.equal(status.modelCallsEnabled, false);
    await writeFile(paths.configPath, '{}');
    await assert.rejects(inspectState(root), /modified|mismatch/);
  } finally { await rm(parent, { recursive: true, force: true }); }
});

test('diagnosis rejects runtime/native or implicit engineering backends even when readOnly is true', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-backend-'));
  try {
    const entrypoint = join(parent, 'entry.mjs');
    const configPath = join(parent, 'engineering.json');
    await writeFile(entrypoint, '// synthetic entrypoint, never executed');
    const root = join(parent, 'runtime');
    await initializeState(root, { engineering: { entrypoint, config: configPath } });
    for (const engineering of [undefined, { enabled: false, backend: 'configuredProjectFiles' }, { enabled: true, backend: 'liveXae' }, { enabled: true, backend: 'ads' }, { enabled: true }]) {
      await writeFile(configPath, JSON.stringify({ readOnly: true, writeAllowlist: [], engineering }));
      await assert.rejects(inspectState(root), /saved|backend|configuredProjectFiles/i);
    }
    await writeFile(configPath, JSON.stringify({ readOnly: true, writeAllowlist: [], engineering: { enabled: true, backend: 'configuredProjectFiles', projectFiles: [] } }));
    assert.equal((await inspectState(root)).configurationVerified, true);
  } finally { await rm(parent, { recursive: true, force: true }); }
});

test('initialization writes an isolated upstream global policy without overwriting existing state', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-state-'));
  try {
    const root = join(parent, 'runtime');
    const result = await initializeState(root, {});
    assert.ok(result?.configPath, 'an actual OpenCode global configuration must be created');
    const config = JSON.parse(await readFile(result.configPath, 'utf8'));
    assert.equal(config.default_agent, 'twincat-readonly');
    const before = await readFile(result.configPath);
    await assert.rejects(initializeState(root, {}), /exists/);
    assert.deepEqual(await readFile(result.configPath), before);
  } finally { await rm(parent, { recursive: true, force: true }); }
});
