import { spawn } from 'node:child_process';
import { startKnowledgeBroker } from './knowledge-broker.mjs';
import { validateSettings } from './config.mjs';
import { runtimeEnvironment } from './state.mjs';

// This process and all descendants run inside the native supervisor's Job Object
// (or Unix process group). Owner death therefore also stops the shared backend.
const { root, knowledge } = JSON.parse(process.env.TC_ASSISTANT_KNOWLEDGE);
validateSettings({ knowledge });
const env = { ...process.env }; delete env.TC_ASSISTANT_KNOWLEDGE;
const broker = await startKnowledgeBroker({ root, knowledge, env: runtimeEnvironment(root, {}, env), cwd: process.cwd() });
const child = spawn(process.execPath, process.argv.slice(2), { env, stdio: 'inherit', windowsHide: true, shell: false });
const stop = () => child.kill();
process.once('SIGTERM', stop); process.once('SIGINT', stop);
try {
  process.exitCode = await new Promise((resolve, reject) => { child.once('error', reject); child.once('close', code => resolve(code ?? 1)); });
} finally {
  process.removeListener('SIGTERM', stop); process.removeListener('SIGINT', stop);
  await broker.close();
}
