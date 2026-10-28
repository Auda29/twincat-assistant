import { lstat, mkdir, open, readFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';

export async function knowledgeDirectory(path) {
  try { await mkdir(path, { mode: 0o700 }); } catch (error) { if (error.code !== 'EEXIST') throw error; }
  const stat = await lstat(path);
  if (!stat.isDirectory() || stat.isSymbolicLink()) throw new Error('Redirected knowledge directory');
}
export async function knowledgePaths(root) {
  const base = join(resolve(root), 'knowledge');
  await knowledgeDirectory(base);
  const state = join(base, 'state'), cache = join(base, 'cache'), runs = join(base, 'runs');
  for (const path of [state, cache, runs]) await knowledgeDirectory(path);
  const config = join(base, 'knowledge.toml');
  const expected = '[paths]\nstate = ' + JSON.stringify(state) + '\ncache = ' + JSON.stringify(cache) + '\n';
  try {
    const file = await open(config, 'wx', 0o600);
    try { await file.writeFile(expected); await file.sync(); } finally { await file.close(); }
  } catch (error) {
    if (error.code !== 'EEXIST') throw error;
    const stat = await lstat(config);
    if (!stat.isFile() || stat.isSymbolicLink() || stat.size > 16384 || await readFile(config, 'utf8') !== expected)
      throw new Error('Managed knowledge configuration modified');
  }
  return { base, state, cache, runs, config, receipt: join(base, 'receipt.json') };
}
export async function effectiveKnowledge(root, knowledge) {
  return knowledge.config ? knowledge : { ...knowledge, config: (await knowledgePaths(root)).config, startupTimeout:600000 };
}
