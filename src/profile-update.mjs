import { open, rename, unlink, lstat } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { randomUUID } from 'node:crypto';
import { inspectState, readJsonFile, statePaths, verifyProfileConfiguration } from './state.mjs';
import { createOpenCodeConfig, validateSettings } from './config.mjs';
import { rebasePackageSettings } from './managed-relocation.mjs';

export async function atomicJson(path, value) {
  const temporary = join(dirname(path), '.profile-' + randomUUID() + '.tmp');
  try {
    const file = await open(temporary, 'wx', 0o600);
    try { await file.writeFile(JSON.stringify(value, null, 2) + '\n'); await file.sync(); }
    finally { await file.close(); }
    await rename(temporary, path);
  } finally { await unlink(temporary).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
}

// Upgrade an exact earlier managed configuration before launch. Current
// configurations are left untouched and take no lock.
export async function upgradeProfileConfiguration(root) {
  if ((await inspectState(root)).configurationCurrent) return false;
  await updateProfile(root, settings => settings);
  return true;
}

const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const journalPath = root => join(statePaths(root).root, '.profile-update.json');
async function pending(root) {
  try { await lstat(journalPath(root)); return true; }
  catch (error) { if (error.code === 'ENOENT') return false; throw error; }
}
async function recoverLocked(root) {
  if (!(await pending(root))) return false;
  const paths = statePaths(root);
  for (const directory of [paths.root, dirname(paths.configPath), paths.home, paths.data, paths.cache, paths.state, paths.openchamber, paths.workspace]) {
    const metadata = await lstat(directory);
    if (!metadata.isDirectory() || metadata.isSymbolicLink()) throw new Error('State directory must not be replaced by a symlink');
  }
  const journal = await readJsonFile(journalPath(root));
  if (journal?.version !== 1 || Object.keys(journal).sort().join(',') !== 'after,before,version') throw new Error('Invalid profile update journal');
  for (const pair of [journal.before, journal.after]) {
    if (!pair || Object.keys(pair).sort().join(',') !== 'config,settings') throw new Error('Invalid profile update journal');
    validateSettings(pair.settings);
    await verifyProfileConfiguration(pair.config, pair.settings);
  }
  const settings = await readJsonFile(paths.settingsPath);
  const config = await readJsonFile(paths.configPath);
  if (![journal.before.settings, journal.after.settings].some(value => same(value, settings))
      || ![journal.before.config, journal.after.config].some(value => same(value, config))) throw new Error('Profile changed during interrupted update');
  // Complete only the fully validated recorded update, never synthesize policy.
  await atomicJson(paths.configPath, journal.after.config);
  await atomicJson(paths.settingsPath, journal.after.settings);
  await unlink(journalPath(root));
  return true;
}
export async function recoverProfile(root) {
  if (!(await pending(root))) return false;
  const path = join(statePaths(root).root, 'model-consent.lock');
  const lock = await open(path, 'wx', 0o600);
  try { return await recoverLocked(root); }
  finally { await lock.close(); await unlink(path); }
}

// All profile writers share this lock. A stale lock is never guessed safe to
// remove: after a killed writer the operator must establish process quiescence.
export async function updateProfile(root, transform) {
  const paths = statePaths(root);
  const lockPath = join(paths.root, 'model-consent.lock');
  const lock = await open(lockPath, 'wx', 0o600);
  try {
    await recoverLocked(root);
    const current = await inspectState(root, { recovery: false });
    const settings = validateSettings(rebasePackageSettings(transform(current.settings)));
    const prior = await readJsonFile(paths.configPath);
    const next = createOpenCodeConfig(settings);
    if (!same(settings, current.settings) || !current.configurationCurrent) {
      await atomicJson(journalPath(root), { version: 1,
        before: { settings: current.settings, config: prior }, after: { settings, config: next } });
      await atomicJson(paths.configPath, next);
      await atomicJson(paths.settingsPath, settings);
      await unlink(journalPath(root));
    }
    return await inspectState(root, { recovery: false });
  } finally { await lock.close(); await unlink(lockPath); }
}
