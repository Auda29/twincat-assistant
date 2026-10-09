import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { supervise } from '../src/supervisor.mjs';
import { spawn, execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { initializeState } from '../src/state.mjs';
import { windowsHelperPath } from '../src/windows-native.mjs';
import { once } from 'node:events';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';

async function waitForFile(path) {
  const deadline = Date.now() + 5000;
  while (Date.now() < deadline) {
    try { return JSON.parse(await readFile(path, 'utf8')); } catch { }
    await new Promise(resolve => setTimeout(resolve, 25));
  }
  throw new Error('Owned process fixture did not become ready');
}
async function assertGone(pid) {
  const deadline = Date.now() + 5000;
  while (Date.now() < deadline) {
    try { process.kill(pid, 0); } catch (error) { if (error.code === 'ESRCH') return; throw error; }
    await new Promise(resolve => setTimeout(resolve, 25));
  }
  assert.fail(`Owned process ${pid} survived job closure`);
}

for (const mode of ['cancel', 'root-exit', 'helper-crash']) {
  test(`Windows Job Object reaps descendants after ${mode}`, { skip: process.platform !== 'win32', timeout: 15000 }, async () => {
    const root = await mkdtemp(join(tmpdir(), 'tc-job-'));
    const controller = new AbortController();
    let running;
    let owned;
    try {
      const file = join(root, 'child.json');
      const script = join(root, 'root script.mjs');
      const descendant = `require('fs').writeFileSync(${JSON.stringify(file)},JSON.stringify({pid:process.pid}));setInterval(()=>{},1000)`;
      await writeFile(script, `import {spawn} from 'node:child_process';
        const child=spawn(process.execPath,['-e',${JSON.stringify(descendant)}],{stdio:'ignore'});
        child.unref();
        ${mode === 'root-exit' ? 'setTimeout(()=>process.exit(0),500)' : 'setInterval(()=>{},1000)'};`);
      running = supervise({ command: process.execPath, args: [script], cwd: root, env: { SystemRoot: process.env.SystemRoot ?? 'C:\\Windows' } },
        root, controller.signal, child => { owned = child; });
      const { pid } = await waitForFile(file);
      assert.ok(owned?.pid);
      if (mode === 'cancel') controller.abort();
      if (mode === 'helper-crash') process.kill(owned.supervisorPid);
      await running;
      await assertGone(owned.pid);
      await assertGone(pid);
      await assert.rejects(readFile(join(root, 'launcher.lock')));
    } finally {
      controller.abort();
      await running?.catch(() => {});
      await rm(root, { recursive: true, force: true });
    }
  });
}

test('Windows quoting preserves literal arguments without invoking a shell', { skip: process.platform !== 'win32', timeout: 10000 }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-job-args-'));
  try {
    const output = join(root, 'result.json');
    const script = join(root, 'script with spaces.mjs');
    const args = ['', 'white space', 'literal"quote', 'trailing\\', 'a\\"b', '&not-a-command'];
    await writeFile(script, `import {writeFileSync} from 'node:fs';writeFileSync(${JSON.stringify(output)},JSON.stringify(process.argv.slice(2)));`);
    const result = await supervise({ command: process.execPath, args: [script, ...args], cwd: root,
      env: { SystemRoot: process.env.SystemRoot ?? 'C:\\Windows' } }, root);
    assert.equal(result.code, 0);
    assert.deepEqual(JSON.parse(await readFile(output, 'utf8')), args);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('Windows helper revokes the whole job on Node owner death without killing unrelated processes', { skip: process.platform !== 'win32', timeout: 15000 }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-owner-death-'));
  const sentinel = spawn(process.execPath, ['-e', 'setInterval(()=>{},1000)'], { stdio: 'ignore' });
  let owner;
  let owned;
  try {
    const marker = join(root, 'owned.json');
    const grandchildMarker = join(root, 'grandchild.json');
    const childScript = join(root, 'child.mjs');
    await writeFile(childScript, `import {spawn} from 'node:child_process';
      const code=${JSON.stringify(`require('fs').writeFileSync(${JSON.stringify(grandchildMarker)},JSON.stringify({pid:process.pid}));setInterval(()=>{},1000)`)};
      spawn(process.execPath,['-e',code],{stdio:'ignore'});setInterval(()=>{},1000);`);
    const ownerScript = join(root, 'owner.mjs');
    await writeFile(ownerScript, `import {supervise} from ${JSON.stringify(pathToFileURL(resolve('src/supervisor.mjs')).href)};
      import {writeFileSync} from 'node:fs';
      await supervise({command:process.execPath,args:[${JSON.stringify(childScript)}],cwd:${JSON.stringify(root)},env:{SystemRoot:process.env.SystemRoot}},
        ${JSON.stringify(root)},undefined,owned=>writeFileSync(${JSON.stringify(marker)},JSON.stringify(owned)));`);
    owner = spawn(process.execPath, [ownerScript], { stdio: 'ignore' });
    owned = await waitForFile(marker);
    const descendant = await waitForFile(grandchildMarker);
    const exited = once(owner, 'exit');
    owner.kill();
    await exited;
    await assertGone(owned.pid);
    await assertGone(descendant.pid);
    await assertGone(owned.supervisorPid);
    assert.doesNotThrow(() => process.kill(sentinel.pid, 0));
    // Crashed owner cannot remove its lock; never auto-recover it based only on PID.
    await assert.rejects(supervise({}, root), /lock/);
  } finally {
    if (owner?.exitCode === null && owner.signalCode === null) owner.kill();
    sentinel.kill();
    await rm(root, { recursive: true, force: true });
  }
});

test('Windows supervisor rejects missing executable and honors pre-start cancellation', { skip: process.platform !== 'win32' }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-job-reject-'));
  try {
    const controller = new AbortController(); controller.abort();
    assert.deepEqual(await supervise({}, root, controller.signal), { cancelled: true });
    await assert.rejects(readFile(join(root, 'launcher.lock')));
    await assert.rejects(supervise({ command: join(root, 'missing.exe'), args: [], cwd: root, env: {} }, root), /failed before/);
    await assert.rejects(readFile(join(root, 'launcher.lock')));
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('Windows initialized state has a protected current-user DACL inherited by its files', { skip: process.platform !== 'win32', timeout: 15000 }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-acl-'));
  try {
    const paths = await initializeState(join(root, 'state'), {});
    const results = [];
    const env = Object.fromEntries(['SystemRoot', 'WINDIR', 'DOTNET_ROOT', 'TEMP', 'TMP']
      .filter(key => process.env[key] !== undefined).map(key => [key, process.env[key]]));
    for (const path of [paths.root, paths.settingsPath, paths.configPath]) {
      const { stdout } = await promisify(execFile)(windowsHelperPath(), ['inspect-acl', path], {
        env, windowsHide: true, timeout: 3000, maxBuffer: 4096, shell: false,
      });
      results.push(JSON.parse(stdout));
    }
    assert.equal(results.length, 3);
    assert.equal(results[0].protected, true);
    for (const result of results) {
      assert.equal(result.currentUserOnly, true);
      assert.equal(result.ownerIsCurrentUser, true);
      assert.equal(result.fullControl, true);
      assert.ok(result.rules > 0);
    }
    assert.ok(results[1].inherited > 0);
    assert.ok(results[2].inherited > 0);
    // Negative control: the Windows directory has other principals in its ACL.
    // Reading it must not be mistaken for current-user-only state; never alter it.
    const { stdout } = await promisify(execFile)(windowsHelperPath(), ['inspect-acl', process.env.SystemRoot], {
      env, windowsHide: true, timeout: 3000, maxBuffer: 4096, shell: false,
    });
    const systemAcl = JSON.parse(stdout);
    assert.ok(systemAcl.rules > 0);
    assert.equal(systemAcl.currentUserOnly, false);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('Windows owned child receives only the launch plan environment, not inherited credentials', { skip: process.platform !== 'win32' }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-job-env-'));
  const previous = process.env.TC_UNRELATED_CREDENTIAL;
  try {
    process.env.TC_UNRELATED_CREDENTIAL = 'not-for-the-owned-child';
    const output = join(root, 'environment.json');
    const script = join(root, 'environment.mjs');
    await writeFile(script, `import {writeFileSync} from 'node:fs';
      writeFileSync(${JSON.stringify(output)},JSON.stringify({selected:process.env.TC_SELECTED_CREDENTIAL,unrelated:process.env.TC_UNRELATED_CREDENTIAL,args:process.argv.slice(2)}));`);
    assert.equal((await supervise({ command: process.execPath, args: [script], cwd: root,
      env: { SystemRoot: process.env.SystemRoot, TC_SELECTED_CREDENTIAL: 'synthetic-secret-on-stdin' } }, root)).code, 0);
    const result = JSON.parse(await readFile(output, 'utf8'));
    assert.equal(result.selected, 'synthetic-secret-on-stdin');
    assert.equal(result.unrelated, undefined);
    assert.deepEqual(result.args, []);
  } finally {
    if (previous === undefined) delete process.env.TC_UNRELATED_CREDENTIAL;
    else process.env.TC_UNRELATED_CREDENTIAL = previous;
    await rm(root, { recursive: true, force: true });
  }
});
