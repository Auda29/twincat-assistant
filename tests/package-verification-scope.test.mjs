import assert from 'node:assert/strict';
import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { test } from 'node:test';
import { createManifest } from '../scripts/application-manifest.mjs';

async function fixture(t) {
  const base = await mkdtemp(join(tmpdir(), 'tc-inventory-scope-'));
  t.after(() => rm(base, { recursive: true, force: true }));
  await mkdir(join(base, 'src')); await mkdir(join(base, 'scripts'));
  await cp(resolve('src/package-verification-scope.mjs'), join(base, 'src/package-verification-scope.mjs'));
  // Count actual full verifier calls, without substituting the inventory algorithm.
  const source = await readFile(resolve('scripts/application-manifest.mjs'), 'utf8');
  await writeFile(join(base, 'scripts/application-manifest.mjs'), source.replace('export async function verifyManifest(root) {', 'export let calls = 0; export async function verifyManifest(root) { calls++;'));
  const scope = await import(pathToFileURL(join(base, 'src/package-verification-scope.mjs')));
  const counter = await import(pathToFileURL(join(base, 'scripts/application-manifest.mjs')));
  const packages = [];
  for (const name of ['old', 'new']) {
    const root = join(base, name); await mkdir(root);
    await writeFile(join(root, 'non-entrypoint.txt'), 'original');
    await createManifest(root, { version: '0.0.0-synthetic', architecture: 'x64' });
    packages.push(root);
  }
  return { ...scope, counter, packages };
}

test('parallel and nested calls verify each full package once per operation', async t => {
  const { withPackageVerificationScope: operation, verifyOperationManifest: verify, counter, packages } = await fixture(t);
  await operation(async () => {
    await Promise.all(packages.flatMap(root => [verify(root), verify(root), operation(() => verify(root))]));
    const result = await verify(packages[0]); result.files.length = 0;
    assert.equal((await verify(packages[0])).files.length, 1);
  });
  assert.equal(counter.calls, 2);
  await operation(() => Promise.all(packages.map(verify)));
  assert.equal(counter.calls, 4);
  await Promise.all([operation(() => verify(packages[0])), operation(() => verify(packages[0]))]);
  assert.equal(counter.calls, 6, 'independent concurrent operations do not share');
});

test('fresh operations, later broker calls and inherited callbacks detect tampering', async t => {
  const { withPackageVerificationScope: operation, verifyOperationManifest: verify, counter, packages: [root] } = await fixture(t);
  let inherited;
  let release;
  const gate = new Promise(r => { release = r; });
  await operation(async () => {
    await verify(root);
    inherited = gate.then(() => verify(root));
  });
  await writeFile(join(root, 'non-entrypoint.txt'), 'tampered');
  await assert.rejects(operation(() => verify(root)), /inventory mismatch/);
  await assert.rejects(verify(root), /inventory mismatch/, 'independent broker call');
  release(); await assert.rejects(inherited, /inventory mismatch/, 'inherited callback cannot reuse expired startup context');
  assert.equal(counter.calls, 4);
});

test('failed operations revoke their contexts and full inventory rejects additions and deletions', async t => {
  const { withPackageVerificationScope: operation, verifyOperationManifest: verify, counter, packages: [root] } = await fixture(t);
  let later;
  await assert.rejects(operation(async () => {
    await verify(root);
    later = () => operation(() => verify(root));
    throw new Error('preparation failed');
  }), /preparation failed/);
  await writeFile(join(root, 'extra.txt'), 'not inventoried');
  await assert.rejects(later(), /inventory mismatch/);
  assert.equal(counter.calls, 2);
  await rm(join(root, 'extra.txt'));
  await rm(join(root, 'non-entrypoint.txt'));
  await assert.rejects(operation(() => verify(root)), /inventory mismatch/);
});
