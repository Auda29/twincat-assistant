import assert from 'node:assert/strict';
import test from 'node:test';
import { Config } from '@opencode/schema';
import { Schema } from 'effect';
import { sep } from 'node:path';
import { createOpenCodeConfig, READ_TOOLS } from '../src/config.mjs';
import { EDITOR_ACTIONS } from '../src/editor-tools.mjs';

test('server configuration keeps knowledge modern and engineering legacy without code mode', () => {
  const path = name => `${sep}operator${sep}${name}`;
  const config = createOpenCodeConfig({
    knowledge: { executable: path('twincat-knowledge'), config: path('knowledge.toml') },
    engineering: { entrypoint: path('engineering.mjs'), config: path('engineering.json') },
  });
  assert.ok(config.mcp.servers.knowledge, 'knowledge server must be configured');
  assert.ok(config.mcp.servers.engineering, 'engineering server must be configured');
  assert.deepEqual(config.mcp.servers.knowledge.command, [path('twincat-knowledge'), 'serve', '--transport', 'stdio', '--config', path('knowledge.toml')]);
  assert.equal(config.mcp.servers.knowledge.protocol, '2026-07-28');
  assert.equal(config.mcp.servers.engineering.protocol, 'legacy');
  assert.equal(config.mcp.servers.knowledge.codemode, false);
  assert.equal(config.mcp.servers.engineering.codemode, false);
  assert.equal(config.mcp.servers.knowledge.timeout.startup,15000,'existing profiles retain their reviewed policy');
  assert.equal(createOpenCodeConfig({knowledge:{executable:path('knowledge'),startupTimeout:600000}}).mcp.servers.knowledge.timeout.startup,600000);
  assert.throws(()=>createOpenCodeConfig({knowledge:{executable:path('knowledge'),startupTimeout:600001}}));
  assert.doesNotThrow(() => Schema.decodeUnknownSync(Config.Info)(config));
});

test('settings reject unknown keys, relative/blank paths and implicit source-code disclosure', () => {
  for (const settings of [null, [], { shell: 'anything' }, { knowledge: {} }, { knowledge: { executable: '' } }, { knowledge: { executable: 'relative.exe' } }, { engineering: { entrypoint: '/entry.mjs', config: '../config' } }, { model: 'openai/example' }, { sourceCodeDisclosureAccepted: 'yes' }, { model: 'openai/example', sourceCodeDisclosureAccepted: false }]) {
    assert.throws(() => createOpenCodeConfig(settings), /settings|path|disclosure/i, JSON.stringify(settings));
  }
  const config = createOpenCodeConfig({ model: 'openai/example', sourceCodeDisclosureAccepted: true });
  assert.equal(config.model, 'openai/example');
  assert.deepEqual(config.experimental.policies.at(-1), { action: 'provider.use', resource: 'openai', effect: 'allow' });
});

test('read-only profile uses a default-deny policy with only exact documentation and saved-project tools', () => {
  const config = createOpenCodeConfig({});
  assert.equal(config.default_agent, 'twincat-readonly');
  assert.equal(config.warming, false);
  assert.equal(config.experimental.policies[0].resource, '*');
  assert.equal(config.experimental.policies[0].effect, 'deny');
  const allowed = config.experimental.policies.filter(x => x.action === 'permission' && x.effect === 'allow').map(x => x.resource);
  assert.ok(allowed.includes('knowledge_tc_get_topic:*'));
  assert.ok(allowed.includes('engineering_plc_read_pou:*'));
  assert.deepEqual(allowed, [...READ_TOOLS, 'question', ...EDITOR_ACTIONS].map(name => name + ':*'));
  assert.deepEqual(config.permissions.filter(rule => rule.effect === 'allow').map(rule => rule.action), [...READ_TOOLS, 'question']);
  assert.deepEqual(config.agents['twincat-readonly'].permissions, config.permissions);
  assert.deepEqual(config.agents['twincat-editor'].permissions.filter(rule => rule.effect === 'allow').map(rule => rule.action),
    [...READ_TOOLS, 'question', ...EDITOR_ACTIONS]);
  assert.equal(config.mcp.servers.editor, undefined, 'the profile cannot enable an editor connection');
  assert.doesNotThrow(() => Schema.decodeUnknownSync(Config.Info)(config));
});
