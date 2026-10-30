import assert from 'node:assert/strict';
import test from 'node:test';
import fsPromises, { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path, { join } from 'node:path';
import { createThemeRuntime } from '../upstream/node_modules/@openchamber/web/server/lib/opencode/theme-runtime.js';
import { installTwinCatThemes, TWINCAT_DARK, TWINCAT_LIGHT, TWINCAT_THEMES, withTwinCatThemes } from '../src/themes.mjs';

const luminance = hex => {
  const [r, g, b] = [1, 3, 5].map(at => parseInt(hex.slice(at, at + 2), 16) / 255)
    .map(value => value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4);
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
const contrast = (a, b) => { const [x, y] = [luminance(a), luminance(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };

test('the TwinCAT themes pass the pinned OpenChamber theme loader', async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-themes-'));
  try {
    assert.deepEqual(await installTwinCatThemes(root), ['twincat-light', 'twincat-dark']);
    const warnings = [];
    const runtime = createThemeRuntime({ fsPromises, path, themesDir: join(root, 'themes'), maxThemeJsonBytes: 512 * 1024,
      logger: { warn: message => warnings.push(message), info() {}, error() {} } });
    const loaded = await runtime.readCustomThemesFromDisk();
    assert.deepEqual(warnings, []);
    assert.deepEqual(loaded.map(theme => theme.metadata.id).sort(), ['twincat-dark', 'twincat-light']);
    assert.deepEqual(loaded.map(theme => theme.metadata.variant).sort(), ['dark', 'light']);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('TwinCAT theme text stays readable on its surfaces', () => {
  for (const theme of TWINCAT_THEMES) {
    const { surface, status, primary, syntax } = theme.colors;
    for (const background of [surface.background, surface.elevated, surface.muted]) {
      assert.ok(contrast(surface.foreground, background) >= 7, theme.metadata.id + ' body text');
      assert.ok(contrast(surface.mutedForeground, background) >= 4.5, theme.metadata.id + ' secondary text');
    }
    for (const color of [status.error, status.warning, status.success, primary.base])
      assert.ok(contrast(color, surface.background) >= 3, theme.metadata.id + ' accent ' + color);
    for (const background of [primary.base, primary.hover, primary.active])
      assert.ok(contrast(primary.foreground, background) >= 4.5, theme.metadata.id + ' primary button text');
    assert.ok(contrast(syntax.tokens.variableLocal, syntax.base.background) >= 4.5, theme.metadata.id + ' local variables');
  }
  assert.ok(contrast('#ffffff', TWINCAT_LIGHT.colors.primary.base) >= 4.5, 'white text on the light accent');
  assert.equal(TWINCAT_LIGHT.colors.syntax.base.keyword, '#0000ff', 'Structured Text keywords keep the TwinCAT editor blue');
  assert.equal(TWINCAT_DARK.colors.syntax.base.comment, '#6a9955');
});

test('theme files are refreshed in place and other themes stay untouched', async () => {
  const root = await mkdtemp(join(tmpdir(), 'tc-themes-refresh-'));
  try {
    await installTwinCatThemes(root);
    await writeFile(join(root, 'themes', 'twincat-light.json'), '{}');
    await writeFile(join(root, 'themes', 'own.json'), 'operator theme');
    await installTwinCatThemes(root);
    assert.equal(JSON.parse(await readFile(join(root, 'themes', 'twincat-light.json'), 'utf8')).metadata.id, 'twincat-light');
    assert.equal(await readFile(join(root, 'themes', 'own.json'), 'utf8'), 'operator theme');
    assert.deepEqual((await readdir(join(root, 'themes'))).filter(name => name.endsWith('.tmp')), []);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('TwinCAT themes replace only the OpenChamber defaults', () => {
  assert.deepEqual(withTwinCatThemes({}), { lightThemeId: 'twincat-light', darkThemeId: 'twincat-dark' });
  assert.deepEqual(withTwinCatThemes({ lightThemeId: 'openchamber-light', darkThemeId: 'openchamber-dark', projects: [] }),
    { lightThemeId: 'twincat-light', darkThemeId: 'twincat-dark', projects: [] });
  assert.deepEqual(withTwinCatThemes({ lightThemeId: 'ayu-light', darkThemeId: 'openchamber-dark' }),
    { lightThemeId: 'ayu-light', darkThemeId: 'twincat-dark' }, 'a deliberately chosen theme is kept');
});
