import { lstat, readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';
import { verifyManifest } from '../scripts/application-manifest.mjs';

export const applicationRoot = fileURLToPath(new URL('../', import.meta.url));
// The builder changes this constant only in the staged copy.
export const requirePackage = false;
const exists = path => lstat(path).catch(error => { if (error.code === 'ENOENT') return null; throw error; });

// The marker is part of the inventory. A manifest without the marker is never a
// development checkout. This is corruption detection, not a trust signature.
export async function verifyPackageIfPresent() {
  const marker = join(applicationRoot, 'src/package-runtime.json');
  const markerStat = await exists(marker);
  const manifestStat = await exists(join(applicationRoot, 'application-manifest.json'));
  if (!markerStat && !manifestStat && !requirePackage) return false;
  if (!markerStat?.isFile() || markerStat.isSymbolicLink() || !manifestStat) throw new Error('Incomplete application package');
  const manifest = await verifyManifest(applicationRoot);
  if (manifest.platform !== process.platform || manifest.architecture !== process.arch) throw new Error('Application package platform mismatch');
  const config = JSON.parse(await readFile(marker, 'utf8'));
  if (JSON.stringify(config) !== JSON.stringify({ version: 1 })) throw new Error('Invalid application package marker');
  return true;
}
