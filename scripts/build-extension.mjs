import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import * as esbuild from 'esbuild';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const source = join(root, 'extensions', 'twincat-assistant');
const output = join(root, 'dist', 'extension');

await rm(output, { recursive: true, force: true });
await mkdir(join(output, 'panel'), { recursive: true });

await esbuild.build({
  entryPoints: [join(source, 'panel', 'main.mjs')],
  outfile: join(output, 'panel', 'main.js'),
  bundle: true,
  format: 'iife',
  platform: 'browser',
  target: ['es2020'],
  legalComments: 'eof',
  logLevel: 'info',
});

await Promise.all([
  cp(join(source, 'package.json'), join(output, 'package.json')),
  cp(join(source, 'panel', 'index.html'), join(output, 'panel', 'index.html')),
  cp(join(source, 'THIRD_PARTY_NOTICES.txt'), join(output, 'THIRD_PARTY_NOTICES.txt')),
]);

const packageText = await readFile(join(output, 'package.json'), 'utf8');
const manifest = JSON.parse(packageText);
if (manifest.openchamber?.contributes?.panel?.entry !== 'panel/index.html') {
  throw new Error('Built extension manifest does not point at panel/index.html');
}

await writeFile(join(output, 'BUILD.txt'), 'Built by scripts/build-extension.mjs as a browser IIFE for a sandboxed OpenChamber iframe.\n');
console.log(`Built ${join(output, 'panel', 'main.js')}`);
