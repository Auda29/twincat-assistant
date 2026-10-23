import { spawn } from 'node:child_process';
import { open, unlink } from 'node:fs/promises';
import { join } from 'node:path';
import { superviseWindows } from './windows-native.mjs';

function groupSignal(pid, signal) {
  if (!pid) return;
  try { process.kill(-pid, signal); }
  catch (error) { if (error.code !== 'ESRCH') throw error; }
}
export async function supervise(plan, root, signal, onStarted = () => {}) {
  if (signal?.aborted) return { cancelled: true };
  const lockPath = join(root, 'launcher.lock');
  let lock;
  try { lock = await open(lockPath, 'wx', 0o600); }
  catch { throw new Error('Launcher lock already exists; do not start a duplicate instance'); }
  let child;
  let timer;
  const stop = () => {
    groupSignal(child?.pid, 'SIGTERM');
    timer ??= setTimeout(() => groupSignal(child?.pid, 'SIGKILL'), 2000);
    timer.unref();
  };
  try {
    await lock.writeFile(JSON.stringify({ supervisorPid: process.pid }) + '\n');
    await lock.sync();
    if (signal?.aborted) return { cancelled: true };
    if (process.platform === 'win32') return await superviseWindows(plan, signal, onStarted);
    child = spawn(plan.command, plan.args, { cwd: plan.cwd, env: plan.env, detached: true, shell: false, stdio: ['ignore', 'inherit', 'inherit'] });
    const completed = new Promise((resolve, reject) => {
      child.once('error', () => reject(new Error('Upstream process could not start')));
      child.once('close', (code, childSignal) => resolve({ code, signal: childSignal, cancelled: signal?.aborted ?? false }));
    });
    signal?.addEventListener('abort', stop, { once: true });
    if (signal?.aborted) stop();
    onStarted(child);
    return await completed;
  } finally {
    signal?.removeEventListener('abort', stop);
    clearTimeout(timer);
    // Clean up only this start's owned process group, including helper descendants.
    groupSignal(child?.pid, 'SIGTERM');
    groupSignal(child?.pid, 'SIGKILL');
    await lock.close();
    await unlink(lockPath);
  }
}
