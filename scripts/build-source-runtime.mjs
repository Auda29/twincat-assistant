import { execFileSync, spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile, lstat, open } from 'node:fs/promises';
import { dirname, join, resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { applySourceOverlay, replacementInputs } from './source-overlay.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const lock = JSON.parse(await readFile(join(root, 'upstream/source-lock.json'), 'utf8'));
const sources = join(root, 'local-data/upstream');
const artifacts = join(root, 'artifacts/source-runtime');
const prepareOnly = process.argv.includes('--prepare');
const baseline = process.argv.includes('--baseline');
const uiOnly = process.argv.includes('--ui-only');
const bun = resolve(process.env.TWINCAT_BUILD_BUN ?? join(root, 'tools/node_modules/bun/bin', process.platform === 'win32' ? 'bun.exe' : 'bun'));
const env = { ...process.env, PATH: dirname(bun) + (process.platform === 'win32' ? ';' : ':') + process.env.PATH,
  OPENCODE_VERSION: lock.opencode.version, OPENCODE_CHANNEL: 'latest', HUSKY: '0', ELECTRON_SKIP_BINARY_DOWNLOAD: '1' };
const git = (cwd, args) => execFileSync('git', args, { cwd, encoding: 'utf8', windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const hash = value => createHash('sha256').update(value).digest('hex');
await mkdir(sources, { recursive: true });
await mkdir(artifacts, { recursive: true });
const preparedFile = join(artifacts, 'prepared.json');
const previous = await readFile(preparedFile, 'utf8').then(JSON.parse).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
for (const name of ['opencode', 'openchamber']) {
  const spec = lock[name];
  const target = join(sources, name);
  if (!await lstat(target).catch(error => { if (error.code === 'ENOENT') return null; throw error; })) {
    git(sources, ['clone', '--depth', '1', '--branch', spec.tag, '--single-branch', spec.repository, name]);
  }
  if (git(target, ['rev-parse', 'HEAD']) !== spec.revision) throw new Error('Unexpected ' + name + ' source revision');
  const pkg = JSON.parse(await readFile(join(target, 'package.json'), 'utf8'));
  if (pkg.version !== spec.version || pkg.packageManager !== 'bun@' + lock.bun) throw new Error('Source/package version mismatch');
}
const chamber = join(sources, 'openchamber');
const edits = JSON.parse(await readFile(join(root, 'upstream/overlays/openchamber/changes.json'), 'utf8'));
const expected = new Map();
for (const edit of edits) {
  const original = execFileSync('git', ['show', lock.openchamber.revision + ':' + edit.file], { cwd: chamber, encoding: 'utf8', windowsHide: true });
  const changed = await applySourceOverlay(original, edit);
  expected.set(edit.file, baseline ? original : changed);
}
const normalize = value => value.replace(/\r\n/g, '\n');
const changedPaths = git(chamber, ['diff', '--name-only', 'HEAD']).split('\n').filter(Boolean);
if (changedPaths.some(path => !expected.has(path)) || git(join(sources, 'opencode'), ['diff', '--name-only', 'HEAD']))
  throw new Error('Unreviewed changes in upstream sources');
for (const [file, changed] of expected) {
  const target = join(chamber, file);
  const actual = normalize(await readFile(target, 'utf8'));
  const original = execFileSync('git', ['show', lock.openchamber.revision + ':' + file], { cwd: chamber, encoding: 'utf8', windowsHide: true });
  if (actual !== original && actual !== changed && previous?.files?.[file] !== hash(actual)) throw new Error('Source file has unrelated changes: ' + file);
  await writeFile(target, changed);
}
await writeFile(preparedFile, JSON.stringify({ version: 1, revision: lock.openchamber.revision,
  files: Object.fromEntries([...expected].map(([file, content]) => [file, hash(content)])) }, null, 2) + '\n');
if (!baseline) {
  const target = join(chamber, 'packages/ui/src/lib/twincat');
  await mkdir(target, { recursive: true });
  for (const file of ['twincat.ts', 'install.ts']) {
    const content = await readFile(join(root, 'upstream/overlays/openchamber', file));
    await writeFile(join(target, file), content);
  }
}
if (prepareOnly) { console.log('Prepared exact source revisions and reviewed TwinCAT overlay.'); process.exit(0); }
if (execFileSync(bun, ['--version'], { encoding: 'utf8', windowsHide: true }).trim() !== lock.bun) throw new Error('Use Bun ' + lock.bun);
async function run(name, cwd, args) {
  const log = await open(join(artifacts, name + '.log'), 'w');
  try {
    console.log(name + '…');
    const child = spawn(bun, args, { cwd, env, windowsHide: true, stdio: ['ignore', log.fd, log.fd] });
    await new Promise((resolve, reject) => { child.once('error', reject); child.once('close', code => code === 0 ? resolve() : reject(new Error(name + ' failed; see artifacts/source-runtime/' + name + '.log'))); });
  } catch (error) {
    console.error((await readFile(join(artifacts, name + '.log'), 'utf8')).slice(-10000));
    throw error;
  } finally { await log.close(); }
}
const code = join(sources, 'opencode');
if (uiOnly) {
  const receipt = JSON.parse(await readFile(join(artifacts, 'runtime.json'), 'utf8'));
  if (receipt.opencode.revision !== lock.opencode.revision || receipt.platform !== process.platform || receipt.arch !== process.arch
    || createHash('sha256').update(await readFile(resolve(root, receipt.opencode.path))).digest('hex') !== receipt.hashes[receipt.opencode.path])
    throw new Error('Build the unchanged OpenCode source first');
} else {
  await run('opencode-install', code, ['install', '--frozen-lockfile', '--filter', '@opencode/cli', '--filter', '@opencode/app', '--ignore-scripts', '--cache-dir', join(root, 'local-data/bun-cache-opencode'), '--backend', 'copyfile']);
  await run('opencode-pty', code, ['run', '--cwd', 'packages/core', 'fix-node-pty']);
  await run('opencode-build', code, ['run', '--cwd', 'packages/cli', 'build', '--single', '--skip-install']);
}
await run('openchamber-install', chamber, ['install', '--frozen-lockfile', '--ignore-scripts', '--cache-dir', join(root, 'local-data/bun-cache-openchamber'), '--backend', 'copyfile']);
await run('openchamber-sdk', chamber, ['run', '--cwd', 'packages/sdk', 'build']);
await run('openchamber-build', chamber, ['run', 'build:web']);
const platform = process.platform === 'win32' ? 'windows' : process.platform;
const opencode = join(code, `packages/cli/dist/cli-${platform}-${process.arch}/bin/opencode${process.platform === 'win32' ? '.exe' : ''}`);
const openchamber = join(chamber, 'packages/web/bin/cli.js');
const files = [opencode, openchamber, join(chamber, 'packages/web/dist/index.html')];
const hashes = Object.fromEntries(await Promise.all(files.map(async file => [relative(root, file).replace(/\\/g, '/'), createHash('sha256').update(await readFile(file)).digest('hex')])));
const receipt = { version: 1, sourceContract: baseline ? 0 : 1, platform: process.platform, arch: process.arch,
  opencode: { path: relative(root, opencode).replace(/\\/g, '/'), ...lock.opencode },
  openchamber: { path: relative(root, openchamber).replace(/\\/g, '/'), ...lock.openchamber }, hashes };
receipt.inputs = Object.fromEntries(await Promise.all(['upstream/source-lock.json', 'upstream/overlays/openchamber/changes.json',
  'upstream/overlays/openchamber/twincat.ts', 'upstream/overlays/openchamber/install.ts',
  'scripts/source-overlay.mjs', ...replacementInputs(edits),
  'tools/package-lock.json', 'scripts/build-source-runtime.mjs']
  .map(async file => [file, hash(await readFile(join(root, file)))])));
await writeFile(join(artifacts, baseline ? 'baseline.json' : 'runtime.json'), JSON.stringify(receipt, null, 2) + '\n');
console.log('Built existing OpenCode and OpenChamber sources. Runtime receipt written.');
