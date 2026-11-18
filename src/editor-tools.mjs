// Reviewed host-backed engineering tools. New upstream tools remain denied.
export const DEVELOPMENT_EDITOR_TOOL_NAMES = Object.freeze([
  'tc_session_describe', 'tc_project_association', 'tc_build_project', 'plc_build_project',
  'tc_build_and_get_errors', 'tc_error_list', 'tc_output_read', 'tc_operation_get',
  'tc_operation_cancel', 'plc_source_targets', 'plc_source_read', 'plc_source_preview',
  'plc_source_apply', 'plc_source_rollback', 'plc_source_journal',
]);
// Release policy is an explicit subset; development definitions remain available in code.
export const EDITOR_TOOL_NAMES = Object.freeze(DEVELOPMENT_EDITOR_TOOL_NAMES.filter(name => !name.startsWith('plc_source_')));
export const DEVELOPMENT_EDITOR_ACTIONS = Object.freeze(DEVELOPMENT_EDITOR_TOOL_NAMES.map(name => 'editor_' + name));
export const EDITOR_ACTIONS = Object.freeze(EDITOR_TOOL_NAMES.map(name => 'editor_' + name));

const DEBUG_TOOL_NAMES = Object.freeze([...EDITOR_TOOL_NAMES, 'plc_debug_state', 'plc_debug_read']);
export function ownedEngineeringProfile(profile = 'build') {
  switch (profile) {
    case 'build': return { argument: '--host-bootstrap-stdin', names: EDITOR_TOOL_NAMES };
    case 'editor': return { argument: '--host-editor-bootstrap-stdin', names: DEVELOPMENT_EDITOR_TOOL_NAMES };
    case 'debug': return { argument: '--host-debug-bootstrap-stdin', names: DEBUG_TOOL_NAMES };
    default: throw new Error('Invalid owned Engineering profile');
  }
}
