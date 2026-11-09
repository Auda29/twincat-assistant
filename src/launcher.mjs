import { withPackageVerificationScope } from './package-verification-scope.mjs';
import { isAbsolute } from 'node:path';
import { lstat } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { prepareChatWorkspace, runtimeEnvironment } from './state.mjs';
import { upgradeProfileConfiguration } from './profile-update.mjs';

async function prepareLaunchOperation(root, options, inherited = process.env) {
  if (typeof inherited.OPENCHAMBER_UI_PASSWORD !== 'string' || !inherited.OPENCHAMBER_UI_PASSWORD.trim()) throw new Error('A UI password must be supplied through OPENCHAMBER_UI_PASSWORD');
  if (!Number.isInteger(options.port) || options.port < 1024 || options.port > 65535) throw new Error('Invalid loopback port');
  for (const path of [options.openchamber, options.opencode]) {
    if (typeof path !== 'string' || !isAbsolute(path) || !(await lstat(path)).isFile()) throw new Error('Select absolute upstream executable files');
  }
  await upgradeProfileConfiguration(root);
  const status = await prepareChatWorkspace(root);
  const env = runtimeEnvironment(root, status.settings, inherited);
  env.OPENCODE_BINARY = options.opencode;
  env.OPENCHAMBER_UI_PASSWORD = inherited.OPENCHAMBER_UI_PASSWORD;
  const args = [options.openchamber, 'serve', '--foreground', '--host', '127.0.0.1', '--port', String(options.port)];
  if (status.settings.knowledge?.bridge) {
    env.TC_ASSISTANT_KNOWLEDGE = JSON.stringify({ root: status.paths.root, knowledge: status.settings.knowledge });
    args.unshift(fileURLToPath(new URL('./shared-chat-runtime.mjs', import.meta.url)));
  }
  return { command: process.execPath, args, cwd: status.paths.workspace, env };
}
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
const capture = promisify(execFile);
export async function runVersion(command, args, env, cwd, expected, timeout = 10000) {
  try {
    // Operator-selected binaries are trusted code, but version probes do not need secrets.
    const probeEnv = Object.fromEntries(['PATH', 'Path', 'SystemRoot', 'WINDIR', 'COMSPEC', 'PATHEXT', 'TEMP', 'TMP', 'TMPDIR', 'LANG', 'LC_ALL', 'HOME', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'XDG_CONFIG_HOME', 'XDG_DATA_HOME', 'XDG_CACHE_HOME', 'XDG_STATE_HOME'].filter(key => env[key] !== undefined).map(key => [key, env[key]]));
    const { stdout } = await capture(command, args, { env: probeEnv, cwd, timeout, maxBuffer: 16384, shell: false, windowsHide: true });
    const version = stdout.trim().match(/^(?:opencode v|OpenChamber v|v)?(\d+\.\d+\.\d+)$/)?.[1];
    if (version !== expected) throw new Error();
    return version;
  } catch { throw new Error('Upstream version verification failed'); }
}

export function prepareLaunch(...args) {
  return withPackageVerificationScope(() => prepareLaunchOperation(...args));
}
