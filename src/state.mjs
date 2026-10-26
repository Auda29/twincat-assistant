import { mkdir, open, readFile, lstat, rm } from 'node:fs/promises';
import { constants } from 'node:fs';
import { resolve, join, dirname } from 'node:path';
import { createOpenCodeConfig, validateSettings } from './config.mjs';
import { readWindowsJson, secureWindowsDirectory } from './windows-native.mjs';

export const MAX_SETTINGS_BYTES = 65536;
export async function readJsonFile(path) {
  if (process.platform === 'win32') return readWindowsJson(resolve(path));
  const metadata = await lstat(path);
  if (!metadata.isFile() || metadata.isSymbolicLink() || metadata.size > MAX_SETTINGS_BYTES) {
    throw new Error('Expected a bounded regular settings file');
  }
  if (!constants.O_NOFOLLOW) throw new Error('Secure JSON reads require native no-follow support; this platform is not enabled');
  const file = await open(path, constants.O_RDONLY | constants.O_NOFOLLOW | (constants.O_NONBLOCK ?? 0));
  try {
    const stat = await file.stat();
    if (!stat.isFile() || stat.size > MAX_SETTINGS_BYTES || stat.dev !== metadata.dev || stat.ino !== metadata.ino) throw new Error('Invalid settings file');
    const data = Buffer.alloc(MAX_SETTINGS_BYTES + 1);
    const { bytesRead } = await file.read(data, 0, data.length, 0);
    if (bytesRead > MAX_SETTINGS_BYTES) throw new Error('Settings file is too large');
    return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(data.subarray(0, bytesRead)));
  } finally { await file.close(); }
}
export function statePaths(root) {
  root = resolve(root);
  return { root, configPath: join(root, 'config', 'opencode.json'), settingsPath: join(root, 'settings.json'), home: join(root, 'home'), data: join(root, 'data'), cache: join(root, 'cache'), state: join(root, 'state'), openchamber: join(root, 'openchamber'), workspace: join(root, 'workspace') };
}
async function exclusiveJson(path, value) {
  const file = await open(path, 'wx', 0o600);
  try { await file.writeFile(JSON.stringify(value, null, 2) + '\n'); await file.sync(); }
  finally { await file.close(); }
}
export async function initializeState(root, input) {
  const settings = validateSettings(input);
  const config = createOpenCodeConfig(settings);
  const paths = statePaths(root);
  await mkdir(dirname(paths.root), { recursive: true, mode: 0o700 });
  // Do not adopt or overwrite an existing operator directory.
  await mkdir(paths.root, { mode: 0o700 });
  try {
    if (process.platform === 'win32') await secureWindowsDirectory(paths.root);
    for (const directory of [dirname(paths.configPath), paths.home, paths.data, paths.cache, paths.state, paths.openchamber, paths.workspace]) await mkdir(directory, { mode: 0o700 });
    await exclusiveJson(paths.settingsPath, settings);
    await exclusiveJson(paths.configPath, config);
    return paths;
  } catch (error) {
    await rm(paths.root, { recursive: true, force: true });
    throw error;
  }
}
export async function inspectState(root) {
  const paths = statePaths(root);
  const settings = validateSettings(await readJsonFile(paths.settingsPath));
  const actual = await readJsonFile(paths.configPath);
  if (JSON.stringify(actual) !== JSON.stringify(createOpenCodeConfig(settings))) {
    throw new Error('OpenCode configuration modified: policy mismatch; initialize a new state directory');
  }
  for (const directory of [paths.root, dirname(paths.configPath), paths.home, paths.data, paths.cache, paths.state, paths.openchamber, paths.workspace]) {
    const metadata = await lstat(directory);
    if (!metadata.isDirectory() || metadata.isSymbolicLink()) throw new Error('State directory must not be replaced by a symlink');
  }
  const files = [];
  for (const path of [settings.knowledge?.executable, settings.knowledge?.config, settings.engineering?.entrypoint, settings.engineering?.config].filter(Boolean)) {
    const metadata = await lstat(path);
    if (!metadata.isFile()) throw new Error('Configured MCP path is not a regular file');
    files.push({ path, exists: true });
  }
  if (settings.engineering) {
    const config = await readJsonFile(settings.engineering.config);
    if (config?.engineering?.enabled !== true || config.engineering.backend !== 'configuredProjectFiles') {
      throw new Error('Engineering MCP requires the explicitly enabled configuredProjectFiles saved-file backend');
    }
    if (config.readOnly !== true || (config.writeAllowlist !== undefined && (!Array.isArray(config.writeAllowlist) || config.writeAllowlist.length !== 0))) {
      throw new Error('Engineering MCP configuration must be explicitly readOnly with no writeAllowlist');
    }
  }
  return { configurationVerified: true, nativeEngineering: 'unavailable', corpus: settings.knowledge ? 'not_probed' : 'not_configured', modelCallsEnabled: settings.sourceCodeDisclosureAccepted === true && Boolean(settings.model), files, paths, settings };
}

export async function prepareChatWorkspace(root) {
  const status = await inspectState(root);
  // The pinned UI bootstraps its legacy chats location even with a relocated
  // OPENCHAMBER_DATA_DIR. Both roots must exist before the first browser login.
  // Walk one component at a time so an existing file or redirected directory
  // fails before any descendant is created. Existing chats are never replaced.
  for (const [base, segments] of [
    [status.paths.home, ['.config', 'openchamber', 'chats']],
    [status.paths.openchamber, ['chats']],
  ]) {
    let directory = base;
    for (const segment of segments) {
      directory = join(directory, segment);
      try { await mkdir(directory, { mode: 0o700 }); }
      catch (error) { if (error.code !== 'EEXIST') throw error; }
      const metadata = await lstat(directory);
      if (!metadata.isDirectory() || metadata.isSymbolicLink()) {
        throw new Error('Chat workspace must contain regular directories, not files or redirected directories');
      }
    }
  }
  return status;
}

export function runtimeEnvironment(root, input, inherited = process.env) {
  const settings = validateSettings(input);
  const paths = statePaths(root);
  const env = {};
  for (const key of ['PATH', 'Path', 'SystemRoot', 'WINDIR', 'COMSPEC', 'PATHEXT', 'TEMP', 'TMP', 'TMPDIR', 'LANG', 'LC_ALL']) {
    if (inherited[key] !== undefined) env[key] = inherited[key];
  }
  Object.assign(env, {
    HOME: paths.home, USERPROFILE: paths.home,
    APPDATA: paths.configPath.replace(/[\\/]opencode\.json$/, ''), LOCALAPPDATA: paths.data,
    XDG_CONFIG_HOME: dirname(paths.configPath), XDG_DATA_HOME: paths.data,
    XDG_CACHE_HOME: paths.cache, XDG_STATE_HOME: paths.state,
    OPENCODE_CONFIG_DIR: dirname(paths.configPath), OPENCHAMBER_DATA_DIR: paths.openchamber,
    OPENCHAMBER_RELAY_HOST: 'off', OPENCHAMBER_HOST: '127.0.0.1',
    OPENCHAMBER_OPENCODE_HOSTNAME: '127.0.0.1',
  });
  const keys = { openai: 'OPENAI_API_KEY', anthropic: 'ANTHROPIC_API_KEY', google: 'GOOGLE_GENERATIVE_AI_API_KEY' };
  const key = settings.model && keys[settings.model.split('/')[0]];
  if (settings.sourceCodeDisclosureAccepted === true && key && inherited[key] !== undefined) env[key] = inherited[key];
  return env;
}
