# Private native host acceptance, 2026-11-06

Status: configuration implementation and synthetic tests complete. Actual host,
UI, member, paste and first-after-logon gates remain OPEN. No IDE or backend was
started, no installer run, no payload/registry/ACL changed by this work.
`LOCAL_RUNTIME_RECOVERY_20261105.md` supersedes the machine state in PAUSE.

## Process override contract for the installer owner

`TWINCAT_ASSISTANT_TEST_CONFIG` is an environment variable on one deliberately
launched TcXaeShell process. Never use SetEnvironmentVariable with User/Machine,
setx, global native-chat.json changes or a shared launcher shortcut.
No CLI parser or production profile feature is added.

The value must be the canonical absolute local file:
`%LOCALAPPDATA%\TwinCatAssistant\host-tests\<id>\native-chat.json`.
`<id>` contains 1–64 ASCII letters, digits, underscore or hyphen. UNC, relative,
noncanonical paths, alternate filenames and missing files fail closed. The
configuration has exactly two case-sensitive JSON keys:

```json
{
  "StateRoot": "C:\\Users\\TESTUSER\\AppData\\Local\\TwinCatAssistant\\host-tests\\RUNID\\state",
  "DotNetRoot": "C:\\Program Files\\dotnet"
}
```

Substitute the actual Windows profile path and system ProgramW6432 folder.
There is no fallback from an invalid test configuration to operator state.
Only StateRoot and system DotNetRoot come from this file. RepositoryRoot,
NodePath and McpSettingsPath still come from the validated installer selection
`assistant-installation.json` beside the loaded host assembly, including its
package-entry verification. A prototype without a selector cannot run in test
mode. A RootSuffix alone does not select this configuration.

| Data | Exact private destination |
| --- | --- |
| Configuration | `<profile>\native-chat.json` |
| DPAPI UI credential | `<profile>\native-chat\ui-password.bin` |
| Preferences / chat bindings / project navigation | `<profile>\native-chat\...` |
| WebView user data and cookies | `<profile>\WebView2\<shell-pid>` |
| Backend, NativeMCP, knowledge state | `<profile>\state\...` |

NativeMcpProject and NativeKnowledge already pass config.StateRoot explicitly
and clear their child environment; this change keeps those protocols unchanged.
Backend transport also clears inherited environment. Reparse points in existing
private ancestors/trees and multiply linked files are rejected before use.
Inherited WEBVIEW2_USER_DATA_FOLDER and WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
are refused in test mode. The actual WebView environment folder is checked
before browser initialization. Registry WebView policy must also be checked by
the coordinator before launch; a registry policy forcing a different folder is
a blocked test, never a reason to change operator registry policy.
These checks are an accidental-redirection guard, not protection against an
adversarial same-user process replacing files concurrently. Keep the private
profile quiescent during validation/launch. Private WebView directory validation
walks existing browser files and can add startup work; actual latency is unmeasured.

Without the variable, all previous configuration, credential, preference,
binding, WebView and installed operator-state fallback paths remain unchanged.

Installer integration MUST bind the actual operator SID and exact canonical
`<profile>\state` before install/update/rollback, not whichever config happens
to be global. Record profile config path/hash, state and SID in the transaction
and validate them again before mutation. Do not publish a managed test extension
into the ordinary operator extension search path. Parent owns installer changes
and final builds. This work modifies no scripts/installer files.
The contract was sent to parent on 2026-11-06.

## Preparation and verification

Run in Windows PowerShell 5.1 from this worktree after building its host:

```powershell
powershell -NoProfile -File scripts/Test-PrivateHostConfiguration.ps1
powershell -NoProfile -File scripts/New-PrivateHostAcceptanceProfile.ps1 -ProfileId NEW_UNIQUE_ID
```

The preparation command creates only a fresh private config and prints its
path, state and current SID for installer coordination. It never launches the
IDE, installs or imports credentials. Existing profiles are refused. No
passwords, cookies, chats or customer sources may be copied. The regression
script creates fresh synthetic fixture directories and packages, does not run
the fixture executables, and restores its own process environment in finally.
It retains synthetic evidence directories; it never recursively deletes profiles.

Current validation: x86 Release Rebuild succeeded using readonly installed
TwinCAT interop `C:\TwinCAT\3.1\Components\TcBlockDiagram\Common\TCatSysManagerLib.dll`
and readonly SDK 8.0.425 from
`C:\Users\NiklasW\.t3\worktrees\twincat-assistant\t3-48883751\local-data\dotnet8`.
There are six existing VSTHRD010 warnings in KnowledgeIntegration and
DiagnosticsIntegration, outside this change.

