# Server responsibility inventory

Recorded 2026-10-29 on `backend`, starting from main commit
`de4eef86655e553c6b1045853b5aedf6587ec4b9`.

This first step of the next redesign phase inventories existing implementation
and assigns responsibilities to the agreed modules. Assignment is a target;
package names, processes, APIs and formats are unchanged. Removal proposals
remain joint decisions.

## Findings

OpenChamber combines Chat presentation with a substantial local server. That
server starts OpenCode, protects access, bridges APIs/events and adds session
features. Shipping only the built UI cannot replace it.

TwinCAT Assistant already has a shared launcher, isolated operator profile and
process supervision. Build on those foundations: organize ownership and document
startup first, then adopt individual existing OpenChamber modules with their
implementation and behavior.

## Authoritative sources and review boundary

| Foundation | Reviewed state |
| --- | --- |
| TwinCAT Assistant | `de4eef8`; [source lock](../upstream/source-lock.json), [runtime integration](RUNTIME_INTEGRATION.md) |
| OpenCode | `2.0.23`, revision `0fd7e2829449b052abf0078666669302923d77af` |
| OpenChamber | `2.1.1`, revision `e302062e3be0686986594fddabdafd8a97c129e5` |
| TwinCAT adaptations | [Declared patches](../upstream/overlays/openchamber/changes.json), [source contract](../upstream/overlays/openchamber/twincat.ts), [WebView adapter](../src/embedded-chat.mjs) |

Generated upstream checkouts in the fixed runtime checkout were read only; HEADs
match the lock. OpenChamber server files are unchanged at this checkpoint.
Existing modifications affect web entry/composer and our source contract.
Upstream links below pin exact revisions.

This is source review. No services or provider sign-ins were started, and no
operator database content or credentials were read. Later implementation changes
still require runtime verification.

## Existing startup and process ownership

1. The native [Host](../native/IdeHost/NativeChatRuntime.cs) reads runtime
   configuration and starts [native-chat.mjs](../src/native-chat.mjs), passing
   state path, port, local UI password and optional MCP configuration through a
   bounded control channel.
2. The launcher checks profile, versions and free port.
   [runtime-paths.mjs](../src/runtime-paths.mjs) selects verified source or
   reference distribution; [launcher.mjs](../src/launcher.mjs) builds the
   environment and `openchamber serve --foreground --host 127.0.0.1` invocation.
3. [supervisor.mjs](../src/supervisor.mjs) holds the profile lock. Windows
   [native supervision](../src/windows-native.mjs) uses a Job Object; Unix uses a
   process group.
4. With Knowledge configured, [shared-chat-runtime.mjs](../src/shared-chat-runtime.mjs)
   runs the broker and starts the OpenChamber CLI as a child. Knowledge MCP remains
   a separate process. The wrapper stops its broker with the owned Chat process.
5. OpenChamber's [entry point][oc-entry] composes modules; its [startup pipeline][oc-startup]
   registers events, proxy and static files. HTTP can be ready while OpenCode is
   still starting. [Lifecycle][oc-lifecycle] starts the separate OpenCode binary
   with `serve --hostname ... --port ...` and manages readiness, health and restart.
6. Native login uses `/auth/session`; only its HttpOnly cookie enters WebView2.
   A reachable protected Chat listener and ready OpenCode are different states.

Several controlled processes currently run. Whether one can be removed remains
open. OpenChamber server is Node/Express; OpenCode is a separate built binary.
One release does not imply one process runtime.

## Required shared backend responsibilities

**Core** includes TwinCAT Assistant's local service management while retaining
OpenCode's agent implementation. **Chat** means the existing OpenChamber WebView UI.

