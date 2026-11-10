# Shared startup contract for Core, Chat and Host

Inventory dated 2026-10-29 at integrated revision `57fb2cc`, including frontend
commit `ddc9dce6b2662090b223149e0b4d032ef47b2b83` without conflicts. Earlier design
verification refers to this state. Startup preparation was consolidated on
2026-10-30.

This contract develops the [server inventory](BACKEND_SERVER_INVENTORY.md), describing
the existing startup path and requirements for shared management in Assistant Core.
Initial consolidation has been implemented in [runtime-start.mjs](../src/runtime-start.mjs)
since 2026-10-30. This document introduces no service, API, protocol version or
additional process.

## Responsibilities

| Module | Startup responsibility |
| --- | --- |
| Assistant Host | Native configuration, local service password, owned-start lifetime, WebView, login, navigation and loading presentation |
| Assistant Core | Validate profile/approved configuration, prepare Chat resources, select artifacts, supervise processes and reuse existing server tasks |
| Assistant Chat | Load existing UI, initialize runtime/source contract, load composer/sessions and present status |
| Engineering MCP | Project-scoped tools through the existing MCP contract; currently reads saved PLC files |
| Knowledge MCP | Corpus/search tools; its shared broker belongs to local runtime management |

OpenCode owns model integration, agent execution, tools and conversation database.
OpenChamber still performs its existing server tasks. Core/Chat/Host describe
ownership, not a required process count.

## Inputs and configuration ownership

Native `native-chat.json` uses existing `NodePath`, `RepositoryRoot`, `StateRoot`,
`DotNetRoot` and optional `McpSettingsPath`. The Host checks absolute normalized
paths and required executables. The local service password is separately
DPAPI-protected in native data. Provider credentials belong to OpenCode.
See [NativeChatConfiguration/NativeChatCredentials](../native/IdeHost/NativeChatRuntime.cs).

The existing `src/native-chat.mjs` request is one UTF-8 JSON line on stdin,
currently without a `version` field.

| Field | Existing validation/meaning |
| --- | --- |
| `state` | Required absolute operator-profile path, at most 2048 characters, no NUL |
| `port` | Required integer 1024–65535; binds only to `127.0.0.1` |
| `password` | Required non-whitespace string, 12–1024 characters, no NUL; passed unchanged to preserve Unicode |
| `mcpConfig` | Optional absolute native MCP configuration path, at most 2048 characters, no NUL; file checked separately |

Unknown fields are refused. Exactly one leading UTF-8 BOM is accepted. Node bounds
input to 32,768 bytes and waits at most ten seconds for the first request.
Additional lines stop the running request. Provider, model and PLC source are
not accepted as extra startup fields. See
[request validation, parser and main](../src/native-chat.mjs).

Operator CLI `init`, `doctor` and `start` remain. `start` uses the same profile,
launcher and supervisor. Explicit binary paths remain operator options, not
native browser/startup fields. The CLI expects `OPENCHAMBER_UI_PASSWORD`;
`prepareLaunch` currently checks only that it is nonempty. Consolidation must not
assume native length restrictions already apply to both entry points.
See [CLI](../src/cli.mjs) and [prepareLaunch](../src/launcher.mjs).

## Owned startup sequence

1. **Validate request and free port.** `startNativeChat` refuses occupied ports
   before creating a profile. A Host may instead connect to an already reachable,
   compatible protected service without owning it.
2. **Validate/create profile.** Initialize a missing profile through existing
   functions; check existing managed policy/directories. Do not replace damaged
   or divergent settings as an empty profile.
3. **Validate optional MCP setup.** Global native setup prepares Knowledge.
   Engineering is configured only for an exact solution/PLC identity in its
   managed project workspace.
4. **Select/verify artifacts.** Source receipt binds revision, platform, contract
   version, inputs and artifact hashes. Explicit source selection, or automatic
   selection through an existing receipt, fails on mismatch. Explicit reference
   distribution selection remains. Broken source builds never silently fall back.
5. **Prepare Chat profile completely.** `prepareLaunch` invokes
   `prepareChatWorkspace`: both required Chat directories, both TwinCAT theme
   files, existing default selection and TwinCAT commands precede server startup.
   Custom themes and existing projects remain. Existing defaults are updated in
   authoritative OpenChamber `preferences.json`, including UI selection. Custom
   colors and unrelated setting timestamps remain; unknown/damaged documents are
   not overwritten. Repeated preparation preserves chats.
