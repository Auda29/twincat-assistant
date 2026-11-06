// Actual packaged read-only MCP probe. Uses synthetic files, never an IDE or ADS call.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { verifyManifest } from '../../scripts/application-manifest.mjs';
import { probeServer } from '../../src/probe.mjs';

if (!process.argv[2]) throw new Error('Supply the absolute application package directory');
const application = resolve(process.argv[2]);
const output = resolve(process.argv[3] ?? 'tests/prompt-evaluation/evidence/packaged-accessor-backend.json');
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const manifest = await verifyManifest(application);
const engineeringRoot = join(application, 'runtime/mcp/engineering');
const mcpManifest = await verifyManifest(engineeringRoot);
const provenance = JSON.parse(await readFile(join(engineeringRoot, 'build-provenance.json'), 'utf8'));
assert.equal(mcpManifest.version, provenance.revision);
const temporary = await mkdtemp(join(tmpdir(), 'tc-packaged-accessors-'));
try {
  const project = join(temporary, 'PLC.plcproj');
  const source = join(temporary, 'FB_Axis.TcPOU');
  const projectText = '<Project><ItemGroup><Compile Include="FB_Axis.TcPOU" /></ItemGroup></Project>';
  const sourceText = '<TcPlcObject><POU Name="FB_Axis"><Declaration><![CDATA[FUNCTION_BLOCK FB_Axis]]></Declaration><Implementation><ST><![CDATA[// parent body]]></ST></Implementation><Property Name="Speed"><Declaration><![CDATA[PROPERTY Speed : LREAL]]></Declaration><Get><Declaration><![CDATA[VAR\nEND_VAR]]></Declaration><Implementation><ST><![CDATA[Speed := fActualVelocity;]]></ST></Implementation></Get><Set><Declaration><![CDATA[VAR\nEND_VAR]]></Declaration><Implementation><ST><![CDATA[fSetVelocity := Speed;]]></ST></Implementation></Set></Property></POU></TcPlcObject>';
  await writeFile(project, projectText); await writeFile(source, sourceText);
  const config = join(temporary, 'engineering.json');
  await writeFile(config, JSON.stringify({ targetAmsNetId: '127.0.0.1.1.1', readOnly: true, writeAllowlist: [],
    engineering: { enabled: true, backend: 'configuredProjectFiles', projectFiles: [{ path: project, type: 'plc' }] } }));
  const server = { protocol: 'legacy', command: [join(application, 'runtime/node/node.exe'),
    join(engineeringRoot, 'packages/mcp/dist/index.js'), '--config', config] };
  const env = Object.fromEntries(['PATH','Path','SystemRoot','WINDIR','TEMP','TMP'].filter(k => process.env[k]).map(k => [k, process.env[k]]));
  const options = { cwd: temporary, env, timeout: 30000 };
  const list = await probeServer('engineering', server, { name: 'plc_list_pous', arguments: {} }, options);
  assert.notEqual(list.result.isError, true);
  const content = await probeServer('engineering', server, { name: 'plc_read_pou', arguments: { pou: 'FB_Axis.Speed' } }, options);
  assert.notEqual(content.result.isError, true);
  const pou = content.result.structuredContent?.pou;
  assert.equal(pou?.kind, 'property'); assert.equal(pou?.qualifiedName, 'FB_Axis.Speed');
  assert.equal(pou?.sourceFile, source);
  assert.equal(pou?.implementation, 'Speed := fActualVelocity;\nfSetVelocity := Speed;');
  assert.equal('get' in pou || 'set' in pou || 'accessor' in pou, false);
  assert.equal(await readFile(project, 'utf8'), projectText);
  assert.equal(await readFile(source, 'utf8'), sourceText);
  const evidence = { schemaVersion: 1, utc: new Date().toISOString(), applicationVersion: manifest.version,
    applicationManifestSha256: digest(await readFile(join(application, 'application-manifest.json'))),
    engineeringManifestSha256: digest(await readFile(join(engineeringRoot, 'application-manifest.json'))),
    provenance, input: { projectText, sourceText }, list, content,
    finding: 'Packaged engineering MCP returns Get/Set concatenated without accessor boundaries. C1 must report ambiguity, not invent a split.',
    scope: 'Real packaged Node/MCP, synthetic configuredProjectFiles only; no IDE, provider, PLC, ADS, write or compiler action.' };
  await writeFile(output, JSON.stringify(evidence, null, 2) + '\n', { flag: 'wx' });
  console.log('PASS actual packaged accessor output; input files unchanged; accessor identity is absent. Evidence: ' + output);
} finally { await rm(temporary, { recursive: true, force: true }); }
