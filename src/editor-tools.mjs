// Reviewed host-backed engineering tools. New upstream tools remain denied.
export const EDITOR_TOOL_NAMES = Object.freeze([
  'tc_session_describe', 'tc_project_association', 'tc_build_project', 'plc_build_project',
  'tc_build_and_get_errors', 'tc_error_list', 'tc_output_read', 'tc_operation_get',
  'tc_operation_cancel', 'plc_source_targets', 'plc_source_read', 'plc_source_preview',
  'plc_source_apply', 'plc_source_rollback', 'plc_source_journal',
]);
export const EDITOR_ACTIONS = Object.freeze(EDITOR_TOOL_NAMES.map(name => 'editor_' + name));
