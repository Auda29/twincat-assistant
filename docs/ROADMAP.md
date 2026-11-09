# Implementation Roadmap

Priorities updated: **2026-11-02**. The older implementation and milestone
snapshots below retain their dates. [Status](STATUS.md) records implementation
evidence; this document defines the order of work and milestone exit gates.
[MVP](MVP.md) describes the eventual product, not the permissions of the current
prototype. No delivery dates or effort estimates are committed.

The [solution startup investigation](SOLUTION_STARTUP_INVESTIGATION.md) remains open. Host acceptance must include cold startup with restored PLC editors and the Assistant tool window. The observed toolbox wait has not been attributed to the Assistant.

## Agreed priorities (2026-11-02)

Bereinigung vom 09.11.2026: Der neue Produktbranch entfernt weitere Serverdienste
und nicht mehr benötigte Pakete, vereinfacht die Einstellungen und ersetzt eigene
Produkttexte. Die [Umfangs- und Nachweisübersicht](PRODUCT_CLEANUP_20261109.md)
benennt auch die verbleibenden gemeinsamen Hilfen und Client-Einstiege.
Dieser Stand ist vom veröffentlichten Preview-3-Installer getrennt. Punkt 2
bleibt eine schrittweise Bereinigung; Punkte 5–7 wurden dabei nicht begonnen.

Integrationsstand vom 06.11.2026: Private native Testkonfiguration, begrenzte
Wiederverwendung voller Paketprüfungen pro Startvorgang und die P2-Korrektur vor
Profil-Recovery-Schreibzugriffen sind zusammengeführt. 159 Node-Tests ohne Skip,
151 native Regressionen, 29 Konfigurationschecks sowie der vollständige echte
Paket-Smoke mit beiden MCPs und Neustart bestehen. Der
[aktuelle Nachweis](INTEGRATED_ACCEPTANCE_20261106.md) trennt diese Ergebnisse
von der weiter offenen Host-/UI-Abnahme. Ein frisches Shell-Profil lädt weiterhin
den globalen Prototyp; eine sichere Trennung der Erweiterungen ist nicht belegt.
Der Installer verweigert deshalb das Test-Override am gemeinsamen Host. Die
Arbeitsinstallation bleibt auf dem wiederhergestellten Stand. Punkte 5–7 wurden
nicht begonnen; die vier Abschlussaufträge sind noch nicht alle abgenommen.

Host-Zwischenstand vom 05.11.2026: Die read-only Inventarisierung bestätigt
TcXaeShell/x86 mit TwinCAT 3.1.4024.78 als erstes Alpha-Installationsziel.
Die [Host-Abnahmematrix](HOST_ACCEPTANCE_MATRIX.md) und
[`host-targets.json`](../native/host-targets.json) halten konkrete Versionen,
Installationskriterien und weiterhin offene Abnahmen fest. Zusätzlich ist
TwinCAT in VS2019 installiert, aber nicht abgenommen. Punkt 1 bleibt bis zum
Abschluss der offenen Prüfungen im echten Zielhost offen; die Einstufung als
Alpha-Ziel ersetzt diese Prüfungen nicht.

