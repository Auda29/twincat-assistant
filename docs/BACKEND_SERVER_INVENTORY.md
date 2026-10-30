# Bestandsaufnahme der Serveraufgaben

Stand: 29.10.2026. Arbeitsbranch `backend`, Ausgangscommit
`de4eef86655e553c6b1045853b5aedf6587ec4b9` auf `main`.

Dies ist Schritt 1 der nächsten Umbauphase. Er erfasst die vorhandene
Implementierung und ordnet ihre Aufgaben den vereinbarten Modulen zu.
Die Zuordnung beschreibt das Ziel. Paketnamen, Prozesse, APIs und Datenformate
werden mit dieser Bestandsaufnahme nicht geändert. Vorschläge zum Entfernen
bleiben gemeinsame Entscheidungen.

## Ergebnis

OpenChamber besteht aus Chatdarstellung und einem umfangreichen lokalen
Server. Der Server startet OpenCode, schützt den Zugang, vermittelt API und
Ereignisse und ergänzt eigene Sitzungsfunktionen. Deshalb kann seine Ablösung
nicht mit dem Ausliefern der gebauten Oberfläche allein abgeschlossen werden.

Der TwinCAT Assistant hat bereits einen gemeinsamen Launcher, ein isoliertes
Operator-Profil und Prozessaufsicht. Darauf bauen wir auf. Der nächste kleine
Umsetzungsschritt sollte diese vorhandenen Zuständigkeiten ordnen und den
Startvertrag festhalten. Die Übernahme einzelner OpenChamber-Servermodule folgt
danach mit ihrem bestehenden Code und Verhalten.

## Verbindlicher Quellstand und Untersuchungsgrenze

| Grundlage | Geprüfter Stand |
| --- | --- |
| TwinCAT Assistant | Commit `de4eef8`; [Source-Lock](../upstream/source-lock.json), [Runtime-Integration](RUNTIME_INTEGRATION.md) |
| OpenCode | `2.0.23`, Revision `0fd7e2829449b052abf0078666669302923d77af` |
| OpenChamber | `2.1.1`, Revision `e302062e3be0686986594fddabdafd8a97c129e5` |
| TwinCAT-Anpassungen | [Deklarierte Quellpatches](../upstream/overlays/openchamber/changes.json), [Source-Vertrag](../upstream/overlays/openchamber/twincat.ts), [WebView-Adapter](../src/embedded-chat.mjs) |

Die vorhandenen, generierten Upstream-Checkouts am festen Laufzeit-Checkout
wurden ausschließlich gelesen. Ihre HEADs entsprechen dem Source-Lock.
OpenChambers Serverdateien sind unverändert; seine vorhandenen Änderungen
betreffen den Web-Einstieg und den Composer einschließlich unseres Source-Vertrags.
Die folgenden Upstream-Verweise zeigen auf die festgelegten Revisionen.

Die Bestandsaufnahme ist eine Quellcodeprüfung. Es wurden keine Dienste
gestartet, Anbieteranmeldungen durchgeführt oder Inhalte der Operator-Datenbank
und Zugangsdaten gelesen. Die Dokumentation ersetzt keine erneute Laufzeitprüfung
nach einem späteren Umbau.

## Heutiger Start und Prozessbesitz

1. Der native [Host](../native/IdeHost/NativeChatRuntime.cs) liest die
   Laufzeitkonfiguration und startet [native-chat.mjs](../src/native-chat.mjs).
   Der Host übergibt Zustandspfad, Port, lokales UI-Passwort und optional die
   MCP-Konfiguration über einen begrenzten Kontrollkanal.
2. Der Launcher prüft Profil, Versionen und freien Port.
   [runtime-paths.mjs](../src/runtime-paths.mjs) wählt den geprüften Quellbuild
   oder die Referenzdistribution. [launcher.mjs](../src/launcher.mjs) erzeugt
   Umgebung und Aufruf von `openchamber serve --foreground --host 127.0.0.1`.
