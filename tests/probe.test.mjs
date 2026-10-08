import assert from 'node:assert/strict';
import test from 'node:test';
import { probeServer } from '../src/probe.mjs';

test('operator probe rejects mutation tools before any server process can be launched', async () => {
  await assert.rejects(probeServer('engineering', { command: ['/missing/server'], protocol: 'legacy' }, { name: 'plc_write', arguments: {} }), /read-only/);
  await assert.rejects(probeServer('knowledge', { command: ['/missing/server'], protocol: '2026-07-28' }, { name: 'arbitrary_shell', arguments: {} }), /read-only/);
});
