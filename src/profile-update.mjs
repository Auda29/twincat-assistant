import { open, rename, unlink } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { inspectState } from './state.mjs';
import { createOpenCodeConfig, validateSettings } from './config.mjs';

export async function atomicJson(path, value) {
  const temporary = join(dirname(path), '.profile-' + randomUUID() + '.tmp');
  try {
    const file = await open(temporary, 'wx', 0o600);
    try { await file.writeFile(JSON.stringify(value, null, 2) + '\n'); await file.sync(); }
    finally { await file.close(); }
    await rename(temporary, path);
  } finally { await unlink(temporary).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
}

// Share the existing consent lock with MCP setup. Unknown policy changes and
// interrupted two-file updates still fail inspection; they are never repaired.
export async function updateProfile(root, transform) {
  const initial = await inspectState(root);
  const lockPath = join(initial.paths.root, 'model-consent.lock');
  const lock = await open(lockPath, 'wx', 0o600);
  try {
    const current = await inspectState(root);
    const settings = validateSettings(transform(current.settings));
    if (JSON.stringify(settings) !== JSON.stringify(current.settings)) {
      await atomicJson(current.paths.configPath, createOpenCodeConfig(settings));
      await atomicJson(current.paths.settingsPath, settings);
    }
    return await inspectState(root);
  } finally { await lock.close(); await unlink(lockPath); }
}
