// Reviewed host-backed engineering tools. New upstream tools remain denied.
export const LEGACY_DEVELOPMENT_EDITOR_TOOL_NAMES = Object.freeze([
  'tc_session_describe', 'tc_project_association', 'tc_build_project', 'plc_build_project',
  'tc_build_and_get_errors', 'tc_error_list', 'tc_output_read', 'tc_operation_get',
  'tc_operation_cancel', 'plc_source_targets', 'plc_source_read', 'plc_source_preview',
  'plc_source_apply', 'plc_source_rollback', 'plc_source_journal',
]);
export const DEVELOPMENT_EDITOR_TOOL_NAMES = Object.freeze([...LEGACY_DEVELOPMENT_EDITOR_TOOL_NAMES, 'plc_source_create']);
export const EDITOR_TOOL_NAMES = Object.freeze(LEGACY_DEVELOPMENT_EDITOR_TOOL_NAMES.filter(name => !name.startsWith('plc_source_')));

const EDITOR_SERVER = 'engineering_editor';
const DEBUG_SERVER = 'engineering_debug';
const BETA11_DEBUG_TOOL_NAMES = Object.freeze([...EDITOR_TOOL_NAMES, 'plc_debug_state', 'plc_debug_read']);
export const DEBUG_TOOL_NAMES = Object.freeze([...BETA11_DEBUG_TOOL_NAMES,
  'plc_debug_wait_until', 'plc_debug_watch', 'plc_debug_unwatch', 'plc_debug_list_watches',
]);

const actions = (server, names) => Object.freeze(names.map(name => server + '_' + name));
export const DEVELOPMENT_EDITOR_ACTIONS = actions(EDITOR_SERVER, DEVELOPMENT_EDITOR_TOOL_NAMES);
export const EDITOR_ACTIONS = actions(EDITOR_SERVER, EDITOR_TOOL_NAMES);
export const DEBUG_ACTIONS = actions(DEBUG_SERVER, DEBUG_TOOL_NAMES);

// Exact action names used only to recognize earlier managed configurations.
export const LEGACY_BUILD_ACTIONS = actions('editor', EDITOR_TOOL_NAMES);
export const LEGACY_DEVELOPMENT_EDITOR_ACTIONS = actions('editor', LEGACY_DEVELOPMENT_EDITOR_TOOL_NAMES);
export const BETA11_EDITOR_ACTIONS = actions('editor', DEVELOPMENT_EDITOR_TOOL_NAMES);
export const LEGACY_DEBUG_ACTIONS = actions('editor', BETA11_DEBUG_TOOL_NAMES);

export function ownedEngineeringProfile(profile = 'build') {
  switch (profile) {
    case 'build': return { argument: '--host-bootstrap-stdin', names: EDITOR_TOOL_NAMES, server: EDITOR_SERVER };
    case 'editor': return { argument: '--host-editor-bootstrap-stdin', names: DEVELOPMENT_EDITOR_TOOL_NAMES, server: EDITOR_SERVER };
    case 'debug': return { argument: '--host-debug-bootstrap-stdin', names: DEBUG_TOOL_NAMES, server: DEBUG_SERVER };
    default: throw new Error('Invalid owned Engineering profile');
  }
}