The native build consumed a copied prebuilt EmbeddedChat.js from the normal
Main checkout as an explicitly external input, SHA256
`73b621eaa9306bf6f60ead270de58817d5c35bf01f99528be8814e1f8ac889d2`.
Only native outputs were built here. No source-runtime receipt was created or
copied and no new source-runtime build is claimed. Parent must generate final
integrated inputs/builds itself.

29 configuration checks passed against this worktree's built x86 assembly.
Private evidence: `local-data/private-host-config-2f905e78e7d34254ac11664bd9d5d032/result.json`.
They include legacy/default load compatibility, exact private state and selected
package/MCP settings, inherited browser override rejection, foreign and relative
paths, junctions for credentials/state/WebView, hardlinked preferences/config,
missing selector/config and unchanged synthetic foreign sentinel. These are
configuration tests, not installed package, backend, native timeout or UI passes.

## Coordinated actual acceptance plan, not yet executed

1. Parent finishes integrated package and native builds, selector and isolated
   installation design. Review the exact SID/state binding and hashes before
   any mutation. Record original extension/config digests without reading
   credential values. Preserve the current Main backend path and recovery state.
2. Choose a fresh profile and a separately installed test extension in a verified
   isolated shell extension search path. Confirm the payload includes this
   override implementation. Confirm RootSuffix is isolated as well; it is not
   sufficient for native configuration by itself. Check no browser policy forces
   foreign user data. All owned mutable paths must resolve inside this profile.
3. Only after parent coordinates launch, construct ProcessStartInfo for the
   known TcXaeShell executable with UseShellExecute=false and its own RootSuffix.
   Set TWINCAT_ASSISTANT_TEST_CONFIG only in start.EnvironmentVariables. Record
   returned PID/start time and exact command/profile. Do not use ROT discovery,
   foreign shell automation, forced process termination or global env changes.
4. Use synthetic projects A and B created under `<profile>\state\workspace`,
   with a synthetic PLC project, PROGRAM MAIN, FB_Acceptance, one method,
   property getter/setter and action. Use counters and constants only. No
   customer project, provider call, PLC/ADS/runtime action, activation/download,
   or compileEngineeringProject. Create/open the fixtures in the owned shell
   without compiling. Verify project identity switches A to B and back and
   distinguish saved/dirty evidence in the native view.
5. Observe chat docking, focus, reconnect, project/thread binding and selection
   persistence. Open POU declaration and implementation plus method, property
   getter/setter and action editors. Record the actual editor kind, selected
   context, dirty state, screenshot and pass/fail for each. Never infer a member
   pass from a POU pass or a direct API fixture.
6. Render synthetic, frozen answer fixtures with labelled ST declaration and
   implementation blocks. Copy the actual visible code and paste into the
   correct synthetic editor pane. Observe exact text, newlines, Unicode,
   indentation, no Markdown fences and no declaration in implementation.
   Include a member example and a deliberately wrong target rejection. Undo
   the paste or retain it only in the synthetic project. No engineering compile.
7. For first-after-logon, the operator logs off/on when other work is safe, then
   starts only the coordinated private shell. Record timestamp, PID, private
   config and native protected-ready duration including the fixed 80-second
   limit. A warm restart is a separate observation. No timeout claim from a
   harness with a longer limit.
8. Exit the owned shell with its normal File/Exit UI. Resolve save prompts only
   for synthetic files. Observe shell, backend and listener exit normally and
   verify original config/extension digests remain unchanged. Do not kill a
   foreign IDE or use forced cleanup as a passing exit gate. Record any orphan
   as a failure requiring coordination. Keep private evidence for review.

## Current operator blocker

The complete computer-use SKILL.md was read, followed by its guidance and the
relevant official API reference. The supported JS entry point `@oai/sky`
imported successfully. A fresh official `sky.list_windows()` returned:

`Computer Use native pipe is unavailable: failed to connect native pipe: Das System kann die angegebene Datei nicht finden. (os error 2)`

No helper executable, custom pipe, PS UIAutomation, or alternative native input
was attempted. For actual UI acceptance, the operator must restore the official
Computer Use connection and leave the desktop unlocked, or perform the listed
visible checks manually in the coordinated owned shell with evidence. Logoff/
logon is an operator action. No UI gate is passed by this document.
