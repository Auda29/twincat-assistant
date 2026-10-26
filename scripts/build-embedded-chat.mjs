import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { EMBEDDED_ASSETS } from '../src/embedded-chat.mjs';
for (const asset of EMBEDDED_ASSETS) {
  const bytes = await readFile(new URL(`../upstream/node_modules/@openchamber/web/dist${asset.path}`, import.meta.url));
  if (createHash('sha256').update(bytes).digest('hex') !== asset.sha256) throw new Error('Pinned OpenChamber asset changed: ' + asset.path);
}
await build({ entryPoints: ['src/embedded-chat.mjs'], bundle: true, format: 'iife', platform: 'browser', target: 'es2020',
  globalName: 'TwinCatEmbedded', outfile: 'native/IdeHost/obj/EmbeddedChat.js', legalComments: 'eof' });
console.log('Built version-verified embedded chat adapter.');