3. [supervisor.mjs](../src/supervisor.mjs) hält die Profilsperre.
   Unter Windows übernimmt der [native Supervisor](../src/windows-native.mjs)
   den Prozessbesitz mit Job Object; unter Unix wird eine Prozessgruppe verwendet.
4. Bei konfiguriertem Knowledge-Broker startet der Launcher zusätzlich
   [shared-chat-runtime.mjs](../src/shared-chat-runtime.mjs). Dieser Prozess
   betreibt den Broker und startet die OpenChamber-CLI als Kindprozess.
   Der Knowledge-MCP bleibt ein eigener Prozess. Der Wrapper beendet den Broker
   zusammen mit seinem besessenen Chatprozess.
5. OpenChambers [Server-Einstieg][oc-entry] setzt die Module zusammen.
   Seine [Startup-Pipeline][oc-startup] registriert unter anderem
   Ereignisvermittlung, Proxy und statische Dateien. Der HTTP-Listener kann
   bereit sein, während OpenCode noch startet. Der [Lifecycle][oc-lifecycle]
   startet das separate OpenCode-Binary mit `serve --hostname ... --port ...`
   und übernimmt dessen Bereitschaft, Gesundheitsprüfung und Neustart.
6. Die native Anmeldung verwendet `/auth/session`. Nur das resultierende
   HttpOnly-Cookie geht an WebView2. Ein erreichbarer, geschützter Chatlistener
   und ein arbeitsbereiter OpenCode-Server sind unterschiedliche Zustände.

Heute laufen damit mehrere kontrollierte Prozesse. Ob ein Prozess entfallen
kann, ist offen. OpenChambers Server ist Node/Express-Code; unser OpenCode ist
ein separat gebautes Binary. Eine gemeinsame Veröffentlichung bedeutet noch
keine gemeinsame Prozesslaufzeit.

## Aufgaben, die der gemeinsame Backend-Unterbau benötigt

In der Tabelle bezeichnet **Core** den TwinCAT Assistant Core einschließlich
der lokalen Dienstverwaltung. Seine Agentenimplementierung bleibt OpenCode.
**Chat** bezeichnet die bestehenden OpenChamber-Komponenten im WebView.

