import { createHash } from 'node:crypto';
import { lstat, readdir, readFile, writeFile } from 'node:fs/promises';
import { isAbsolute, join } from 'node:path';
import { createReadStream } from 'node:fs';

const manifestName = 'application-manifest.json';
async function digest(path) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest('hex');
}

// A manifest describes regular files only. Reject links instead of following a
// junction outside the staging directory or shipping machine-specific targets.
async function inventory(root) {
  const rootStat = await lstat(root);
  if (!rootStat.isDirectory() || rootStat.isSymbolicLink()) throw new Error('Invalid package root');
  const files = [];
  const names = new Set();
  async function walk(dir, prefix = '') {
    for (const name of (await readdir(dir)).sort()) {
      const relative = prefix ? `${prefix}/${name}` : name;
      if (name === '.' || name === '..' || /[\\:<>"|?*\x00-\x1f]/.test(name) || /[. ]$/.test(name) || /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(name)) throw new Error('Invalid package path');
      const folded = relative.toLowerCase();
      if (names.has(folded)) throw new Error('Ambiguous package path');
      names.add(folded);
      const path = join(dir, name);
      const stat = await lstat(path);
      if (stat.isSymbolicLink()) throw new Error(`Package links are not permitted: ${relative}`);
      if (stat.isDirectory()) await walk(path, relative);
      else if (stat.isFile()) {
        if (relative === manifestName) continue;
        files.push({ path: relative, size: stat.size, sha256: await digest(path) });
      } else throw new Error(`Unsupported package entry: ${relative}`);
    }
  }
  await walk(root);
  return files;
}
function validateMetadata(m) {
  if (!m || m.schemaVersion !== 1 || typeof m.version !== 'string' || !/^[0-9A-Za-z][0-9A-Za-z.+-]*$/.test(m.version)
      || m.platform !== 'win32' || !['x64', 'arm64', 'ia32'].includes(m.architecture)
      || !Array.isArray(m.files) || Object.keys(m).sort().join(',') !== 'architecture,files,platform,schemaVersion,version') throw new Error('Invalid manifest metadata');
}
export async function createManifest(root, { version, architecture }) {
  if (!isAbsolute(root)) throw new Error('Package directory must be absolute');
  // Never silently replace a previously issued manifest.
  const manifest = { schemaVersion: 1, version, platform: 'win32', architecture, files: await inventory(root) };
  validateMetadata(manifest);
  await writeFile(join(root, manifestName), JSON.stringify(manifest, null, 2) + '\n', { flag: 'wx' });
  return manifest;
}
export async function verifyManifest(root) {
  if (!isAbsolute(root)) throw new Error('Package directory must be absolute');
  const path = join(root, manifestName);
  const stat = await lstat(path);
  if (!stat.isFile() || stat.isSymbolicLink() || stat.size > 32 * 1024 * 1024) throw new Error('Invalid manifest file');
  const manifest = JSON.parse(await readFile(path, 'utf8'));
  validateMetadata(manifest);
  const actual = await inventory(root);
  if (JSON.stringify(manifest.files) !== JSON.stringify(actual)) throw new Error('Package inventory mismatch');
  return manifest;
}
