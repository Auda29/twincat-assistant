import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { readFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { test } from 'node:test';

// Opt-in against the disposable output of Measure-PackageVerificationStartup.
// Never point this at an installed or issued package.
const root = process.env.TC_TEST_SCOPED_PACKAGE;
test('independent later MCP requests and inherited callbacks reject package tampering', { skip: !root, timeout: 180000 }, async () => {
  const selected = resolve(root);
  assert.equal(JSON.parse(await readFile(join(selected, 'application-manifest.json'), 'utf8')).version, '0.0.0-scope-synthetic');
  const code = `
    import assert from 'node:assert/strict';
    import { readFile, writeFile } from 'node:fs/promises';
    import { pathToFileURL } from 'node:url';
    import { join } from 'node:path';
    const root=process.argv[1];
    const { withPackageVerificationScope: operation }=await import(pathToFileURL(join(root,'src/package-verification-scope.mjs')));
    const { readNativeMcpSettings: request }=await import(pathToFileURL(join(root,'src/native-mcp.mjs')));
    const settings=join(root,'runtime/mcp/default-settings.json');
    const target=join(root,'package-lock.json');
    const original=await readFile(target);
    let release, later;
    const gate=new Promise(r=>{release=r;});
    await operation(async()=>{
      await Promise.all([request(settings),request(settings)]);
      later=gate.then(()=>request(settings));
    });
    try {
      await writeFile(target,'synthetic tampering of a non-entrypoint');
      await assert.rejects(request(settings),/inventory mismatch/);
      release(); await assert.rejects(later,/inventory mismatch/);
    } finally { await writeFile(target,original); }
  `;
  await promisify(execFile)(join(selected,'runtime/node/node.exe'), ['--input-type=module','-e',code,selected], { windowsHide: true, timeout: 160000 });
});