| Aufgabe | Heutiger Eigentümer und Quellcode | Verwendeter Vertrag / Verhalten | Zielzuordnung |
| --- | --- | --- | --- |
| Start, Bereitschaft, Neustart, Beenden | Unser Launcher und Supervisor; OpenChamber [Lifecycle][oc-lifecycle], [Startup][oc-startup], [Shutdown][oc-shutdown] | Zwei Ebenen von Prozessbesitz; Port- und Profilsperre, Versionen, separate OpenCode-Bereitschaft, Aufräumen eigener Kinder | Gemeinsame Verwaltung im Core; Host signalisiert seine Lebensdauer. Bestehende Aufsicht weiterverwenden. |
| Chatoberfläche ausliefern | OpenChamber [Static-Routes][oc-static] | Gebautes `dist`, gehashte Assets, Cache-Regeln und SPA-Fallback; API-only ist ein separater Modus | Auslieferung durch den lokalen Backend-Dienst; Darstellung und Build-Artefakte gehören zum Chat. |
| Zugang zum lokalen Dienst | Host-Anmeldung; OpenChamber [UI-Auth][oc-ui-auth], [Core-Routes][oc-core-routes], [Request-Security][oc-security] | `/auth/session`, portgebundene Cookies, Zugriffsschutz für API und WebSockets, Origin-Prüfungen | Dienstschutz im Core; native Anmeldung und Cookie-Übergabe im Host. Anbieter-Login bleibt ein eigener Vertrag. |
| OpenCode-API vermitteln | OpenChamber [Proxy][oc-proxy], [Network-Runtime][oc-network] | `/api/*`, interne OpenCode-Zugangsdaten, Header und Verzeichniszuordnung, Timeouts, Fehler, Bereitschaftsprüfung; eigene Routen stehen vor dem allgemeinen Proxy | Core übernimmt den vorhandenen Vermittlungscode. Chat behält zunächst seine bestehenden Aufrufe. |
| Streaming und Wiederverbindung | OpenChamber [Event-Stream][oc-events], [Proxy][oc-proxy], UI-Synchronisation | OpenCode-SSE; browserseitig unter anderem `/api/global/event/ws` und `/api/event/ws`; zusätzlich SSE-Pfade. Replay, Heartbeats, Backpressure und Lückenreparatur gehören zum Vertrag. | Serverteil im Core; Ereignisverarbeitung und Anzeige im Chat. Beide Seiten gemeinsam abnehmen. |
| Nachrichten, Modelle, Abbruch und Werkzeuge | OpenCode [Session-Protokoll][core-session-api], [Integration-Protokoll][core-integration-api], [MCP-Handler][core-mcp]; OpenChamber [SDK-Wrapper][oc-client] | Prompt und synthetischer Kontext werden zugestellt, das Modell je Sitzung gewählt; Abbruch nutzt `/api/session/:sessionID/interrupt`. OpenCode führt Agenten- und Werkzeugablauf aus. | Bestehende OpenCode-Implementierung im Core; Composer, Auswahl und Darstellung im Chat. |
| Anbieter und Anmeldung | OpenCode Integration/Credential APIs; OpenChamber [Provider-Routen][oc-provider-routes], [Auth-Leser][oc-provider-auth], [Modellsuche][oc-model-discovery] | API-Key, OAuth und unterstützte Anmeldeverfahren; Anbieter-Konfiguration und Modellkatalog. Die vollständige Credential-Liste wird vor dem Browser-Proxy ausdrücklich gesperrt. | Anbieterimplementierung und Credentials im Core; vorhandene Login-Oberflächen im Chat. Dienstpasswort und Modellzugang getrennt halten. |
| Verlauf und Sitzungszusätze | OpenCode-Datenbank; OpenChamber [Session-Service][oc-sessions], [Metadaten][oc-metadata], [Session-Folders][oc-folders], [Proxy][oc-proxy] | Native OpenCode-Sitzungen plus OpenChamber-Archivierung, Metadaten und Ordner. Der Proxy ergänzt Archivzustand und noch nicht übernommene Altmetadaten. | Persistenz und Serverdienste im Core; Verlaufsauswahl im Chat. Bestehende IDs und Projektzuordnungen bleiben erhalten. |
| Nachrichtenwarteschlange | OpenChamber [Message-Queue][oc-queue], ChatInput und Queue-Store | `/api/message-queue*`, `message-queue.json`, Reihenfolge, Halten, Bearbeiten und spätere Zustellung mit eingefrorenen Anhängen/Kontext | Serverteil zunächst vollständig im Core weiterverwenden; Queue-Bedienung im Chat. Dies ist eine zusätzliche Warteschlange vor OpenCodes eigener dauerhafter Inbox. |
| Sitzungsstatus und Aufmerksamkeit | OpenChamber [Session-Runtime][oc-session-runtime], [Watcher][oc-watcher], [Notification-Routen][oc-notifications] | Unter anderem `/api/sessions/status`, `/api/sessions/snapshot`, `/api/session-activity`; aus Ereignissen abgeleitete Aktivität und ausstehende Interaktionen | Serverstatus im Core; Anzeige im Chat. Diese Routen sind auch ohne mobile Push-Nachrichten relevant. |
| Einstellungen und Konfiguration | OpenChamber [Settings][oc-settings], [Konfigurationsrouten][oc-config-routes], [Provider-Routen][oc-provider-routes]; unser [Profil](../src/state.mjs) | `settings.json`, `preferences.json`, OpenCode-Konfigurationsschichten; Agenten, Commands, MCPs, Snippets und Plugins. Änderungen haben unterschiedliche Besitzer und können laufend beobachtet werden. | Verwaltung im Core, Einstellungsdarstellung im Chat. Unsere verwaltete TwinCAT-Policy bleibt überprüft; allgemeine Konfigurationsschreiber sind gesondert zu beurteilen. |
| Anhänge und Dateien | Chat [Composer][oc-composer], [Nachrichtenzusammenbau][oc-outgoing], SDK-Wrapper; Web [Files-API][oc-files-api] und [FS-Routen][oc-files] | Native POU-Referenzen liefern eingefrorenen Kontext; weitere Dateiwege verwenden unter anderem `/api/fs/read`, `/api/fs/raw`, `/api/fs/stat` oder `/api/fs/upload`. Nicht jeder Anhang läuft über Upload. | Auswahl und Vormerken im Chat, native Erfassung im Host, benötigter Dateitransport im Core. Datei-Lesen/Upload von allgemeinem Schreiben und Ausführen trennen. |
| TwinCAT-Projekt und POU-Kontext | [Native Projektvorbereitung](../src/native-mcp.mjs), [Workspace-Verwaltung](../src/project-workspace.mjs), [Host-Bindung](../native/IdeHost/ChatBindingStore.cs), Source-Vertrag | Kanonische Solution-/PLC-Identität, Sitzung und historische Workspaces; getrennte Zustände für aktives Objekt, vorgemerkten Snapshot und bestätigte Sendung | Host erfasst IDE-Kontext; Core hält Projekt-/Sitzungszuordnung; Chat führt den echten Composer-Zustand. Kein gleichnamiger Ersatz für die bestehenden Verträge. |
| Engineering-MCP anbinden | OpenCode-MCP-Client; unsere projektbezogene Konfiguration; bestehender `twincat-mcp-mono` | `configuredProjectFiles`, genau ein freigegebenes PLC-Projekt pro verwalteter Konfiguration, aktuell lesend | Eigenständiger Engineering-MCP; Core verwaltet Verbindung und Projektgrenze. Keine eigene parallele Werkzeugimplementierung. |
| Knowledge-MCP anbinden | [Knowledge-Broker](../src/knowledge-broker.mjs), [Stdio-Client](../src/knowledge-client.mjs), bestehender `twincat-knowledge-mcp` | Gemeinsamer Backend-Prozess innerhalb der besessenen Laufzeit, Vorladen, freigegebene Werkzeuge, Suchtreffer/Topic-Inhalte vollständig an das Modell | Eigenständiger Knowledge-MCP; gemeinsamer Broker und Lebenszyklus im Core. Die Korpusvorbereitung bleibt fachlich beim Knowledge-MCP. |

