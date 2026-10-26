# Native project chat, candidate 0.1.5

Updated 2026-10-26. This implements the next chat-integration goal on
`feat/project-bound-chat`, based on the 0.1.3 follow-up branch. Installation and
user acceptance of this candidate are separate from the local tests below.

## User workflow

The native window starts or reconnects the protected local chat when opened.
First-run password setup and OpenChamber's own login remain. The compact header
shows the active POU, PLC project, dirty evidence and snapshot uncertainty.
Connection, model/provider setup and manual inventory live under **Details**;
the local code preview is collapsed under **ST-Vorschau**.

1. Open the intended ST POU and sign in to the local chat.
2. Choose **Neuer Projektchat**. This creates a real OpenChamber session in the
   isolated chat workspace and binds it to this IDE lifetime, solution and PLC
   project. It does not transfer declaration or implementation.
3. Alternatively, open a previous conversation through **Verlauf** and explicitly
   choose **Chat zuordnen** if it needs revalidation. A conversation belonging to
   another PLC project or another live IDE cannot be retargeted. A new host
   lifetime requires an explicit bind, including after PID reuse.
   Without an active POU, **Neuer Chat** starts a general conversation with no
   native project context. It remains usable without an editor. Details also
   offers **Neuer Chat ohne Projektkontext**. A general chat cannot attach native
   source until the user explicitly assigns it to a project; opening an editor
   alone never performs that assignment.
4. Choose **POU an Chat anhängen** to stage the current snapshot as a visible
   composer reference. This click authorizes its transfer into the local chat
   composer. The text includes exact identity, revision, provenance and the
   warning that the exact unsaved editor buffer is not verified. It is sent to
   the selected provider only when the user submits the message.
5. Sign in to a provider in OpenChamber's own provider settings. Under Details,
   choose it in **Angemeldeten Anbieter freigeben**, then click **Anbieter für
   freigegebene Inhalte zulassen**. This works before a model is selected.
   Afterwards choose a model in the original chat picker. If a matching model
   was already selected when granting consent, it is retained as the default;
   provider-only consent invents no model and removes an obsolete other-provider
   default. This is a provider-level policy, not a restriction to
   one model ID. It preserves engineering/tool deny rules. No credential is
   entered into or returned through the native integration.

The 0.1.4 provider/model ordering was a deadlock: the default `provider.use` deny
also removes an authenticated provider and its models from the upstream catalog.
The operator reported this after OpenAI browser sign-in. An isolated real-runtime
test reproduces a connected integration with no provider/models in that catalog.
The 0.1.5 consent choices instead use public integration IDs/names, excluding MCP
and web-search integrations. No connection IDs, account labels, keys or tokens
are passed to the native selector. Consent still requires an explicit native
click and a valid chat binding; signing in alone never grants model use.
The adapter also corrects the config-store export: `l` (`Be`) owns directory
providers/model selection. `m` (`bs`) is the separate session-preference store.
The old fixture wrote synthetic selection fields into that preference store and
therefore missed the incorrect mapping. The new compiled-control regression
requires the real provider/model picker catalog to populate after consent and
never injects a synthetic selection into either store.

Following an editor never attaches source. Switching POU does not retarget the
conversation; switching project or losing context disables attachments and
blocks new prompt/command requests for a mismatched session. Existing history
remains readable. Reconnection never replays an attachment or message.
The header distinguishes the last staged snapshot from a subsequently changed
active source revision. It does not claim that the composer snapshot updates
automatically, or that a removed reference remains attached.

## Reused upstream and adaptation

OpenChamber still owns conversation rendering, history, composer, model setup,
streaming, cancellation and session persistence. There is no replacement chat,
model router or agent harness. The separate SDK diagnostic panel remains separate
from this native integration; the native engineering pipe/MCP adapter is not
silently presented as connected.

The public SDK provides session/attachment operations but cannot reshape the
whole app or bind a native IDE. `src/embedded-chat.mjs` is therefore a narrowly
versioned adapter to the existing stores in OpenChamber 2.1.1. The reviewed source
tag is `e302062e3be0686986594fddabdafd8a97c129e5`. Its mapping is:

| Distributed asset | Existing export | Reviewed source |
| --- | --- | --- |
| `index-rxO-w0yQ.js` | `u` | `useUIStore` |
| `useAppFontEffects-DC1U1Z6i.js` | `z` | `useSessionUIStore` |
| Same asset | `du` | `useInputStore` |
| Same asset | `l` | `useConfigStore` |

Both assets are verified by SHA-256 during the build and again in the browser
before importing them. A changed version fails closed. `build-embedded-chat.mjs`
bundles the adapter into a VSIX assembly resource. No `node_modules` file is
patched, no third-party source fork is shipped, and no minified symbol is guessed
at runtime. An upstream upgrade requires a reviewed mapping and renewed tests.

The adapter uses the same pending reference queue as SDK `host.attach`, including
the 16,000-character text ceiling. Oversized POUs are refused, never truncated.
An attachment is guarded by session ID, session-selection epoch, per-document
nonce and native source revision. Session changes clear pending native references.
The native read is refreshed and revalidated after asynchronous browser reads;
the revision includes the editor lifecycle generation for same-path reopen.

Chat starts with the sidebar collapsed, generic starter chips and unrelated
context surfaces hidden, and an invitation to start a native project chat. The
composer is inactive without a valid binding. Initial operator preferences point
at the empty private workspace, preventing a generic project-folder onboarding
dialog. Existing operator project preferences are preserved. The PLC project
directory is never selected as the general-purpose OpenChamber workspace.

