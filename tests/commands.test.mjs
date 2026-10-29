import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, mkdir, readFile, readdir, rm, symlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { installTwinCatCommands, TWINCAT_COMMANDS } from '../src/commands.mjs';

test('TwinCAT commands are written as read-only agent command files and refreshed in place', async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-commands-'));
  try {
    assert.deepEqual(await installTwinCatCommands(root), ['explain', 'review', 'variable', 'docs', 'pou-docs']);
    const files = (await readdir(join(root, 'commands'))).sort();
    assert.deepEqual(files, ['docs.md', 'explain.md', 'pou-docs.md', 'review.md', 'variable.md']);
    for (const name of Object.keys(TWINCAT_COMMANDS)) {
      const text = await readFile(join(root, 'commands', name + '.md'), 'utf8');
      assert.match(text, /^---\ndescription: ".+"\nagent: twincat-readonly\n---\n/);
      assert.ok(text.includes('$ARGUMENTS'), name + ' accepts arguments');
    }
    await writeFile(join(root, 'commands', 'docs.md'), 'stale');
    await writeFile(join(root, 'commands', 'own.md'), 'operator command');
    await installTwinCatCommands(root);
    assert.match(await readFile(join(root, 'commands', 'docs.md'), 'utf8'), /knowledge_tc_get_corpus_status/);
    assert.equal(await readFile(join(root, 'commands', 'own.md'), 'utf8'), 'operator command', 'other command files stay untouched');
    assert.deepEqual((await readdir(join(root, 'commands'))).filter(name => name.endsWith('.tmp')), []);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('a redirected command directory is refused instead of followed', async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-commands-link-'));
  try {
    const target = join(root, 'elsewhere'); await mkdir(target);
    const config = join(root, 'config'); await mkdir(config);
    try { await symlink(target, join(config, 'commands'), process.platform === 'win32' ? 'junction' : 'dir'); }
    catch { return; }
    await assert.rejects(installTwinCatCommands(config), /regular directory/);
    assert.deepEqual(await readdir(target), []);
  } finally { await rm(root, { recursive: true, force: true }); }
});

