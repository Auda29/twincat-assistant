import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, rm, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { supervise } from '../src/supervisor.mjs';

test('supervision rejects duplicate starts and reaps a real owned child on cancellation', async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-supervisor-'));
  const controller = new AbortController();
  let pid;
  try {
    const running = supervise({ command: process.execPath, args: ['-e', 'setInterval(()=>{},1000)'], cwd: root,
      env: process.platform === 'win32' ? { SystemRoot: process.env.SystemRoot ?? 'C:\\Windows' } : {} }, root, controller.signal, child => { pid = child.pid; });
    for (let i = 0; i < 100 && !pid; i++) await new Promise(resolve => setTimeout(resolve, 10));
    assert.ok(pid, 'a real upstream-owned child must be started');
    await assert.rejects(supervise({ command: process.execPath, args: [], cwd: root, env: {} }, root, controller.signal), /already|lock/);
    controller.abort();
    await running;
    assert.throws(() => process.kill(pid, 0), /ESRCH/);
    await assert.rejects(readFile(join(root, 'launcher.lock')));
  } finally { controller.abort(); await rm(root, { recursive: true, force: true }); }
});
