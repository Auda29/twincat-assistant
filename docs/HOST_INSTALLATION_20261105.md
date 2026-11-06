# Actual first CI installation, 2026-11-05

The Parent installed the first actual CI candidate into the real supported
TcXaeShell x86 / TwinCAT 3.1.4024.78 host after the operator explicitly confirmed
same-user UAC. This is the C0 candidate, not the later C1 acceptance payload.
No IDE, provider session, PLC, ADS or real engineering project was started by
the installation and backend checks recorded here.

## Candidate and transaction

- CI source: `46f96a17697128721c6c1651a8a0f09dc2297d43`,
  [successful run 42034006930](https://github.com/Auda29/twincat-assistant/actions/runs/42034006930).
- Application version: `0.1.0-dev.1`, x64 backend with x86 host payload.
- Exact downloaded setup SHA-256:
  `3b8f9c9ccb09943310c080f44c5277b545e1da1f267f54d60be2ee5ce4482519`.
- The closed-host preflight checked that EXE, both 13-file prototype inventories,
  original native-configuration/marker byte backups and fresh destination paths.
- The original unowned extension was moved on the same volume outside the
  extension search directory. An independent hash-checked copy also remains.
- The actual EXE completed with exit code 0 at `2026-11-05T14:25:31Z`.
  `C:\Program Files\TwinCatAssistant-Alpha\state.json` selects that version;
  the extension selector points to its `versions/0.1.0-dev.1/application`.
- A subsequent full application inventory verified all **22443** entries.
  The real extension equals its retained installer snapshot; all 13 original
  prototype files retain their baseline sizes and SHA-256 values.

## Selected state and actual native startup

The original native configuration is retained byte-for-byte for restoration.
The acceptance configuration selects a fresh synthetic StateRoot and the
installed default MCP settings. The former runtime hint referenced a temporary
SDK. For this test only, DotNetRoot was changed to the installer-verified
`C:\Program Files\dotnet`. Both the initial post-install configuration and its
receipt were retained before that change; the current configuration digest is
recorded. No operator profile directory or credential store was copied back.
This does not change the product's documented custom-runtime contract.

The **installed** `TwinCatAssistant.IdeHost.dll` was loaded in 32-bit Windows
PowerShell, and its public `NativeChatConfiguration.Load()` selected the actual
installed Node, application, synthetic state and system .NET runtime.
The same installed assembly's `NativeChatSession.StartAsync()` then started the
real packaged backend and passed its protected readiness check.

- Initial native launch: protected-ready, total 59869 ms including stop.
- Restart: protected-ready after 55888 ms; prior synthetic data sentinel kept.
- An independent process observer saw launcher exit code **0** on the restart;
  the session was stopped. This is not an IDE-window shutdown observation.
- No provider, model request or engineering-project action occurred.

Private raw logs, hashes, configuration backups and receipts remain under
`local-data/alpha-host-baseline-20261105-140304/`. In particular:
`first-ci-uac-result.json`, `first-ci-installed-manifest-check.json`,
`first-ci-extension-check.json`, `first-ci-native-selection.json`,
`first-ci-native-startup.json` and `first-ci-native-restart.json`.
The initial startup receipt's `stoppedNormally` field only inferred session
closure from `IsRunning`; the separate restart receipt reports the independently
observed exit code. Only the latter is used as process-exit evidence.

## Still open

The later C1 installer, actual update/policy-aware rollback, installed IDE UI,
member editors, actual TwinCAT paste and first-after-logon acceptance remain
open. The prototype and original configuration are still retained; neither
has been restored or deleted during these checks. The old C0 EXE cannot perform
a C1-aware policy downgrade. Use the final C1 rollback implementation when it
has been built and checked; a file swap alone does not prove a working return.
