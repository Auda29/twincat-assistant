# Documentation

The MVP documents describe the target product. Start with [Status](STATUS.md) for
what exists and has been tested, then [Roadmap](ROADMAP.md#where-we-are-now) for
the next slice and exit gates. Current focus: active-editor-first local context
after the x86 read-only shell smoke test; not write/build work or a larger manual
object selector. Native editor/chat and amd64 acceptance remain open.

- [Installation and real local checks](INSTALLATION.md)
- [OpenChamber extension](EXTENSION.md)
- [Diagnostic bridge and portable safety core](BRIDGE.md)
- [Native Windows acceptance](WINDOWS_ACCEPTANCE.md)
- [Active editor implementation and acceptance](ACTIVE_EDITOR.md)
- [Native chat start and connection](NATIVE_CHAT.md)
- [Dependency audit and release blockers](SECURITY_AUDIT.md)
- [Native 0.1.3 preview release notes](releases/native-v0.1.3.md)
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