| Responsibility | Existing owner/source | Contract/behavior | Target ownership |
| --- | --- | --- | --- |
| Startup/readiness/restart/shutdown | Our launcher/supervisor; OpenChamber [lifecycle][oc-lifecycle], [startup][oc-startup], [shutdown][oc-shutdown] | Two ownership layers; port/profile lock, versions, separate OpenCode readiness, owned-child cleanup | Shared Core management; Host signals lifetime; reuse supervision |
| Serve Chat UI | [Static routes][oc-static] | Built `dist`, hashed assets, cache rules, SPA fallback; API-only is separate | Local backend serves it; presentation/build artifacts belong to Chat |
| Local service access | Host login; [UI auth][oc-ui-auth], [core routes][oc-core-routes], [request security][oc-security] | `/auth/session`, port-bound cookies, API/WebSocket access and origin checks | Core protects service; Host signs in/transfers cookie; provider login remains separate |
| Bridge OpenCode API | [Proxy][oc-proxy], [network runtime][oc-network] | `/api/*`, internal OpenCode credentials, headers/directory mapping, timeouts/errors/readiness; specific routes precede generic proxy | Adopt existing bridge in Core; Chat initially retains calls |
| Streaming/reconnect | [Event stream][oc-events], [proxy][oc-proxy], UI synchronization | OpenCode SSE; browser `/api/global/event/ws`, `/api/event/ws` and SSE routes; replay, heartbeat, backpressure, gap repair | Server in Core; events/presentation in Chat; accept both together |
| Messages/models/cancel/tools | OpenCode [session protocol][core-session-api], [integration protocol][core-integration-api], [MCP handler][core-mcp]; [SDK wrapper][oc-client] | Prompt/synthetic context delivery, per-session model; `/api/session/:sessionID/interrupt`; OpenCode executes agent/tools | Existing OpenCode in Core; composer/selection/presentation in Chat |
| Providers/login | OpenCode integration/credential APIs; [provider routes][oc-provider-routes], [auth reader][oc-provider-auth], [model discovery][oc-model-discovery] | API keys, OAuth, supported login, provider configuration/catalog; full credential listing explicitly refused before browser proxy | Implementation/credentials in Core, existing login UI in Chat; service/model credentials separate |
| History/session extras | OpenCode database; [session service][oc-sessions], [metadata][oc-metadata], [folders][oc-folders], [proxy][oc-proxy] | Native OpenCode sessions plus archive/metadata/folders; proxy enriches archive state and unadopted legacy metadata | Persistence/services in Core, history picker in Chat; retain IDs/project mappings |
| Message queue | [Queue][oc-queue], ChatInput/queue store | `/api/message-queue*`, `message-queue.json`, order/hold/edit/later delivery with frozen context | Initially retain entire server part in Core; UI in Chat; distinct from OpenCode durable inbox |
| Session status/attention | [Session runtime][oc-session-runtime], [watcher][oc-watcher], [notification routes][oc-notifications] | `/api/sessions/status`, `/api/sessions/snapshot`, `/api/session-activity`; event-derived activity/pending interactions | Server status in Core, display in Chat; needed without mobile push too |
| Settings/configuration | [Settings][oc-settings], [configuration routes][oc-config-routes], [provider routes][oc-provider-routes], our [profile](../src/state.mjs) | `settings.json`, `preferences.json`, OpenCode layers; agents/commands/MCPs/snippets/plugins; owners/watchers differ | Core manages, Chat presents; managed TwinCAT policy remains verified; assess generic writers separately |
| Attachments/files | [Composer][oc-composer], [outgoing assembly][oc-outgoing], SDK; [Files API][oc-files-api], [FS routes][oc-files] | Frozen native POU references; other paths use `/api/fs/read`, `/api/fs/raw`, `/api/fs/stat`, `/api/fs/upload`; not all attachments upload | Chat selects/stages, Host captures, Core transports; separate reads/uploads from generic writes/exec |
| TwinCAT project/POU context | [Native preparation](../src/native-mcp.mjs), [workspaces](../src/project-workspace.mjs), [Host binding](../native/IdeHost/ChatBindingStore.cs), source contract | Canonical solution/PLC identity, session, historical workspaces; distinct active object/staged snapshot/confirmed-send states | Host captures IDE; Core maps project/session; Chat owns actual composer state; do not replace contracts by similarly named substitutes |
| Engineering MCP | OpenCode MCP client, project configuration, existing `twincat-mcp-mono` | `configuredProjectFiles`, exactly one approved PLC project per managed config, currently read-only | Independent MCP; Core manages connection/project boundary; no parallel tool implementation |
| Knowledge MCP | [Broker](../src/knowledge-broker.mjs), [stdio client](../src/knowledge-client.mjs), existing `twincat-knowledge-mcp` | Shared backend in owned runtime, preload, approved tools, complete hits/topics delivered to model | Independent MCP; shared broker/lifecycle in Core; corpus preparation remains with Knowledge MCP |

## Additional services and open choices

[Feature composition][oc-features], [bootstrap routes][oc-bootstrap] and server
entry register further services. These are proposals; step 1 removes nothing.
A hidden UI does not establish server-route removal.

