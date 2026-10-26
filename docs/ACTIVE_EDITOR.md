# Active editor context, M1.2

Updated 2026-10-26. Candidate VSIX version `0.1.2` is installed in x86 following
operator coordination. This is implementation evidence, not an accepted native MVP.

## User-visible behavior

The native window follows an active ST PROGRAM, FUNCTION_BLOCK or FUNCTION
automatically. A compact header identifies the POU, PLC project and solution,
with dirty state, source provenance and a persistent local-only disclosure status.
Full process/project/tree identity is available in tooltips. The local ST preview
is optional; it updates without Refresh, Read or selecting an object.

Manual catalog enumeration and reads remain under Diagnostics. They use separate
source/status controls, so neither a diagnostic selection nor a chat connection
message replaces the active object's context or the inventory completeness warning.
The catalog still has the 200-target limit; automatic following does not use it.

Chat continues to be the existing OpenChamber WebView2 surface. Its connection
button uses the configured loopback port, which can be changed in Diagnostics.
Service startup, authentication, model transport and native/session attachment
have not been added by this slice.

Later candidate `0.1.3` adds protected service startup and connection/error
handling; see [native chat](NATIVE_CHAT.md). Model transport and native/session
attachment remain separate.

## Binding and lifecycle

`NativeActiveEditorReader` resolves `DTE.ActiveDocument.ProjectItem.Object` to the
actual TwinCAT `ITcSmTreeItem`. It requires an ST POU and binds it to the IDE PID,
solution, containing PLC project, full native document moniker and tree path.
There is no name-only search or fallback to the manual inventory.

The in-process tracker also requires the selected document frame's moniker to
match that DTE document. It subscribes to document-frame selection, RDT,
document and solution events. Notifications invalidate existing source immediately
and coalesce for 180 ms. A one-second fallback poll reads only the active object,
because a custom TwinCAT editor may not emit ordinary text events on each edit.
COM reads remain on the IDE UI thread.

Each invalidation advances a publication generation. Reads recheck identity and
source stability, then recheck the current frame/document before publishing.
Closing and reopening the same path cannot make an earlier generation valid.
Disposal cancels timers, prevents further publication and removes subscriptions.
Cancelling a solution-close prompt does not permanently suspend following.

Missing or ambiguous context, non-ST editors, methods and actions display an
unbound state and clear the previous active source. Member editors are not
silently represented as their parent POU.

## Source and dirty-state boundary

Source always carries the label `Automation Interface snapshot`. The current
custom editor did not expose an ordinary EnvDTE TextDocument in the read-only
probe. There is no claim that this snapshot is the exact unsaved editor buffer.
`Unsaved editor text is not verified` remains visible even when document evidence
is available.

Saved-source comparison uses only the active document's `.TcPOU` path. TwinCAT's
`;POU/member` suffix is retained for exact RDT lookup. Source XML is bounded,
DTD/external resolution is disabled, and the live/saved POU UUID and name must
match before content equality contributes evidence. Missing correlation remains
`unknown`; a saved flag alone does not become `known-clean`.

No source is sent to WebView2, a chat, a session, MCP or a model. The reader and
tracker expose no saves, source writes, compiler, activation or runtime actions.

## Verification performed

- Both candidate `0.1.2` VSIX payloads build locally with the pinned SDK.
- After the operator saved work and closed the shell, the x86 payload was
  installed with all 13 files verified. The 0.1.1 folder was preserved outside
  the extension search path; the existing empty discovery marker's contents were
  preserved. Evidence is in ignored `local-data/native-install-x86-m12-0.1.2`.
- A normally launched shell loaded the synthetic solution and the installed
  extension/package. The registered command opened a visible assistant tool
  window. The measured 77 ms was a repeated command with the package already
  loaded, not a cold-start benchmark or a first-click acceptance result.
- Opening each synthetic POU through the IDE's `File.OpenFile` command succeeded.
  The production reader returned distinct project/object identities and different
  implementation text for `PLC_A/FB_Motor` and `PLC_B/FB_Motor`; focusing the
  assistant preserved the active document. The custom POU editor still exposed
  no TextDocument. This checks the direct reader, not the in-process header's
  event delivery. External read times of about 117-200 ms include cross-process
  Automation Interface calls and are not in-process responsiveness measurements.
- The user confirms switching those two editors updates the assistant without
  Refresh/Read. Supplied screenshots show `PLC_A / ContextChecks.sln` and
  `PLC_B / ContextChecks.sln` with the correct active POU. A later screenshot
  shows `Changes: unsaved changes` after adding a real unsaved ST comment.
  The matching external probe reports `DocumentSaved = false`, `known-dirty`
  and implementation length increasing from 13 to 29 characters. This is evidence
  for this native editor case; the general unverified-buffer label remains.
