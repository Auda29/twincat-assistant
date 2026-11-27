import { isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Config } from '@opencode/schema';
import { Schema } from 'effect';
import {
  ANALYSIS_PROFILE, EDITOR_PROFILE, DEBUG_PROFILE, previousAnalysisPrompts, analysisSystemPrompt,
  editorSystemPrompt, debugSystemPrompt, previousBeta9DebugSystemPrompt,
  previousC3EditorSystemPrompt, previousC4EditorSystemPrompt, previousBeta10EditorSystemPrompt,
  previousBeta11EditorSystemPrompt, previousBeta11DebugSystemPrompt,
} from './agent-prompts.mjs';
import { C2_ANALYSIS_PROMPTS } from './analysis-prompts-c2.mjs';
import {
  DEBUG_ACTIONS, DEVELOPMENT_EDITOR_ACTIONS, LEGACY_BUILD_ACTIONS,
  LEGACY_DEVELOPMENT_EDITOR_ACTIONS, BETA11_EDITOR_ACTIONS, LEGACY_DEBUG_ACTIONS,
} from './editor-tools.mjs';

function object(value, keys) {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      Object.keys(value).some(key => !keys.includes(key))) {
    throw new Error('Invalid settings object or unknown settings key');
  }
}
function absolute(value) {
  if (typeof value !== 'string' || !value.trim() || value.includes('\0') || !isAbsolute(value)) {
    throw new Error('Settings paths must be nonempty absolute paths');
  }
}
export function validateSettings(settings) {
  object(settings, ['knowledge', 'engineering', 'provider', 'model', 'sourceCodeDisclosureAccepted']);
  if (settings.knowledge !== undefined) {
    object(settings.knowledge, ['executable', 'config', 'startupTimeout', 'bridge']);
    absolute(settings.knowledge.executable);
    if (settings.knowledge.config !== undefined) absolute(settings.knowledge.config);
    if (settings.knowledge.bridge !== undefined) absolute(settings.knowledge.bridge);
    if (settings.knowledge.startupTimeout !== undefined && (!Number.isInteger(settings.knowledge.startupTimeout)
      || settings.knowledge.startupTimeout < 15000 || settings.knowledge.startupTimeout > 600000)) throw new Error('Invalid knowledge startup timeout');
  }
  if (settings.engineering !== undefined) {
    object(settings.engineering, ['entrypoint', 'config']);
    absolute(settings.engineering.entrypoint); absolute(settings.engineering.config);
  }
  if (settings.sourceCodeDisclosureAccepted !== undefined && typeof settings.sourceCodeDisclosureAccepted !== 'boolean') {
    throw new Error('Invalid source code disclosure settings');
  }
  if (settings.model !== undefined) {
    if (typeof settings.model !== 'string' || !/^[a-zA-Z0-9_-]+\/[^\s\0]+$/.test(settings.model) || settings.sourceCodeDisclosureAccepted !== true) {
      throw new Error('Model settings require provider/model and explicit source code disclosure acceptance');
    }
  }
  if (settings.provider !== undefined && (typeof settings.provider !== 'string'
      || !/^[a-zA-Z0-9_-]{1,80}$/.test(settings.provider) || settings.sourceCodeDisclosureAccepted !== true
      || (settings.model && settings.model.split('/')[0] !== settings.provider))) {
    throw new Error('Provider settings require an exact provider and explicit source code disclosure acceptance');
  }
  return structuredClone(settings);
}

// Exact upstream tool names. New tools remain denied until reviewed.
export const READ_TOOLS = Object.freeze([
  'knowledge_tc_browse_topics', 'knowledge_tc_get_corpus_status',
  'knowledge_tc_get_topic', 'knowledge_tc_search',
  'knowledge_tc_find_symbols', 'knowledge_tc_get_symbol', 'knowledge_tc_find_libraries',
  'knowledge_tc_get_library', 'knowledge_tc_lookup_error',
  'engineering_tc_list_workbenches', 'engineering_tc_list_projects',
  'engineering_tc_project_state', 'engineering_plc_list_pous',
  'engineering_plc_read_pou', 'engineering_plc_search_code',
  'engineering_plc_describe_pou', 'engineering_plc_list_libraries',
  'engineering_plc_describe_library',
]);