| Feature/modules | Dependency to inspect | Proposed decision, still open |
| --- | --- | --- |
| Terminal/PTY/shell and generic FS, `terminal/`, `fs/` | Attachment transport shares Files with write/delete/rename/exec | Consider terminal/generic editing/execution removal; preserve attachments and split helpers deliberately |
| Git/worktrees, `git/`, repository credentials, managed SSH | Proxy/session creation know worktree readiness; general Chat can create managed directories | Consider Git/login removal; retain project chats and historical directories |
| GitHub/GitLab/Linear, source control, tracked items, walkthroughs | Full issue/PR references enter composer; server provider caches/accounts | Decide separately from model providers; GitHub panel removal must not break model-provider login |
| Tunnel/relay/pairing/remote clients/Spaces | URL, WebSocket/auth share local helpers; launcher already sets relay off | Consider remote/Spaces removal; retain local protection/transport |
| Browser control/dev server/dev tunnel | Privileged routes and WebSockets | Assess individually for TwinCAT scope |
| Speech/dictation/TTS, `dictation/`, `tts/` | Existing composer speech, extra models/provider access/streaming | Decide usage jointly; not merely presentation |
| Push/APNS/desktop notifications, `notifications/` | Same composition supplies status, attention and blockers | Separate external notifications from necessary status service |
| Goal/Assist/Work, routing/small model/magic prompts/scheduling | Observes sessions, changes delivery, adds model calls; permission-auto-accept changes approval | Decide each automation independently; OpenCode agent execution/manual approval remain core |
| Agent memory/project context/session knowledge/message search | Own persistence/session metadata; `session-knowledge/` is not Beckhoff Knowledge MCP | Inventory existing data/use before removal |
| Guests/extensions/control API/managed OpenChamber agent tool | Managed plugins/config layers at OpenCode startup; shared session services | Decide extension/additional agent-tool scope independently of OpenCode tool execution |
| Quotas/model metadata/themes/PWA/updaters/installers | Chat consumes metadata/themes; upstream updaters can act independently | Separate UI, data-service and update ownership; target one TwinCAT release |

Registrations/imports are traceable in [feature routes][oc-features],
[bootstrap][oc-bootstrap], [startup][oc-startup] and [entry][oc-entry]. This groups
responsibilities rather than enumerating every HTTP route.

## Data ownership and paths to preserve

Relative backend paths start at the configured operator profile. Native data is
separate under `%LOCALAPPDATA%\TwinCatAssistant`. See [redesign plan](TWINCAT_RUNTIME_UI_PLAN.md)
and [runtime integration](RUNTIME_INTEGRATION.md) for current defaults.

