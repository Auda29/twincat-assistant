import { readFile, realpath } from 'node:fs/promises';
import { prepareLaunch, runVersion } from './launcher.mjs';
import { runtimePaths } from './runtime-paths.mjs';

// Preparation only. CLI signals and the native readiness/owner protocol stay
// with their entry points; no service or launcher lock exists until supervision.
export async function prepareRuntimeLaunch(root, options, inherited = process.env) {
  const selected = options.openchamber && options.opencode ? null : await runtimePaths(options.runtime);
  let openchamber = options.openchamber ?? selected.openchamber;
  let opencode = options.opencode ?? selected.opencode;
  // The operator CLI has always canonicalized its trusted binary paths. Native
  // receipt-selected artifacts retain prepareLaunch's regular-file checks.
  if (options.resolveOperatorPaths) {
    openchamber = await realpath(openchamber);
    opencode = await realpath(opencode);
  }
  const plan = await prepareLaunch(root, { openchamber, opencode, port: options.port }, inherited);
  const sourceLock = JSON.parse(await readFile(new URL('../upstream/source-lock.json', import.meta.url), 'utf8'));
  await runVersion(plan.env.OPENCODE_BINARY, ['--version'], plan.env, plan.cwd, sourceLock.opencode.version);
  await runVersion(process.execPath, [openchamber, '--version'], plan.env, plan.cwd, sourceLock.openchamber.version);
  return plan;
}