Implementierungsstand vom selben Tag: Installer-Registrierung, Update und
Payload-Rollback sind mit synthetischen Hosts geprüft. Der Host löst den
Runtime-Start jetzt aus dem ausgewählten Paket auf. Die Migration verwalteter
Konfigurationen zwischen geprüften Geschwisterversionen ist implementiert;
Grenzen stehen in [Installer-Relokation](INSTALLER_RELOCATION.md). Der
vollständige Paketbau und Installerbau sind jetzt lokal erfolgreich. Der
Knowledge-Eigentümer hat die Bündelung freigegeben; die Freigabe ist im Repo
dokumentiert. Der [Buildnachweis](ALPHA_DELIVERY_BUILD.md) enthält die echten
Paket-/MCP-Startprüfungen und die Kontrolle des eingebetteten Installer-Payloads.
Der echte [Windows-CI-Lauf](https://github.com/Auda29/twincat-assistant/actions/runs/42034006930)
hat Paketstart, beide MCPs, Neustart, EXE-Bau und eingebettete Hashprüfung
bestanden. Echte Installation, Update und Rollback im Host bleiben offen;
Punkt 3 ist damit nicht abgeschlossen. Der Bau relocatierbarer MCP-Eingaben ist umgesetzt und
geprüft; [Anwendungseingaben](APPLICATION_INPUTS.md) beschreibt den Buildweg.
Der Analyse-Prompt C1 ist in beiden Konfigurationspfaden eingebunden und
mit Migrations- und Vertragstests geprüft. Die
[Prompt-Abnahme](PROMPT_ACCEPTANCE_20261105.md) belegt 17 echte Modellantworten,
34 Renderings im x86-WPF-WebView2-Testcontrol und 40 exakte Kopierinhalte.
Darstellung in der installierten IDE und tatsächliches Einfügen in TwinCAT
bleiben für Punkt 4 zu prüfen. Der alte C0-Kandidat kennt C1 nicht;
die Installer-Rückkehr braucht die geprüfte selektive Policy-Migration.
Details stehen im [Installer-Vertrag](INSTALLER_CONTRACT.md) und in der
[Prompt-Dokumentation](PROFILE_PROMPTS.md).

Weitere Abschlussnachweise vom 05.11.2026: Die
[Auslieferungsabgrenzung](DELIVERY_SCOPE.md) für Punkt 2 ist implementiert und
mit einem echten vollständigen Paket geprüft. Die
[isolierte Host-Abnahme](HOST_ACCEPTANCE_20261105.md) belegt direkte
Editor-/Projektlesevorgänge im Zielhost; sichtbare UI, Member-Editoren und
First-after-logon sind weiterhin offen. Der integrierte Folgekandidat
[`0.2.0-preview.2`](releases/alpha-0.2.0-preview.2.md) ist noch kein
installierter oder abgenommener Release. Punkte 5–7 wurden nicht begonnen.

Die folgenden Zukunftsthemen sind in dieser Reihenfolge vereinbart. Sie sind
geplant, nicht als umgesetzt oder abgenommen markiert. Die Liste legt keinen
gemeinsamen Release-Termin für alle sieben Punkte fest.

1. **Kompatibilität eingrenzen und Zielhosts festlegen.** Die unterstützte
   Hostkombination für die erste installierbare Alpha bestimmen. Ausgangspunkt
   ist die bisher praktisch geprüfte TcXaeShell/x86. Technische Unterschiede zu
   TwinCAT 4026, der 64-Bit-TwinCAT-Shell und TwinCAT innerhalb von Visual Studio
   untersuchen. Eine Kompatibilitätsmatrix mit konkretem TwinCAT-/XAE-Build,
   Shell- beziehungsweise Visual-Studio-Version, Prozessarchitektur und
   Windows-Version führen. Benötigte Anpassungen und Installationspakete je
   Kombination bestimmen und Installation, Start, Andocken, WebView/Chat,
   Projekt- und Editorbindung, Baustein-/Member-Erkennung, Anhänge sowie beide
   MCPs im jeweiligen echten Host prüfen. Unterschiede beim Zugriff auf
   ungespeicherten Editorinhalt ausdrücklich erfassen. Erfolgreiche Builds und
   vorhandene x86-Abnahmen belegen noch keine Kompatibilität der anderen Hosts.
   Die erste Installerversion kann den bereits geprüften Host bedienen, während
   weitere Varianten ihre eigene Umsetzung und Abnahme erhalten. Die Ergebnisse
   auch bei Editor-Profil und Inline-Vervollständigung berücksichtigen.

2. **Backend für die Auslieferung vorbereiten und schrittweise bereinigen.**
   Zuerst den Produktstart vom Entwickler-Checkout lösen, Installationspfade
   vereinheitlichen und die benötigten Bestandteile als gebautes Anwendungspaket
   mit einem geprüften Manifest abgrenzen. Auf Basis der
   [Server-Bestandsaufnahme](BACKEND_SERVER_INVENTORY.md) ungenutzte Funktionen,
   Routen und Abhängigkeiten prüfen und schrittweise entfernen. Sichtbare und
   interne OpenCode-/OpenChamber-Bezeichnungen dort ersetzen, wo sie unsere
   Produktmodule meinen: TwinCAT Assistant Core, Chat und Host. Herkunft,
   Lizenzen und erforderliche Drittanbieterhinweise bleiben nachvollziehbar.
   Bestehende Anbieter, Logins, Modelle, Streaming, Abbruch, Agentenablauf,
   Werkzeugaufrufe, beide MCPs und Gesprächsspeicherung bleiben erhalten.
   Speicherpfade, Konfigurationsschlüssel und Datenformate nur mit geprüfter
   Migration ändern; vorhandene Chats, Projektbindungen und Anmeldungen erhalten.
   Die weitere Bereinigung wird in kleinen Schritten fortgeführt; ihre komplette
   Fertigstellung ist keine Voraussetzung für den ersten Installer.

3. **Einfache Installation aus der CI bereitstellen.** Ein versioniertes,
   für Nutzer direkt installierbares Paket aus den geprüften CI-Builds erzeugen.
   Host, Core, Chat, beide MCPs und ihre Laufzeitvoraussetzungen gemeinsam
   berücksichtigen. Die Installation soll ohne Git-Checkout, npm oder eigenes
   Kompilieren möglich sein. Paketformat, die zunächst unterstützte
   IDE-/Architekturvariante und benötigte Laufzeitkomponenten vor der Umsetzung
   festlegen. Erstinstallation, Update und Rückkehr zum vorherigen Stand prüfen;
   vorhandene Konfiguration, Chats und Anmeldungen bei Updates erhalten.
   Aufbauend auf [Installation](INSTALLATION.md) und
   [Startvertrag](START_CONTRACT.md) dokumentieren, was der Nutzer tun muss.

4. **Systemprompts der Profile prüfen.** Mit dem Analyse-Profil beginnen und
   gemeinsame Regeln für spätere Profile festhalten. Prüfen, ob der Agent
   TwinCAT-/Structured-Text-Code lesbar und im vorhandenen Renderer korrekt
   darstellt: passende Codeblöcke, Einrückung, klare Zuordnung von Deklaration,
   Implementierung, Aktion, Methode und Property sowie vollständige, kopierbare
   Ausschnitte. Gespeicherter Code, angehängter Snapshot und verifizierter
   Editorinhalt müssen sprachlich unterscheidbar bleiben. An repräsentativen
   TwinCAT-Fragen prüfen, einschließlich Quellenangaben, Unsicherheit und der
   tatsächlichen Werkzeuge und Rechte des jeweiligen Profils. Die Promptprüfung
   kann unabhängig vom Installer auch früher durchgeführt werden.

5. **Editor-Profil als Gegenpart zu Analyse ergänzen.** Kontrollierte Änderungen
   am eindeutig gebundenen PLC-Projekt ermöglichen. Analyse bleibt lesend.
   Bestehende Agenten- und Werkzeuglogik weiterverwenden; die Schreibanbindung
   mit Vorschau/Diff, Freigabe, Prüfung des Ausgangsstands und Wiederherstellung
   gemäß [Engineering Safety](ENGINEERING_SAFETY.md) umsetzen. Ungespeicherte
   Änderungen und Projektwechsel berücksichtigen. Das Profil allein erteilt
   noch keine Schreibrechte und schaltet keine PLC-Runtime-Aktionen frei.

6. **Mehrsprachigkeit ergänzen, zunächst Deutsch und Englisch.** Die Oberfläche
   von Host und Chat einschließlich Menüs, Dialogen, Status- und Fehlermeldungen
   konsistent übersetzen. Sprache auswählbar machen und die Auswahl speichern.
   Texte zentral verwalten und weitere Sprachen später ergänzen können.
   Beide Sprachen auf vollständige Übersetzungen und passende Darstellung prüfen.
   Quellcode und vorhandene Gesprächsinhalte bleiben in ihrer Originalsprache;
   die Antwortsprache des Agenten bei der Prüfung der Profilprompts berücksichtigen.

7. **Inline-Codevervollständigung ergänzen.** Vorschläge direkt im TwinCAT-Editor
   anbieten, getrennt vom Chatablauf. Vorab unterstützte Editoren und ST-Objekte,
   Auslösung, Kontextumfang, Modellanbindung und akzeptable Antwortzeit festlegen.
   Vorschläge müssen sich ausdrücklich übernehmen oder verwerfen lassen und bei
   weiterem Tippen oder Editorwechsel veralten beziehungsweise abbrechen.
   Ungespeicherter Text und dessen Übertragung brauchen einen eindeutigen Vertrag;
   keine automatische Übernahme oder Speicherung von Vorschlägen.

Der nächste größere Meilenstein ist eine einfach installierbare Version mit
einem verlässlichen Analyse-Profil. Editor-Profil und Inline-Vervollständigung
bauen auf diesem Stand auf.

Die offene [Knowledge-Startzeit-Untersuchung](KNOWLEDGE_STARTUP_INVESTIGATION.md)
bleibt separat dokumentiert. Die neue Prioritätenliste erklärt sie nicht für
erledigt. Schreib- und Compiler-Meilensteine behalten ihre eigenen Abnahmen.

## Current implementation branch

As of 2026-10-29, `feat/twincat-runtime-ui` builds the pinned OpenCode source
unchanged and adds a source contract to OpenChamber's existing composer. Project
history restoration, configuration-independent identity, legacy data compatibility
and context/attachment observations are implemented. See
[Runtime integration](RUNTIME_INTEGRATION.md) and the
[stepwise plan](TWINCAT_RUNTIME_UI_PLAN.md). Selecting modules to remove remains
a joint next step. Native installation and engineering milestones stay separate.

## Where we are now

| Track | State | Evidence / next gate |
| --- | --- | --- |
| Integration foundation | Merged in PR #1 | Local CLI, reused upstreams, both MCP fixture integrations and portable safety components; not native MVP acceptance |
| Native read-only prototype | PR [#2](https://github.com/Auda29/twincat-assistant/pull/2) merged on 2026-10-23 | Foundation merged; x86 0.1.2 installed; both 0.1.2 payloads build locally |
| x86 shell smoke test | Passed, user-reported | Menu, opening, docking, closing/reopening; local ST display observed |
| Active-editor-first UX | Implemented in 0.1.2; core x86 flow verified | User/screenshot evidence for same-name PLC_A/PLC_B following and unsaved status; 7 live reader lifecycle/isolation checks; extended host acceptance remains open |
| Native chat and model context | Installed 0.1.5; scoped x86 chat goal accepted | User-confirmed project refusal/return, explicit POU reference, provider consent/model selection and correct real response; MCP and extended acceptance remain open |
| Native project MCPs | Preview 0.1.8 installed in x86 | All 13 payload hashes and the 0.1.7 rollback copy verified; both MCP paths configured and tested with synthetic data. Actual corpus and installed startup/project acceptance remain open |
| amd64 shell acceptance | Blocked | No installed TcXaeShell64 is available for live validation |
| Live source writes / compiler | Outside the current milestone | Future M3/M4 work; no authorization to implement or invoke these now; runtime control remains excluded |

PR #2 merged the native foundation. Native MVP/release acceptance remains open.
Green CI or the x86 smoke test does not establish that acceptance. Supporting
one host does not prove the other.

## Decisions recorded on 2026-10-09

1. **Read-only first.** Current work permits no source writes, implicit saves,
   compiler invocation, activation, download, online change, forcing or runtime
   control. Portable write-policy code is not live write capability.
2. **Two architecture targets.** TcXaeShell/x86 and TcXaeShell64/amd64 share host
   logic but require separate installation and acceptance evidence.
3. **Active-editor-first, chat-first UX.** The current WPF buttons and 200-object
   selector are diagnostic scaffolding, not the intended product interface.
   Normal use must not require Refresh, Read locally or reselecting the FB already
   open in the editor. The assistant should follow the active POU locally and
   present a compact, persistent context/dirty-state indicator beside reused chat.
4. **Discovery is secondary.** Increasing the 200-target cap or adding pagination
   is not the next main deliverable. Optional project search/browsing can follow;
   active-object resolution must not depend on a complete preloaded catalog.
5. **Local context is not disclosure consent.** Automatic following must not
   silently send source to a model, MCP or persisted session, or retarget a thread
   or future approval. Ambiguous binding must clear stale context and fail closed.

## Next slice: M1.2 active-editor following

Implementation is present in candidate `0.1.2`, which is installed in x86.
The user confirms active following and unsaved-change status in the real native
window. Seven live direct-reader lifecycle/isolation checks also pass. Extended
host lifecycle, cold-start/responsiveness and amd64 acceptance remain open.
See [implementation/evidence](ACTIVE_EDITOR.md).
The slice is assessed against these requirements:

1. Resolve the selected IDE, solution, PLC project and active ST POU exactly.
   Names or tab titles alone are insufficient; unsupported/ambiguous editors
   must be visibly unbound rather than guessed.
2. Subscribe to editor/solution lifecycle events. Coalesce redundant events and
   keep COM work on its supported thread without rescanning the entire catalog
   on every activation. Revalidate identity before publishing any delayed result.
3. Read the active object's local context automatically. Distinguish actual
   unsaved editor text from saved-file or Automation Interface snapshots; retain
   `unknown` where exact document/RDT evidence is unavailable.
4. Replace normal diagnostic controls with a compact persistent context status;
   keep manual inventory/read controls optional for diagnostics. Chat remains
   OpenChamber's UI, not a parallel implementation.
5. Verify with a separate synthetic project: switch between two POUs, make
   unsaved changes, switch to a non-ST/no editor, close/reopen the solution,
   reopen the same path, and exercise two IDE instances. No build or PLC action.

**Exit evidence:** exact active-object following without dropdown/refresh/read
steps, no stale or wrong-object display, persistent uncertainty/disclosure status,
and no source transport or writes. Record measured cold-start behavior separately;
the reported first-opening delay is not yet a proven lost-click defect.

## Completed local foundation (not native MVP acceptance)

- [x] Isolated CLI/profile, version-gated loopback start and POSIX supervision.
- [x] Real direct calls to modern knowledge and legacy saved-project MCPs using
  synthetic fixtures and the production bounded client.
- [x] Read-only context extension, reproducible bundle and browser SDK-contract tests.
- [x] Portable proposal/approval/apply/rollback core and authenticated diagnostic pipe.
- [x] Local build/test commands and native acceptance handoff.

The full OpenChamber/model workflow and native host validation remain separate
gates. No diagnostic fixture is a live TwinCAT engineering backend.

## M0: Repository and Target Environment

**Outcome:** A clear baseline without claiming already implemented product features.

- [x] Prepare a private repository and add the MVP and architecture plans.
- [x] Record the x86 TcXaeShell/XAE and local SDK/WebView2 baseline.
- [ ] Record a real TcXaeShell64/amd64 baseline and its prerequisites.
- [x] Record upstream version pins, MCP revisions and protocol configuration.
- [ ] Supply and validate an authorized real documentation corpus.
- [ ] Define a suitable synthetic ST test project and manual test cases.
- [ ] Complete license/redistribution review, including the packaged vendor interop.

Real customer/machine projects, corpus data, credentials, and user state remain
outside the repository.

## M1: Read-Only Native Host and Editor Context (current milestone)

**Outcome:** A usable native read-only host follows the correct active editor,
with actual embedded chat and reliable evidence boundaries on both architectures.

- [x] M1.1/x86: install the prototype and verify menu/open/dock/close/reopen
  (user-reported smoke evidence; keyboard/focus/resize/cold-start checks remain).
- [x] M1.1/x86: display a manually selected ST object locally. This does not prove
  active-editor binding or unsaved-text correctness.
- [ ] M1.2: implement and accept active-editor-first local context, as specified above.
- [x] M1.2 implementation: direct active-POU binding, lifecycle invalidation,
  provenance/uncertainty and compact UI with optional diagnostics.
- [x] M1.2 x86 delivery: install candidate 0.1.2 with verified files and preserve
  the old payload; observe extension/package loading and a visible tool window.
- [x] M1.2 core x86 acceptance: observed PLC_A/PLC_B automatic header following
  and unsaved-comment status; live reader isolation, unsupported/no editor,
  solution close and same-path reopen checks.
- [ ] M1.2 extended acceptance: verify display clearing through all solution/
  window lifecycle cases, focus, cold-start and large-object responsiveness;
  establish broader actual unsaved-buffer semantics before claiming those.
- [ ] M1.3: load the real authenticated OpenChamber UI in native WebView2; test
  focus, resize, safe navigation, connection errors and close/reopen behavior.
- [x] M1.3 connection implementation: protected native runtime start/reuse,
  current-user encrypted local password, real readiness/navigation/error states
  and owned shutdown. Real transport/credential checks pass; installed WebView
  login and full conversation acceptance remain separate.
- [x] M1.3 x86 first-run/login-page smoke: user confirms local password setup and
  the real OpenChamber login page in the installed 0.1.3 window. Authenticated
  conversation and the wider focus/lifecycle matrix remain open.
- [ ] Complete the x86 synthetic-editor, lifecycle and identity acceptance record.
- [ ] Validate amd64 installation targets and repeat the host/editor/WebView2
  acceptance separately in a real TcXaeShell64.

No write, rollback or compiler test belongs to this milestone. Such tests move to
M3/M4 and need separate scope/approval. There is no production runtime connection.

If reliable dirty-state detection or conflict control is unavailable, agent
writes remain disabled. A working chat view alone does not satisfy the agreed
MVP. Any fallback host requires an explicit decision and must not silently be
presented as shell compatibility.

## M2: Project-Bound Agent Context and Both MCPs

**Outcome:** The reused chat/agent workflow can use the correct native context and
both existing MCPs under an explicitly authorized disclosure policy, still read-only.

The completed scoped x86 chat-integration goal delivers installed 0.1.5: a compact native host,
persistent exact IDE/project/session binding, explicit POU composer snapshots,
provider consent and bounded reconnect. The operator confirms the actual model
reply to an explicitly attached synthetic POU. See [implementation and evidence](CHAT_INTEGRATION.md).
Preview 0.1.8 adds both native-to-MCP connections with the existing saved-file
engineering backend and per-project workspaces. Actual tool calls and structured
Knowledge sources pass synthetic-corpus/project checks; no model call is made.
No operator corpus is configured yet. The live/unsaved backend, real corpus and
complete M2 installed acceptance remain separate. See [native MCP integration](NATIVE_MCP.md).

- [ ] Start the local agent infrastructure or connect to a compatible running
  instance; avoid unintended duplicate starts.
- [ ] Persistently associate the thread with the specific project/IDE binding.
- [x] Connect both MCPs and test tool catalogs and actual calls with synthetic
  projects/corpus in the protected native runtime (0.1.8 preview).
- [x] Check the knowledge MCP's protocol profile and `structuredContent` with
  the pinned server and a synthetic activated corpus.
- [ ] Configure an actual documentation corpus and accept installed project/MCP
  behavior with the operator; component checks do not close this gate.
- [ ] Connect accepted native context to the monorepo MCP/session boundary;
  show what context is attached and distinguish saved/live/unsaved evidence.
- [ ] Make startup/reconnection a normal host lifecycle, not a user-managed port
  and refresh workflow; retain authenticated supervision and ownership.
- [ ] Add optional bounded project search/browsing with visible completeness;
  do not require it for the active-editor workflow.
- [ ] Verify activity views, sources, follow-up questions, and cancellation.
- [ ] Close and reopen the thread; revalidate its binding.
- [ ] Test solution switching and multiple IDE instances.

## M3: Controlled ST Writes

**Outcome:** An agent can use exactly the promised write scope.

- [ ] Connect the live engineering backend in the monorepo.
- [ ] Define write contracts for an existing ST target object.
- [ ] Integrate object-level diffs and approval.
- [ ] Recheck the baseline state immediately before applying a change.
- [ ] Block dirty target objects; do not implicitly save someone else's changes.
- [ ] Log the agent's before/after states and actual outcome.
- [ ] Implement controlled rollback with a post-change state check.
- [ ] Restrict general file/shell tools for the engineering agent profile and
  test attempted bypasses.
- [ ] Handle cancellation, timeouts, and process termination without automatic
  write retries.

## M4: Build and Full MVP Acceptance

**Outcome:** Chat, MCPs, writes, and real validation form a complete engineering
workflow.

- [ ] Bind build execution to the specific project binding.
- [ ] Capture build completion, timeouts, and fresh error/warning messages.
- [ ] Provide navigable diagnostics and build output in the thread.
- [ ] Test a successful build and an intentionally failing build.
- [ ] Optionally integrate existing runtime diagnostics in read-only mode.
- [ ] Verify all [MVP acceptance criteria](MVP.md#acceptance-criteria).
- [ ] Document installation, startup, limitations, and tested versions.

## After the MVP

Only after acceptance and explicit prioritization:

- Edit methods, properties, actions, and new blocks.
- Handle multiple objects as a controlled change set.
- Expand beyond the accepted active-POU workflow and improve conflict handling;
  unsaved-state correctness for the current object is already a pre-MVP gate.
- Improve semantic reference/call analysis.
- Support additional Visual Studio/TwinCAT hosts.
- Add HMI and I/O engineering.
- Evaluate advanced runtime features under a separate safety concept.

## Testing and Evidence Principle

Each feasibility result records the host/upstream version, input, expected
result, and observed result. Synthetic tests do not prove full TwinCAT product
coverage. A practical shell validation cannot be replaced by an ordinary web
browser test.
