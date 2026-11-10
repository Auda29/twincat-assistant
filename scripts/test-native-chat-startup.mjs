// Actual standalone WPF/WebView startup against an owned synthetic backend.
// No IDE, installed configuration, customer source, or model request is used.
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, writeFile, open } from 'node:fs/promises';
import { createServer } from 'node:net';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { resolve, join } from 'node:path';
import { configureNativeModel } from '../src/native-chat-model.mjs';

if (process.platform !== 'win32') throw new Error('Windows WPF and WebView2 are required');
const root = fileURLToPath(new URL('../', import.meta.url));
const fixture = join(root, 'local-data', 'chat-startup-smoke-' + randomUUID());
await mkdir(fixture, { recursive: true });
const reservation = createServer();
reservation.listen(0, '127.0.0.1');
await once(reservation, 'listening');
const port = reservation.address().port;
await new Promise(resolve => reservation.close(resolve));
const origin = `http://127.0.0.1:${port}`;
const password = 'synthetic-product-ui-only-not-an-operator-credential';
const log = await open(join(fixture, 'backend.log'), 'w');
const child = spawn(process.execPath, [join(root, 'src/native-chat.mjs')], {
  cwd: root, windowsHide: true, stdio: ['pipe', 'pipe', log.fd],
});
const closed = once(child, 'close');
const ready = new Promise((resolve, reject) => {
  let output = '';
  child.stdout.on('data', data => {
    output += data;
    if (output.includes('\n')) {
      try {
        if (JSON.parse(output.split('\n')[0]).status !== 'ready') throw new Error('Fixture backend failed');
        resolve();
      } catch (error) { reject(error); }
    }
  });
  child.once('error', reject);
  child.once('exit', () => reject(new Error('Fixture backend exited before readiness')));
});
const timer = setTimeout(() => child.stdin.end(), 240000);
try {
  child.stdin.write(JSON.stringify({ state: join(fixture, 'state'), port, password }) + '\n');
  await ready;
  await configureNativeModel({ state: join(fixture, 'state'), provider: 'openai', acceptSourceDisclosure: true });
  const login = await fetch(origin + '/auth/session', {
    method: 'POST', headers: { origin, 'content-type': 'application/json' }, body: JSON.stringify({ password }),
  });
  if (!login.ok) throw new Error('Synthetic login failed');
  const cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
  const headers = { origin, cookie, 'content-type': 'application/json',
    'x-opencode-directory': encodeURIComponent(join(fixture, 'state', 'workspace')), 'x-opencode-directory-encoding': 'uri' };
  const connected = await fetch(origin + '/api/integration/openai/connect/key', {
    method: 'POST', headers, body: JSON.stringify({ key: 'synthetic-not-a-real-api-key' }),
  });
  if (connected.status !== 204) throw new Error('Synthetic provider setup failed');
  await fetch(origin + '/api/debug/location', { method: 'DELETE', headers });
  await writeFile(join(fixture, 'connection.json'), JSON.stringify({ origin, password }));
  const env = { ...process.env };
  for (const key of Object.keys(env)) if (key.toLowerCase() === 'psmodulepath') delete env[key];
  const args = ['-NoProfile', '-STA', '-File', join(root, 'scripts/Test-NativeChatStartup.ps1'),
    '-FixtureDirectory', fixture, '-OutputDirectory', fixture];
  if (process.argv[2]) args.push('-HostAssembly', resolve(process.argv[2]));
  // Match the supported TcXaeShell x86 process and packaged x86 WebView loader.
  const powershell = join(process.env.WINDIR, 'SysWOW64', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
  const test = spawn(powershell, args, { cwd: root, env, windowsHide: true, stdio: 'inherit' });
  const [code] = await once(test, 'close');
  if (code !== 0) throw new Error('Native startup regression failed: ' + code);
  console.log('PASS native startup; evidence: ' + join(fixture, 'result.json'));
} finally {
  clearTimeout(timer);
  child.stdin.end();
  await closed;
  await log.close();
}
