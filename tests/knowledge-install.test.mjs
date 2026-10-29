import assert from 'node:assert/strict';
import test from 'node:test';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const capture = promisify(execFile);
const pin = JSON.parse(await readFile(new URL('../upstream/knowledge-lock.json', import.meta.url), 'utf8'));
const install = root => capture('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
  resolve('scripts/Install-Knowledge.ps1'), '-InstallRoot', root], { windowsHide: true, timeout: 15000 });

test('Knowledge installer refuses an unrelated existing directory without modifying it', { skip: process.platform !== 'win32' }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-knowledge-install-'));
  try {
    await writeFile(join(root, 'keep.txt'), 'existing operator file');
    await assert.rejects(install(root), error => error.code !== 0 && /not a completed managed Knowledge installation/.test(error.stderr));
    assert.equal(await readFile(join(root, 'keep.txt'), 'utf8'), 'existing operator file');
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('Knowledge installer rejects tampered release artifacts before running an installed executable', { skip: process.platform !== 'win32' }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-knowledge-checksum-'));
  try {
    await writeFile(join(root, 'installation.json'), JSON.stringify({ version: pin.version, revision: pin.revision }));
    await mkdir(join(root, 'packages'));
    for (const artifact of pin.artifacts) await writeFile(join(root, 'packages', artifact.name), 'tampered release content');
    await assert.rejects(install(root), error => error.code !== 0 && /checksum mismatch/.test(error.stderr));
    assert.equal(await readFile(join(root, 'packages', pin.artifacts[0].name), 'utf8'), 'tampered release content');
  } finally { await rm(root, { recursive: true, force: true }); }
});
