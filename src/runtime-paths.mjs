import { lstat, readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, relative, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyPackageIfPresent } from './package-integrity.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
export async function runtimePaths(mode = process.env.TWINCAT_ASSISTANT_RUNTIME) {
  if (await verifyPackageIfPresent()) {
    if (mode !== undefined) throw new Error('Runtime selection is unavailable in an application package');
    return { opencode: join(root, 'runtime/opencode/opencode.exe'),
      openchamber: join(root, 'upstream/node_modules/@openchamber/web/bin/cli.js'), sourceContract: 1 };
  }
  if (mode !== undefined && !['source', 'distribution'].includes(mode)) throw new Error('Invalid runtime selection');
  const receiptFile = join(root, 'artifacts/source-runtime/runtime.json');
  const stat = await lstat(receiptFile).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
  if (mode === 'source' || (mode !== 'distribution' && stat)) {
    if (!stat?.isFile() || stat.isSymbolicLink() || stat.size > 16384) throw new Error('Source runtime has not been built');
    const receipt = JSON.parse(await readFile(receiptFile, 'utf8'));
    const lock = JSON.parse(await readFile(join(root, 'upstream/source-lock.json'), 'utf8'));
    if (receipt.version !== 1 || receipt.sourceContract !== 1 || receipt.platform !== process.platform || receipt.arch !== process.arch)
      throw new Error('Source runtime contract/platform mismatch');
    for (const name of ['opencode', 'openchamber']) {
      if (receipt[name]?.revision !== lock[name].revision || receipt[name]?.version !== lock[name].version) throw new Error('Source runtime revision mismatch');
    }
    const edits = JSON.parse(await readFile(join(root, 'upstream/overlays/openchamber/changes.json'), 'utf8'));
    const replacements = edits.filter(edit => edit.replacement !== undefined).map(edit => {
      if (!/^(server\/[a-z0-9-]+\.js|ui\/[a-z0-9-]+\.tsx)$/.test(edit.replacement)) throw new Error('Invalid source replacement input');
      return 'upstream/overlays/openchamber/' + edit.replacement;
    });
    for (const file of ['upstream/source-lock.json', 'upstream/overlays/openchamber/changes.json',
      'upstream/overlays/openchamber/twincat.ts', 'upstream/overlays/openchamber/install.ts',
      'scripts/source-overlay.mjs', ...replacements,
      'tools/package-lock.json', 'scripts/build-source-runtime.mjs']) {
      if (receipt.inputs?.[file] !== createHash('sha256').update(await readFile(join(root, file))).digest('hex'))
        throw new Error('Source build inputs changed; rebuild the runtime');
    }
    const path = value => {
      if (typeof value !== 'string' || !value.startsWith('local-data/upstream/')) throw new Error('Invalid source runtime path');
      const result = resolve(root, value);
      if (relative(join(root, 'local-data/upstream'), result).startsWith('..')) throw new Error('Invalid source runtime path');
      return result;
    };
    for (const file of [receipt.opencode.path, receipt.openchamber.path,
      'local-data/upstream/openchamber/packages/web/dist/index.html']) {
      const expected = receipt.hashes?.[file];
      if (!/^[a-f0-9]{64}$/.test(expected) || createHash('sha256').update(await readFile(path(file))).digest('hex') !== expected)
        throw new Error('Source runtime artifact changed; rebuild it');
    }
    return { opencode: path(receipt.opencode.path), openchamber: path(receipt.openchamber.path), sourceContract: 1 };
  }
  const cli = join(root, 'upstream/node_modules/@opencode/cli');
  // The pinned package installs its declared bin target on every platform; its
  // .exe suffix is also used for the Linux/macOS native binary, not just Windows.
  const binary = JSON.parse(await readFile(join(cli, 'package.json'), 'utf8')).bin?.opencode;
  if (!['./bin/opencode', './bin/opencode.exe'].includes(binary)) throw new Error('Unexpected OpenCode binary declaration');
  return { opencode: resolve(cli, binary),
    openchamber: join(root, 'upstream/node_modules/@openchamber/web/bin/cli.js'), sourceContract: 0 };
}
