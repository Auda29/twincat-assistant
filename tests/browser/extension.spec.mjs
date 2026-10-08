import test, { expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(dirname(dirname(fileURLToPath(import.meta.url))));
const fixture = await readFile(join(root, 'tests/browser/host-fixture.html'), 'utf8');
const panel = await readFile(join(root, 'dist/extension/panel/index.html'), 'utf8');
const bundle = await readFile(join(root, 'dist/extension/panel/main.js'), 'utf8');
const guestDocument = panel.replace('<script src="main.js"></script>', `<script>${bundle}</script>`);

async function openGuest(page) {
  await page.setContent(fixture);
  await page.evaluate((documentText) => {
    document.querySelector('#guest').srcdoc = documentText;
  }, guestDocument);
  await expect(page.frameLocator('#guest').locator('#native-bridge')).toHaveText('Not connected: native TwinCAT bridge unavailable.');
}

async function setHostState(page, state) {
  await page.evaluate((next) => window.__setState(next), state);
}

async function setDirectory(page, directory) {
  await page.evaluate((next) => window.__setDirectory(next), directory);
}

async function setSession(page, session) {
  await page.evaluate((next) => window.__setSession(next), session);
}

test('host contract renders long and hostile context as text and stays fail-closed', async ({ page }) => {
  await openGuest(page);
  const longDirectory = `C:/TwinCAT/${'<img src=x onerror=alert(1)>'.repeat(40)}`;
  const hostileTitle = `<script>alert(1)</script>${'Review '.repeat(80)}`;
  await setHostState(page, {
    directory: longDirectory,
    session: { id: 'session-hostile', title: hostileTitle, busy: false }
  });

  const guest = page.frameLocator('#guest');
  await expect(guest.locator('#project')).toHaveText(longDirectory);
  await expect(guest.locator('#session')).toHaveText(hostileTitle);
  await expect(guest.locator('#engineering')).toHaveText('Blocked: native TwinCAT bridge is not connected.');
  await expect(guest.locator('#attach')).toBeEnabled();
  expect(await page.locator('script').count()).toBe(1);
  expect(await guest.locator('script').count()).toBe(1);
  expect(await page.evaluate(() => window.__host.attach)).toBeNull();
});

test('missing session disables attach and a later session switch rebinds current context', async ({ page }) => {
  await openGuest(page);
  await setHostState(page, { directory: '/projects/plant', session: null });
  const guest = page.frameLocator('#guest');
  await expect(guest.locator('#project')).toHaveText('/projects/plant');
  await expect(guest.locator('#session')).toHaveText('No active session');
  await expect(guest.locator('#attach')).toBeDisabled();

  await setHostState(page, { directory: '/projects/plant', session: { id: 'session-two', title: 'Second session', busy: true } });
  await expect(guest.locator('#session')).toHaveText('Second session');
  await expect(guest.locator('#binding')).toHaveText('Verified (/projects/plant::session-two)');
  await expect(guest.locator('#activity')).toHaveText('Running');
  await expect(guest.locator('#attach')).toBeEnabled();

  await guest.locator('#attach').click();
  await expect(guest.locator('#operation')).toHaveText('Verified context attached.');
  const attached = await page.evaluate(() => window.__host.attach);
  expect(attached).toMatchObject({
    providerId: 'twincat-assistant',
    id: 'twincat-context:session-two',
    data: { projectDirectory: '/projects/plant', sessionId: 'session-two', nativeBridge: 'not-connected' }
  });
});

test('host attach refusal is visible and does not become engineering readiness', async ({ page }) => {
  await openGuest(page);
  await page.evaluate(() => window.__setMode('fail'));
  await setHostState(page, {
    directory: '/projects/plant',
    session: { id: 'session-failure', title: 'Attach failure', busy: false }
  });
  const guest = page.frameLocator('#guest');
  await guest.locator('#attach').click();
  await expect(guest.locator('#host-error')).toHaveText('Host error: Fixture refused attach.');
  await expect(guest.locator('#engineering')).toHaveText('Blocked: native TwinCAT bridge is not connected.');
  await expect(guest.locator('#attach')).toBeDisabled();
});

test('directory changes invalidate the old session until a fresh session event arrives', async ({ page }) => {
  await openGuest(page);
  await setHostState(page, {
    directory: '/projects/one',
    session: { id: 'session-one', title: 'First session', busy: false }
  });
  const guest = page.frameLocator('#guest');
  await expect(guest.locator('#binding')).toHaveText('Verified (/projects/one::session-one)');

  await setDirectory(page, '/projects/two');
  await expect(guest.locator('#project')).toHaveText('/projects/two');
  await expect(guest.locator('#session')).toHaveText('No active session');
  await expect(guest.locator('#binding')).toHaveText('Unbound');
  await expect(guest.locator('#attach')).toBeDisabled();

  await setSession(page, { id: 'session-two', title: 'Second session', busy: false });
  await expect(guest.locator('#binding')).toHaveText('Verified (/projects/two::session-two)');
  await expect(guest.locator('#attach')).toBeEnabled();
});

test('pending attach ignores a success for a switched context and permits one request', async ({ page }) => {
  await openGuest(page);
  await setHostState(page, {
    directory: '/projects/one',
    session: { id: 'session-one', title: 'First session', busy: false }
  });
  await page.evaluate(() => window.__setMode('delay'));
  const guest = page.frameLocator('#guest');
  await guest.locator('#attach').click();
  await expect(guest.locator('#operation')).toHaveText('Attaching verified context…');
  await expect(guest.locator('#attach')).toBeDisabled();

  await setHostState(page, {
    directory: '/projects/one',
    session: { id: 'session-one', title: 'First session', busy: true }
  });
  await expect(guest.locator('#attach')).toBeDisabled();
  await guest.locator('#attach').click({ force: true });
  expect(await page.evaluate(() => window.__host.attachCount)).toBe(1);

  await setDirectory(page, '/projects/two');
  await setSession(page, { id: 'session-two', title: 'Second session', busy: false });
  await expect(guest.locator('#binding')).toHaveText('Verified (/projects/two::session-two)');
  await expect.poll(() => page.evaluate(() => window.__host.pendingAttachResults.length)).toBe(1);
  await page.evaluate(() => window.__releaseAttach());
  await expect(guest.locator('#operation')).toHaveText('Context changed; reattach the current context.');
  await expect(guest.locator('#host-error')).toHaveText('');
  await expect(guest.locator('#attach')).toBeEnabled();
});

test('pending attach ignores an error for a switched context', async ({ page }) => {
  await openGuest(page);
  await setHostState(page, {
    directory: '/projects/one',
    session: { id: 'session-one', title: 'First session', busy: false }
  });
  await page.evaluate(() => window.__setMode('delay-fail'));
  const guest = page.frameLocator('#guest');
  await guest.locator('#attach').click();
  await expect.poll(() => page.evaluate(() => window.__host.pendingAttachResults.length)).toBe(1);

  await setDirectory(page, '/projects/two');
  await setSession(page, { id: 'session-two', title: 'Second session', busy: false });
  await expect(guest.locator('#binding')).toHaveText('Verified (/projects/two::session-two)');
  await expect.poll(() => page.evaluate(() => window.__host.pendingAttachResults.length)).toBe(1);
  await page.evaluate(() => window.__releaseAttach());
  await expect(guest.locator('#operation')).toHaveText('Context changed; reattach the current context.');
  await expect(guest.locator('#host-error')).toHaveText('');
  await expect(guest.locator('#attach')).toBeEnabled();
});

test('disposed panel does not render a late attach result', async ({ page }) => {
  await openGuest(page);
  await setHostState(page, {
    directory: '/projects/one',
    session: { id: 'session-one', title: 'First session', busy: false }
  });
  await page.evaluate(() => window.__setMode('delay'));
  const guest = page.frameLocator('#guest');
  await guest.locator('#attach').click();
  await expect(guest.locator('#operation')).toHaveText('Attaching verified context…');

  await expect.poll(() => page.evaluate(() => window.__host.pendingAttachResults.length)).toBe(1);
  await page.frames()[1].evaluate(() => window.dispatchEvent(new Event('pagehide')));
  await page.evaluate(() => window.__releaseAttach());
  await expect(guest.locator('#operation')).toHaveText('Attaching verified context…');
  await expect(guest.locator('#host-error')).toHaveText('');
});
