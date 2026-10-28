import { execFile, spawn } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { isAbsolute } from 'node:path';

const capture = promisify(execFile);
function helperEnvironment() {
  return Object.fromEntries(['SystemRoot', 'WINDIR', 'PATH', 'Path', 'TEMP', 'TMP', 'DOTNET_ROOT']
    .filter(key => process.env[key] !== undefined).map(key => [key, process.env[key]]));
}
export function windowsHelperPath() {
  const path = process.env.TC_ASSISTANT_WINDOWS_HELPER
    ?? fileURLToPath(new URL('../native/WindowsHost/bin/Release/net8.0-windows/WindowsHost.exe', import.meta.url));
  if (!isAbsolute(path)) throw new Error('Windows helper must be an absolute operator-selected path');
  return path;
}

export async function readWindowsJson(path) {
  try {
    const { stdout } = await capture(windowsHelperPath(), ['read-json', path],
      { encoding: 'buffer', maxBuffer: 65536, timeout: 10000, windowsHide: true, shell: false, env: helperEnvironment() });
    return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(stdout));
  } catch { throw new Error('Expected a bounded regular JSON file; native Windows validation failed'); }
}

export async function secureWindowsDirectory(path) {
  try { await capture(windowsHelperPath(), ['secure-directory', path], { timeout: 10000, windowsHide: true, shell: false, env: helperEnvironment() }); }
  catch { throw new Error('Unable to restrict Windows state directory access'); }
}

export async function acquireKnowledgeLease(root) {
  const helper=spawn(windowsHelperPath(),['hold-knowledge-lock',root],{
    shell:false,windowsHide:true,stdio:['pipe','pipe','ignore'],env:helperEnvironment(),
  });
  const lost=new AbortController();let releasing=false;
  helper.stdin.on('error',()=>{if(!releasing)lost.abort();});
  const closed=new Promise(resolve=>helper.once('close',()=>{if(!releasing)lost.abort();resolve();}));
  try {
    await new Promise((resolve,reject)=>{
      let output='';const timer=setTimeout(()=>reject(new Error('Knowledge lease timed out')),10000);
      const finish=action=>{clearTimeout(timer);action();};
      helper.once('error',()=>finish(()=>reject(new Error('Knowledge lease unavailable'))));
      helper.once('close',()=>finish(()=>reject(new Error('Another documentation setup is running'))));
      helper.stdout.on('data',bytes=>{
        output+=bytes.toString('utf8');
        if(output.length>1024)return finish(()=>reject(new Error('Invalid knowledge lease')));
        if(output.includes('\n')) {
          try{if(JSON.parse(output.split('\n')[0]).held!==true)throw new Error();finish(resolve);}
          catch{finish(()=>reject(new Error('Invalid knowledge lease')));}
        }
      });
    });
    return {signal:lost.signal,async release(){releasing=true;helper.stdin.end();const timer=setTimeout(()=>helper.kill(),3000);try{await closed;}finally{clearTimeout(timer);}}};
  } catch(error){releasing=true;helper.stdin.destroy();helper.kill();await closed;throw error;}
}

export async function superviseWindows(plan, signal, onStarted = () => {}, onOutput = null) {
  if (signal?.aborted) return { cancelled: true };
  const helper = spawn(windowsHelperPath(), ['supervise'], {
    shell: false, windowsHide: true, stdio: ['pipe', 'pipe', onOutput ? 'pipe' : 'inherit'], env: helperEnvironment(),
  });
  if (onOutput) helper.stderr.on('data', onOutput);
  let stopped = false;
  let killTimer;
  let ready = false;
  let buffer = '';
  const stop = () => {
    if (stopped) return;
    stopped = true;
    if (!helper.stdin.destroyed) helper.stdin.end('stop\n');
    killTimer = setTimeout(() => helper.kill(), 5000);
    killTimer.unref();
  };
  helper.stdin.on('error', () => stop());
  const completed = new Promise((resolve, reject) => {
    const rejectProtocol = () => { stop(); reject(new Error('Native Windows supervisor returned invalid readiness')); };
    helper.once('error', () => reject(new Error('Native Windows supervisor could not start; build the helper first')));
    helper.stdout.on('data', bytes => {
      buffer += bytes.toString('utf8');
      if (buffer.length > 1024) { rejectProtocol(); return; }
      const newline = buffer.indexOf('\n');
      if (newline < 0) return;
      try {
        const message = JSON.parse(buffer.slice(0, newline));
        if (ready || message.started !== true || !Number.isInteger(message.pid) || message.pid <= 0) throw new Error();
        ready = true;
        buffer = '';
        onStarted({ pid: message.pid, supervisorPid: helper.pid });
      } catch { rejectProtocol(); }
    });
    helper.once('close', (code, childSignal) => {
      if (!ready && !signal?.aborted) reject(new Error('Native Windows supervisor failed before starting the owned child'));
      else resolve({ code, signal: childSignal, cancelled: signal?.aborted ?? false });
    });
  });
  signal?.addEventListener('abort', stop, { once: true });
  try {
    helper.stdin.write(JSON.stringify(plan) + '\n');
    if (signal?.aborted) stop();
    return await completed;
  } finally {
    signal?.removeEventListener('abort', stop);
    clearTimeout(killTimer);
    helper.stdin.destroy();
    if (helper.exitCode === null) helper.kill(); // closing the native job handle kills every owned descendant
  }
}