The visual direction follows Achromatic ledger, seed `685133378`, retaining the
IDE/OpenChamber fonts and theme. Its identity is the exact POU/project context,
not decorative branding. The chat occupies the remaining width; native controls
wrap on narrow tool windows and code is shown only on request.

## Native and storage boundaries

The browser receives no general COM host object and WebView messages remain
disabled. The host invokes a small adapter API itself. Only the explicit POU
action serializes source into a browser call. Native prompt interception checks
the current IDE/project/session binding independently of the composer UI.

Known file, Git, source-control, terminal, shell, development-server and upstream
update HTTP mutation routes are blocked in the embedded host. Direct session
shell actions are blocked even for a bound conversation. This is a reviewed
boundary for the pinned app, not a same-user sandbox or a guarantee for arbitrary
third-party plugins/protocols. Existing upstream dependency/security blockers
remain in [the audit](SECURITY_AUDIT.md).

Thread bindings are metadata-only JSON under the current user's protected
`native-chat/threads` directory. Reads are bounded and reject redirected leaf
files; binding writes serialize across IDEs and replace files atomically. Source
text is not stored in those records. Original session references/history are
managed by OpenChamber after the user's attachment/submission actions.

`native-chat-model.mjs` accepts only the trusted state path, exact provider ID,
optional matching provider/model ID and
explicit disclosure acceptance over bounded UTF-8 stdin. It verifies the original
policy, changes the selected provider/default model and preserves deny rules.
The native host then reloads the isolated OpenCode location through the existing
authenticated API and reads back its effective config documents. No provider
call is needed for this validation. A crash between the two atomic profile-file
replacements leaves an integrity mismatch, which the launcher refuses; there is
no automatic repair of unknown policy changes.

Connection loss triggers bounded retries after 2, 5 and 15 seconds. The existing
process-ownership rules still apply. Profile locks are never removed merely to
make a retry succeed. A failed or uncertain session creation/model setup/source
attachment is not automatically repeated.

## Verification and remaining acceptance

Run the root checks and build both native targets, then:

```powershell
node --test tests/integration/native-chat.test.mjs
powershell.exe -NoProfile -STA -File scripts/Test-ChatIntegration.ps1 -DotNetRoot 'C:\absolute\sdk-root'
powershell.exe -NoProfile -STA -File scripts/Test-NativeView.ps1
```

The integration script uses the real compiled WPF control, WebView2 and pinned
runtime, with synthetic editor snapshots and private paths. It verifies protected
login, actual session creation, persistent metadata binding, visible composer
attachment, native project mismatch/shell refusal, provider-policy readback,
workspace reload and owned runtime reconnection without source replay. It does
not pretend its snapshots come from a live IDE and never calls a model.

Local 0.1.5 verification on 2026-10-26: 44 Node tests, 8 browser contracts, 69 native
policy/context/chat checks, 21 WPF view checks, 22 compiled-control integration
checks and 3 Windows/native Node runtime checks pass. Both 0.1.5 VSIX targets build without
warnings/errors. Composer references use a readable POU/project label plus a
short identity digest; authorization still checks the full identity and revision.
The Windows owner-death test allows bounded cleanup retries for temporary
directory handles after its separate listener-shutdown assertion has passed.
Persistent locks or a surviving listener still fail the test.
The provider regression uses an isolated fake API credential, verifies that deny
hides the connected provider/models, grants only that provider explicitly, and
checks both the real upstream catalog and the actual UI config-store catalog.
It makes no provider request/model call and does not inspect the user's credential.
Policy verification polls bounded readbacks while the config watcher settles;
the consent write and location mutation are not replayed.

Local UI renders cover narrow/medium/wide windows. Raw logs and screenshots stay
in ignored `local-data`. The x86 0.1.4 update was installed after UAC confirmation;
all 13 payload hashes match and 0.1.3 is preserved outside extension discovery.
A normally launched `ContextChecks` host exposes the installed Assistant command,
opens its tool window, resolves PLC_A/PLC_B distinctly and starts the chat listener.
Linux and Windows CI checks pass at `6310628`. Native window automation still
fails with `native pipe unavailable` / `os error 2`.
The operator confirms all three first-flow checks in that installed host:
compact native header, PLC_B project-chat binding, and a visible FB_Motor composer
reference after **POU an Chat anhängen**, without submitting a message. This is
live x86 visual evidence, separate from the synthetic component fixtures.
For 0.1.4, the operator also confirms that switching to PLC_A with the PLC_B chat selected
blocks source attachment and chat input, and returning to PLC_B restores its
matching assignment. This confirms the installed project's visible refusal/return
flow. The x86 0.1.5 update is subsequently installed with 13 payload hashes
verified and 0.1.4 preserved outside extension discovery. The saved synthetic
host was closed for the update, then normally reopened. Its installed command,
tool window, distinct POU reads and local chat listener pass the smoke. The
OpenAI account store was not read or changed by installation. Linux and Windows
CI pass at `6902f0c`. The operator confirms that the installed 0.1.5 selector shows
OpenAI, the explicit grant succeeds and model selection works. The actual answer
to the explicitly attached synthetic PLC_B/FB_Motor is also user-confirmed:
the prompt asking which value the implementation assigns to bRun returned exactly
`bRun := TRUE;`. This closes the scoped x86 chat-integration goal: compact UI,
project/session binding, explicit POU disclosure and the actual model response.
Start/reconnect and no-replay invariants are separately covered by the compiled
control/runtime regression; this one answer is not broad provider/model validation.
amd64 live installation, extended lifecycle/focus/responsiveness,
general exact unsaved-buffer semantics, both real MCPs/corpus and native-to-MCP
transport remain separate gates. No source-write, compiler, PLC/runtime or
machine validation is claimed.