| Data | Current owner/storage | Adoption requirement |
| --- | --- | --- |
| Conversations/messages/durable inbox/provider credentials | OpenCode `data/opencode/opencode.db`, existing channel/IDs | Retain owner; test format/path changes only on separate copies |
| Project workspaces/approved MCP configuration | Backend `projects/`, managed configuration | Retain stable identity, historical directories and journal |
| Native session bindings | Host `%LOCALAPPDATA%\TwinCatAssistant\native-chat\threads\` | Retain durable StateRoot/session binding and transient origin/IDE checks |
| Local service password/runtime config | Host `native-chat/ui-password.bin` via DPAPI; `native-chat.json` under `%LOCALAPPDATA%\TwinCatAssistant` | Separate from operator profile; naming must not move them |
| OpenChamber settings | `openchamber/settings.json`, `openchamber/preferences.json`; project config/themes under same root | Reuse settings/migrations; branding alone does not justify path changes |
| Front queue | `openchamber/message-queue.json` | Preserve requests/order/frozen context; distinct from Core inbox |
| Folders/archive/session extras | `sessions-directories.json`, archive state, `session.metadata.openchamber`; legacy `sessions-metadata.json` | Preserve proxy enrichment/legacy adoption; database retention alone is insufficient |
| Unsent composer/references | Chat stores, separate from confirmed messages | Observe stage/remove/queue/send confirmation through original composer |
| Documentation corpus/broker | Knowledge MCP and managed configuration | Retain corpus/validation; measure cold startup separately |

This inventory moves neither fixed runtime checkout, RepositoryRoot, operator
profile nor database.

## Contracts to verify together

1. Keep service and provider access separate. Cookie login, API/WebSocket security
   and OpenCode login have different owners. A simplified proxy must never expose
   the server-internal credential list to Chat.
2. Preserve route order: configuration/status, credential refusal, body handling
   and special event paths precede the generic proxy.
3. Preserve the event contract across OpenCode SSE, OpenChamber WS bridge, replay
   and Chat sync. Successful prompt POST alone establishes no streaming,
   reconnect or cancellation behavior.
4. Distinguish delivery from queueing. Model input uses that request's frozen
   context; later POUs cannot reinterpret staged work. Ambiguous transport errors
   must not cause unintended duplicates.
5. Maintain project boundaries across HTTP, events, history and MCP directory/location
   mapping. Names alone do not establish POU/PLC identity.
6. Identify source types. Engineering reads saved files; an attachment proves only
   its actual snapshot. Unsaved action/method capture remains a Host/context task,
   not something solved by moving servers.
7. Report listener, login, Chat mount, OpenCode and Knowledge readiness separately.
   Failed history loading must not be mistaken for empty history and create a chat.

## Recommended first implementation

The [shared startup contract](START_CONTRACT.md) now defines inputs, sequence,
readiness, ownership and acceptance on the integrated frontend state. Startup-code
consolidation remains next.

Step 2 assigns existing startup to Core, first documenting configuration, profile,
verified artifacts, ports, access, ownership, readiness and shutdown. Host remains
the initiator of its local runtime's start/end. Preserve the existing process
chain initially. Later, adopt one bounded module with its required helpers/tests
at a time. Remove a process only after assuming all needed responsibilities and
practically verifying the remaining startup path.

Backend/frontend must coordinate affected calls before interface changes.
Technical names `assistant-core`, `assistant-chat`, `assistant-host` follow in a
separate traceable step.

### Existing verification foundation

These checks existed in source and were not rerun for this documentation-only step:

- Startup/shutdown/native access: local `tests/native-chat.test.mjs`,
  `tests/launcher.test.mjs`, `tests/supervisor.test.mjs`,
  `scripts/Test-ChatIntegration.ps1`; upstream `opencode/lifecycle*.test.js`,
  `opencode/shutdown-runtime.test.js`, `ui-auth/ui-auth.test.js`,
  `ui-auth/session-cookie.test.js`.
- Bridging/reconnect: upstream `server/opencode-proxy.test.js`,
  `server/sse-routes.test.js`, `server/lib/event-stream/`, `packages/ui/src/sync/`.
- Providers/policy: upstream `opencode/auth.test.js`,
  `opencode/provider-source-route.test.js`, `opencode/core-routes.test.js`,
  original OpenCode provider/integration tests.
- Queue/history/attachments: upstream `message-queue/runtime.test.js`,
  `openchamber-sessions/session-metadata-store.test.js`, `fs/routes.test.js`,
  `packages/ui/src/components/chat/composer/submit/__tests__/`; local
  `tests/embedded-chat.test.mjs`, `tests/integration/session-compatibility.test.mjs`,
  `tests/integration/native-mcp.test.mjs`.

Upstream paths without a package prefix are relative to `packages/web/server/lib/`;
the two `server/` paths are relative to `packages/web/`. Implementation selects
affected checks and adds concrete missing acceptance cases.

## Original inventory verification

The original review confirmed 50 local document links, 33 pinned upstream file
links and seven local plus 14 explicit upstream test files/directories. Another
24 targeted source checks confirmed selected routes, filenames and startup calls.
`git diff --check` reported no formatting errors.

The original diff contained only this inventory and links in the documentation
index/redesign plan. Runtime, UI, MCP implementation, configuration and installation
were unchanged.

[oc-entry]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/index.js
[oc-lifecycle]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/lifecycle.js
[oc-startup]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/startup-pipeline-runtime.js
[oc-shutdown]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/shutdown-runtime.js
[oc-static]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/static-routes-runtime.js
[oc-ui-auth]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/ui-auth/ui-auth.js
[oc-core-routes]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/core-routes.js
[oc-security]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/security/request-security.js
[oc-proxy]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/proxy.js
[oc-network]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/network-runtime.js
[oc-events]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/event-stream/DOCUMENTATION.md
[oc-client]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/ui/src/lib/opencode/client.ts
[oc-provider-routes]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/routes.js
[oc-provider-auth]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/auth.js
[oc-model-discovery]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/model-discovery.js
[oc-sessions]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/openchamber-sessions/routes.js
[oc-metadata]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/openchamber-sessions/session-metadata-store.js
[oc-folders]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/session-folders/routes.js
[oc-queue]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/message-queue/runtime.js
[oc-session-runtime]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/session-runtime.js
[oc-watcher]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/watcher.js
[oc-notifications]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/notifications/routes.js
[oc-settings]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/settings-runtime.js
[oc-config-routes]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/config-entity-routes.js
[oc-composer]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/ui/src/components/chat/ChatInput.tsx
[oc-outgoing]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/ui/src/components/chat/composer/submit/buildOutgoingMessage.ts
[oc-files-api]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/src/api/files.ts
[oc-files]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/fs/routes.js
[oc-features]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/feature-routes-runtime.js
[oc-bootstrap]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/bootstrap-runtime.js
[core-session-api]: https://github.com/anomalyco/opencode/blob/0fd7e2829449b052abf0078666669302923d77af/packages/protocol/src/groups/session.ts
[core-integration-api]: https://github.com/anomalyco/opencode/blob/0fd7e2829449b052abf0078666669302923d77af/packages/protocol/src/groups/integration.ts
[core-mcp]: https://github.com/anomalyco/opencode/blob/0fd7e2829449b052abf0078666669302923d77af/packages/server/src/handlers/mcp.ts
