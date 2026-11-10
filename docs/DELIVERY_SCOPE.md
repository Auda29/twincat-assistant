# Bounded delivery scope for Core, Chat and Host

**Follow-up, 2026-11-09:** actual removal of server modules, CLI functionality,
native dependencies and product text is documented in
[Product cleanup](PRODUCT_CLEANUP_20261109.md). Its scope supersedes corresponding
“later” decisions in the original matrix below. Evidence dated 2026-11-05 remains
historical evidence.

This step implements roadmap item 2 using BACKEND_SERVER_INVENTORY.md. It does
not claim complete removal of all upstream extras. The source-built application
path is authoritative. The explicit reference distribution remains unchanged for
development/compatibility checks. No operator data is read, moved or deleted.

The only delivered client is native TwinCAT Assistant Host with WebView against
the local server. Web connections to the same source server receive the same
explicit 404 responses. Electron, VS Code, hosted mobile and Capacitor mobile
are not delivery targets and were neither built nor accepted. Other clients
connecting to this server cannot use refused HTTP features either. Standalone
upstream distributions do not receive this overlay.

## Decisions and implementation

| Inventory area | Decision for this step | Implementation and remaining limit |
| --- | --- | --- |
| Startup, auth, proxy, events, providers, models, messages, tools | Retain | Launcher, credential lock, SSE/WS bridge and agent implementation unchanged. No API/provider contract renaming. |
| History, archives, folders, queue, session status | Retain | IDs, databases, queue files and metadata remain with their owners. |
| Settings, attachments, project/POU context, both MCPs | Retain | Read, raw/stat/list, preview and upload remain reachable. Native project binding and frozen references unchanged. |
| Terminal/PTY | Disable | Server entry no longer imports/creates terminal runtime. No terminal WS handler, shell discovery or PTY idle interval. HTTP family also refused before proxy. Embedded context rail hides entry point. |
| Generic filesystem mutation/execution | Disable | All write/delete/rename/exec/mkdir/clone/reveal methods and subpaths return 404 before original routes. Upload intentionally remains. This is not a global filesystem sandbox. |
| Browser control and dev server | Disable | Route registration removed; HTTP families refused before proxy. Embedded browser/terminal panels remain hidden. Internal broker/scanner helpers remain because of shared consumers. |
| Updater/installer | Disable independent upstream updaters | OpenCode upgrade/upgrade-status/install-v2 and OpenChamber update-check/update-install return 404. Version/health/compatibility remain readable. Installer and CI unchanged here. |
| Package inventory | Reduce | After locked npm-ci, redundant npm @opencode/cli and platform packages are removed. Explicit source Core remains under runtime/opencode. Server *.test.js/*.spec.js files are removed. New transitive CLI consumers fail the build. Licenses remain. |
| Git/worktrees, repository accounts/SSH, GitHub/GitLab/Linear, tracked items, walkthroughs | Decompose later | No accounts, historical directories or reference data deleted. Embedded navigation already hides panels; this does not establish removal of server services. |
| Tunnel/relay/pairing/remote/Spaces | Separate later | Existing relay-off and loopback startup remain. Shared auth/transport modules retained. No new general remote-access refusal is claimed. |
| Dictation/TTS, push/APNS, quotas/model metadata, themes/PWA | Retain until separate decomposition | Speech needs a product decision; push shares status composition, and metadata/themes are used by Chat. No blanket dependency removal. |
| Goal/Assist/Work, routing/small model/magic prompts, scheduler, permission auto-accept | Review individually later | No new automation enabled. Existing embedded refusal of interactive auto-accept remains. Agent execution and manual tool approval remain independent. |
| Agent memory, project context, session knowledge, message search | Retain pending data/usage review | Existing persistence is not deleted. Session knowledge is not the Beckhoff Knowledge MCP. |
| Guests/extensions, control API, managed agent tool, plugins | Separate incrementally later | Shared configuration/session services and TwinCAT reference transport retained. |

## Naming and attribution

Product module ownership is named TwinCAT Assistant Core, Chat and Host as in
START_CONTRACT.md. Source Chat's HTML, PWA and dynamic window titles use TwinCAT
Assistant Chat. Native provider hints refer to Chat; native Knowledge status
refers to Core. Upstream package names, version outputs, API/environment names,
configuration keys, data paths and third-party licenses remain unchanged.
OpenCode and OpenChamber remain correct attribution names; functions such as
`createOpenCodeConfig` write their contracts and are not product-brand names to
replace.

## Reproduction and evidence boundaries

- `node scripts/build-source-runtime.mjs` actually builds both pinned sources.
  Overlay changes require a new source build. `--ui-only` may reuse only the
  unchanged Core verified by receipt and binary hash.
- `node --test tests/delivery-scope.test.mjs` checks refused/retained routes,
  encoded paths, package removal, licenses/product modules and refusal of new
  CLI consumers.
- `npm test` checks Node contracts including data/policy and process ownership.
  Build WindowsHost using SDK 8.0.425 first.
- `node --test tests/integration/native-chat.test.mjs` checks the actual source
  listener, cookie-authenticated GET/POST refusals, synthetic-key provider
  registration, model catalog/consent and clean process-tree shutdown.
- `node scripts/build-application-package.mjs <new-absolute-path>` verifies
  receipt, MCP manifests and versions and builds the complete application.
- `node scripts/test-built-application.mjs <package-path>` checks protected
  startup, refused routes, both real MCPs on synthetic files, restart and unchanged
  integrity. No model, PLC, ADS or compiler action.

Packaged lockfiles describe npm inputs before explicit reduction; the final
application manifest describes retained files. At this checkpoint, PTY/speech
packages remain in the locked upstream dependency installation: blanket removal
would not be verified separation. The removed npm Core distribution is fully
replaced by the explicit source Core.

Fixtures do not establish real provider OAuth, model responses, paid-model
streaming acceptance or installed IDE use. Real Host acceptance, update/rollback
and provider checks remain external gates owned by the parent. This step does
not begin items 5–7. Naming changes and fixtures cannot close those gates.

## Local verification, 2026-11-05

- Complete source build of OpenCode 2.0.23 and OpenChamber 2.1.1, then verified
  UI rebuild with final title change. Receipt `artifacts/source-runtime/runtime.json`,
  SHA-256 `8471c25df8800dacf185e7b4999bbf90094fa0349d2b2a74a757304c2e5fbeb4`.
- Isolated SDK 8.0.425; WindowsHost and x86 IdeHost built, 151 native
  policy/context/chat regressions passed. Existing VSTHRD010 warnings in Knowledge
  and diagnostics remain visible.
- 131 serial Node tests passed without skips. An earlier parallel run passed
  130/131: the process fixture missed readiness under load. The serial full run
  resolves that result. Missing worktree dependencies/helpers were installed
  and are not external blockers.
- Four integration cases passed without skips: synthetic package builder;
  actual native source listener with route refusals and provider/model catalog;
  native input boundary; separate session/credential data copy between reference
  distribution and source runtime.
- Twelve original upstream event-stream/startup-pipeline tests passed. UI/Web
  typechecks and server-entry syntax check passed.
- `oxlint` on three changed upstream modules reports 60 existing errors. The
  untouched pinned files report the same 60 errors in existing `typeof` checks
  and conditional object spreads; the new route refusal adds neither pattern.
  Lint is not claimed green. Dead-code analysis ran and existing backlog was
  reviewed; the terminal parser remains required by the event-stream import.
- Complete package `artifacts/scope-delivery-application`: 22,085 inventoried
  files, 690,756,123 file bytes. No npm `@opencode/cli`/platform packages or Chat
  server test files. Manifest SHA-256:
  `c33a4b3deb4f26b7a014cdb63a87a7cfcd0f496bc2828510b9e016ce3384a583`.
- Complete package smoke passed protected loopback startup, actual GET/POST
  login and POST route refusals, saved synthetic POU reading through Engineering
  MCP, corpus-status probing through the actual Knowledge MCP, restart with a
  preserved synthetic state file and complete integrity reverification.
  Knowledge correctly reports `not_available` without a corpus; this does not
  accept production knowledge search. The full local run took approximately four
  minutes including repeated full verification, not an installed Host startup
  measurement.

Ignored worktree logs: `local-node-serial.log`, `local-native-build.log`,
`local-scope-integration.log`, `local-upstream-regressions.log`,
`local-web-typecheck.log`, `local-ui-typecheck.log`, `local-overlay-lint.log`,
`local-baseline-lint.log`, `local-dead-code.log`, `local-package-build.log` and
`local-built-package-smoke.log`. The parent rebuilds final integrated artifacts.
This local package was neither pushed nor installed.
