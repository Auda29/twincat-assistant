import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, access, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { trimChatPackage } from '../scripts/trim-chat-package.mjs';
import { applySourceOverlay } from '../scripts/source-overlay.mjs';
import { createHash } from 'node:crypto';

test('whole-module overlays reject a changed baseline and escaping replacement paths', async () => {
  const source = 'original module\n';
  const edit = { file: 'module.js', originalSha256: createHash('sha256').update(source).digest('hex'), replacement: 'server/chat-cli.js' };
  assert.match(await applySourceOverlay(source.replaceAll('\n', '\r\n'), edit), /TwinCAT Assistant Chat/);
  await assert.rejects(applySourceOverlay('modified', edit), /baseline changed/);
  await assert.rejects(applySourceOverlay(source, { ...edit, replacement: '../secret.js' }), /Invalid module replacement/);
});

test('delivery removes orphan feature code and native dependencies but preserves shared dependencies and notices', async () => {
  const stage = await mkdtemp(join(tmpdir(), 'assistant-lean-package-'));
  async function put(path, text = '') { const target = join(stage, path); await mkdir(dirname(target), { recursive: true }); await writeFile(target, text); }
  const base = 'upstream/node_modules/';
  const web = base + '@openchamber/web/';
  try {
    await put(web + 'package.json', JSON.stringify({ name: '@openchamber/web', dependencies: { 'node-pty': '1', 'sherpa-onnx-node': '1', ws: '1' } }));
    await put(web + 'bin/cli.js', "import '../server/index.js';");
    await put(web + 'bin/lib/unused-service.js', '// removed CLI service');
    await put(web + 'server/index.js', "import './lib/opencode/core.js';");
    await put(web + 'server/lib/opencode/core.js', "import 'ws';");
    await put(web + 'server/lib/terminal/runtime.js', "import 'node-pty';");
    await put(web + 'server/lib/terminal/LICENSE', 'Retained origin notice');
    for (const [name, dependencies] of [['node-pty', { shared: '1', 'pty-private': '1' }], ['sherpa-onnx-node', { 'sherpa-onnx-win-x64': '1' }],
      ['sherpa-onnx-win-x64', {}], ['ws', { shared: '1' }], ['shared', {}], ['pty-private', {}]]) {
      await put(base + name + '/package.json', JSON.stringify({ name, dependencies }));
    }
    const result = await trimChatPackage(stage);
    assert.equal(result.removedServerFiles, 2);
    assert.ok(result.removedPackages.includes('sherpa-onnx-win-x64'));
    assert.ok(result.removedPackages.includes('pty-private'));
    for (const path of ['node-pty', 'sherpa-onnx-node', 'sherpa-onnx-win-x64', 'pty-private']) await assert.rejects(access(join(stage, base, path)));
    for (const path of [base + 'shared/package.json', base + 'ws/package.json', web + 'server/lib/opencode/core.js', web + 'server/lib/terminal/LICENSE']) await access(join(stage, path));
    const pkg = JSON.parse(await readFile(join(stage, web, 'package.json')));
    assert.deepEqual(pkg.dependencies, { ws: '1' });
  } finally { await rm(stage, { recursive: true, force: true }); }
});
