import { open, rename, unlink } from 'node:fs/promises';
import { resolve, dirname, join, isAbsolute } from 'node:path';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { inspectState } from './state.mjs';
import { createOpenCodeConfig, validateSettings } from './config.mjs';

export function validateModelConsent(value) {
  if (!value || Object.keys(value).some(key => !['state', 'model', 'acceptSourceDisclosure'].includes(key))
      || typeof value.state !== 'string' || !isAbsolute(value.state) || value.state.length > 2048 || value.state.includes('\0')
      || typeof value.model !== 'string' || value.model.length > 240 || !/^[a-zA-Z0-9_-]+\/[^\s\0]+$/.test(value.model)
      || value.acceptSourceDisclosure !== true) throw new Error('Explicit model and source disclosure consent required');
  return value;
}

async function atomicJson(path, value) {
  const temporary = join(dirname(path), '.model-' + randomUUID() + '.tmp');
  try {
    const file = await open(temporary, 'wx', 0o600);
    try { await file.writeFile(JSON.stringify(value, null, 2) + '\n'); await file.sync(); } finally { await file.close(); }
    await rename(temporary, path);
  } finally { await unlink(temporary).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
}

export async function configureNativeModel(input) {
  const request = validateModelConsent(input);
  const status = await inspectState(request.state); // do not repair or adopt modified policy
  const lockPath = join(status.paths.root, 'model-consent.lock');
  const lock = await open(lockPath, 'wx', 0o600);
  try {
    // Recheck under the exclusive writer lock. A crash between the two atomic
    // replacements leaves a policy mismatch, which the launcher rejects.
    const current = await inspectState(request.state);
    const settings = validateSettings({ ...current.settings, model: request.model, sourceCodeDisclosureAccepted: true });
    await atomicJson(current.paths.configPath, createOpenCodeConfig(settings));
    await atomicJson(current.paths.settingsPath, settings);
    const verified = await inspectState(request.state);
    if (!verified.modelCallsEnabled) throw new Error('Model policy not verified');
    return { configured: true, model: settings.model };
  } finally { await lock.close(); await unlink(lockPath); }
}

async function main() {
  const chunks = [];
  let size = 0;
  for await (const chunk of process.stdin) {
    size += chunk.length;
    if (size > 8192) throw new Error('Input too large');
    chunks.push(chunk);
  }
  const input = new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks));
  await configureNativeModel(JSON.parse(input));
  process.stdout.write('{"configured":true}\n');
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { await main(); } catch { process.stdout.write('{"configured":false}\n'); process.exitCode = 1; }
}
