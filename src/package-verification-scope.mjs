import { AsyncLocalStorage } from 'node:async_hooks';
import { isAbsolute } from 'node:path';
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
  }
  return structuredClone(await verification);
}
