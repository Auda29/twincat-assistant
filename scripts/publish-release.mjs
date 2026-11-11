import { readdir, readFile, lstat } from 'node:fs/promises';
import { resolve, join, basename } from 'node:path';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';

const repo = 'Auda29/twincat-assistant';
const gh = (...args) => execFileSync('gh', args, { encoding: 'utf8', stdio: ['ignore','pipe','pipe'] }).trim();
const files = [];
async function walk(root) {
  for (const entry of await readdir(root, { withFileTypes: true })) {
    const file = join(root, entry.name);
    if (entry.isSymbolicLink()) throw new Error('Release links rejected');
    if (entry.isDirectory()) await walk(file);
    else if (entry.isFile()) files.push(file);
    else throw new Error('Invalid release entry');
  }
}
if (process.env.GITHUB_REPOSITORY !== repo || process.env.GITHUB_REF !== 'refs/heads/main'
    || !/^[a-f0-9]{40}$/.test(process.env.GITHUB_SHA ?? '')) throw new Error('Publishing requires the trusted main workflow');
await walk(resolve(process.argv[2] ?? 'release-artifacts'));
const provenanceFile = files.find(file => basename(file) === 'BUILD_PROVENANCE.json');
if (!provenanceFile || files.length !== 3) throw new Error('Expected exactly installer, checksums and provenance');
const provenance = JSON.parse((await readFile(provenanceFile,'utf8')).replace(/^\uFEFF/,''));
const version = provenance.version;
if (!/^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$/.test(version) || provenance.sourceCommit !== process.env.GITHUB_SHA)
  throw new Error('Release source/version mismatch');
const expected = new Set([`TwinCAT-Assistant-${version}-x64-setup.exe`, 'SHA256.txt', 'BUILD_PROVENANCE.json']);
if (files.some(file => !expected.delete(basename(file))) || expected.size) throw new Error('Unexpected release artifacts');
const sums = await readFile(files.find(file => basename(file) === 'SHA256.txt'),'utf8');
for (const file of files.filter(file => basename(file) !== 'SHA256.txt')) {
  const digest = createHash('sha256').update(await readFile(file)).digest('hex');
  if (!sums.split(/\r?\n/).includes(`${digest}  ${basename(file)}`)) throw new Error('Local release checksum mismatch');
}
const tag = 'native-v' + version;
const notes = `docs/releases/${tag}.md`;
await readFile(notes); // refuse missing reviewed English notes before creating a tag
gh('api', `repos/${repo}/git/refs`, '-X', 'POST', '-f', `ref=refs/tags/${tag}`, '-f', `sha=${provenance.sourceCommit}`);
gh('release','create',tag,...files,'--repo',repo,'--verify-tag','--draft','--prerelease=false',
  '--title',`TwinCAT Assistant ${version}`,'--notes-file',notes);
const release = JSON.parse(gh('api',`repos/${repo}/releases?per_page=20`)).find(item => item.tag_name === tag && item.draft);
if (!release) throw new Error('Draft release not found');
for (const file of files) {
  const asset = release.assets.find(asset => asset.name === basename(file));
  const digest = 'sha256:' + createHash('sha256').update(await readFile(file)).digest('hex');
  if (asset?.size !== (await lstat(file)).size || asset.digest !== digest) throw new Error('Uploaded release checksum mismatch; draft retained');
}
gh('release','edit',tag,'--repo',repo,'--draft=false','--prerelease=false','--latest');
const latest = JSON.parse(gh('api',`repos/${repo}/releases/latest`));
if (latest.tag_name !== tag || latest.draft || latest.prerelease) throw new Error('Latest publication was not confirmed');
console.log(latest.html_url);
