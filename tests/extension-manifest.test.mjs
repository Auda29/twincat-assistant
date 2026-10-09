import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const manifestPath = join(root, 'extensions/twincat-assistant/package.json');

test('extension manifest is installable and requests no engineering capabilities', async () => {
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  const contributes = manifest.openchamber.contributes;

  assert.equal(manifest.name, 'twincat-assistant');
  assert.equal(manifest.openchamber.apiVersion, 1);
  assert.equal(contributes.panel.entry, 'panel/index.html');
  assert.equal(contributes.attach, 'panel');
  assert.deepEqual(contributes.capabilities, []);
  assert.equal('service' in contributes, false);
  assert.equal('integration' in contributes, false);
  assert.equal('filesystem' in contributes, false);
  assert.equal('origins' in contributes, false);
  assert.deepEqual(contributes.tools.map(({ match }) => match), ['knowledge_*', 'engineering_*']);
});

test('production panel uses the SDK host boundary and safe text rendering', async () => {
  const source = await readFile(join(root, 'extensions/twincat-assistant/panel/main.mjs'), 'utf8');
  const presentation = await readFile(join(root, 'extensions/twincat-assistant/panel/presentation.mjs'), 'utf8');
  const production = `${source}\n${presentation}`;

  assert.match(production, /connectHost/);
  assert.match(source, /onReady/);
  assert.match(source, /onSession/);
  assert.match(source, /host\.attach/);
  assert.match(source, /host\.dispose/);
  assert.doesNotMatch(source, /innerHTML/);
  assert.doesNotMatch(source, /localStorage/);
  assert.match(production, /Native TwinCAT bridge: not connected/);
});
