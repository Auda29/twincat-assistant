import assert from 'node:assert/strict';
import test from 'node:test';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdtemp, readFile, rm, writeFile, lstat } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { initializeState } from '../src/state.mjs';
import { runtimePaths } from '../src/runtime-paths.mjs';
import { prepareRuntimeLaunch } from '../src/runtime-start.mjs';

const capture = promisify(execFile);
const password = 'synthetic-runtime-password';

test('common preparation installs themes and commands before version execution and preserves existing operator data', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-start-order-'));
  try {
    const root = join(parent, 'state');
    const paths = await initializeState(root, {});
    const preferences = { lightThemeId: 'operator-theme', projects: [{ id: 'kept', path: '/synthetic/project' }], activeProjectId: 'kept' };
    await writeFile(join(paths.openchamber, 'settings.json'), JSON.stringify(preferences));
    const history = join(paths.data, 'existing-chat-sentinel');
    await writeFile(history, 'existing conversation data');
    const chamber = join(parent, 'version-and-start.mjs');
    await writeFile(chamber, `
      import assert from 'node:assert/strict';
      import { readFileSync, existsSync, writeFileSync } from 'node:fs';
      import { join, dirname } from 'node:path';
      const root = dirname(process.cwd());
      if (!process.argv.includes('--version')) { writeFileSync(join(root, 'unexpected-serve'), 'started'); process.exit(9); }
      for (const name of ['twincat-light', 'twincat-dark']) assert.ok(existsSync(join(root, 'openchamber/themes', name + '.json')));
      for (const name of ['explain', 'review', 'variable', 'docs', 'pou-docs']) assert.ok(existsSync(join(root, 'config/commands', name + '.md')));
      const preferences = JSON.parse(readFileSync(join(root, 'openchamber/settings.json'), 'utf8'));
      assert.equal(preferences.lightThemeId, 'operator-theme');
      assert.equal(preferences.darkThemeId, 'twincat-dark');
      for (const key of ['OPENCHAMBER_UI_PASSWORD', 'OPENAI_API_KEY', 'GITHUB_TOKEN']) assert.equal(process.env[key], undefined);
      writeFileSync(join(root, 'version-checked'), 'profile ready before execution');
      console.log('OpenChamber v2.1.1');
    `);
    const env = { ...process.env, OPENCHAMBER_UI_PASSWORD: password, OPENAI_API_KEY: 'must-not-reach-probe', GITHUB_TOKEN: 'must-not-reach-probe' };
    // A partial override still selects the other artifact from the real pinned distribution.
    const plan = await prepareRuntimeLaunch(root, { runtime: 'distribution', openchamber: chamber, port: 3210, resolveOperatorPaths: true }, env);
    assert.equal(plan.env.OPENCODE_BINARY, (await runtimePaths('distribution')).opencode);
    assert.deepEqual(plan.args, [chamber, 'serve', '--foreground', '--host', '127.0.0.1', '--port', '3210']);
    assert.equal(plan.env.OPENCHAMBER_UI_PASSWORD, password);
    assert.equal(plan.cwd, paths.workspace);
    assert.equal(await readFile(join(root, 'version-checked'), 'utf8'), 'profile ready before execution');
    assert.deepEqual(JSON.parse(await readFile(join(paths.openchamber, 'settings.json'), 'utf8')), { ...preferences, darkThemeId: 'twincat-dark' });
    assert.equal(await readFile(history, 'utf8'), 'existing conversation data');
    await assert.rejects(lstat(join(root, 'launcher.lock')), { code: 'ENOENT' });
    await assert.rejects(lstat(join(root, 'unexpected-serve')), { code: 'ENOENT' });
  } finally { await rm(parent, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); }
});

test('a failed Core version gate does not execute Chat or acquire runtime ownership', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-start-refuse-'));
  try {
    const root = join(parent, 'state');
    await initializeState(root, {});
    const chamber = join(parent, 'must-not-run.mjs');
    const marker = join(parent, 'executed');
    await writeFile(chamber, `import { writeFileSync } from 'node:fs'; writeFileSync(${JSON.stringify(marker)}, 'executed'); console.log('2.1.1');`);
    // Explicit operator paths bypass runtime selection exactly as before. Node's
    // actual version is deliberately incompatible with the pinned Core version.
    await assert.rejects(prepareRuntimeLaunch(root, { openchamber: chamber, opencode: process.execPath, runtime: 'not-selected', port: 3210 },
      { ...process.env, OPENCHAMBER_UI_PASSWORD: password }), { message: 'Upstream version verification failed' });
    await assert.rejects(lstat(marker), { code: 'ENOENT' });
    await assert.rejects(lstat(join(root, 'launcher.lock')), { code: 'ENOENT' });
  } finally { await rm(parent, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); }
});

test('operator entry refuses a mismatched Chat version before serving and keeps its fixed error boundary', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-start-cli-refuse-'));
  try {
    const root = join(parent, 'state');
    await initializeState(root, {});
    const chamber = join(parent, 'wrong-version.mjs');
    const marker = join(parent, 'served');
    await writeFile(chamber, `import { writeFileSync } from 'node:fs'; if (process.argv.includes('--version')) console.log('9.9.9'); else writeFileSync(${JSON.stringify(marker)}, 'served');`);
    const runtime = await runtimePaths('distribution');
    await assert.rejects(capture(process.execPath, [resolve('src/cli.mjs'), 'start', '--state', root, '--opencode', runtime.opencode, '--openchamber', chamber], {
      windowsHide: true, cwd: parent, timeout: 30000,
      env: { ...process.env, OPENCHAMBER_UI_PASSWORD: password, TWINCAT_ASSISTANT_RUNTIME: 'ignored-for-two-operator-paths' },
    }), error => error.code === 1 && error.stdout === '' && error.stderr === 'Assistant command failed. Check command options, regular settings files, state integrity and documented prerequisites.\n');
    await assert.rejects(lstat(marker), { code: 'ENOENT' });
    await assert.rejects(lstat(join(root, 'launcher.lock')), { code: 'ENOENT' });
  } finally { await rm(parent, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); }
});