function createBaseOpenCodeConfig(settings, runtime) {
  const node = runtime.node ?? process.execPath;
  const knowledgeClient = runtime.knowledgeClient ?? fileURLToPath(new URL('./knowledge-client.mjs', import.meta.url));
  settings = validateSettings(settings);
  const servers = {};
  if (settings.knowledge) {
    servers.knowledge = {
      type: 'local', protocol: '2026-07-28', codemode: false,
      command: settings.knowledge.bridge
        ? [node, knowledgeClient, settings.knowledge.bridge]
        : [settings.knowledge.executable, 'serve', '--transport', 'stdio',
          ...(settings.knowledge.config ? ['--config', settings.knowledge.config] : [])],
      timeout: { startup: settings.knowledge.startupTimeout ?? 15000, catalog: 15000, execution: 30000 },
    };
  }
  if (settings.engineering) {
    servers.engineering = {
      type: 'local', protocol: 'legacy', codemode: false,
      command: [node, settings.engineering.entrypoint, '--config', settings.engineering.config],
      timeout: { startup: 15000, catalog: 15000, execution: 30000 },
    };
  }
  const permissions = [
    { action: '*', resource: '*', effect: 'deny' },
    ...READ_TOOLS.map(action => ({ action, resource: '*', effect: 'allow' })),
    { action: 'question', resource: '*', effect: 'allow' },
  ];
  const config = {
    $schema: 'https://opencode.ai/config.json',
    default_agent: ANALYSIS_PROFILE, update: 'disable', share: 'disabled',
    snapshots: false, formatter: false, lsp: false, warming: false,
    permissions,
    agents: {
      [ANALYSIS_PROFILE]: {
        description: 'Read-only TwinCAT documentation and saved-project assistant',
        mode: 'primary', permissions,
        system: analysisSystemPrompt({ knowledge: Boolean(settings.knowledge), engineering: Boolean(settings.engineering) }),
      },
    },
    experimental: {
      policies: [
        { action: 'permission', resource: '*', effect: 'deny' },
        ...[...READ_TOOLS, 'question'].map(action => ({ action: 'permission', resource: `${action}:*`, effect: 'allow' })),
        { action: 'provider.use', resource: '*', effect: 'deny' },
      ],
    },
    mcp: { servers },
  };
  if (settings.model) config.model = settings.model;
  const provider = settings.provider ?? settings.model?.split('/')[0];
  if (provider) config.experimental.policies.push({ action: 'provider.use', resource: provider, effect: 'allow' });
  return config;
}

const editorDescription = 'Native-approved ST source edits, object creation and local PLC builds';
const debugDescription = 'Approved live PLC reads and local PLC builds';

export function createOpenCodeConfig(settings = {}, runtime = {}) {
  return createNativeProfileConfig(settings, runtime, [
    [EDITOR_PROFILE, editorDescription, DEVELOPMENT_EDITOR_ACTIONS, editorSystemPrompt],
    [DEBUG_PROFILE, debugDescription, DEBUG_ACTIONS, debugSystemPrompt],
  ], [...DEVELOPMENT_EDITOR_ACTIONS, ...DEBUG_ACTIONS]);
}

function createNativeProfileConfig(settings, runtime, nativeAgents, nativeActions) {
  const config = createBaseOpenCodeConfig(settings, runtime);
  for (const [id, description, actions, system] of nativeAgents) {
    config.agents[id] = {
      description, mode: 'primary',
      permissions: [...config.permissions, ...actions.map(action => ({ action, resource: '*', effect: 'allow' }))],
      system: system(),
    };
  }
  // Core applies these as a global ceiling. Agent policy and the ephemeral
  // native broker both enforce the narrower active catalog.
  const insertion = config.experimental.policies.findIndex(rule => rule.action === 'provider.use');
  config.experimental.policies.splice(insertion, 0,
    ...nativeActions.map(action => ({ action: 'permission', resource: `${action}:*`, effect: 'allow' })));
  Schema.decodeUnknownSync(Config.Info)(config);
  return config;
}

