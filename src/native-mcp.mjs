import { withPackageVerificationScope } from './package-verification-scope.mjs';
import { lstat, mkdir, open, realpath } from 'node:fs/promises';
import { isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readJsonFile, inspectState } from './state.mjs';
import { createOpenCodeConfig, previousPolicyConfig, validateSettings } from './config.mjs';
import { ANALYSIS_PROFILE, previousAnalysisPrompts, analysisSystemPrompt } from './agent-prompts.mjs';
import { atomicJson, updateProfile } from './profile-update.mjs';
import { effectiveKnowledge } from './knowledge-paths.mjs';
import { findProjectWorkspace, projectKey, updateProjectConfiguration, withProjectLock } from './project-workspace.mjs';
import { currentPackageSettings, previousPackageRuntime } from './managed-relocation.mjs';
import { applicationRoot, requirePackage, verifyPackageIfPresent } from './package-integrity.mjs';

function path(value) {
  if (typeof value !== 'string' || !value || value.length > 2048 || value.includes('\0') || !isAbsolute(value))
    throw new Error('An absolute MCP/project path is required');
  return resolve(value);
}
async function regularFile(value) {
  value = path(value);
  const stat = await lstat(value);
  if (!stat.isFile() || stat.isSymbolicLink()) throw new Error('A regular MCP/project file is required');
  return value;
}
function exactKeys(value, allowed) {
  if (!value || typeof value !== 'object' || Array.isArray(value) || Object.keys(value).some(key => !allowed.includes(key)))
    throw new Error('Invalid native MCP settings');
}
async function readNativeMcpSettingsOperation(file) {
  if (requirePackage) await verifyPackageIfPresent();
  const value = await readJsonFile(path(file));
  exactKeys(value, ['knowledge', 'engineering']);
  exactKeys(value.knowledge, ['executable', 'config']);
  exactKeys(value.engineering, ['entrypoint']);
  const knowledge = { executable: await regularFile(requirePackage ? join(applicationRoot, 'runtime/mcp/knowledge/twincat-knowledge.exe') : value.knowledge.executable) };
  if (value.knowledge.config !== undefined) knowledge.config = await regularFile(value.knowledge.config);
  const engineering = { entrypoint: await regularFile(requirePackage ? join(applicationRoot, 'runtime/mcp/engineering/packages/mcp/dist/index.js') : value.engineering.entrypoint) };
  validateSettings({ knowledge });
  return { knowledge, engineering };
}
async function configureNativeMcpOperation(root, settingsFile) {
  const settings = await readNativeMcpSettings(settingsFile);
  const inspected = await inspectState(path(root));
  settings.knowledge = await effectiveKnowledge(root, settings.knowledge);
  settings.knowledge.bridge = join(root, 'knowledge', 'broker.json');
  // Current project startups should not compete with provider/model writers
  // for a global write lock when no global configuration change is necessary.
  if (inspected.configurationCurrent && !inspected.settings.engineering
      && JSON.stringify(inspected.settings.knowledge) === JSON.stringify(settings.knowledge)) return settings;
  await updateProfile(path(root), current => {
    const { engineering, ...general } = current;
    return { ...general, knowledge: settings.knowledge };
  });
  return settings;
}
async function privateDirectory(directory) {
  try { await mkdir(directory, { mode: 0o700 }); }
  catch (error) { if (error.code !== 'EEXIST') throw error; }
  const stat = await lstat(directory);
  if (!stat.isDirectory() || stat.isSymbolicLink()) throw new Error('Redirected native MCP workspace rejected');
}
async function managedJson(file, expected) {
  try {
    const handle = await open(file, 'wx', 0o600);
    try { await handle.writeFile(JSON.stringify(expected, null, 2) + '\n'); await handle.sync(); }
    finally { await handle.close(); }
  } catch (error) {
    if (error.code !== 'EEXIST') throw error;
    if (JSON.stringify(await readJsonFile(file)) !== JSON.stringify(expected))
      throw new Error('Native MCP workspace modified; refusing an unknown project or policy');
  }
}
async function prepareNativeProjectOperation(input) {
  exactKeys(input, ['state', 'mcpConfig', 'solution', 'project', 'workspace']);
  const state = path(input.state);
  const solution = await regularFile(input.solution);
  const project = await regularFile(input.project);
  if (!/\.sln$/i.test(solution) || !/\.plcproj$/i.test(project)) throw new Error('Exact solution and PLC project files are required');
  const settings = await configureNativeMcp(state, input.mcpConfig);
  const status = await inspectState(state);
  const canonical = async value => {
    const actual = await realpath(value);
    return process.platform === 'win32' ? actual.toLowerCase() : actual;
  };
  const identity = { solution: await canonical(solution), project: await canonical(project), settings };
  const parent = join(status.paths.root, 'projects');
  await privateDirectory(parent);
  return withProjectLock(parent, projectKey(identity.solution, identity.project), async () => {
    const found = await findProjectWorkspace(parent, identity);
    const { key, index, workspaces } = found;
    const workspace = input.workspace === undefined ? found.workspace : path(input.workspace);
    if (!workspaces.includes(workspace)) throw new Error('Workspace belongs to another project');
    await privateDirectory(workspace);
    const engineeringConfig = join(workspace, 'engineering.json');
    await managedJson(engineeringConfig, {
      targetAmsNetId: '127.0.0.1.1.1', readOnly: true, writeAllowlist: [],
      engineering: { enabled: true, backend: 'configuredProjectFiles', projectFiles: [{ path: identity.project, type: 'plc' }] },
    });
    const configuration = (selected, system = analysisSystemPrompt({ knowledge: true, engineering: true, project: true }), runtime = {}, legacy = false) => {
      exactKeys(selected, ['knowledge', 'engineering']);
      exactKeys(selected.engineering, ['entrypoint']);
      const configSettings = { knowledge: selected.knowledge,
        engineering: { entrypoint: selected.engineering.entrypoint, config: engineeringConfig } };
      const config = legacy ? previousPolicyConfig(configSettings, runtime, system) : createOpenCodeConfig(configSettings, runtime);
      // Provider/default model belong to the protected global profile.
      const agent = { ...config.agents[ANALYSIS_PROFILE], system };
      return { mcp: config.mcp, agents: { [ANALYSIS_PROFILE]: agent } };
    };
    // Exact configurations written by earlier releases; only their prompt differs.
    const previous = async (selected, actual) => {
      const known = currentPackageSettings(selected) ? previousAnalysisPrompts({ knowledge: Boolean(selected.knowledge), engineering: true, project: true }).map(system => configuration(selected, system, {}, true)) : [];
      const runtime = await previousPackageRuntime(actual, selected);
      if (runtime) known.push(configuration(selected, undefined, runtime), ...previousAnalysisPrompts({ knowledge: Boolean(selected.knowledge), engineering: true, project: true }).map(system => configuration(selected, system, runtime, true)));
      return known;
    };
    // Adopt the 0.1.9 directory before updating its managed MCP configuration.
    await atomicJson(index, { version: 2, solution: identity.solution, project: identity.project,
      directory: found.workspace.slice(parent.length + 1) });
    await updateProjectConfiguration(workspace, identity, settings, configuration, previous, currentPackageSettings);
    return { configured: true, workspace, workspaces, project, solution, key, source: 'saved-project-files',
      corpus: settings.knowledge.config ? 'not_probed' : 'not_configured' };
  });
}

async function main() {
  await (await import('./package-integrity.mjs')).verifyPackageIfPresent();
  const chunks = []; let size = 0;
  for await (const chunk of process.stdin) {
    size += chunk.length;
    if (size > 16384) throw new Error('Native MCP request is too large');
    chunks.push(chunk);
  }
  const input = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks)));
  process.stdout.write(JSON.stringify(await prepareNativeProject(input)) + '\n');
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { await withPackageVerificationScope(main); }
  catch (error) {
    // Fixed diagnostic codes only. No paths, exception text or upstream output.
    const failure = error?.code === 'PROJECT_WORKSPACE_LOCKED' ? 'project_locked' : 'setup_failed';
    process.stdout.write(JSON.stringify({ configured: false, failure }) + '\n');
    process.exitCode = 1;
  }
}

export function readNativeMcpSettings(...args) {
  return withPackageVerificationScope(() => readNativeMcpSettingsOperation(...args));
}

export function configureNativeMcp(...args) {
  return withPackageVerificationScope(() => configureNativeMcpOperation(...args));
}

export function prepareNativeProject(...args) {
  return withPackageVerificationScope(() => prepareNativeProjectOperation(...args));
}
