import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { isAbsolute } from 'node:path';

const capture = promisify(execFile);
function helperEnvironment() {
  return Object.fromEntries(['SystemRoot', 'WINDIR', 'PATH', 'Path', 'TEMP', 'TMP', 'DOTNET_ROOT']
    .filter(key => process.env[key] !== undefined).map(key => [key, process.env[key]]));
}
export function windowsHelperPath() {
  const path = process.env.TC_ASSISTANT_WINDOWS_HELPER
    ?? fileURLToPath(new URL('../native/WindowsHost/bin/Release/net8.0-windows/WindowsHost.exe', import.meta.url));
  if (!isAbsolute(path)) throw new Error('Windows helper must be an absolute operator-selected path');
  return path;
}

export async function readWindowsJson(path) {
  try {
    const { stdout } = await capture(windowsHelperPath(), ['read-json', path],
      { encoding: 'buffer', maxBuffer: 65536, timeout: 10000, windowsHide: true, shell: false, env: helperEnvironment() });
    return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(stdout));
  } catch { throw new Error('Expected a bounded regular JSON file; native Windows validation failed'); }
}

export async function secureWindowsDirectory(path) {
  try { await capture(windowsHelperPath(), ['secure-directory', path], { timeout: 10000, windowsHide: true, shell: false, env: helperEnvironment() }); }
  catch { throw new Error('Unable to restrict Windows state directory access'); }
}

export async function superviseWindows(plan, signal, onStarted) {
  if (signal?.aborted) return { cancelled: true };
  const helper = spawn(windowsHelperPath(), ['supervise'], {
    shell: false, windowsHide: true, stdio: ['pipe', 'pipe', 'inherit'], env: helperEnvironment(),
  });
  let stopped = false;
  let killTimer;
  let ready = false;
  let buffer = '';
  const stop = () => {
    if (stopped) return;
    stopped = true;
    if (!helper.stdin.destroyed) helper.stdin.end('stop\n');
    killTimer = setTimeout(() => helper.kill(), 5000);
    killTimer.unref();
  };
  helper.stdin.on('error', () => stop());
  const completed = new Promise((resolve, reject) => {
    const rejectProtocol = () => { stop(); reject(new Error('Native Windows supervisor returned invalid readiness')); };
    helper.once('error', () => reject(new Error('Native Windows supervisor could not start; build the helper first')));
    helper.stdout.on('data', bytes => {
      buffer += bytes.toString('utf8');
      if (buffer.length > 1024) { rejectProtocol(); return; }
      const newline = buffer.indexOf('\n');
      if (newline < 0) return;
      try {
        const message = JSON.parse(buffer.slice(0, newline));
        if (ready || message.started !== true || !Number.isInteger(message.pid) || message.pid <= 0) throw new Error();
        ready = true;
        buffer = '';
        onStarted({ pid: message.pid, supervisorPid: helper.pid });
      } catch { rejectProtocol(); }
    });
    helper.once('close', (code, childSignal) => {
      if (!ready && !signal?.aborted) reject(new Error('Native Windows supervisor failed before starting the owned child'));
      else resolve({ code, signal: childSignal, cancelled: signal?.aborted ?? false });
    });
  });
  signal?.addEventListener('abort', stop, { once: true });
  try {
    helper.stdin.write(JSON.stringify(plan) + '\n');
    if (signal?.aborted) stop();
    return await completed;
  } finally {
    signal?.removeEventListener('abort', stop);
    clearTimeout(killTimer);
    helper.stdin.destroy();
    if (helper.exitCode === null) helper.kill(); // closing the native job handle kills every owned descendant
  }
}