// Construct a previously generated policy, including its exact permissions. The
// five typed tools did not exist in any of the recognized earlier releases.
const TYPED_KNOWLEDGE_TOOLS = new Set([
  'knowledge_tc_find_symbols', 'knowledge_tc_get_symbol', 'knowledge_tc_find_libraries',
  'knowledge_tc_get_library', 'knowledge_tc_lookup_error',
]);
export function previousPolicyConfig(settings = {}, runtime = {}, system, { typedKnowledge = false } = {}) {
  const config = createBaseOpenCodeConfig(settings, runtime);
  const oldPermissions = config.permissions.filter(rule => typedKnowledge || !TYPED_KNOWLEDGE_TOOLS.has(rule.action));
  config.permissions = oldPermissions;
  config.agents[ANALYSIS_PROFILE].permissions = oldPermissions;
  config.agents[ANALYSIS_PROFILE].system = system;
  config.experimental.policies = config.experimental.policies.filter(rule =>
    typedKnowledge || ![...TYPED_KNOWLEDGE_TOOLS].some(name => rule.action === 'permission' && rule.resource === `${name}:*`));
  return config;
}

// Match complete earlier configurations only, including the old policy and the
// historical prompt for this exact settings scope.
export function previousOpenCodeConfigs(settings = {}, runtime = {}) {
  const scope = { knowledge: Boolean(settings.knowledge), engineering: Boolean(settings.engineering) };
  const c2 = C2_ANALYSIS_PROMPTS.profile[2 * Number(scope.knowledge) + Number(scope.engineering)];
  const legacyConfig = (description, editorActions, editorPrompt, debugPrompt) => {
    const agents = [[EDITOR_PROFILE, description, editorActions, editorPrompt]];
    const debugActions = debugPrompt ? LEGACY_DEBUG_ACTIONS : [];
    if (debugPrompt) agents.push([DEBUG_PROFILE, debugDescription, debugActions, debugPrompt]);
    return createNativeProfileConfig(settings, runtime, agents, [...new Set([...LEGACY_BUILD_ACTIONS, ...editorActions, ...debugActions])]);
  };
  const c3 = legacyConfig('Native-approved source review, unsaved buffer edits and project builds',
    LEGACY_DEVELOPMENT_EDITOR_ACTIONS, previousC3EditorSystemPrompt);
  const beta6 = legacyConfig('Native-approved project builds and diagnostics', LEGACY_BUILD_ACTIONS, previousC4EditorSystemPrompt);
  // Beta 7, 8 and 9 shipped the same managed configuration.
  const previousEditorDescription = 'Native-approved XML buffer edits and local PLC builds';
  const beta9 = legacyConfig(previousEditorDescription, LEGACY_DEVELOPMENT_EDITOR_ACTIONS, previousBeta10EditorSystemPrompt, previousBeta9DebugSystemPrompt);
  const beta10 = legacyConfig(previousEditorDescription, LEGACY_DEVELOPMENT_EDITOR_ACTIONS, previousBeta10EditorSystemPrompt, previousBeta11DebugSystemPrompt);
  const beta11 = legacyConfig(editorDescription, BETA11_EDITOR_ACTIONS, previousBeta11EditorSystemPrompt, previousBeta11DebugSystemPrompt);
  return [c3, beta6, beta9, beta10, beta11, ...previousAnalysisPrompts(scope).map(system => previousPolicyConfig(settings, runtime, system)),
    previousPolicyConfig(settings, runtime, c2, { typedKnowledge: true })];
}
