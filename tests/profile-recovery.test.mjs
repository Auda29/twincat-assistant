import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { initializeState, inspectState } from '../src/state.mjs';
import { createOpenCodeConfig } from '../src/config.mjs';
import { updateProfile } from '../src/profile-update.mjs';
const json = value => JSON.stringify(value, null, 2) + '\n';
for (const phase of ['journal', 'config', 'settings']) test('profile recovery after ' + phase + ' commit boundary preserves operator data', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-profile-recover-'));
  try {
    const root = join(parent, 'profile'), before = {}, after = { provider: 'openai', sourceCodeDisclosureAccepted: true };
    const paths = await initializeState(root, before);
    const sentinel = join(paths.data, 'chat-login-binding'); await writeFile(sentinel, 'keep');
    await writeFile(join(root, '.profile-update.json'), json({version:1,before:{settings:before,config:createOpenCodeConfig(before)},after:{settings:after,config:createOpenCodeConfig(after)}}));
    if (phase !== 'journal') await writeFile(paths.configPath, json(createOpenCodeConfig(after)));
    if (phase === 'settings') await writeFile(paths.settingsPath, json(after));
    assert.deepEqual((await inspectState(root)).settings, after);
    assert.equal(await readFile(sentinel, 'utf8'), 'keep');
    await assert.rejects(readFile(join(root, '.profile-update.json')), {code:'ENOENT'});
    await updateProfile(root, () => before);
    assert.deepEqual((await inspectState(root)).settings, before);
  } finally { await rm(parent,{recursive:true,force:true}); }
});
test('profile recovery refuses unknown policy, foreign live files and active or stale locks without rewriting', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-profile-reject-'));
  try {
    const root=join(parent,'profile'), settings={}; const paths=await initializeState(root,settings);
    const config=createOpenCodeConfig(settings), pair={settings,config};
    const journal=join(root,'.profile-update.json');
    await writeFile(journal,json({version:1,before:pair,after:pair}));
    const original=await readFile(paths.configPath);
    await writeFile(join(root,'model-consent.lock'),'');
    await assert.rejects(inspectState(root),{code:'EEXIST'});
    await rm(join(root,'model-consent.lock'));
    await writeFile(paths.configPath,json({...config,foreign:true}));
    await assert.rejects(inspectState(root),/Profile changed/);
    await writeFile(paths.configPath,original);
    await writeFile(journal,json({version:1,before:pair,after:{settings,config:{...config,foreign:true}}}));
    await assert.rejects(inspectState(root),/policy mismatch/);
    assert.deepEqual(await readFile(paths.configPath),original);
  } finally { await rm(parent,{recursive:true,force:true}); }
});