- Seven real direct-reader lifecycle checks pass in a second isolated IDE:
  distinct process identities for the same solution/POU, separate saved content
  from the first IDE's unsaved edit, rejection of non-ST/no editor/closed solution,
  and a fresh same-path reopen. The first IDE's active object, source and dirty
  state are unchanged afterwards. The owned second IDE was closed.
- `npm run check`: 32 Node tests and 8 production-panel browser contracts pass.
- `native/Tests`: 40 portable policy/context checks pass. These exercise the
  production identity, generation/publication, presentation and XML evidence code,
  including different IDEs/PLCs/documents, same-path reopen, reentrant edits,
  disposal, wrong UUIDs/names, ambiguous XML and unavailable dirty evidence.
- `scripts/Test-NativeView.ps1`: 11 checks pass against the real compiled WPF
  control, using synthetic presentation inputs. Light/dark and empty/active/
  dirty/unsupported states render at widths 340, 520 and 900. This caught and
  corrected invisible expander labels on a dark host surface.
- A read-only probe of the existing x86 IDE confirmed that a custom member editor
  exposes its exact ProjectItem/TreeItem and containing PLC project, without a
  TextDocument. The new reader correctly rejects that unsupported member editor.
- An isolated x86 Automation Interface fixture created a separate synthetic
  solution with two PLCs and same-name FBs. Opening its first FB through
  `ProjectItem.Open` failed with `0x800706BE` and the owned test IDE exited, both
  with code and primary logical views. No positive direct-reader assertion ran.
  This is a failed acceptance attempt, not proof of a resolver defect or success.
  The existing user IDE was not changed. Those failed owned hosts exited; the
  later normally launched `ContextChecks` host remains available for acceptance.

The WPF harness runs in a separate STA process, initializes only that process's
Shell thread helper, and never attaches to an IDE or starts WebView2. Its inputs
are synthetic; its assertions and PNGs do not establish event delivery, actual
host colors, editor correctness or installation acceptance.

Reproduce the view checks after `Build-Native.ps1`:

```powershell
powershell.exe -NoProfile -STA -File scripts/Test-NativeView.ps1
```

PNGs are written under ignored `local-data/native-view`. Current local test/build
logs are under `local-data/m12-*.log`; this branch has not been checked in CI.

## Remaining extended native acceptance

Installation is complete. Use the separate synthetic project, with no connected
production PLC, to record:

1. Measure a first-click assistant opening and verify the optional local preview.
   Same-name PLC_A/PLC_B automatic header following is already user-verified.
2. Extend the real unsaved-comment case with declaration edits, save and undo.
   Confirm honest source/dirty status; never generalize one successful case to
   all custom editor-buffer semantics.
3. Switch to a non-ST editor, a member editor and no editor. Verify old source
   and identity tooltips clear; focusing the assistant itself must keep the
   selected document context.
4. Close/cancel close/switch/reopen the solution, including the same path, and
   repeat with two IDEs. Check subscription cleanup and repeated window opening.
5. Measure cold opening and active-read latency/responsiveness, including a large
   POU. Diagnostic read metrics are available after refreshing Diagnostics.

The scoped implementation and core x86 behavior are verified. The full host
acceptance matrix, broader editor-buffer semantics and responsiveness checks
remain open. Native chat and all amd64 live acceptance remain separate gates.
No TwinCAT compiler or machine validation is claimed.

Computer Use could not connect to its native pipe (`os error 2`), so automated
visual/keyboard acceptance is unavailable in this session. An external
`ProjectItem.Open` also crashed the normally launched synthetic host with
`0x800706BE`. The Windows event record attributes termination to
`System.Threading.ThreadStateException` in the CoDeSys editor's
`System.Windows.Forms.Control.SetAcceptDrops`; this does not establish the
underlying cause. No customer project was opened or changed by those tests.
The later command-based file-open route avoided this failure and provides the
current test workspace. The user provided visual header/unsaved-comment evidence;
direct-reader solution lifecycle and two-IDE checks also passed. Full lifecycle
display clearing, two-IDE display isolation and cold-start behavior remain open.

## API references

- [Microsoft document selection element behavior](https://learn.microsoft.com/en-us/previous-versions/visualstudio/visual-studio-2010/bb163257(v=vs.100))
- [Microsoft native document frame properties](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.__vsfpropid?view=visualstudiosdk-2022)
- [Microsoft ProjectItem.Object](https://learn.microsoft.com/en-us/dotnet/api/envdte.projectitem.object?view=visualstudiosdk-2022)
