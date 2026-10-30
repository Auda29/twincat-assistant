import { lstat, mkdir, open, readFile, rename, unlink } from 'node:fs/promises';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';

// TwinCAT color themes for the embedded OpenChamber chat. The light theme
// follows the Visual Studio "Blue" look of the TwinCAT XAE shell with cooler,
// calmer surfaces; the dark theme follows Visual Studio Dark with a slight
// navy tint. Code uses familiar Visual Studio editor colors. The native
// context bar reads its colors back from the active theme.
const metadata = (id, name, variant, description) =>
  ({ id, name, variant, description, version: '1.0.0', author: 'TwinCAT Assistant', tags: [variant, 'twincat', 'visual-studio'] });

export const TWINCAT_LIGHT = Object.freeze({
  metadata: metadata('twincat-light', 'TwinCAT', 'light', 'Visual Studio Blue surfaces with TwinCAT blue accents and Structured Text editor colors'),
  colors: {
    primary: { base: '#1e63b8', hover: '#1a56a0', active: '#164a8a', foreground: '#ffffff', muted: '#1e63b880' },
    surface: { background: '#fbfcfe', foreground: '#1c2433', muted: '#f1f4f9', mutedForeground: '#566176', elevated: '#ffffff', subtle: '#f6f8fb', overlay: '#1c243320' },
    interactive: { border: '#d8dee8', selection: '#1e63b829', borderHover: '#b9c3d3', focusRing: '#1e63b855', hover: '#1c24330d', active: '#1c243314' },
    status: { error: '#c4314b', warning: '#9a6700', success: '#2e7d32', successForeground: '#ffffff', info: '#1e63b8' },
    syntax: {
      base: { background: '#f5f7fa', foreground: '#1c2433', comment: '#008000', keyword: '#0000ff', string: '#a31515', number: '#098658',
        function: '#795e26', variable: '#001080', type: '#267f99', operator: '#383a42' },
      tokens: { stringEscape: '#ee0000', variableProperty: '#001080', variableLocal: '#001080', className: '#267f99', punctuation: '#383a42', url: '#1e63b8' },
      highlights: { diffAdded: '#2e7d32', diffRemoved: '#c4314b', diffModified: '#9a6700', diffModifiedBackground: '#9a670020', lineNumber: '#8a94a6' },
    },
    pr: { draft: '#6e7781', blocked: '#c4314b', merged: '#6f42c1' },
    chat: { userMessageBackground: '#eaf1fb', divider: '#dce2eb' },
    markdown: { link: '#1e63b8', linkHover: '#164a8a', inlineCode: '#0b5cad', inlineCodeBackground: '#eef2f8',
      blockquote: '#566176', blockquoteBorder: '#d8dee8', listMarker: '#1e63b899' },
    tools: { border: '#d8dee899', icon: '#566176', title: '#1c2433', description: '#566176',
      edit: { addedBackground: '#2e7d3222', removedBackground: '#c4314b22', lineNumber: '#8a94a6' } },
  },
});

export const TWINCAT_DARK = Object.freeze({
  metadata: metadata('twincat-dark', 'TwinCAT', 'dark', 'Visual Studio Dark surfaces with a navy tint, TwinCAT blue accents and Structured Text editor colors'),
  colors: {
    primary: { base: '#3d8bfd', hover: '#5a9dff', active: '#77aeff', foreground: '#071a33', muted: '#3d8bfd80' },
    surface: { background: '#1b1e24', foreground: '#e3e7ee', muted: '#22262e', mutedForeground: '#9aa3b2', elevated: '#242933', subtle: '#1f232a', overlay: '#00000066' },
    interactive: { border: '#343a46', selection: '#3d8bfd33', borderHover: '#4a5263', focusRing: '#3d8bfd66', hover: '#ffffff12', active: '#ffffff1f' },
    status: { error: '#f0707a', warning: '#e0b44c', success: '#6cc17a', info: '#5aa2ff' },
    syntax: {
      base: { background: '#1e2228', foreground: '#e3e7ee', comment: '#6a9955', keyword: '#569cd6', string: '#ce9178', number: '#b5cea8',
        function: '#dcdcaa', variable: '#9cdcfe', type: '#4ec9b0', operator: '#d4d4d4' },
      tokens: { commentDoc: '#608b4e', stringEscape: '#d7ba7d', variableProperty: '#9cdcfe', variableLocal: '#9cdcfe', className: '#4ec9b0', punctuation: '#a0a8b5' },
      highlights: { diffAdded: '#6cc17a', diffRemoved: '#f0707a', diffModified: '#5aa2ff', lineNumber: '#4b5363' },
    },
    pr: { open: '#6cc17a', blocked: '#f0707a', merged: '#b392f0' },
    chat: { userMessageBackground: '#1f2a3a', divider: '#2e343f' },
    markdown: { link: '#5aa2ff', linkHover: '#7db6ff', inlineCode: '#9cdcfe', inlineCodeBackground: '#262b34',
      blockquote: '#9aa3b2', blockquoteBorder: '#343a46', listMarker: '#3d8bfd99' },
    tools: { border: '#343a4699', icon: '#a7b0be', title: '#e3e7ee', description: '#9aa3b2',
      edit: { addedBackground: '#6cc17a22', removedBackground: '#f0707a22', lineNumber: '#4b5363' } },
  },
});

