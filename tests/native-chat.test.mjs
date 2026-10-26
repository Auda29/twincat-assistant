import assert from 'node:assert/strict';
import test from 'node:test';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { isProtectedChatReady, parseNativeChatRequest, validateNativeChatRequest } from '../src/native-chat.mjs';

test('native chat request requires an explicit isolated state, port and bounded password', () => {
  const request = { state: join(tmpdir(), 'synthetic-native-chat'), port: 3123, password: 'synthetic-password-only' };
  assert.equal(validateNativeChatRequest(request), request);
  for (const change of [{ state: 'relative' }, { state: request.state + '\0' }, { state: request.state + 'a'.repeat(2049) }, { port: 80 }, { port: 70000 }, { password: '' },
    { password: ' '.repeat(12) }, { password: 'a'.repeat(1025) }, { password: 'a'.repeat(12) + '\0' }, { provider: 'not-allowed' }])
    assert.throws(() => validateNativeChatRequest({ ...request, ...change }));
});

test('native chat request accepts the UTF-8 preamble a .NET Framework owner may write', () => {
  const request = { state: join(tmpdir(), 'synthetic-native-chat'), port: 3123, password: 'synthetic-password-only' };
  assert.deepEqual(parseNativeChatRequest('﻿' + JSON.stringify(request)), request);
  assert.deepEqual(parseNativeChatRequest(JSON.stringify(request)), request);
  assert.throws(() => parseNativeChatRequest('﻿﻿' + JSON.stringify(request)));
  assert.throws(() => parseNativeChatRequest('﻿' + JSON.stringify({ ...request, port: 80 })));
});

test('native readiness requires a protected auth endpoint, not merely a responding port', async () => {
  let mode = 'protected';
  const server = createServer((request, response) => {
    if (mode === 'redirect') { response.writeHead(302, { location: '/other' }).end(); return; }
    if (mode === 'html') { response.writeHead(200).end('<html>unrelated service</html>'); return; }
    if (mode === 'disabled') { response.writeHead(200).end('{"authenticated":true,"disabled":true}'); return; }
    if (mode === 'oversize') { response.writeHead(401).end(' '.repeat(8192)); return; }
    if (mode === 'wrong') { response.writeHead(401).end('{"error":"other server"}'); return; }
    response.writeHead(401, { 'content-type': 'application/json' }).end('{"authenticated":false,"locked":true}');
  });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const origin = `http://127.0.0.1:${server.address().port}`;
  try {
    assert.equal(await isProtectedChatReady(origin), true);
    for (mode of ['redirect', 'html', 'disabled', 'oversize', 'wrong'])
      assert.equal(await isProtectedChatReady(origin), false, mode);
    const cancel = new AbortController(); cancel.abort();
    assert.equal(await isProtectedChatReady(origin, cancel.signal), false);
  } finally { await new Promise(resolve => server.close(resolve)); }
  assert.equal(await isProtectedChatReady(origin), false, 'refused connection must not become readiness');
});
