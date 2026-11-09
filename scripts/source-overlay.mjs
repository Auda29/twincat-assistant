import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';

const overlayRoot = new URL('../upstream/overlays/openchamber/', import.meta.url);

// Exact substitutions keep small adaptations readable. Whole-module replacements
// make the product composition explicit without carrying unused upstream wiring.
export async function applySourceOverlay(original, edit) {
  if (edit.replacement !== undefined) {
    if (edit.changes !== undefined || !/^(server\/[a-z0-9-]+\.js|ui\/[a-z0-9-]+\.tsx)$/.test(edit.replacement)
        || !/^[a-f0-9]{64}$/.test(edit.originalSha256)) throw new Error('Invalid module replacement');
    const digest = createHash('sha256').update(original.replace(/\r\n/g, '\n')).digest('hex');
    if (digest !== edit.originalSha256) throw new Error('Source replacement baseline changed: ' + edit.file);
    return readFile(new URL(edit.replacement, overlayRoot), 'utf8');
  }
  let changed = original;
  for (const [before, after] of edit.changes) {
    if (changed.split(before).length !== 2) throw new Error('Source overlay no longer matches ' + edit.file);
    changed = changed.replace(before, after);
  }
  return changed;
}

export function replacementInputs(edits) {
  return edits.filter(edit => edit.replacement !== undefined).map(edit => {
    if (!/^(server\/[a-z0-9-]+\.js|ui\/[a-z0-9-]+\.tsx)$/.test(edit.replacement)) throw new Error('Invalid replacement input');
    return 'upstream/overlays/openchamber/' + edit.replacement;
  });
}
