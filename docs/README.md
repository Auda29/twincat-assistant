# Documentation

The MVP documents describe the target product. Start with [Status](STATUS.md) for
what exists and has been tested, then [Roadmap](ROADMAP.md) for the work sequence
and exit gates. The current planning branch describes the
[OpenCode/OpenChamber specialization for TwinCAT](TWINCAT_RUNTIME_UI_PLAN.md).
Native acceptance and future engineering write/build milestones remain separate.

- [Installation and real local checks](INSTALLATION.md)
- [Agreed product priorities and host compatibility planning](ROADMAP.md#agreed-priorities-2026-11-02)
- [TwinCAT 4024/4026 installation variants and host compatibility research](HOST_COMPATIBILITY.md)
- [Planned OpenCode/OpenChamber specialization for TwinCAT](TWINCAT_RUNTIME_UI_PLAN.md)
- [Source runtime, persistent project chats and context states](RUNTIME_INTEGRATION.md)
- [Backend server inventory and Core/Chat/Host ownership](BACKEND_SERVER_INVENTORY.md)
- [Common startup contract for Core, Chat and Host](START_CONTRACT.md)
- [First alpha host target and remaining real-host acceptance](HOST_ACCEPTANCE_MATRIX.md)
- [Windows installer, host registration and rollback contract](INSTALLER_CONTRACT.md)
- [Building relocatable MCP inputs and collecting runtime notices](APPLICATION_INPUTS.md)
- [Application package layout and installed runtime selection](APPLICATION_STAGING.md)
- [Local complete application and installer build evidence](ALPHA_DELIVERY_BUILD.md)
- [Managed profile and project configuration across package versions](INSTALLER_RELOCATION.md)
- [Analysis profile prompts, migration and evaluation cases](PROFILE_PROMPTS.md)
- [OpenChamber extension](EXTENSION.md)
- [Diagnostic bridge and portable safety core](BRIDGE.md)
- [Slow solution load and PLC editor restoration investigation](SOLUTION_STARTUP_INVESTIGATION.md)
- [Native Windows acceptance](WINDOWS_ACCEPTANCE.md)
- [Active editor implementation and acceptance](ACTIVE_EDITOR.md)
- [Native chat start and connection](NATIVE_CHAT.md)
- [Native Knowledge and TwinCAT MCP integration, 0.1.8 preview](NATIVE_MCP.md)
- [Automatic Beckhoff documentation setup, 0.1.9](KNOWLEDGE_SETUP.md)
- [Project-bound native chat and compact UI, 0.1.6 candidate](CHAT_INTEGRATION.md)
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
   stays read-only; live writes and compiler integration are later milestones.

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
