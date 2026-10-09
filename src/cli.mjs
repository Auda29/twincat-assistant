import { parseArgs } from 'node:util';
import { isAbsolute } from 'node:path';
import { realpath } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { prepareLaunch, runVersion } from './launcher.mjs';
import { supervise } from './supervisor.mjs';
import { initializeState, inspectState, readJsonFile } from './state.mjs';

const HELP = `TwinCAT Assistant operator tools (no native IDE or runtime writes)\n\ninit --settings ABSOLUTE_JSON --state NEW_ABSOLUTE_DIRECTORY\ndoctor --state ABSOLUTE_DIRECTORY\nstart --state ABSOLUTE_DIRECTORY [--port 3123] [--opencode ABSOLUTE_BINARY] [--openchamber ABSOLUTE_CLI_JS]\n\nstart requires OPENCHAMBER_UI_PASSWORD and the pinned upstream installation.\n`;
try {
  const { values, positionals } = parseArgs({ options: { settings: { type: 'string' }, state: { type: 'string' }, help: { type: 'boolean', short: 'h' }, port: { type: 'string' }, opencode: { type: 'string' }, openchamber: { type: 'string' } }, allowPositionals: true, strict: true });
  if (values.help || positionals.length === 0) console.log(HELP);
  else {
    if (positionals.length !== 1 || !values.state || !isAbsolute(values.state)) throw new Error('Select a command and an absolute state directory');
    const command = positionals[0];
    let result;
    if (command === 'init') {
      if (!values.settings || !isAbsolute(values.settings)) throw new Error('Select an absolute operator settings file');
      result = { initialized: true, ...await initializeState(values.state, await readJsonFile(values.settings)) };
    } else if (command === 'doctor') {
      if (values.settings) throw new Error('doctor reads saved settings; --settings is not applicable');
      const { settings, ...status } = await inspectState(values.state);
      result = status;
    } else if (command === 'start') {
      if (values.settings) throw new Error('start reads saved settings');
      const upstream = fileURLToPath(new URL('../upstream/node_modules/', import.meta.url));
      const plan = await prepareLaunch(values.state, {
        openchamber: await realpath(values.openchamber ?? `${upstream}@openchamber/web/bin/cli.js`),
        opencode: await realpath(values.opencode ?? `${upstream}@opencode/cli/bin/opencode.exe`),
        port: Number(values.port ?? 3123),
      });
      await runVersion(plan.env.OPENCODE_BINARY, ['--version'], plan.env, plan.cwd, '2.0.23');
      await runVersion(process.execPath, [plan.args[0], '--version'], plan.env, plan.cwd, '2.1.1');
      const controller = new AbortController();
      const stop = () => controller.abort();
      process.on('SIGINT', stop); process.on('SIGTERM', stop);
      try {
        result = await supervise(plan, values.state, controller.signal);
        if (!result.cancelled && result.code !== 0) process.exitCode = 1;
      } finally { process.off('SIGINT', stop); process.off('SIGTERM', stop); }
    } else throw new Error('Unknown command');
    console.log(JSON.stringify(result, null, 2));
  }
} catch {
  // Parser, JSON and upstream errors can contain operator data; keep errors fixed.
  console.error('Assistant command failed. Check command options, regular settings files, state integrity and documented prerequisites.');
  process.exitCode = 1;
}