## Zusätzliche Serverfunktionen und offene Auswahl

Die [Feature-Komposition][oc-features], die [Basisrouten][oc-bootstrap] und der
Server-Einstieg registrieren weitere Dienste. Die folgende Einteilung ist ein
Vorschlag zur späteren Auswahl. In Schritt 1 wird nichts davon entfernt.
Eine ausgeblendete Oberfläche sagt nicht aus, dass die zugehörige Serverroute
entfallen ist.

| Funktionsgruppe und vorhandene Module | Abhängigkeit, die vor einer Reduktion zu prüfen ist | Vorschlag, noch zu entscheiden |
| --- | --- | --- |
| Terminal/PTY, Shell und allgemeine FS-Operationen, `terminal/`, `fs/` | Anhangstransport nutzt denselben Files-Bereich wie `write`, `delete`, `rename` und `exec`; gemeinsame Hilfen gezielt aufteilen | Terminal und allgemeine Bearbeitung/Ausführung als Reduktionskandidaten prüfen. Benötigte Anhänge erhalten. |
| Git/Worktrees, `git/`, Repository-Zugangsdaten und Managed SSH | Proxy und Sitzungsanlage kennen Worktree-Bereitschaft; allgemeiner Chat kann verwaltete Chat-Verzeichnisse anlegen | Git-Verwaltung und die zugehörigen Anmeldewege als Reduktionskandidaten prüfen. Projektchats und frühere Verzeichnisse weiter öffnen können. |
| GitHub/GitLab/Linear, Source-Control, Tracked Items und Walkthroughs | Referenzen können vollständigen Issue-/PR-Kontext an den Composer liefern; serverseitige Provider-Caches und Konten | Umfang getrennt von den Modellanbietern entscheiden. Das Entfernen eines GitHub-Panels darf keinen Modellanbieter-Login beschädigen. |
| Tunnel, Relay, Pairing, Remote Clients und isolierte Spaces | URL-Auflösung, WebSockets und Authentifizierung teilen Hilfsmodule mit dem lokalen Chat; unser Launcher setzt Relay bereits auf `off` | Fernzugriff und Spaces als Reduktionskandidaten prüfen; lokalen Dienstschutz und Transport erhalten. |
| Browser-Control, Dev-Server und Dev-Tunnel | Eigene privilegierte Routen und WebSocket-Verbindungen | Für den TwinCAT-Umfang einzeln beurteilen. |
| Spracheingabe, Diktat und TTS, `dictation/`, `tts/` | Composer besitzt bereits Spracheingabe; zusätzliche Modelle, Anbieterzugang und Streaming | Nutzung gemeinsam entscheiden; nicht als bloßen Darstellungsanteil behandeln. |
| Push/APNS und Desktop-Benachrichtigungen, `notifications/` | Dieselbe Route-Komposition liefert Sitzungsstatus, Aufmerksamkeit und Blockierungen | Externe Benachrichtigungen getrennt vom benötigten Statusdienst entscheiden. |
| Goal/Assist/Work, Routing, Small Model, Magic Prompts und geplante Aufgaben | Server beobachtet Sitzungen, verändert Zustellung oder fügt Modellaufrufe hinzu. `permission-auto-accept` verändert Freigabeverhalten. | Jede Zusatzautomatik einzeln entscheiden. OpenCodes Agentenablauf und manuelle Werkzeugfreigaben bleiben eigenständige Kernfunktionen. |
| Agent Memory, Project Context, Session Knowledge und Nachrichtensuche | Eigene Persistenz und Sitzungsmetadaten; OpenChambers `session-knowledge/` ist ein anderes Modul als unser Beckhoff-Knowledge-MCP | Vorhandene Daten und Nutzung aufnehmen, bevor eine Funktion entfällt. |
| Gäste/Erweiterungen, Control-API und verwaltetes OpenChamber-Agentenwerkzeug | Managed Plugins und Konfigurationsschichten werden beim OpenCode-Start eingebunden; gemeinsame Sitzungsdienste werden weiterverwendet | Erweiterungsumfang und zusätzliches OpenChamber-Agentenwerkzeug getrennt vom OpenCode-Werkzeugablauf entscheiden. |
| Quoten, zusätzliche Modellmetadaten, Themes/PWA, Updater und Installer | Modellinformationen und Themes werden vom Chat verwendet; Upstream-Updater können unabhängig vom gemeinsamen Build arbeiten | Darstellung, Datendienste und Update-Besitz getrennt zuordnen. Gemeinsame TwinCAT-Veröffentlichung als Ziel festhalten. |

