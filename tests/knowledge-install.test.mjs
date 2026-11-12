import assert from 'node:assert/strict';
import test from 'node:test';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { copyFile, mkdir, mkdtemp, readFile, rm, writeFile, access } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const capture = promisify(execFile);
// Copy the actual released lock into an isolated repository; the live repository
// may intentionally be pinned to a development build that must never be installed.
const pin = JSON.parse(await readFile(new URL('./fixtures/knowledge-release-lock.json', import.meta.url), 'utf8'));
async function installerFixture(parent, lock = pin) {
  const repository = join(parent, 'repository');
  await mkdir(join(repository, 'scripts'), { recursive: true });
  await mkdir(join(repository, 'upstream'));
  const script = join(repository, 'scripts', 'Install-Knowledge.ps1');
  await copyFile(resolve('scripts/Install-Knowledge.ps1'), script);
  await writeFile(join(repository, 'upstream', 'knowledge-lock.json'), JSON.stringify(lock));
  return root => capture('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    script, '-InstallRoot', root], { windowsHide: true, timeout: 15000 });
}

test('Knowledge installer refuses an unrelated existing directory without modifying it', { skip: process.platform !== 'win32' }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-knowledge-install-'));
  try {
    const install = await installerFixture(root);
    const target = join(root, 'existing'); await mkdir(target);
    await writeFile(join(target, 'keep.txt'), 'existing operator file');
    await assert.rejects(install(target), error => error.code !== 0 && /not a completed managed Knowledge installation/.test(error.stderr));
    assert.equal(await readFile(join(target, 'keep.txt'), 'utf8'), 'existing operator file');
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('Knowledge installer rejects tampered release artifacts before running an installed executable', { skip: process.platform !== 'win32' }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-knowledge-checksum-'));
  try {
    const install = await installerFixture(root);
    const target = join(root, 'existing'); await mkdir(target);
    await writeFile(join(target, 'installation.json'), JSON.stringify({ version: pin.version, revision: pin.revision }));
    await mkdir(join(target, 'packages'));
    for (const artifact of pin.artifacts) await writeFile(join(target, 'packages', artifact.name), 'tampered release content');
    await assert.rejects(install(target), error => error.code !== 0 && /checksum mismatch/.test(error.stderr));
    assert.equal(await readFile(join(target, 'packages', pin.artifacts[0].name), 'utf8'), 'tampered release content');
  } finally { await rm(root, { recursive: true, force: true }); }
});


test('public installer refuses a development-only pin before touching an install root', { skip: process.platform !== 'win32' }, async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-knowledge-dev-pin-'));
  try {
    // A release-shaped version ensures the explicit development guard, rather
    // than the generic version check, is responsible for refusal.
    const install = await installerFixture(root, { ...pin, developmentOnly: true });
    const target = join(root, 'not-created');
    await assert.rejects(install(target), error => error.code !== 0 && /Development-only Knowledge pins cannot be installed/.test(error.stderr));
    await assert.rejects(access(target), { code: 'ENOENT' });
    const existing = join(root, 'existing'); await mkdir(existing);
    await writeFile(join(existing, 'keep.txt'), 'kept');
    await assert.rejects(install(existing), error => error.code !== 0 && /Development-only Knowledge pins cannot be installed/.test(error.stderr));
    assert.equal(await readFile(join(existing, 'keep.txt'), 'utf8'), 'kept');
  } finally { await rm(root, { recursive: true, force: true }); }
});
