import { resolve, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';
import { updateProfile } from './profile-update.mjs';

export function validateModelConsent(value) {
  if (!value || Object.keys(value).some(key => !['state', 'provider', 'model', 'acceptSourceDisclosure'].includes(key))
      || typeof value.state !== 'string' || !isAbsolute(value.state) || value.state.length > 2048 || value.state.includes('\0')
      || (!value.provider && !value.model)
      || (value.provider !== undefined && (typeof value.provider !== 'string' || !/^[a-zA-Z0-9_-]{1,80}$/.test(value.provider)))
      || (value.model !== undefined && (typeof value.model !== 'string' || value.model.length > 240 || !/^[a-zA-Z0-9_-]+\/[^\s\0]+$/.test(value.model)))
      || (value.provider && value.model && value.provider !== value.model.split('/')[0])
      || value.acceptSourceDisclosure !== true) throw new Error('Explicit model and source disclosure consent required');
  return value;
}

export async function configureNativeModel(input) {
  const request = validateModelConsent(input);
  const verified = await updateProfile(request.state, current => {
    const provider = request.provider ?? request.model.split('/')[0];
    const next = { ...current, provider, sourceCodeDisclosureAccepted: true };
    if (request.model) next.model = request.model;
    else if (next.model?.split('/')[0] !== provider) delete next.model;
    return next;
  });
  if (!verified.modelCallsEnabled) throw new Error('Model policy not verified');
  return { configured: true, model: verified.settings.model };
}

async function main() {
  const chunks = [];
  let size = 0;
  for await (const chunk of process.stdin) {
    size += chunk.length;
    if (size > 8192) throw new Error('Input too large');
    chunks.push(chunk);
  }
  const input = new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks));
  await configureNativeModel(JSON.parse(input));
  process.stdout.write('{"configured":true}\n');
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { await main(); } catch { process.stdout.write('{"configured":false}\n'); process.exitCode = 1; }
}
