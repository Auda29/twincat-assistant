import { AsyncLocalStorage } from 'node:async_hooks';
import { isAbsolute, join } from 'node:path';
import { lstat } from 'node:fs/promises';
import { verifyManifest } from '../scripts/application-manifest.mjs';

const operations = new AsyncLocalStorage();

// Only immutable application inventories belong here. Mutable state, selection,
// path ownership and policy checks must run on every call as before.
export async function withPackageVerificationScope(operation) {
  if (operations.getStore()?.active) return operation();
  const context = { active: true, inventories: new Map() };
  return operations.run(context, async () => {
    try { return await operation(); }
    finally {
      // Async resources may retain the store after run returns. Revoke it too.
      context.active = false;
      context.inventories.clear();
    }
  });
}

export async function verifyOperationManifest(root) {
  const context = operations.getStore();
  if (!context?.active || !isAbsolute(root)) return verifyManifest(root);
  const key = root; // Exact input only; never collapse paths across reparse components.
  let verification = context.inventories.get(key);
  if (!verification) {
    verification = verifyManifest(root);
    // Store the pending promise before awaiting: parallel callers share the full
    // inventory, including failures. A new operation always starts empty.
    context.inventories.set(key, verification);
  } else {
    // Keep the verifier's cheap boundary guards on every use. Only the immutable
    // recursive inventory is shared; a replaced root/manifest is never adopted.
    const rootStat = await lstat(root);
    if (!rootStat.isDirectory() || rootStat.isSymbolicLink()) throw new Error('Invalid package root');
    const manifestStat = await lstat(join(root, 'application-manifest.json'));
    if (!manifestStat.isFile() || manifestStat.isSymbolicLink() || manifestStat.size > 32 * 1024 * 1024)
      throw new Error('Invalid manifest file');
  }
  return structuredClone(await verification);
}
