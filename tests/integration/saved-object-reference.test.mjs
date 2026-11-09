import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { probeServer } from '../../src/probe.mjs';

const engineering = process.env.TC_ENGINEERING_CHECKOUT;
test('saved references resolve large objects and their members by MCP ID without truncating source', {
  skip: !engineering, timeout: 60000,
}, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-saved-reference-'));
  try {
    const project = join(root, 'Reference.plcproj');
    const sourceFile = join(root, 'FB_Large.TcPOU');
    const config = join(root, 'engineering.json');
    const body = '// saved source\n' + 'bRun := TRUE;\n'.repeat(2000);
    await writeFile(project, '<Project><ItemGroup><Compile Include="FB_Large.TcPOU" /></ItemGroup></Project>');
    await writeFile(sourceFile, '<TcPlcObject><POU Name="FB_Large" Id="{013d444c-37b8-4280-a11c-930ae7545600}">'
      + '<Declaration><![CDATA[FUNCTION_BLOCK FB_Large]]></Declaration><Implementation><ST><![CDATA[' + body
      + ']]></ST></Implementation><Method Name="Execute" Id="{013d444c-37b8-4280-a11c-930ae7545601}">'
      + '<Declaration><![CDATA[METHOD Execute]]></Declaration><Implementation><ST><![CDATA[// exact member]]></ST>'
      + '</Implementation></Method></POU></TcPlcObject>');
    await writeFile(config, JSON.stringify({ targetAmsNetId: '127.0.0.1.1.1', readOnly: true, writeAllowlist: [],
      engineering: { enabled: true, backend: 'configuredProjectFiles', projectFiles: [{ path: project, type: 'plc' }] } }));
    const server = { protocol: 'legacy', command: [process.execPath, resolve(engineering, 'packages/mcp/dist/index.js'), '--config', config] };
    const call = async (name, args) => {
      const response = await probeServer('engineering', server, { name, arguments: args }, { cwd: root, env: process.env });
      assert.notEqual(response.result.isError, true);
      return response.result.structuredContent ?? JSON.parse(response.result.content.find(part => part.type === 'text').text);
    };
    const listed = await call('plc_list_pous', {});
    const pou = listed.pous.find(item => item.name === 'FB_Large');
    assert.ok(pou?.id);
    assert.equal(resolve(pou.sourceFile).toLowerCase(), sourceFile.toLowerCase());
    const saved = await call('plc_read_pou', { pou: pou.id });
    // The saved-file backend normalizes surrounding whitespace.
    assert.equal(saved.pou.implementation, body.trim());
    assert.ok(saved.pou.implementation.length > 16000);
    assert.equal(saved.pou.id, pou.id);
    const member = listed.pous.find(item => item.name === 'Execute');
    assert.ok(member?.id);
    const method = await call('plc_read_pou', { pou: member.id });
    assert.equal(method.pou.id, member.id);
    assert.equal(method.pou.implementation, '// exact member');
    assert.notEqual(method.pou.id, saved.pou.id);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
