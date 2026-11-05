import { isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Config } from '@opencode/schema';
import { Schema } from 'effect';
import { ANALYSIS_PROFILE, PREVIOUS_ANALYSIS_PROMPTS, analysisSystemPrompt } from './agent-prompts.mjs';

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
  'engineering_tc_list_workbenches', 'engineering_tc_list_projects',
  'engineering_tc_project_state', 'engineering_plc_list_pous',
  'engineering_plc_read_pou', 'engineering_plc_search_code',
  'engineering_plc_describe_pou', 'engineering_plc_list_libraries',
  'engineering_plc_describe_library',
]);

export function createOpenCodeConfig(settings = {}) {
  settings = validateSettings(settings);
  const servers = {};
  if (settings.knowledge) {
    servers.knowledge = {
      type: 'local', protocol: '2026-07-28', codemode: false,
      command: settings.knowledge.bridge
        ? [process.execPath, fileURLToPath(new URL('./knowledge-client.mjs', import.meta.url)), settings.knowledge.bridge]
        : [settings.knowledge.executable, 'serve', '--transport', 'stdio',
          ...(settings.knowledge.config ? ['--config', settings.knowledge.config] : [])],
      timeout: { startup: settings.knowledge.startupTimeout ?? 15000, catalog: 15000, execution: 30000 },
    };
  }
  if (settings.engineering) {
    servers.engineering = {
      type: 'local', protocol: 'legacy', codemode: false,
      command: [process.execPath, settings.engineering.entrypoint, '--config', settings.engineering.config],
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
  // Validate against the pinned upstream V2 contract, not a handwritten copy.
  Schema.decodeUnknownSync(Config.Info)(config);
  return config;
}

// Exact configurations written by earlier releases for the same settings. They
// differ only by a reviewed earlier Analysis prompt and may be upgraded in place.
export function previousOpenCodeConfigs(settings = {}) {
  const current = createOpenCodeConfig(settings);
  return PREVIOUS_ANALYSIS_PROMPTS.profile.map(system => {
    const config = structuredClone(current);
    config.agents[ANALYSIS_PROFILE].system = system;
    return config;
  });
}
