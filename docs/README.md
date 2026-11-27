# Documentation

Repository documentation and GitHub release titles and notes are maintained in
English. The product UI remains available in German and English. Preserve
localized UI/error quotations when needed as evidence, with an English explanation.

Start with [Status](STATUS.md) for the released scope and verification evidence,
then [Roadmap](ROADMAP.md) for remaining work and exit gates. [MVP](MVP.md)
defines the full product requirements. Dated prototype and planning documents
retain their historical scope; they do not override the current profile guide.

## Current release

[0.2.0 Beta 12](releases/native-v0.2.0-beta.12.md) adopts Engineering MCP 0.5.13
with Debug waits and watches, separate dynamic MCP server names,
side-by-side source approvals, reliable profile switching and corrected
selective build preflight. The [latest published release](https://github.com/Auda29/twincat-assistant/releases/latest)
includes the installer, checksums and provenance.

Published on **2026-11-27**, application `0.2.0-beta.12` / native `0.2.0.18`
is built from `201a9f0ccbbbc099ef5edc6a3675168c2f70536b`. All 23 stages of
the local Windows release pipeline passed. Full installed-host workflow
acceptance and real-corpus quality/performance checks remain open.

## Start here

- [Install Beta 12 and review verification](releases/native-v0.2.0-beta.12.md)
- [Profiles and native approvals](EDITOR_PROFILE.md)
- [Project chats and attachments](CHAT_INTEGRATION.md)
- [Set up Beckhoff documentation search](KNOWLEDGE_SETUP.md)
- [Architecture and data boundaries](ARCHITECTURE.md)
- [Current status](STATUS.md) and [remaining work](ROADMAP.md)

The released MCP inputs are Engineering 0.5.13 and Knowledge 0.1.2. The
[MCP-first integration record](MCP_P1_INTEGRATION_20261112.md) retains earlier
private-development evidence.

## Earlier releases

| Release | Main changes |
| --- | --- |
| [Beta 11](releases/native-v0.2.0-beta.11.md) | Project-wide ST discovery, approved edits and object creation |
| [Beta 10](releases/native-v0.2.0-beta.10.md) | Automatic read-only Debug connection; Engineering 0.5.8 |
| [Beta 9](releases/native-v0.2.0-beta.9.md) | PLC association and COM invocation fixes; Engineering 0.5.7 |
| [Beta 8](releases/native-v0.2.0-beta.8.md) | Shared composer profile menu and clearer Debug availability |
| [Beta 7](releases/native-v0.2.0-beta.7.md) | Analysis, Editor and Debug profiles |
| [Beta 6](releases/native-v0.2.0-beta.6.md) | Chat restart recovery and background build readiness |
| [Beta 5](releases/native-v0.2.0-beta.5.md) | Selective builds, typed Knowledge tools and pinned-history fix |
| [Beta 4](releases/native-v0.2.0-beta.4.md) | Startup/member-context hardening and Knowledge validation overlay |
| [Beta 3](releases/native-v0.2.0-beta.3.md) | Startup performance corrections and shared product icon |
| [Beta 2](releases/native-v0.2.0-beta.2.md) | Extraction fix and version-aware installer |
| [Beta 1](releases/native-v0.2.0-beta.1.md) / [Setup 1](releases/native-v0.2.0-beta.1-setup.1.md) | Product cleanup, DE/EN language selection and installer extraction correction |

## Development and historical evidence

- [Preview 6 native chat startup fix](releases/native-v0.2.0-preview.6.md)
- [Preview 5 Unicode project-path fix](releases/native-v0.2.0-preview.5.md)
- [Preview 4 project-setup fix and update](releases/native-v0.2.0-preview.4.md)
- [Preview 3 installer and first-menu-click fix](releases/native-v0.2.0-preview.3.md)
- [Graphical Windows setup](INSTALLER_UI.md)
- [German/English UI, shared language choice and verification](LANGUAGE_SUPPORT.md)

- [Installation and real local checks](INSTALLATION.md)
- [Agreed product priorities and host compatibility planning](ROADMAP.md#agreed-priorities-2026-11-02)
- [TwinCAT 4024/4026 installation variants and host compatibility research](HOST_COMPATIBILITY.md)
- [Planned OpenCode/OpenChamber specialization for TwinCAT](TWINCAT_RUNTIME_UI_PLAN.md)
- [Source runtime, persistent project chats and context states](RUNTIME_INTEGRATION.md)
- [Backend server inventory and Core/Chat/Host ownership](BACKEND_SERVER_INVENTORY.md)
- [Implemented delivery scope and remaining upstream cleanup](DELIVERY_SCOPE.md)
- [Server/package reduction and product identity, 2026-11-09](PRODUCT_CLEANUP_20261109.md)
- [Common startup contract for Core, Chat and Host](START_CONTRACT.md)
- [First alpha host target and remaining real-host acceptance](HOST_ACCEPTANCE_MATRIX.md)
- [Isolated real-host acceptance evidence from 2026-11-05](HOST_ACCEPTANCE_20261105.md)
- [Windows installer, host registration and rollback contract](INSTALLER_CONTRACT.md)
- [Building relocatable MCP inputs and collecting runtime notices](APPLICATION_INPUTS.md)
- [Application package layout and installed runtime selection](APPLICATION_STAGING.md)
- [Local complete application and installer build evidence](ALPHA_DELIVERY_BUILD.md)
- [Actual CI installer evidence and coordinated host handoff](ALPHA_CI_HOST_HANDOFF.md)
- [First actual CI installation and installed native backend checks](HOST_INSTALLATION_20261105.md)
- [Integrated alpha 0.2.0-preview.2 build evidence](releases/alpha-0.2.0-preview.2.md)
- [Managed profile and project configuration across package versions](INSTALLER_RELOCATION.md)
- [Profile prompts, migration and evaluation cases](PROFILE_PROMPTS.md)
- [Real model and native WebView renderer acceptance](PROMPT_ACCEPTANCE_20261105.md)
- [OpenChamber extension](EXTENSION.md)
- [Diagnostic bridge and portable safety core](BRIDGE.md)
- [Slow solution load and PLC editor restoration investigation](SOLUTION_STARTUP_INVESTIGATION.md)
- [Beta 2 UI-thread configuration work and startup performance correction](STARTUP_PERFORMANCE_20261110.md)
- [Historical beta hardening, Knowledge overlay and release pipeline](BETA_HARDENING_20261110.md)
- [Native Windows acceptance](WINDOWS_ACCEPTANCE.md)
- [Active editor implementation and acceptance](ACTIVE_EDITOR.md)
- [Native chat start and connection](NATIVE_CHAT.md)
- [Native Knowledge and Engineering MCP integration](NATIVE_MCP.md)
- [Automatic Beckhoff documentation setup](KNOWLEDGE_SETUP.md)
- [Project-bound native chat and integration history](CHAT_INTEGRATION.md)
- [Dependency audit and release blockers](SECURITY_AUDIT.md)
- [TwinCAT Assistant 0.2.0 Preview release notes](releases/native-v0.2.0-preview.1.md)
- [Native 0.1.3 preview release notes](releases/native-v0.1.3.md)
- [Native 0.1.6 preview release notes](releases/native-v0.1.6.md)
- [Native 0.1.8 preview release notes](releases/native-v0.1.8.md)
- [Executed verification and review findings](VERIFICATION.md)

| Document | Contents |
| --- | --- |
| [MVP](MVP.md) | User workflow, feature scope, exclusions, and acceptance criteria |
| [Architecture](ARCHITECTURE.md) | Reuse strategy, components, communication, and open decisions |
| [Roadmap](ROADMAP.md) | Technical feasibility validation and implementation sequence |
| [Engineering Safety](ENGINEERING_SAFETY.md) | Approvals, conflicts, builds, rollback, and runtime protection |

## Guiding Decisions

1. Reuse existing OpenChamber/OpenCode code rather than merely reproducing its appearance.
2. Support explicitly tested x86 and amd64 TwinCAT shell versions with separate
   acceptance, rather than promising general Visual Studio/TwinCAT compatibility.
3. Integrate both existing MCPs and extend engineering functionality in the
   monorepo instead of building parallel TwinCAT tool catalogs.
4. The MVP is a freely usable chat with agent capabilities, not a rigid wizard
   with prescribed form steps.
5. Keep offline engineering strictly separate from online/runtime actions.
6. The assistant follows the active ST editor locally; manual Refresh/Read/object
   selection is diagnostic scaffolding, not the normal product workflow.
7. Automatic local context is not source-disclosure consent. Current native work
   keeps Analysis read-only; each optional PLC build requires native approval.
   Editor source edits, rollback and object creation require individual native
   approval. Debug is read-only. PLC runtime mutations remain outside the
   release permissions.

## Updating the Plan

Add new technical findings to the relevant documents. In particular, unresolved
assumptions must not silently be treated as proven capabilities. Major
architecture decisions will receive dedicated ADRs once a concrete decision and
its alternatives are available.

At each accepted change or test result, reconcile Status (evidence), Roadmap
(priority/exit gates) and MVP (product requirements). Date the snapshot and state
the code/payload versions separately. Distinguish user-reported smoke evidence,
automated regressions and synthetic-editor acceptance. Keep source, screenshots,
raw local logs and credentials out of commits. Do not silently expand scope or
mark a whole architecture/milestone complete from one successful subtest.