export const TWINCAT_THEMES = Object.freeze([TWINCAT_LIGHT, TWINCAT_DARK]);
const DEFAULT_THEMES = Object.freeze({ lightThemeId: 'openchamber-light', darkThemeId: 'openchamber-dark' });

async function writeOwnedFile(directory, name, content) {
  const path = join(directory, name);
  const existing = await lstat(path).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
  if (existing && (!existing.isFile() || existing.isSymbolicLink())) throw new Error('Theme file must be a regular file');
  if (existing && await readFile(path, 'utf8') === content) return;
  const temporary = join(directory, '.' + name + '-' + randomUUID() + '.tmp');
  try {
    const handle = await open(temporary, 'wx', 0o600);
    try { await handle.writeFile(content); await handle.sync(); } finally { await handle.close(); }
    await rename(temporary, path);
  } finally { await unlink(temporary).catch(error => { if (error.code !== 'ENOENT') throw error; }); }
}

// Writes the TwinCAT theme files into OpenChamber's custom theme directory.
// Only these two files are touched; a redirected directory is refused.
export async function installTwinCatThemes(openchamberDirectory) {
  const directory = join(openchamberDirectory, 'themes');
  try { await mkdir(directory, { mode: 0o700 }); }
  catch (error) { if (error.code !== 'EEXIST') throw error; }
  const metadata = await lstat(directory);
  if (!metadata.isDirectory() || metadata.isSymbolicLink()) throw new Error('Theme directory must be a regular directory');
  for (const theme of TWINCAT_THEMES) await writeOwnedFile(directory, theme.metadata.id + '.json', JSON.stringify(theme, null, 2) + '\n');
  return TWINCAT_THEMES.map(theme => theme.metadata.id);
}

// Selects the TwinCAT themes while the operator still uses OpenChamber's
// defaults. A theme the operator chose deliberately is kept.
export function withTwinCatThemes(preferences) {
  const value = { ...preferences };
  for (const [key, fallback] of Object.entries(DEFAULT_THEMES)) {
    const twinCat = key === 'lightThemeId' ? TWINCAT_LIGHT.metadata.id : TWINCAT_DARK.metadata.id;
    if (value[key] === undefined || value[key] === fallback) value[key] = twinCat;
  }
  return value;
}

// Current OpenChamber profiles are authoritative in preferences.json. Updating
// only its legacy settings.json mirror is undone by the first server read/write.
// Keep the upstream schema, per-surface choices and unrelated field stamps.
export async function migrateTwinCatThemePreferences(openchamberDirectory, readPreferences) {
  const path = join(openchamberDirectory, 'preferences.json');
  const metadata = await lstat(path).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
  if (!metadata) return false; // The upstream server seeds a fresh profile itself.
  if (!metadata.isFile() || metadata.isSymbolicLink()) throw new Error('Theme preferences must be a regular file');
  const document = await readPreferences(path);
  const { parsePreferencesDocument } = await import('../upstream/node_modules/@openchamber/web/server/lib/opencode/settings-files.js');
  if (!parsePreferencesDocument(JSON.stringify(document)).ok) throw new Error('Unsupported or invalid theme preferences');
  const value = structuredClone(document);
  const defaults = { 'openchamber-light': 'twincat-light', 'openchamber-dark': 'twincat-dark' };
  const now = Date.now();
  let changed = false;
  const migrate = entry => {
    if (!entry || typeof entry.value !== 'string' || !Object.hasOwn(defaults, entry.value)) return;
    entry.value = defaults[entry.value];
    entry.updatedAt = now;
    changed = true;
  };
  for (const key of ['lightThemeId', 'darkThemeId', 'themeId']) {
    const entry = value.fields[key];
    if (!entry) continue;
    migrate(entry);
    for (const surface of Object.values(entry.surfaces ?? {})) migrate(surface);
  }
  if (changed) await writeOwnedFile(openchamberDirectory, 'preferences.json', JSON.stringify(value, null, 2) + '\n');
  return changed;
}
