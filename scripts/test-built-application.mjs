import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { createServer } from 'node:net';
import { randomBytes } from 'node:crypto';
import { verifyManifest } from './application-manifest.mjs';
import { probeServer } from '../src/probe.mjs';
import { runtimeEnvironment } from '../src/state.mjs';

const application = resolve(process.argv[2] ?? '');
if (!process.argv[2]) throw new Error('Supply an application package directory');
await verifyManifest(application);
const temporary = await mkdtemp(join(tmpdir(), 'tc-built-application-'));
const state = join(temporary, 'state');
const node = join(application, 'runtime/node/node.exe');
let child, closed;
async function start() {
  const reservation = createServer();
  reservation.listen(0, '127.0.0.1'); await once(reservation, 'listening');
  const port = reservation.address().port;
  await new Promise(resolve => reservation.close(resolve));
  const password = randomBytes(32).toString('hex');
  child = spawn(node, [join(application, 'src/native-chat.mjs')], { cwd: temporary, windowsHide: true, stdio: ['pipe','pipe','pipe'] });
  closed = once(child, 'close');
  const ready = new Promise((resolve, reject) => {
    let output = '';
    child.once('error', reject);
    child.once('close', () => reject(new Error('Packaged launcher exited before readiness')));
    child.stdout.on('data', chunk => {
      output += chunk;
      if (output.length > 4096) return reject(new Error('Invalid native readiness output'));
      if (output.includes('\n')) {
        try { const value = JSON.parse(output.split('\n')[0]); assert.deepEqual(value, { status: 'ready', port }); resolve(); }
        catch { reject(new Error('Packaged launcher did not report readiness')); }
      }
    });
  });
  child.stderr.resume();
  child.stdin.write(JSON.stringify({ state, port, password, mcpConfig: join(application, 'runtime/mcp/default-settings.json') }) + '\n');
  let timer;
  try { await Promise.race([ready, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Packaged readiness timed out')), 90000); })]); }
  finally { clearTimeout(timer); }
  const response = await fetch(`http://127.0.0.1:${port}/auth/session`);
  assert.equal(response.status, 401);
  const auth = await response.json(); assert.equal(auth.authenticated, false); assert.equal(auth.locked, true);
}
async function stop() {
  if (!child) return;
  child.stdin.end();
  let timer;
  try { await Promise.race([closed, new Promise(resolve => { timer = setTimeout(() => { child.kill(); resolve(); }, 10000); })]); await closed; }
  finally { clearTimeout(timer); child = null; }
}
try {
  await start();
  const solution = join(temporary, 'Synthetic.sln'), project = join(temporary, 'Synthetic.plcproj');
  await writeFile(solution, 'synthetic');
  const projectText = '<Project><ItemGroup><Compile Include="FB_Probe.TcPOU" /></ItemGroup></Project>';
  await writeFile(project, projectText);
  await writeFile(join(temporary, 'FB_Probe.TcPOU'), '<TcPlcObject><POU Name="FB_Probe" Id="{013d444c-37b8-4280-a11c-930ae7545600}"><Declaration><![CDATA[FUNCTION_BLOCK FB_Probe]]></Declaration><Implementation><ST><![CDATA[// combined-package-probe]]></ST></Implementation></POU></TcPlcObject>');
  const request = { state, mcpConfig: join(application, 'runtime/mcp/default-settings.json'), solution, project };
  const setup = spawn(node, [join(application, 'src/native-mcp.mjs')], { cwd: temporary, windowsHide: true, stdio: ['pipe','pipe','pipe'] });
  let output = ''; setup.stdout.on('data', chunk => { output += chunk; }); setup.stderr.resume();
  const setupClosed = once(setup, 'close'); setup.stdin.end(JSON.stringify(request) + '\n');
  const [code] = await setupClosed; assert.equal(code, 0, 'Packaged project configuration failed');
  const location = JSON.parse(output);
  const config = JSON.parse(await readFile(join(location.workspace, 'opencode.json'), 'utf8'));
  const settings = JSON.parse(await readFile(join(state, 'settings.json'), 'utf8'));
  const options = { env: runtimeEnvironment(state, settings), cwd: location.workspace, timeout: 30000 };
  const engineering = await probeServer('engineering', config.mcp.servers.engineering, { name: 'plc_read_pou', arguments: { pou: 'FB_Probe' } }, options);
  assert.notEqual(engineering.result.isError, true); assert.match(JSON.stringify(engineering.result), /combined-package-probe/);
  const knowledge = await probeServer('knowledge', config.mcp.servers.knowledge, { name: 'tc_get_corpus_status', arguments: {} }, options);
  assert.equal(knowledge.result.structuredContent.status, 'not_available');
  assert.equal(await readFile(project, 'utf8'), projectText);
  const sentinel = join(state, 'data/build-smoke-sentinel'); await writeFile(sentinel, 'preserved');
  await stop(); await start();
  assert.equal(await readFile(sentinel, 'utf8'), 'preserved');
  await stop();
  await verifyManifest(application);
  console.log('Built package passed protected startup, both real MCP probes, restart, state preservation and unchanged-package checks. No provider or PLC action.');
} finally {
  await stop();
  await rm(temporary, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
}
