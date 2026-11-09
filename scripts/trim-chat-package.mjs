import { lstat, readdir, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, parse, resolve, sep } from 'node:path';

const excluded = new Set(['terminal', 'browser-control', 'dev-servers', 'dev-tunnel', 'dictation', 'tts',
  'scheduled-tasks', 'session-assist', 'session-goal', 'session-work', 'routing', 'linear', 'walkthrough',
  'tracked-items', 'source-control', 'github', 'gitlab', 'permission-auto-accept', 'agent-tool',
  'openchamber-control', 'guests', 'spaces', 'relay', 'tunnels', 'client-auth']);
const packageName = spec => spec.startsWith('@') ? spec.split('/').slice(0, 2).join('/') : spec.split('/')[0];
const missing = error => { if (error.code === 'ENOENT') return null; throw error; };

async function files(directory) {
  const result = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.isSymbolicLink()) throw new Error('Redirected Chat package entry');
    const path = join(directory, entry.name);
    if (entry.isDirectory()) result.push(...await files(path));
    else if (entry.isFile()) result.push(path);
  }
  return result;
}

// Conservative import closure. Unknown computed imports keep their whole local
// directory. Non-code assets and all notices survive this pass.
export async function trimChatPackage(stage) {
  if (!isAbsolute(stage) || resolve(stage) !== stage || parse(stage).root === stage) throw new Error('Explicit normalized package root required');
  const modules = join(stage, 'upstream/node_modules');
  const web = join(modules, '@openchamber/web');
  const entry = join(web, 'server/index.js');
  if (!await lstat(entry).catch(missing)) return { removedServerFiles: 0, removedPackages: [] };
  const all = [...await files(join(web, 'server')), ...await files(join(web, 'bin'))];
  const code = new Set(all.filter(path => /\.[cm]?js$/.test(path)));
  const seen = new Set(), bare = new Set(), queue = [entry, join(web, 'bin/cli.js')];
  while (queue.length) {
    const file = queue.pop();
    if (seen.has(file) || !code.has(file)) continue;
    seen.add(file);
    const text = await readFile(file, 'utf8');
    const specs = new Set();
    for (const pattern of [/\b(?:import|export)\s+(?:[^;]*?\sfrom\s*)?['"]([^'"]+)['"]/g,
      /\b(?:import|require)\(\s*['"]([^'"]+)['"]\s*\)/g,
      /new\s+URL\(\s*['"](\.[^'"]+\.[cm]?js)['"]\s*,\s*import\.meta\.url/g]) {
      for (const match of text.matchAll(pattern)) specs.add(match[1]);
    }
    for (const spec of specs) {
      if (spec.startsWith('.')) {
        const path = resolve(dirname(file), spec);
        if (!path.startsWith(web + sep)) throw new Error('Chat import escapes package');
        for (const candidate of [path, path + '.js', join(path, 'index.js')]) if (code.has(candidate)) queue.push(candidate);
      } else if (!spec.startsWith('node:')) bare.add(packageName(spec));
    }
    if (/\b(?:import|require)\(\s*[^'"\s]/.test(text)) {
      for (const candidate of code) if (candidate.startsWith(dirname(file) + sep)) queue.push(candidate);
    }
  }
  let removedServerFiles = 0;
  for (const file of code) {
    const relative = file.slice(web.length + 1).replaceAll('\\', '/');
    const family = relative.match(/^server\/lib\/([^/]+)\//)?.[1];
    if (!seen.has(file) && (relative.startsWith('bin/lib/') || excluded.has(family))) {
      await rm(file); removedServerFiles++;
    }
  }
  const pkgFile = join(web, 'package.json');
  const pkg = JSON.parse(await readFile(pkgFile, 'utf8'));
  // These runtime families have been removed from the server composition.
  // Refuse a future direct consumer instead of deleting a required dependency.
  const removing = new Set(['node-pty', 'bun-pty', 'sherpa-onnx-node', 'web-push', '@clack/prompts', 'qrcode-terminal']);
  const metadata = new Map();
  const roots = [];
  for (const entry of await readdir(modules, { withFileTypes: true })) {
    if (!entry.isDirectory() || entry.name === '.bin') continue;
    if (entry.name.startsWith('@')) {
      for (const child of await readdir(join(modules, entry.name), { withFileTypes: true })) if (child.isDirectory()) roots.push(join(modules, entry.name, child.name));
    } else roots.push(join(modules, entry.name));
  }
  for (const directory of roots) {
    const file = join(directory, 'package.json');
    if (!await lstat(file).catch(missing)) continue;
    const value = JSON.parse(await readFile(file, 'utf8'));
    metadata.set(value.name, { directory, value });
    if (/^sherpa-onnx-(?:win|linux|darwin)-/.test(value.name) || /^@lydell\/node-pty-/.test(value.name)) removing.add(value.name);
  }
  for (const name of removing) {
    if (bare.has(name)) throw new Error('Excluded Chat dependency still imported: ' + name);
    delete pkg.dependencies?.[name];
  }
  if (metadata.has(pkg.name)) metadata.get(pkg.name).value = pkg;
  const deps = value => Object.keys({ ...value.dependencies, ...value.optionalDependencies });
  const owners = dependency => [...metadata.values()].filter(({ value }) => deps(value).includes(dependency)).map(({ value }) => value.name);
  let changed;
  do {
    changed = false;
    for (const name of [...removing]) for (const dependency of deps(metadata.get(name)?.value ?? {})) {
      if (!removing.has(dependency) && metadata.has(dependency) && !bare.has(dependency) && owners(dependency).every(owner => removing.has(owner))) {
        removing.add(dependency); changed = true;
      }
    }
  } while (changed);
  for (const name of removing) {
    if (owners(name).some(owner => !removing.has(owner))) throw new Error('Kept dependency still requires excluded package: ' + name);
  }
  const removedPackages = [];
  for (const name of removing) {
    const item = metadata.get(name);
    if (!item) continue;
    const path = resolve(item.directory);
    if (!path.startsWith(resolve(modules) + sep) || (await lstat(path)).isSymbolicLink()) throw new Error('Unsafe dependency removal target');
    await rm(path, { recursive: true }); removedPackages.push(name);
  }
  await writeFile(pkgFile, JSON.stringify(pkg, null, 2) + '\n');
  return { removedServerFiles, removedPackages };
}
