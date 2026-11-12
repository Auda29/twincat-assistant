import { withPackageVerificationScope } from './package-verification-scope.mjs';
import { createServer } from 'node:net';
import { lstat } from 'node:fs/promises';
import { isAbsolute, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { initializeState, inspectState } from './state.mjs';
import { prepareRuntimeLaunch } from './runtime-start.mjs';
import { supervise } from './supervisor.mjs';
import { configureNativeMcp } from './native-mcp.mjs';
import { applicationRoot, verifyPackageIfPresent } from './package-integrity.mjs';
import { createEditorNativeControl, parseEditorControl, readBoundedControlLines } from './editor-native-control.mjs';

export function validateNativeChatRequest(value) {
  if (!value || Object.keys(value).some(key => !['state', 'port', 'password', 'mcpConfig'].includes(key))
      || typeof value.state !== 'string' || value.state.length > 2048 || value.state.includes('\0') || !isAbsolute(value.state)
      || !Number.isInteger(value.port) || value.port < 1024 || value.port > 65535
      || typeof value.password !== 'string' || !value.password.trim() || value.password.length < 12 || value.password.length > 1024
      || value.password.includes('\0')
      || (value.mcpConfig !== undefined && (typeof value.mcpConfig !== 'string' || value.mcpConfig.length > 2048
        || value.mcpConfig.includes('\0') || !isAbsolute(value.mcpConfig)))) throw new Error('Invalid native chat request');
  return value;
}

// .NET Framework's redirected StandardInput writes a UTF-8 preamble when the
// owner's console input code page is UTF-8 (65001, including Windows' system-wide
// UTF-8 option). Accept exactly one leading BOM; the JSON itself stays strict.
export function parseNativeChatRequest(line) {
  return validateNativeChatRequest(JSON.parse(typeof line === 'string' && line.startsWith('﻿') ? line.slice(1) : line));
}

export async function isProtectedChatReady(origin, signal) {
  try {
    const response = await fetch(new URL('/auth/session', origin), {
      redirect: 'error', signal: signal ?? AbortSignal.timeout(1500),
    });
    if (response.status !== 401) { await response.body?.cancel(); return false; }
    const reader = response.body.getReader();
    let size = 0;
    const chunks = [];
    try {
      while (true) {
        const { value, done } = await reader.read();
        if (done) break;
        size += value.length;
        if (size > 4096) return false;
        chunks.push(value);
      }
    } finally { await reader.cancel(); }
    const value = JSON.parse(Buffer.concat(chunks).toString('utf8'));
    return value.authenticated === false && value.locked === true;
  } catch { return false; }
}

async function assertFreePort(port) {
  const server = createServer();
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen({ host: '127.0.0.1', port, exclusive: true }, resolve);
  });
  await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
}

export async function startNativeChat(request, signal, report, options = {}) {
  let packaged = false;
  const plan = await withPackageVerificationScope(async () => {
    packaged = await verifyPackageIfPresent();
    const { state, port } = validateNativeChatRequest(request);
    await assertFreePort(port); // refuse an occupied port rather than adopting another service
    try { await lstat(state); }
    catch (error) {
      if (error.code !== 'ENOENT') throw error;
      await initializeState(state, {}); // first-run profile has no provider, MCP or source disclosure
    }
    if (request.mcpConfig) await configureNativeMcp(state, request.mcpConfig);
    else if ((await inspectState(state)).settings.engineering)
      throw new Error('Native project access requires project-scoped MCP setup, not a global engineering backend');
    return prepareRuntimeLaunch(state, {
      runtime: options.runtime, port,
    }, { ...process.env, OPENCHAMBER_UI_PASSWORD: request.password });
  });
  const { state, port } = request;
  const owner = new AbortController();
  const stop = () => owner.abort();
  signal.addEventListener('abort', stop, { once: true });
  if (signal.aborted) stop();
  const completion = supervise(plan, state, owner.signal);
  // Observe a fast failed start immediately, including native lock/helper failures.
  let finished = false;
  completion.then(() => { finished = true; }, () => { finished = true; });
  try {
    const deadline = Date.now() + (options.timeoutMs ?? 65000);
    const origin = `http://127.0.0.1:${port}`;
    let ready = false;
    while (!signal.aborted && !finished && Date.now() < deadline) {
      if (await isProtectedChatReady(origin, AbortSignal.any([owner.signal, AbortSignal.timeout(1500)]))) {
        ready = true;
        break;
      }
      await new Promise(resolve => setTimeout(resolve, 150));
    }
    if (!ready || signal.aborted || finished) throw new Error('Protected chat startup failed');
    options.onReady?.({ packaged });
    report({ status: 'ready', port }); // no credential or upstream output on this control channel
    return await completion;
  } finally {
    owner.abort();
    signal.removeEventListener('abort', stop);
    await completion.catch(() => {});
  }
}

export function runNativeChatControl({ input, report, onFailure, startChat = startNativeChat }) {
  const controller = new AbortController();
  let received = false, editor;
  const stopInput = readBoundedControlLines(input, line => {
    if (!received) {
      received = true;
      let request;
      try { request = parseNativeChatRequest(line); }
      catch (error) { report({ status: 'failed' }); throw error; }
      void (async () => {
        try {
          await startChat(request, controller.signal, report, { onReady: ({ packaged }) => {
            editor = createEditorNativeControl({ origin: `http://127.0.0.1:${request.port}`, password: request.password,
              node: process.execPath, packaged, applicationRoot, signal: controller.signal, report });
          } });
        } catch {
          report({ status: 'failed' });
          onFailure();
        } finally {
          stopInput();
          input.destroy();
          if (editor) await editor.close();
        }
      })();
      return;
    }
    if (!editor) throw new Error('Editor control before readiness');
    editor.enqueue(parseEditorControl(line));
  }, error => {
    if (error || !received) onFailure();
    controller.abort();
    if (error) input.destroy();
  });
  // Never leave an unconfigured subprocess waiting forever for its owner.
  const timeout = setTimeout(() => { if (!received) { onFailure(); stopInput(); input.destroy(); } }, 10000);
  timeout.unref();
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runNativeChatControl({ input: process.stdin, report: value => process.stdout.write(JSON.stringify(value) + '\n'),
    onFailure: () => { process.exitCode = 1; } });
}