6. **Build environment/launch plan.** Existing `command`, `args`, `cwd`, `env`
   point HOME/XDG/OpenChamber paths at the isolated operator profile. Relay stays
   off. Arbitrary IDE secrets/foreign configuration injection are not inherited.
7. **Check versions and take process ownership.** Actual version outputs must
   match OpenCode `2.0.23` and OpenChamber `2.1.1`. The supervisor acquires
   `launcher.lock` and starts owned processes only. With Knowledge configured,
   the existing wrapper runs one shared broker and starts OpenChamber as a child.
   OpenChamber still starts the separate OpenCode binary itself.
8. **Confirm protected service.** Unauthenticated GET `/auth/session` must return
   HTTP 401 with `authenticated: false`, `locked: true` while the process is alive.
   Only then does the native launcher report `ready`.
9. **Native login and Chat loading.** Copy only the HttpOnly cookie into WebView2,
   navigate to the intended origin and initialize the adapter. Source builds
   await the source contract. Login and Chat mount, or the intended manual-login
   fallback, end the loading screen.
10. **Establish session/tools separately.** Load complete project-scoped history.
    Knowledge/Engineering report actual availability independently of Chat mount.
    Failed history loading must not create a new chat as a presumed empty history.

Sources: [native entry](../src/native-chat.mjs), [profile preparation](../src/state.mjs),
[themes](../src/themes.mjs), [commands](../src/commands.mjs),
[runtime selection](../src/runtime-paths.mjs), [launch plan](../src/launcher.mjs),
[supervisor](../src/supervisor.mjs), [Knowledge wrapper](../src/shared-chat-runtime.mjs),
[Host connection](../native/IdeHost/AssistantControl.cs),
[source contract](../upstream/overlays/openchamber/twincat.ts) and
[Chat integration](../native/IdeHost/ChatIntegration.cs).

Preparation is not a cross-component transaction. Managed theme/command files or
MCP settings may already have changed before a later startup failure. Preparation
remains repeatable; startup does not promise automatic rollback of an entire
operator profile.

## Separate readiness states

These are conceptual states, not additional implemented stdin fields.

| Area | Evidence of readiness | Not established |
| --- | --- | --- |
| Prepared profile | Verified policy; required directories/themes/commands | Process startup or provider login |
| Local service | Owned process alive; expected protected auth response | Completed OpenCode/Knowledge startup |
| Chat | Successful navigation, adapter, login and existing Chat mount | Configured model/corpus |
| OpenCode | Actual OpenChamber OpenCode-readiness/API probe passed | Selected, approved, reachable provider |
| Project chat | Complete history and exact project/session binding | Automatically attached editor source |
| Engineering | Managed PLC project, MCP connection and required read call passed | Live IDE/compiler/ADS/machine verification |
| Knowledge | Valid corpus, connected Knowledge MCP, usable status/search results | An index file alone does not prove a usable connection |

Slow Knowledge cold startup must not hold ordinary Chat until corpus verification
ends. General Chat requires no PLC project and receives no Engineering MCP.
Missing providers/documentation are setup states, not reasons to recreate data.

Sources: [connection state](../native/Shared/ChatConnectionState.cs),
[loading screen](../native/IdeHost/SplashIntegration.cs),
[Knowledge integration](../native/IdeHost/KnowledgeIntegration.cs),
[project preparation](../src/native-mcp.mjs), [broker](../src/knowledge-broker.mjs),
[runtime integration](RUNTIME_INTEGRATION.md).

## Responses and shutdown

Successful native control output remains `{"status":"ready","port":3123}`.
Node-boundary failures return `{"status":"failed"}` without credentials or upstream
error text. `ready` confirms the protected service, not model/MCP readiness.
The launcher stays alive to supervise after responding.

The native Windows chain separates bounded control output from upstream logs.
The Host discards server logs rather than placing them in dialogs or chats.
Shared CLI/Host implementation must not corrupt this channel with extra log
lines or a new CLI output shape.

| Event | Required behavior |
| --- | --- |
| Compatible existing service, no ownership | Authenticate/connect without gaining the right to stop it |
| Host stops owned startup | Close stdin; cancellation reaches supervisor and owned process tree |
| Owner/helper dies | Windows Job Object/process-group supervision stops owned descendants, including Knowledge; unrelated processes remain |
| Service dies | Report loss and use existing reconnect; next attempt gets a new generation |
| Navigation/browser failure | End loading with error/reconnect; successful `Navigate()` alone is not loaded Chat |
| Late response | Generation, NavigationId and document generation prevent adoption into a changed state |
| Occupied port/profile lock | Refuse startup; never kill foreign processes or delete locks based on guessed PID state |

