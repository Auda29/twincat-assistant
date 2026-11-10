# Automatic documentation setup

Native 0.1.9 discovers installed Beckhoff InfoSys help and prepares it for the
Knowledge MCP from the native **Knowledge search** panel. It reuses the pinned
Knowledge CLI for import, validation and snapshot activation. No documentation
files are bundled with the Assistant.

## Knowledge software version

The Assistant pins Knowledge MCP **0.1.1** separately in
[knowledge-lock.json](../upstream/knowledge-lock.json). On Windows,
[Install-Knowledge.ps1](../scripts/Install-Knowledge.ps1) installs the verified
release wheel and hashed dependencies in a versioned environment. Native setup
selects that executable; the existing TOML, active snapshot and documentation
package remain in place. See [native MCP configuration](NATIVE_MCP.md).

This release adds conservative cleanup of marked loader caches left behind by
terminated processes. It preserves active readers and unmarked legacy caches.
It does not change the four-tool protocol, convert the corpus or reduce the
first full-corpus load. Restart the Assistant runtime after switching its MCP
executable so the shared broker starts the selected installation.

## Normal use

1. After native chat starts, the Assistant looks for installed help in the
   standard Help Library catalog. The banner offers **Set up** and the
   overflow menu always exposes **Wissenssuche einrichten**.
2. Select the installed help. A unique German source is preselected, with
   English as the fallback. Multiple revisions require an explicit selection;
   the Assistant does not guess the correct revision.
3. Click **Set up**. Import and validation run outside the IDE process with
   phase feedback and an **Cancel** action. The project chat remains usable.
   The final activation/connection check disables ordinary cancellation;
   closing the host still revokes owned work.
4. The Assistant reconnects Knowledge for the selected chat directory. It shows
   **Wissenssuche bereit · Deutsch** after the connection is confirmed. A
   prepared index alone is labelled as prepared, not connected.

The panel can be closed while work continues. **Search again** refreshes discovery.
When no supported help is found, the panel explains how to retry after installing
InfoSys. A changed source or a newer installed archive revision offers an index
update. Existing validated snapshots are retained for rollback. A supplied
external TOML configuration is preserved and is not replaced by this workflow.

## Storage and runtime

When no explicit Knowledge TOML is supplied, the native launcher creates a
private, stable configuration under `<StateRoot>\knowledge\knowledge.toml`.
It selects that directory's `state` and `cache`. Each import gets a separate
`runs\<uuid>\package`; the upstream CLI activates it only after validation.
The manifest pin comes from the local import report. A receipt records the
selected source metadata, language, verified manifest and article count after
an actual MCP status call succeeds. Discovery and receipt checks do not read
article bodies. Original InfoSys archives are never modified.

The stable configuration keeps existing project workspace identities and
conversations intact across later index activation. Provider/model policy and
credentials remain separate. Only the Knowledge MCP is reconnected. The agent
still has the same reviewed read-only tool allowlist; it cannot trigger imports.

The import action records the user's local-indexing authorization as the CLI's
rights-decision reference. This records the action and does not establish vendor
authenticity or redistribution rights. Article data and caches stay in local
private state, outside tracked source and published releases.

Full InfoSys packages can take several minutes to import and to load on a cold
MCP start. Managed Knowledge profiles therefore permit up to ten minutes for
startup. Existing profiles without that optional setting retain their reviewed
timeouts. The UI never displays a made-up percentage. An interrupted final
verification can resume from its already activated package without importing
the archive again.

## Shared native connection and search results

The protected native runtime owns one Knowledge backend shared by its general
and project chat contexts. Each context uses a small stdio relay over a private
authenticated named pipe, or a Unix socket. The endpoint and random token live
in `knowledge/broker.json` under the protected operator profile. Relays cannot
select an executable, another corpus or additional tools. Only the four reviewed
Knowledge read tools are forwarded; engineering remains scoped to each PLC.

The broker starts the backend on demand and retains it when a chat context
disconnects. Managed snapshot activation changes its revision and replaces the
backend once. The final setup probe uses this same backend when the native
runtime is running. Probes outside that runtime still use a standalone server.
External TOML contents remain unchanged; activation outside the managed setup
requires restarting the chat runtime to select a changed external snapshot.

The MCP SDK's ordinary stdio version negotiation creates a disposable server
process before the session process. The broker instead uses a custom transport
through the SDK's public Transport interface and negotiates on the existing
pipe. This avoids a second full corpus load and Windows console descendants
left by a disposable probe. The broker, relays and backend are all descendants
of the existing native Job Object, so loss of the launcher also revokes them.

Knowledge returns article data in `structuredContent` and a short text summary
such as `count=3`. The chat consumes text tool results. The adapter retains the
original structured result and also supplies its complete bounded JSON as text,
including search IDs, article sections and citations. Error state and source
fields are preserved. No model or provider setting is changed by this adapter.

