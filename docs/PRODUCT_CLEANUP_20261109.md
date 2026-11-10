# Product cleanup, 2026-11-09

This implementation has been included in [Beta 1](releases/native-v0.2.0-beta.1.md)
since 2026-11-10. Package identities and findings below describe the original
verification state. The beta has its own complete build and `BUILD_PROVENANCE.json`.

This state belongs to `feat/lean-product-runtime`, based on published Preview 3.
It changes neither those release artifacts nor the local installation. Its
verification packages retain the existing version number; they are neither a
new Preview 3 download nor a new release. Publication requires a distinct version
and package acceptance.

## Removed functionality

| Area | Change |
| --- | --- |
| Server startup | Browser broker, dev-server scanner, terminal/dictation, remote/relay/Spaces composition and their startup/shutdown services are removed from product composition. The listener requires loopback. |
| Automation | Assist, Goal, Work, Routing, Scheduler, Dispatch and Auto-Accept services are no longer created or started. New goal/worktree requests in the shared session service are refused before side effects. |
| Routes | Git/repository accounts, Linear, walkthroughs, tracked items, guests/extensions and control routes are not registered. Existing filesystem write/execution routes are removed from their module. Explicit refusals prevent fall-through to the Core proxy. |
| Chat events | `/api/openchamber/events` remains an independent authenticated SSE channel. Metadata delivery therefore needs no scheduler. Event names and connection IDs remain compatible. |
| CLI | Only version probing, help and password-protected `serve --foreground --host 127.0.0.1 --port …` remain. No daemon, service, tunnel or updater commands. Version probing retains the pinned dependency contract. |
| Client startup | Global push/browser-provider/memory/routing synchronization, repository/Linear account queries and terminal keepalive are removed from App/MainLayout. Chat/status events and enterprise policy remain. |
| Settings | Product navigation contains providers, appearance, chat, sessions/retention, shortcuts and About. Other persisted setting slugs fall back to product navigation. Native project/MCP setup remains with the Host. |
| Composer | File attachments, model selection, send, queue, stop and focus remain. Microphone, Goal and automatic permission acceptance are removed from the footer. The original editor and attachments remain. |
| Application package | Conservative import analysis removes only unreachable files in named feature families and unused packages. Shared consumers prevent package removal. Unknown computed imports conservatively retain local modules. |

The first complete package build removed 241 server/CLI files and 24 packages,
including `node-pty`, `bun-pty`, `sherpa-onnx-node` and its Windows runtime,
`web-push`, `@clack/prompts`, `qrcode-terminal` and exclusive transitive dependencies.
That build preceded the final SSE/client correction and establishes size/removal
only. Final package startup requires separate evidence.

## Product identity and attribution

Language files use TwinCAT Assistant Core and Chat for product modules, including
additional feature dictionaries. This covers 2,731 original and 686 additional
strings. External offering names OpenCode Go, Zen and Console remain. Keys and
placeholders are unchanged. The product receives a TC mark and matching SVG
favicon. The loading screen and window title identify TwinCAT Assistant.

About reads the actual application version through the protected product endpoint.
It does not present the OpenChamber version as the product version or offer an
upstream self-updater. OpenCode and OpenChamber remain visible as foundations with
their project links. Licenses and notices are retained. Technical package, API,
event, environment and persistence names remain compatible parts of their contracts.

Whole-module replacements remain readable under `upstream/overlays/openchamber/`.
`scripts/source-overlay.mjs` checks the original normalized SHA-256 before replacing
a module. Smaller changes remain exact substitutions. The build receipt and package
builder bind replacement files too. Unknown source changes are still rejected.

## Preserved state and limits

No existing chats, sign-ins, accounts, project bindings or historical feature
directories are deleted or read. Removing runtime functionality neither migrates
nor cleans up its old user data. Locked npm files document installation inputs;
the final manifest describes what is actually delivered.

The unchanged Core remains a complete pinned engine binary. Removing Chat files
cannot remove its internal third-party names or built-in CLI parts. Shared
session, Git, memory/knowledge and configuration helpers remain partly reachable
because ordinary conversations, metadata and old state use them. Internal session
knowledge is distinct from the TwinCAT Knowledge MCP.

The generic web client still contains old panel/starter/statistics entry points
and some queries for removed features. The native adapter already hides general
panels, the sidebar and starters. Browser verification without the adapter therefore
reports expected 404s for removed functionality and is not clean acceptance of the
generic client. These client remnants, shared helpers and older PWA raster images
need further bounded cleanup. Electron, VS Code and Mobile are not delivery targets;
their source trees are not rebranded as TwinCAT products.

## Verification

- Actual pinned Chat/UI build; unchanged Core reused by hash.
- 26 focused Node/integration checks passed without skips, including protected
  startup, provider sign-in with a synthetic key, model catalog, profile recovery
  and the package-builder fixture.
- After separating SSE: actual source startup, opening SSE frame, goal/worktree
  refusal and controlled process-tree shutdown passed.
- UI/Web typechecks and focused UI lint; the original composer remains.
- Lint on eight affected UI/composition files reports no errors. Five existing
  `setLinkedReferences` hook warnings remain; the exact reconstructed baseline
  reports the same five warnings.
- Project-wide Knip ran and reported 74 unused files, 297 exports, 214 exported
  types and three configuration notices across the upstream monorepo. This is
  not a zero-finding cleanup result. Other clients' files and tests were not
  indiscriminately removed.
- Isolated browser: login, composer, product navigation and About with actual
  application version. No model request or provider OAuth acceptance.

Ignored verification files are under `artifacts/` and
`output/playwright/lean-product/`. Browser profiles and all state directories are
synthetic. This establishes no installed IDE usage, PLC action, model response,
real update/rollback or new CI delivery. At this dated checkpoint, roadmap items
5–7 remain unchanged and open.

## Complete verification package

`artifacts/lean-product-verified-20261109-application` contains 21,537 files and
630,292,500 file bytes. Published Preview 3 contains 22,088 files and 690,823,921
bytes. The reduction is 551 files and 60,531,421 bytes. Verification-manifest SHA-256:
`b0603c8f7f75984f554652db03e199370444e0796b013b9282b8cef58369f0da`.

The actual package smoke passed protected startup, product version, chat metadata
SSE, excluded routes, both real MCP probes on synthetic files, restart, state
preservation and final unchanged-package integrity. No provider request or PLC
action occurred.

Only the responsive settings arrangement changed after that build: narrow desktop
WebViews also use compact navigation. The source UI was rebuilt and checked for
that change. The package above establishes the preceding state. A release must
rebuild the complete package from final source; no different or retroactively
rewritten manifest identity is claimed.
