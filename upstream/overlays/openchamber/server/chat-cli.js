#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { parseArgs } from 'node:util';

// The upstream version probe remains a dependency contract. Product version and
// provenance are exposed separately; no daemon, updater, tunnel or service CLI.
const args = process.argv.slice(2);
if (args.length === 1 && ['--version', '-v'].includes(args[0])) {
  console.log(JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8')).version);
} else if (args.length === 0 || args.includes('--help') || args.includes('-h')) {
  console.log('TwinCAT Assistant Chat\nserve --foreground --host 127.0.0.1 --port PORT');
} else {
  try {
    const { values, positionals } = parseArgs({
      args, strict: true, allowPositionals: true,
      options: { foreground: { type: 'boolean' }, host: { type: 'string', default: '127.0.0.1' }, port: { type: 'string', default: '3000' } },
    });
    if (positionals.length !== 1 || positionals[0] !== 'serve' || values.foreground !== true) throw new Error('Use serve --foreground.');
    if (values.host !== '127.0.0.1') throw new Error('Assistant Chat listens only on 127.0.0.1.');
    if (!/^\d+$/.test(values.port) || Number(values.port) < 1024 || Number(values.port) > 65535) throw new Error('Invalid local port.');
    if (!process.env.OPENCHAMBER_UI_PASSWORD?.trim()) throw new Error('A local UI password is required.');
    const { startWebUiServer } = await import('../server/index.js');
    await startWebUiServer({ port: Number(values.port), host: values.host, attachSignals: true, exitOnShutdown: true });
  } catch (error) {
    console.error('TwinCAT Assistant Chat: ' + error.message);
    process.exitCode = 1;
  }
}
