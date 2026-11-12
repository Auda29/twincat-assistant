import { createHash } from 'node:crypto';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { join } from 'node:path';

// Exact released 0.1.1 module. This is a disclosed Assistant build overlay,
// not a claim that the upstream wheel already contains the correction.
export const originalSha256 = '4290e317146046338571b8a44477a5e831c31a25c05a7110a80e17393474bd63';
export const patchedSha256 = 'a50ff0a720b6b8492ac8d8eac4e4ad6d25d869b179c3050567c6937471c98ad0';
// Canonical Git/archive LF bytes were independently compared with the approved
// CRLF release input: line endings are the only difference. Neither input is
// normalized before verification, and each has its own exact reviewed output.
export const lfOriginalSha256 = '741a3aabe3f6e2832508ea769d5bb6c0a2fac8a9aad6ae56d9d6f0b8cf5dde52';
export const lfPatchedSha256 = 'dce392e65c28d9d46318adcd3ffdb0a889984a32318482eb20185bc22894da81';
const reviewed = Object.freeze({ [originalSha256]: patchedSha256, [lfOriginalSha256]: lfPatchedSha256 });
export function isReviewedKnowledgeOverlay(receipt, actualSha256) {
  return receipt?.version === 1 && Object.hasOwn(reviewed, receipt.originalSha256)
    && reviewed[receipt.originalSha256] === receipt.patchedSha256 && actualSha256 === receipt.patchedSha256;
}
export function optimizeKnowledgeValidation(bytes) {
  const expected = reviewed[createHash('sha256').update(bytes).digest('hex')];
  if (!expected)
    throw new Error('Unknown Knowledge provenance module; overlay review required');
  let source = bytes.toString('utf8');
  source = source.replace('from urllib.parse import', 'from functools import lru_cache\nfrom urllib.parse import');
  source = source.replace('def _validate_locator(', '@lru_cache(maxsize=4096)\ndef _validate_locator(');
  const before = 'any(ord(char) < 32 or ord(char) == 127 for char in ';
  for (const value of ['value', 'decoded'])
    source = source.replaceAll(before + value + ')', 're.search(r"[\\x00-\\x1f\\x7f]", ' + value + ')');
  if (source.includes(before)) throw new Error('Unpatched control validator');
  if (createHash('sha256').update(source).digest('hex') !== expected) throw new Error('Unexpected validation overlay output');
  return source;
}
export async function patchKnowledgeValidation(site, distributionVersion = '0.1.1') {
  if (!/^\d+\.\d+\.\d+(?:\.dev\d+)?$/.test(distributionVersion)) throw new Error('Unknown Knowledge distribution version');
  const file = join(site, 'twincat_knowledge/domain/provenance.py');
  const original = await readFile(file);
  const result = optimizeKnowledgeValidation(original);
  const audit = join(site, '..', 'assistant-overlay');
  await mkdir(audit);
  await writeFile(join(audit, 'original-provenance.py'), original);
  await writeFile(file, result);
  // pip's RECORD describes the installed wheel; reflect the disclosed patched
  // file too, rather than leaving a contradictory distribution inventory.
  const record = join(site, `twincat_knowledge-${distributionVersion}.dist-info/RECORD`);
  const lines = (await readFile(record, 'utf8')).split(/\r?\n/);
  const matches = lines.filter(line => line.startsWith('twincat_knowledge/domain/provenance.py,'));
  if (matches.length !== 1) throw new Error('Unknown Knowledge wheel RECORD');
  const hash = createHash('sha256').update(result).digest();
  await writeFile(record, lines.map(line => line === matches[0]
    ? `twincat_knowledge/domain/provenance.py,sha256=${hash.toString('base64url')},${Buffer.byteLength(result)}` : line).join('\n'));
  return { version: 1, distributionVersion, originalSha256: createHash('sha256').update(original).digest('hex'), patchedSha256: hash.toString('hex'), locatorCacheLimit: 4096 };
}
