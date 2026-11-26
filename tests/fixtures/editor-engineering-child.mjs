import { createInterface } from 'node:readline';
import { ownedEngineeringProfile } from '../../src/editor-tools.mjs';

const lines = createInterface({ input: process.stdin });
const mode = process.argv[2] === '--host-editor-bootstrap-stdin' ? 'editor' :
  process.argv[2] === '--host-debug-bootstrap-stdin' ? 'debug' : 'build';
const names = ownedEngineeringProfile(mode).names;
let bootstrap;
const send = value => process.stdout.write(JSON.stringify(value) + '\n');

function sourceTargets() {
  const folders = [
    'ProductionLineControl', 'IntegratedMaterialHandlingAndTracking', 'StationControllersForWorkpiecePreparation',
    'ConveyorAndWorkpieceTransferCoordination', 'InfeedAndOutfeedSynchronization', 'MachineSpecificWorkpieceProcessing',
    'AutomaticOperationAndInterlockManagement', 'CalculationOfConveyorPositionAndOffsets',
    'CommissioningAndProductionRecipeSpecificAdjustments',
  ];
  const targets = Array.from({ length: 500 }, (_, index) => {
    const owner = `FB_WorkpieceProcessing${index}`;
    const member = 'CalculateConveyorPosition';
    const qualifiedName = `${owner}.${member}`;
    const treePath = ['TIPC', 'LineControl', 'LineControl Project', 'POUs', ...folders, owner, member].join('^');
    const physicalFile = ['C:', 'TwinCAT', 'Projects', 'MachineController', 'LineControl', 'POUs', ...folders, owner + '.TcPOU'].join('\\');
    return {
      objectId: `${physicalFile}|${treePath}`, qualifiedName, kind: 'method', sections: ['declaration', 'implementation'],
      language: 'ST', treePath, physicalFile, source: 'automationInterface', sourceMode: 'automationInterface',
      persistence: 'mayPersistToDisk', applySupported: true,
    };
  });
  const structuredContent = {
    protocolVersion: 1, sessionId: bootstrap.sessionId, operationId: 'ca3fd4c1-76e7-42b4-9794-c484a5879bee', status: 'succeeded',
    provenance: { context: bootstrap.expected, source: 'automationInterface', persistence: 'mayPersistToDisk' },
    result: { moniker: null, targets, count: targets.length, truncated: false, source: 'automationInterface', persistence: 'mayPersistToDisk' },
  };
  return { structuredContent, content: [{ type: 'text', text: JSON.stringify(structuredContent, null, 2) }] };
}

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
  if (value.method === 'tools/list') result = { tools: names.map(name => ({ name, inputSchema: { type: 'object' } })) };
  if (value.method === 'tools/call') {
    if (value.params.arguments.exit) { process.exit(1); return; }
    result = { content: [{ type: 'text', text: JSON.stringify({ name: value.params.name, arguments: value.params.arguments,
      hostSession: bootstrap.sessionId, childPid: process.pid,
      secretInArguments: process.argv.some(arg => arg.includes(bootstrap.capability)),
      secretInEnvironment: Object.values(process.env).some(arg => arg.includes(bootstrap.capability)),
      inheritedSentinel: Boolean(process.env.TC_EDITOR_TEST_PRIVATE),
    }) }] };
    if (value.params.name === 'plc_source_targets' && value.params.arguments.largeDiscovery) result = sourceTargets();
    if (value.params.arguments.responseFrameBytes) {
      result = { content: [{ type: 'text', text: '' }] };
      const overhead = Buffer.byteLength(JSON.stringify({ jsonrpc: '2.0', id: value.id, result }) + '\n');
      result.content[0].text = 'x'.repeat(value.params.arguments.responseFrameBytes - overhead);
    }
    if (value.params.arguments.delay) { setTimeout(() => send({ jsonrpc: '2.0', id: value.id, result }), 150); return; }
  }
  send({ jsonrpc: '2.0', id: value.id, result });
});
lines.on('close', () => process.exit(0));
