# Native prototype 0.1.6 preview

This development prerelease replaces the controls above the embedded chat with
the selected compact context bar. OpenChamber continues to own the conversation,
composer, model picker and history. The Node operator package stays at
`0.1.0-dev.1`.

## Changes

- One native context bar follows OpenChamber's light/dark palette, with a blue
  TwinCAT-style POU badge, object/project identity and a compact source-state
  indicator. Narrow tool windows use compact actions. Click the POU name for
  the local ST preview; diagnostics and provider management are in the menu.
- Local chat startup and sign-in are automatic. A fresh profile generates a
  DPAPI-protected credential; the embedded page receives an HttpOnly session
  cookie. Existing credentials are preserved.
- Opening or switching a PLC project opens its newest known conversation or
  creates one. Foreign-project conversations remain readable but cannot accept
  new prompts or native source. A general chat can be used without an ST editor.
- Connecting a model provider enables it automatically. Model selection stays
  in the original composer; engineering and shell permissions remain denied.
- Automatic POU attachment is an optional persisted setting, off by default.
  When enabled, it stages the current POU and changed revisions in a matching
  project chat. Submitting a message still controls transfer to the provider.
- The native launcher accepts the single leading UTF-8 BOM that .NET Framework
  can write to redirected stdin on systems using code page 65001.
- Native labels are German. OpenChamber defaults to German while preserving an
  existing language preference. Its duplicate top-bar chat actions are hidden.

## Verification

Local checks on 2026-10-26 pass:

- 47 Node tests and 8 production-panel browser contracts.
- 76 native policy/context/chat regressions and 26 WPF view checks.
- 31 compiled-control/WebView2/OpenChamber integration checks, including actual
  menu activation, preference persistence, project switching, provider-policy
  readback, automatic attachment and runtime reconnection.
- 2 native Node runtime checks.
- Both x86 and amd64 VSIX variants build without warnings or errors.

The native fixtures use synthetic source, private temporary profiles and a fake
provider credential. They make no model call. WPF renders cover 340, 520 and
900 pixels; embedded-chat captures cover 390, 768 and 1100 pixels. The x86 package
is installed with all 13 payload hashes verified and 0.1.5 preserved outside
extension discovery. The IDE remained closed for the update; startup and live
UI acceptance of 0.1.6 remain open. These results do not establish
TwinCAT compiler, simulation, PLC/runtime or machine validation.

## Distribution

This is a source-only prerelease, following the existing release format. Locally
built VSIX packages contain Beckhoff interop components; redistribution rights
still need review, so no VSIX files are attached. Build and setup instructions
are in [the native prototype guide](../NATIVE_PROTOTYPE.md) and the current
workflow is in [chat integration](../CHAT_INTEGRATION.md).

Production approval, upstream dependency remediation, extended native lifecycle
acceptance, amd64 live installation, general exact unsaved-buffer semantics and
the real MCP/corpus integration remain separate gates. See
[the dependency audit](../SECURITY_AUDIT.md) for the recorded upstream blockers.
