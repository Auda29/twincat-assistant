import { createInterface } from 'node:readline';
import { EDITOR_TOOL_NAMES } from '../../src/editor-session-broker.mjs';

const lines = createInterface({ input: process.stdin });
let bootstrap;
const send = value => process.stdout.write(JSON.stringify(value) + '\n');
lines.on('line', line => {
  const value = JSON.parse(line);
  if (!bootstrap) {
    bootstrap = value;
    send({ kind: 'twincat-assistant-engineering-ready', protocolVersion: 1, sessionId: bootstrap.sessionId });
    return;
  }
  if (!Object.hasOwn(value, 'id')) return;
  let result;
  if (value.method === 'initialize') result = { protocolVersion: '2024-11-05', capabilities: { tools: {} }, serverInfo: { name: 'fixture', version: '1' } };
  if (value.method === 'tools/list') result = { tools: EDITOR_TOOL_NAMES.map(name => ({ name, inputSchema: { type: 'object' } })) };
  if (value.method === 'tools/call') {
    if (value.params.arguments.exit) { process.exit(1); return; }
    result = { content: [{ type: 'text', text: JSON.stringify({ name: value.params.name, arguments: value.params.arguments,
      hostSession: bootstrap.sessionId, childPid: process.pid,
      secretInArguments: process.argv.some(arg => arg.includes(bootstrap.capability)),
      secretInEnvironment: Object.values(process.env).some(arg => arg.includes(bootstrap.capability)),
      inheritedSentinel: Boolean(process.env.TC_EDITOR_TEST_PRIVATE),
    }) }] };
    if (value.params.arguments.delay) { setTimeout(() => send({ jsonrpc: '2.0', id: value.id, result }), 150); return; }
  }
  send({ jsonrpc: '2.0', id: value.id, result });
});
lines.on('close', () => process.exit(0));
