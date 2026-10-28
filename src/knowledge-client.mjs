import { connect } from 'node:net';
import { lstat, readFile } from 'node:fs/promises';
import { isAbsolute, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export async function readBrokerDescriptor(path) {
  if (!isAbsolute(path)) throw new Error('Absolute broker descriptor required');
  const stat = await lstat(path);
  if (!stat.isFile() || stat.isSymbolicLink() || stat.size > 4096) throw new Error('Invalid broker descriptor');
  const value = JSON.parse(await readFile(path, 'utf8'));
  if (value.version !== 1 || !/^[a-f0-9]{64}$/.test(value.token)
      || typeof value.endpoint !== 'string'
      || !(process.platform === 'win32' ? /^\\\\\.\\pipe\\tc-knowledge-[a-f0-9]{32}$/ : /^\/.*\/tc-knowledge-[a-f0-9]{32}\.sock$/).test(value.endpoint))
    throw new Error('Invalid broker descriptor');
  return value;
}

export async function connectKnowledgeBroker(path, mode = 'mcp') {
  const descriptor = await readBrokerDescriptor(path);
  return await new Promise((resolve, reject) => {
    const socket = connect(descriptor.endpoint);
    const timer = setTimeout(() => socket.destroy(new Error('Knowledge broker connection timed out')), 3000);
    const failed = error => { clearTimeout(timer); reject(error); };
    socket.once('error', failed);
    socket.once('connect', () => {
      clearTimeout(timer);
      socket.removeListener('error', failed);
      socket.write(JSON.stringify({ token: descriptor.token, mode }) + '\n');
      resolve(socket);
    });
  });
}

export async function knowledgeBrokerAvailable(path) {
  let socket;
  try {
    socket = await connectKnowledgeBroker(path, 'ping');
    return await new Promise(resolve => {
      const timer = setTimeout(() => { socket.destroy(); resolve(false); }, 2000);
      let reply = '';
      socket.once('error', () => { clearTimeout(timer); resolve(false); });
      socket.once('close', () => { clearTimeout(timer); resolve(false); });
      socket.on('data', bytes => {
        reply += bytes.toString('utf8');
        if (reply.length > 16 || reply.includes('\n')) { clearTimeout(timer); resolve(reply === 'ok\n'); }
      });
    });
  } catch { return false; }
  finally { socket?.destroy(); }
}

export async function knowledgeBrokerStatus(path) {
  const socket = await connectKnowledgeBroker(path, 'status');
  try {
    return await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('Knowledge status timed out')), 2000);
      let buffer = '';
      const fail = error => { clearTimeout(timer); reject(error); };
      socket.once('error', fail); socket.once('close', () => fail(new Error('Knowledge status closed')));
      socket.on('data', bytes => {
        buffer += bytes.toString('utf8');
        if (buffer.length > 4096) { fail(new Error('Invalid Knowledge status')); socket.destroy(); return; }
        if (!buffer.includes('\n')) return;
        clearTimeout(timer);
        try { resolve(JSON.parse(buffer)); } catch (error) { reject(error); }
      });
    });
  } finally { socket.destroy(); }
}

async function main() {
  let socket;
  try {
    socket = await connectKnowledgeBroker(process.argv[2]);
    socket.on('error', () => { process.exitCode = 1; socket.destroy(); process.stdin.destroy(); });
    process.stdin.on('error', () => socket.destroy());
    process.stdout.on('error', () => socket.destroy());
    process.stdin.once('end', () => socket.end());
    socket.once('close', () => process.stdin.destroy());
    process.stdin.pipe(socket);
    socket.pipe(process.stdout);
  } catch { process.stderr.write('Knowledge connection unavailable; start the protected native runtime.\n'); process.exitCode = 1; }
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