Die konkreten Registrierungen und importierten Module sind in
[feature-routes-runtime.js][oc-features], [bootstrap-runtime.js][oc-bootstrap],
[startup-pipeline-runtime.js][oc-startup] und [server/index.js][oc-entry]
nachvollziehbar. Diese Tabelle gruppiert Verantwortlichkeiten; sie ist kein
vollständiges Verzeichnis jeder einzelnen HTTP-Route.

## Daten, deren Besitzer und Pfade erhalten bleiben müssen

Die relativen Backend-Pfade in dieser Tabelle beginnen am konfigurierten
Operator-Profil. Native Host-Daten liegen gesondert unter
`%LOCALAPPDATA%\TwinCatAssistant`. Die heutigen Standardpfade sind im
[Umbauplan](TWINCAT_RUNTIME_UI_PLAN.md) und in der
[Runtime-Integration](RUNTIME_INTEGRATION.md) beschrieben.

| Daten | Heutiger Besitzer / Speicher | Konsequenz für die Übernahme |
| --- | --- | --- |
| Gespräch, Nachrichten, dauerhafte Inbox und Anbieter-Credentials | OpenCode, `data/opencode/opencode.db`; bestehender Kanal und bestehende IDs | OpenCode bleibt Speicherbesitzer. Vor Änderungen an Format oder Pfad ausschließlich mit einer getrennten Datenkopie prüfen. |
| Projekt-Workspaces und deren freigegebene MCP-Konfiguration | Unser Backend, `projects/` und verwaltete Konfigurationsdateien | Stabile Projektidentität, historische Verzeichnisse und Konfigurationsjournal erhalten. |
| Native Sitzungsbindung | Host, `%LOCALAPPDATA%\TwinCatAssistant\native-chat\threads\` | Dauerhafte StateRoot-/Session-Bindung sowie kurzlebige Origin-/IDE-Prüfungen erhalten. |
| Lokales Dienstpasswort und Laufzeitkonfiguration | Host, `native-chat/ui-password.bin` per DPAPI und `native-chat.json` unter `%LOCALAPPDATA%\TwinCatAssistant` | Eigenständige Daten außerhalb des Operator-Profils. Kein Umzug als Nebenwirkung einer Modulbenennung. |
| OpenChamber-Einstellungen | `openchamber/settings.json`, `openchamber/preferences.json`; Projektkonfiguration und Themes unter demselben Datenroot | Einstellungen und Migrationen weiterverwenden. Der Name des Produkts ist kein Grund für eine sofortige Pfadänderung. |
| Vorgelagerte Queue | OpenChamber, `openchamber/message-queue.json` | Aufträge, Reihenfolge und eingefrorenen Kontext erhalten; nicht mit OpenCodes Inbox verwechseln. |
| Ordner, Archivierung und Sitzungszusätze | OpenChamber, unter anderem `sessions-directories.json`, Archivzustand und `session.metadata.openchamber`; Legacy-Datei `sessions-metadata.json` | Proxy-Anreicherung und vorhandene Altübernahme beachten; ein reiner Datenbankerhalt allein deckt diese Zustände nicht ab. |
| Ungesendeter Composer und seine Referenzen | Bestehende Chat-Stores, getrennt von bestätigten Nachrichten | Vormerken, Entfernen, Queue und Sendebestätigung weiter über den originalen Composer beobachten. |
| Dokumentationskorpus und Broker | Knowledge-MCP und unsere verwaltete Knowledge-Konfiguration | Bestehenden Korpus und dessen Validierung erhalten; Kaltstart getrennt messen. |

Der feste Laufzeit-Checkout, `RepositoryRoot`, Operator-Profil und Datenbank
werden durch diese Bestandsaufnahme nicht verschoben.

## Verträge, die bei der Übernahme gemeinsam geprüft werden müssen

1. **Dienstzugang und Anbieterzugang getrennt erhalten.** Native Cookie-Anmeldung,
   API-/WebSocket-Schutz und OpenCode-Anmeldeverfahren haben verschiedene
   Eigentümer. Insbesondere darf die serverinterne Credential-Liste nicht
   durch einen vereinfachten Proxy an den Chat durchgereicht werden.
2. **Routenreihenfolge übernehmen.** Eigene Konfigurations- und Statusrouten,
   Credential-Sperre, Body-Verarbeitung und spezielle Ereigniswege stehen vor
   dem generischen OpenCode-Proxy. Die Reihenfolge beeinflusst das Verhalten.
3. **Ereignisvertrag erhalten.** OpenCode-SSE, OpenChamber-WS-Brücke, Replay und
   Chat-Synchronisation sind aufeinander abgestimmt. Ein erfolgreicher Prompt-POST
   allein prüft weder Streaming noch Wiederverbindung oder Abbruch.
4. **Zustellung und Queue unterscheiden.** Das Modell erhält eingefrorenen
   Kontext der jeweiligen Eingabe. Eine später geöffnete POU darf einen
   vorgemerkten Auftrag nicht umdeuten. Mehrdeutige Transportfehler dürfen
   keinen unbeabsichtigten zweiten Auftrag auslösen.
5. **Projektgrenze durchgängig halten.** Verzeichnis-/Location-Zuordnung muss
   für HTTP, Ereignisse, Verlauf und MCPs zusammenpassen. Gleiche Namen reichen
   für POU- und PLC-Identität nicht aus.
6. **Quellarten benennen.** Engineering liest gespeicherte Dateien. Ein
   angehängter Snapshot belegt nur seinen tatsächlichen Inhalt. Die Erfassung
   ungespeicherter Aktionen/Methoden ist ein eigener Host-/Kontextauftrag und
   wird nicht durch den Umzug eines Servers gelöst.
7. **Verfügbarkeitszustände getrennt melden.** HTTP-Listener, Anmeldung,
   Chat-Mount, OpenCode und Knowledge-Korpus können zu unterschiedlichen Zeiten
   bereit sein. Ein Fehler beim Laden des Verlaufs darf nicht als leerer
   Verlauf zu einem neuen Chat führen.

## Empfohlener erster Umsetzungsschritt

Der [gemeinsame Startvertrag](START_CONTRACT.md) konkretisiert inzwischen
Eingaben, Reihenfolge, Bereitschaft, Prozessbesitz und Abnahme auf dem mit dem
fertigen Frontend-Design zusammengeführten Stand. Die folgende Konsolidierung
des Startcodes bleibt der nächste Umsetzungsschritt.

Als Schritt 2 nehmen wir den vorhandenen Startpfad als Grundlage und ordnen ihn
dem Core zu. Zuerst halten wir einen gemeinsamen Startvertrag mit den vorhandenen
Feldern und Zuständen fest: Konfiguration, Profil, geprüfte Runtime-Artefakte,
Ports, Dienstzugang, Prozessbesitz, Bereitschaft und Beenden. Der Host bleibt
Auftraggeber für Start und Ende seiner lokalen Laufzeit.

Dabei bleibt die bisherige OpenChamber-/OpenCode-Prozesskette zunächst nutzbar.
Für die spätere Übernahme wählen wir jeweils ein bereits abgegrenztes Servermodul
und übernehmen seinen Code mitsamt den benötigten Hilfen und Tests. Ein Prozess
wird erst entfernt, wenn seine gesamte benötigte Verantwortung übernommen und
der verbleibende Startpfad praktisch geprüft ist.

Vor der ersten Schnittstellenänderung stimmen Backend und Frontend die
betroffenen Aufrufe ab. Die technische Benennung `assistant-core`,
`assistant-chat` und `assistant-host` folgt als eigener nachvollziehbarer Schritt.

### Bereits vorhandene Prüfgrundlage

Diese Prüfungen sind als Ausgangspunkt im Quellstand vorhanden. Sie wurden für
diese reine Dokumentationsänderung nicht erneut ausgeführt.

- Start/Beenden und nativer Zugang: unsere `tests/native-chat.test.mjs`,
  `tests/launcher.test.mjs`, `tests/supervisor.test.mjs` und
  `scripts/Test-ChatIntegration.ps1`; upstream `opencode/lifecycle*.test.js`,
  `opencode/shutdown-runtime.test.js`, `ui-auth/ui-auth.test.js` und
  `ui-auth/session-cookie.test.js`.
- Vermittlung und Wiederverbindung: upstream `server/opencode-proxy.test.js`,
  `server/sse-routes.test.js`, Tests in `server/lib/event-stream/` und
  `packages/ui/src/sync/`.
- Anbieter und Policy: upstream `opencode/auth.test.js`,
  `opencode/provider-source-route.test.js`, `opencode/core-routes.test.js`
  sowie die originalen OpenCode-Provider-/Integrationstests.
- Queue, Verlauf und Anhänge: upstream `message-queue/runtime.test.js`,
  `openchamber-sessions/session-metadata-store.test.js`, `fs/routes.test.js`
  und `packages/ui/src/components/chat/composer/submit/__tests__/`;
  unsere `tests/embedded-chat.test.mjs`,
  `tests/integration/session-compatibility.test.mjs` und
  `tests/integration/native-mcp.test.mjs`.

Upstream-Testpfade ohne Paketpräfix sind relativ zu
`packages/web/server/lib/`; die beiden Pfade mit `server/` sind relativ zu
`packages/web/`. Für eine spätere Umsetzung werden aus dieser Grundlage die
betroffenen Tests ausgewählt und um konkrete fehlende Abnahmen ergänzt.

## Prüfung dieser Bestandsaufnahme

Die Prüfung bestätigt 50 lokale Dokumentverweise, 33 Upstream-Dateiverweise an
den festgelegten Revisionen und die Existenz der aufgeführten sieben lokalen
sowie 14 konkreten Upstream-Testdateien/-verzeichnisse. Weitere 24 gezielte
Quelltextprüfungen bestätigen die ausgewählten Routen, Dateinamen und
Startaufrufe. `git diff --check` meldet keine Formatfehler.

Der Arbeitsdiff enthält ausschließlich diese Bestandsaufnahme und ihre
Verlinkung in Dokumentationsindex und Umbauplan. Es gibt keine Änderung an
Runtime, UI, MCP-Implementierungen, Konfiguration oder Installation.

[oc-entry]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/index.js
[oc-lifecycle]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/lifecycle.js
[oc-startup]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/startup-pipeline-runtime.js
[oc-shutdown]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/shutdown-runtime.js
[oc-static]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/static-routes-runtime.js
[oc-ui-auth]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/ui-auth/ui-auth.js
[oc-core-routes]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/core-routes.js
[oc-security]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/security/request-security.js
[oc-proxy]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/proxy.js
[oc-network]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/network-runtime.js
[oc-events]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/event-stream/DOCUMENTATION.md
[oc-client]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/ui/src/lib/opencode/client.ts
[oc-provider-routes]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/routes.js
[oc-provider-auth]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/auth.js
[oc-model-discovery]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/model-discovery.js
[oc-sessions]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/openchamber-sessions/routes.js
[oc-metadata]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/openchamber-sessions/session-metadata-store.js
[oc-folders]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/session-folders/routes.js
[oc-queue]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/message-queue/runtime.js
[oc-session-runtime]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/session-runtime.js
[oc-watcher]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/watcher.js
[oc-notifications]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/notifications/routes.js
[oc-settings]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/settings-runtime.js
[oc-config-routes]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/config-entity-routes.js
[oc-composer]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/ui/src/components/chat/ChatInput.tsx
[oc-outgoing]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/ui/src/components/chat/composer/submit/buildOutgoingMessage.ts
[oc-files-api]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/src/api/files.ts
[oc-files]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/fs/routes.js
[oc-features]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/feature-routes-runtime.js
[oc-bootstrap]: https://github.com/openchamber/openchamber/blob/e302062e3be0686986594fddabdafd8a97c129e5/packages/web/server/lib/opencode/bootstrap-runtime.js
[core-session-api]: https://github.com/anomalyco/opencode/blob/0fd7e2829449b052abf0078666669302923d77af/packages/protocol/src/groups/session.ts
[core-integration-api]: https://github.com/anomalyco/opencode/blob/0fd7e2829449b052abf0078666669302923d77af/packages/protocol/src/groups/integration.ts
[core-mcp]: https://github.com/anomalyco/opencode/blob/0fd7e2829449b052abf0078666669302923d77af/packages/server/src/handlers/mcp.ts