Sources: [NativeChatSession](../native/IdeHost/NativeChatRuntime.cs),
[Windows supervisor](../src/windows-native.mjs), [Job Object](../native/WindowsHost/Program.cs),
[supervisor](../src/supervisor.mjs), [Host](../native/IdeHost/AssistantControl.cs).

Existing-service probing checks expected auth behavior. It does not fully verify
RepositoryRoot, StateRoot or source revision of that service. An explicit service
identity contract would need a separate, coordinated Host change.

## Existing deadlines

| Boundary | Existing value/start point |
| --- | --- |
| First stdin request | 10 seconds from Node entry |
| Each version check | At most 10 seconds |
| Node readiness loop | Default 65 seconds from supervision start, after profile/artifact preparation and version checks |
| Each auth readiness probe | 1.5 seconds; response at most 4096 bytes |
| Native control response | At most 80 seconds from reading after request transmission |
| Native automatic login | 5 seconds per login |
| Chat/adapter splash | 60 seconds from navigation; timeout shows error/reconnect |
| Host stop | Wait 5 seconds, terminate own launcher, then wait another 2 seconds |

These belong to different layers. Profile preparation and version checks are
outside the 65-second loop but inside the native 80-second wait. Consolidation
must account for that distinction. No default changes or new total startup time
are promised here. Knowledge corpus verification has its own lifecycle and must
not consume the Chat splash budget.

## Initial consolidation acceptance

`prepareRuntimeLaunch` shares runtime selection, launch planning and version
checks between `cli.mjs` and `native-chat.mjs`, using expected versions from the
existing source lock. Profile/themes/commands precede version processes as before.
Only successful verification passes a plan to the unchanged supervisor.

Inputs, responses and cancellation signals retain their boundaries. CLI binary
paths still use `realpath`; native entry uses regular files selected by the
runtime receipt. The reference distribution uses verified package-manifest
`bin.opencode`; its pinned `.exe` suffix applies on Linux/macOS too. Native port
verification/MCP setup precede common preparation. The 65-second readiness budget
still starts after version checks; there is no new overall timeout.

Acceptance requires identical artifact/source/distribution selection; both themes,
effective prior defaults and all commands before bootstrap while preserving custom
themes; preserved operator data/project bindings/source kinds/attachment state
without moving RepositoryRoot, profile or database; protected loopback startup,
original provider login, streaming, cancel, queue and actual history; one owned
Knowledge broker with separate Engineering configuration per PLC; and passing
failure/restart/owner-death tests with unchanged native/CLI output.

Existing checks: [launcher](../tests/launcher.test.mjs),
[native entry](../tests/native-chat.test.mjs), [CLI](../tests/cli.test.mjs),
[profile](../tests/state.test.mjs), [themes](../tests/themes.test.mjs),
[commands](../tests/commands.test.mjs), [supervisor](../tests/supervisor.test.mjs),
[Windows lifecycle](../tests/windows-native.test.mjs),
[shared broker](../tests/shared-runtime.test.mjs),
[actual native runtime entry](../tests/integration/native-chat.test.mjs),
[WPF/WebView integration](../scripts/Test-ChatIntegration.ps1).
[Preparation checks](../tests/runtime-start.test.mjs) also cover ordering before
version execution, retained project selection/custom themes, shared version-failure
handling without process ownership and fixed CLI failure before server startup.

### Preceding design-integration evidence

At combined revision `57fb2cc`, reruns passed 81 Node and eight browser tests via
`npm run check`; both native Host builds and 80 native policy/context checks; and
51 WPF plus 59 actual WebView checks with the pinned reference distribution,
light/dark and widths 390/768/1100.

Existing Knowledge/diagnostics threading warnings remained visible. Unchanged
source contract/application bytes match the integrated frontend commit. Its
recorded evidence additionally includes 63 source-WebView checks with both MCPs;
that source run was not repeated here. Synthetic projects, editor states and
credentials establish no IDE/PLC/compiler or paid-model acceptance.

Logs remain local at `local-data/backend-*.log`. Links, concrete contract fields
and deadlines were checked against source; documenting them does not establish
new implemented runtime guarantees.
