import { mkdtemp, mkdir, readFile, rm, writeFile, symlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createManifest, verifyManifest } from '../scripts/application-manifest.mjs';

test('manifest detects changed, extra and missing files after relocation', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'assistant-manifest-'));
  try {
    const root = join(parent, 'stage'); await mkdir(root); await mkdir(join(root, 'src'));
    await writeFile(join(root, 'src', 'main.mjs'), 'synthetic');
    const created = await createManifest(root, { version: '0.1.0-dev.1', architecture: 'x64' });
    const moved = join(parent, 'moved'); const { rename } = await import('node:fs/promises'); await rename(root, moved);
    assert.deepEqual(await verifyManifest(moved), created);
    await writeFile(join(moved, 'src', 'main.mjs'), 'modified');
    await assert.rejects(verifyManifest(moved));
    await writeFile(join(moved, 'src', 'main.mjs'), 'synthetic');
    await writeFile(join(moved, 'extra'), 'x'); await assert.rejects(verifyManifest(moved));
    await rm(join(moved, 'extra')); await rm(join(moved, 'src', 'main.mjs')); await assert.rejects(verifyManifest(moved));
  } finally { await rm(parent, { recursive: true, force: true }); }
});

test('manifest cannot be overwritten and forged paths are rejected', async () => {
  const root = await mkdtemp(join(tmpdir(), 'assistant-manifest-'));
  try {
    await writeFile(join(root, 'file'), 'x'); await createManifest(root, { version: '1', architecture: 'x64' });
    await assert.rejects(createManifest(root, { version: '1', architecture: 'x64' }));
    const path = join(root, 'application-manifest.json'); const manifest = JSON.parse(await readFile(path));
    manifest.files[0].path = '../file'; await writeFile(path, JSON.stringify(manifest));
    await assert.rejects(verifyManifest(root));
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('redirected subdirectories and roots are rejected', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'assistant-manifest-link-'));
  try {
    const root = join(parent, 'root'), other = join(parent, 'other');
    await mkdir(root); await mkdir(other);
    await symlink(other, join(root, 'redirect'), process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(createManifest(root, { version: '1', architecture: 'ia32' }), /links/);
    await symlink(other, join(parent, 'root-link'), process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(createManifest(join(parent, 'root-link'), { version: '1', architecture: 'ia32' }), /package root/);
  } finally { await rm(parent, { recursive: true, force: true }); }
});
