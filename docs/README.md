# Planning Documentation

As of October 8, 2026. These documents describe the agreed target state, not
features that have already been implemented.

| Document | Contents |
| --- | --- |
| [MVP](MVP.md) | User workflow, feature scope, exclusions, and acceptance criteria |
| [Architecture](ARCHITECTURE.md) | Reuse strategy, components, communication, and open decisions |
| [Roadmap](ROADMAP.md) | Technical feasibility validation and implementation sequence |
| [Engineering Safety](ENGINEERING_SAFETY.md) | Approvals, conflicts, builds, rollback, and runtime protection |

## Guiding Decisions

1. Reuse existing OpenChamber/OpenCode code rather than merely reproducing its appearance.
2. Start with one specific TwinCAT shell version rather than promising general
   compatibility with all Visual Studio and TwinCAT versions.
3. Integrate both existing MCPs and extend engineering functionality in the
   monorepo instead of building parallel TwinCAT tool catalogs.
4. The MVP is a freely usable chat with agent capabilities, not a rigid wizard
   with prescribed form steps.
5. Keep offline engineering strictly separate from online/runtime actions.

## Updating the Plan

Add new technical findings to the relevant documents. In particular, unresolved
assumptions must not silently be treated as proven capabilities. Major
architecture decisions will receive dedicated ADRs once a concrete decision and
its alternatives are available.
