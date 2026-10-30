import assert from 'node:assert/strict';
import test from 'node:test';
import fsPromises, { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import crypto from 'node:crypto';
import path, { join } from 'node:path';
import { tmpdir } from 'node:os';
import { createSettingsRuntime } from '../upstream/node_modules/@openchamber/web/server/lib/opencode/settings-runtime.js';
import { initializeState, prepareChatWorkspace } from '../src/state.mjs';

const runtime = settingsFile => createSettingsRuntime({ fsPromises, path, crypto, SETTINGS_FILE_PATH: settingsFile });

test('an existing canonical profile keeps TwinCAT themes on real upstream base and web reads', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-theme-profile-'));
  try {
    const paths = await initializeState(join(parent, 'state'), {});
    const settingsFile = join(paths.openchamber, 'settings.json');
    const profileFile = join(paths.openchamber, 'preferences.json');
    const projects = [{ id: 'existing-project', path: '/synthetic/existing-project' }];
    await writeFile(settingsFile, JSON.stringify({ projects, activeProjectId: 'existing-project', lightThemeId: 'openchamber-light', darkThemeId: 'openchamber-dark' }));
    const profile = { version: 1, extraMetadata: 'preserved', fields: {
      lightThemeId: { value: 'openchamber-light', updatedAt: 101, surfaces: {
        web: { value: 'openchamber-light', updatedAt: 102 }, mobile: { value: 'operator-light', updatedAt: 103 },
      } },
      darkThemeId: { value: 'openchamber-dark', updatedAt: 104, surfaces: { web: { value: 'openchamber-dark', updatedAt: 105 } } },
      themeId: { value: 'openchamber-light', updatedAt: 106 },
      fontSize: { value: 105, updatedAt: 107 },
      customField: { value: { nested: 'unchanged' }, updatedAt: 108 },
    } };
    await writeFile(profileFile, JSON.stringify(profile));
    assert.equal((await runtime(settingsFile).readSettingsFromDisk({ surface: 'web' })).lightThemeId, 'openchamber-light');
    await prepareChatWorkspace(paths.root);
    const server = runtime(settingsFile);
    for (const surface of [null, 'web']) {
      const actual = await server.readSettingsFromDisk({ surface });
      assert.equal(actual.lightThemeId, 'twincat-light');
      assert.equal(actual.darkThemeId, 'twincat-dark');
      assert.equal(actual.themeId, 'twincat-light');
      assert.deepEqual(actual.projects, projects);
      assert.equal(actual.activeProjectId, 'existing-project');
    }
    const migrated = JSON.parse(await readFile(profileFile, 'utf8'));
    assert.equal(migrated.extraMetadata, profile.extraMetadata);
    assert.deepEqual(migrated.fields.fontSize, profile.fields.fontSize);
    assert.deepEqual(migrated.fields.customField, profile.fields.customField);
    assert.deepEqual(migrated.fields.lightThemeId.surfaces.mobile, profile.fields.lightThemeId.surfaces.mobile);
    assert.equal((await server.readSettingsFromDisk({ surface: 'mobile' })).lightThemeId, 'operator-light');
    assert.ok(migrated.fields.lightThemeId.updatedAt > 108);
    const beforeRepeat = await readFile(profileFile);
    await prepareChatWorkspace(paths.root);
    assert.deepEqual(await readFile(profileFile), beforeRepeat, 'repeat startup does not restamp the profile');
  } finally { await rm(parent, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); }
});

test('an explicitly chosen canonical theme remains authoritative over legacy defaults', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-theme-custom-'));
  try {
    const paths = await initializeState(join(parent, 'state'), {});
    const settingsFile = join(paths.openchamber, 'settings.json');
    const profileFile = join(paths.openchamber, 'preferences.json');
    await writeFile(settingsFile, JSON.stringify({ lightThemeId: 'openchamber-light', darkThemeId: 'openchamber-dark' }));
    const profile = JSON.stringify({ version: 1, fields: {
      lightThemeId: { value: 'operator-light', updatedAt: 101 },
      darkThemeId: { value: 'operator-dark', updatedAt: 102 },
    } });
    await writeFile(profileFile, profile);
    await prepareChatWorkspace(paths.root);
    assert.equal(await readFile(profileFile, 'utf8'), profile);
    const actual = await runtime(settingsFile).readSettingsFromDisk({ surface: 'web' });
    assert.equal(actual.lightThemeId, 'operator-light');
    assert.equal(actual.darkThemeId, 'operator-dark');
  } finally { await rm(parent, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); }
});

test('invalid or future canonical profiles are refused without changing either settings file', async () => {
  const parent = await mkdtemp(join(tmpdir(), 'tc-theme-invalid-'));
  try {
    const paths = await initializeState(join(parent, 'state'), {});
    const settingsFile = join(paths.openchamber, 'settings.json');
    const profileFile = join(paths.openchamber, 'preferences.json');
    const settings = JSON.stringify({ lightThemeId: 'openchamber-light', darkThemeId: 'openchamber-dark' });
    await writeFile(settingsFile, settings);
    for (const profile of ['{', '{"version":2,"fields":{}}', '{"version":1,"fields":{"lightThemeId":"invalid"}}']) {
      await writeFile(profileFile, profile);
      await assert.rejects(prepareChatWorkspace(paths.root));
      assert.equal(await readFile(profileFile, 'utf8'), profile);
      assert.equal(await readFile(settingsFile, 'utf8'), settings);
    }
  } finally { await rm(parent, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }); }
});
