import { readdir, lstat, open, unlink } from 'node:fs/promises';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { readJsonFile } from './state.mjs';
import { atomicJson } from './profile-update.mjs';

export function projectKey(solution, project) {
  return createHash('sha256').update(JSON.stringify({ version: 2, solution, project })).digest('hex');
}

async function optionalJson(file) {
  const stat = await lstat(file).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
  if (!stat) return null;
  const value = await readJsonFile(file);
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Project metadata modified');
  return value;
}

// Keep the original directory when adopting a 0.1.9 project. OpenCode owns the
// database and session IDs; adopting a directory never copies or rewrites chats.
export async function findProjectWorkspace(parent, identity) {
  const key = projectKey(identity.solution, identity.project);
  const index = join(parent, key + '.json');
  const value = await optionalJson(index);
  if (value && (value.version !== 2 || value.solution !== identity.solution || value.project !== identity.project
    || !/^[a-f0-9]{64}$/.test(value.directory))) throw new Error('Project index modified');
  const candidates = [];
  for (const entry of await readdir(parent, { withFileTypes: true })) {
    if (!entry.isDirectory() || !/^[a-f0-9]{64}$/.test(entry.name)) continue;
    const directory = join(parent, entry.name);
    const receipt = join(directory, 'project.json');
    try {
      const value = await optionalJson(receipt);
      if (value && value.solution === identity.solution && value.project === identity.project) {
        if (![1, 2].includes(value.version)) throw new Error('Project identity modified');
        candidates.push({ directory: entry.name, updated: (await lstat(receipt)).mtimeMs });
      }
    } catch (error) {
      // An interrupted legacy setup for another PLC is not this project's
      // authority. The indexed/current directory still fails closed.
      if (entry.name === value?.directory || entry.name === key) throw error;
    }
  }
  candidates.sort((a, b) => b.updated - a.updated || a.directory.localeCompare(b.directory));
  const directory = value?.directory ?? candidates[0]?.directory ?? key;
  const workspaces = [...new Set([directory, ...candidates.map(item => item.directory)])].map(item => join(parent, item));
  if (workspaces.length > 64) throw new Error('Too many historical project configurations');
  return { workspace: join(parent, directory), workspaces, key, index };
}

export async function withProjectLock(parent, key, action) {
  const file = join(parent, key + '.lock');
  const lock = await open(file, 'wx', 0o600);
  try { return await action(); }
  finally { await lock.close(); await unlink(file); }
}

// A tiny journal covers the two managed JSON files. After an interrupted write,
// replay only the recorded, validated configuration for this exact PLC project.
// `previous` lists exact configurations of earlier releases for the same
// settings; such a file is upgraded like a settings change, never adopted blindly.
export async function updateProjectConfiguration(workspace, initial, settings, configuration, previous = () => []) {
  const receiptFile = join(workspace, 'project.json');
  const configFile = join(workspace, 'opencode.json');
  const journalFile = join(workspace, '.configuration.json');
  let receipt = await optionalJson(receiptFile);
  if (!receipt) {
    // A new project has no conversations yet. Persist identity before config so
    // a retry after an interrupted first setup still targets the same directory.
    receipt = { version: 2, solution: initial.solution, project: initial.project, settings };
    await atomicJson(receiptFile, receipt);
  }
  if (![1, 2].includes(receipt.version) || receipt.solution !== initial.solution || receipt.project !== initial.project)
    throw new Error('Project workspace modified');
  let actual = await optionalJson(configFile);
  const journal = await optionalJson(journalFile);
  const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
  const managed = (value, selected) => same(value, configuration(selected)) || previous(selected).some(item => same(value, item));
  if (journal) {
    if (journal.version !== 1 || journal.solution !== initial.solution || journal.project !== initial.project)
      throw new Error('Project configuration journal modified');
    const after = configuration(journal.after);
    if ((!same(receipt.settings, journal.before) && !same(receipt.settings, journal.after))
      || (!managed(actual, journal.before) && !managed(actual, journal.after))) throw new Error('Project configuration modified');
    await atomicJson(configFile, after);
    receipt = { ...receipt, version: 2, settings: journal.after };
    await atomicJson(receiptFile, receipt);
    await unlink(journalFile);
    actual = after;
  } else if (actual !== null && !managed(actual, receipt.settings)) throw new Error('Native MCP workspace modified');
  const next = configuration(settings);
  if (same(actual, next) && same(receipt.settings, settings)) {
    if (receipt.version !== 2) await atomicJson(receiptFile, { ...receipt, version: 2 });
    return;
  }
  if (actual === null) {
    await atomicJson(configFile, next);
    await atomicJson(receiptFile, { ...receipt, version: 2, settings });
    return;
  }
  await atomicJson(journalFile, { version: 1, solution: initial.solution, project: initial.project, before: receipt.settings, after: settings });
  await atomicJson(configFile, next);
  await atomicJson(receiptFile, { ...receipt, version: 2, settings });
  await unlink(journalFile);
}
