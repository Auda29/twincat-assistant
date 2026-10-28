import { READ_TOOLS } from './config.mjs';
import { Client } from '@modelcontextprotocol/client';
import { StdioClientTransport } from '@modelcontextprotocol/client/stdio';

export async function probeServer(name, server, call, { env = {}, cwd, timeout = 15000, signal: ownerSignal } = {}) {
  if (!READ_TOOLS.includes(`${name}_${call.name}`)) throw new Error('Probe only permits reviewed read-only tools');
  if (!['legacy', '2026-07-28'].includes(server.protocol)) throw new Error('Unsupported protocol profile');
  const transport = new StdioClientTransport({ command: server.command[0], args: server.command.slice(1), env, cwd, stderr: 'ignore', maxBufferSize: 1048576 });
  const client = new Client({ name: 'twincat-assistant-operator-probe', version: '0.1.0-dev.1' }, {
    capabilities: {}, listMaxPages: 4,
    versionNegotiation: { mode: server.protocol === 'legacy' ? 'legacy' : { pin: server.protocol } },
  });
  const controller = new AbortController();
  const signal = ownerSignal ? AbortSignal.any([ownerSignal, controller.signal]) : controller.signal;
  const deadline = setTimeout(() => controller.abort(), timeout);
  try {
    await client.connect(transport, { timeout, signal });
    const catalog = await client.listTools({}, { timeout, signal });
    const names = catalog.tools.map(tool => tool.name);
    if (new Set(names).size !== names.length || names.length > 256 || !names.includes(call.name)) throw new Error('Unexpected tool catalog');
    const result = await client.callTool(call, undefined, { timeout, signal });
    return { protocol: client.getNegotiatedProtocolVersion(), era: client.getProtocolEra(), server: client.getServerVersion(), toolNames: names, result };
  } catch { throw new Error('Read-only MCP probe failed; check configured upstream version and prerequisites'); }
  finally { clearTimeout(deadline); await client.close(); await transport.close(); }
}