With the installed German 49,970-article package, the local adapter test measured
108 seconds for the first full load, 617 milliseconds for three subsequent
searches across separate clients, and 452 milliseconds to open a returned topic.
Exactly one backend started. Full validation on a fresh runtime start remains
an upstream cost; this change removes repeated loads within that runtime rather
than bypassing package validation. Existing running IDE sessions need one restart
to load this updated Node runtime. No new VSIX installation is needed for these
JavaScript changes.

## Open investigation: slow Knowledge connection

Status: still open. The [2026-11-02 investigation](KNOWLEDGE_STARTUP_INVESTIGATION.md)
attributes most startup time to full-corpus validation, documents a tested local
Knowledge optimization and explains the earlier fixture's two backend starts.
The candidate still needs about a minute and is not installed; the historical
operator-profile write attribution also remains open.

Originally recorded on 2026-10-30. The recording
`30.10.2026_12.37.24_REC.mp4` shows the Knowledge panel open at approximately
01:00–02:35. Its heading remains **Dokumentation bereit · Deutsch · 49 970 Artikel**,
while the connection line alternates between connecting and reconnecting.
No successful connection confirmation is visible during that interval. The
prepared index remains available; this observation does not establish a new
import, an actual backend restart or the cause of the connection delay.

The earlier 108-second first-load measurement above is a separate baseline.
Warm search/topic timings do not demonstrate an acceptable cold start. Building
OpenCode from source and stabilizing the status display do not resolve this
performance issue.

The next investigation must:

- Timestamp host status reads, workspace MCP catalog reads, connect/disconnect
  requests and broker/backend readiness in one trace. Record retry reasons,
  timeouts and backend start counts without credentials or article bodies.
- Measure cold runtime start and warm reconnection separately with the existing
  49,970-article corpus. Identify time spent in corpus validation, broker startup,
  MCP negotiation and UI confirmation, including repeated requests across chat
  contexts.
- Check whether reconnect attempts restart a healthy or still-starting backend,
  and whether the shared broker and cache are reused. Do not reimport or delete
  the prepared index merely to reconnect.

Completion requires measured cold/warm timings, an explained backend start count
and a successful status/search/topic sequence through the Assistant. A ready
index must reach a confirmed connection without an unexplained reconnect loop;
progress and failures must remain understandable while ordinary chat stays
usable. Set a concrete timing target after measuring the individual phases.
The issue remains open until these results are recorded.

## Verification

The 2026-10-28 candidate passes 59 Node tests, 8 browser contracts, 33 WPF view
checks and 43 compiled-control/runtime checks. Both VSIX targets and the updated
Windows helper build without warnings/errors. Real-corpus startup also connects
both MCPs in a separate protected native profile.

Node checks cover bounded discovery, multiple revisions, redirected stores,
external-config preservation, cancellation cleanup and changed-help detection.
Shared-connection tests additionally cover concurrent contexts, complete model
text, cancellation isolation, source-topic retrieval, one reload per activation,
unreviewed-tool refusal, authenticated IPC and owned process-tree shutdown.
The compiled WPF fixture renders selection, progress and prepared states at
340, 520 and 900 pixels. The real WebView2/runtime fixture exercises the button,
version selection, cancellation/retry, connection readback and retained chat:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass `
  -File scripts/Test-ChatIntegration.ps1 -DotNetRoot 'C:\path\to\sdk' `
  -McpSettingsPath 'C:\fixtures\native-mcp.json' -TestKnowledgeSetup
```

The fixture substitutes its own Help Library catalog and synthetic archives;
it does not ingest the operator's help or use operator credentials. An optional
`-KnowledgeStateSource 'C:\path\to\knowledge'` instead checks startup with an
already verified real corpus, using a separate chat profile and cache. It seeds
the selected snapshot before launching the runtime and asserts one backend after
both MCPs connect. It reads only the corpus state/receipt/package, not operator
authentication files.

Local real-data verification on 2026-10-28 imports and activates the installed
German `.47.mshc` archive: **49,970 articles**, followed by a successful MCP
status call and a protected native runtime connection. No model request, PLC
source change, compiler or machine validation occurs in these checks.
The x86 0.1.9 candidate is installed with all 13 payload hashes verified and
0.1.8 preserved outside extension discovery. The operator's managed corpus
selection is ready and existing provider/model settings are preserved.

On Windows, a current-user native mutex serializes setup across IDE windows and
is released on owner death. A recognised leftover setup marker is reclaimed
under that ownership; unknown marker content is refused. Import subprocesses
use the existing kill-on-close Windows Job Object supervision. The protected
launcher helper is therefore rebuilt alongside this version.
