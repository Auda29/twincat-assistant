# Begrenzter Auslieferungsumfang für Core, Chat und Host

Dieser Schritt konkretisiert Punkt 2 anhand von BACKEND_SERVER_INVENTORY.md.
Er beansprucht keine vollständige Entfernung aller Upstream-Zusatzfunktionen.
Maßgeblich ist der source-gebaute Anwendungspfad. Die ausdrückliche Referenz-
distribution bleibt für Entwicklungs- und Kompatibilitätsprüfungen unverändert.
Es werden keine Operator-Daten gelesen, verschoben oder gelöscht.

Der ausgelieferte Client ist ausschließlich der native TwinCAT Assistant Host
mit WebView am lokalen Webserver. Web-Verbindungen zu diesem Source-Server
erhalten dieselben expliziten 404-Antworten. Electron, VS Code, hosted mobile
und Capacitor mobile sind keine Auslieferungsziele dieses Pakets und wurden
nicht gebaut oder abgenommen. Ein Client, der sich trotzdem an diesen Server
bindet, kann die gesperrten HTTP-Funktionen ebenfalls nicht verwenden. Die
eigenständigen Upstream-Distributionen erhalten dieses Overlay nicht.

## Entscheidungen und Umsetzung

| Inventurposition | Entscheidung für diesen Auslieferungsschritt | Umsetzung und verbleibende Grenze |
| --- | --- | --- |
| Start, Auth, Proxy, Ereignisse, Anbieter, Modelle, Nachrichten und Werkzeuge | Behalten | Launcher, Credential-Sperre, SSE/WS-Brücke und Agentenimplementierung unverändert. Keine Namensersetzung in APIs oder Anbieterverträgen. |
| Verlauf, Archive, Ordner, Queue, Sitzungsstatus | Behalten | Bestehende IDs, Datenbanken, Queue-Dateien und Metadaten bleiben bei ihren Besitzern. |
| Einstellungen, Anhänge, Projekt-/POU-Kontext, beide MCPs | Behalten | Lesen, raw/stat/list, Preview und Upload bleiben erreichbar. Native Projektbindung und eingefrorene Referenzen unverändert. |
| Terminal/PTY | Deaktivieren | Server-Einstieg importiert/erzeugt die Terminal-Runtime nicht mehr. Kein Terminal-WS-Handler, keine Shell-Erkennung, kein PTY-Leerlaufintervall. HTTP-Familie zusätzlich vor Proxy gesperrt. Bestehende eingebettete Context-Rail blendet den Einstieg aus. |
| Allgemeine FS-Manipulation/Ausführung | Deaktivieren | Alle Methoden für write/delete/rename/exec/mkdir/clone/reveal mit Unterpfaden liefern 404 vor der ursprünglichen Route. Upload bleibt bewusst erhalten. Dies ersetzt keine globale Dateisystem-Sandbox. |
| Browser-Control und Dev-Server | Deaktivieren | Routenregistrierungen entfernt, HTTP-Familien vor Proxy gesperrt. Eingebettete Browser-/Terminal-Panels bleiben ausgeblendet. Interne Browser-Broker und Scanner-Hilfen bleiben wegen weiterer gemeinsamer Aufrufer im Code. |
| Updater/Installer | Eigenständige Upstream-Updater deaktivieren | OpenCode upgrade/upgrade-status/install-v2 und OpenChamber update-check/update-install liefern 404. Version/health/compatibility bleiben lesbar. Installer und CI werden hier nicht geändert. |
| Paketbestand | Reduzieren | Nach gesperrtem npm-ci entfällt die redundante npm-Distribution @opencode/cli samt Plattformpaketen. Der explizit konfigurierte source-gebaute Core bleibt unter runtime/opencode. Serverdateien *.test.js/*.spec.js entfallen. Neue transitive CLI-Verbraucher führen zum Buildfehler. Lizenzen bleiben erhalten. |
| Git/Worktrees, Repository-Accounts/SSH, GitHub/GitLab/Linear, Tracked Items, Walkthroughs | Später schrittweise zerlegen | Keine Löschung von Konten, historischen Verzeichnissen oder Referenzdaten. Die vorhandene eingebettete Navigation blendet die zugehörigen Panels bereits aus. Serverdienste sind damit nicht als entfernt ausgewiesen. |
| Tunnel/Relay/Pairing/Remote/Spaces | Spätere Trennung | Bestehendes Relay-off und Loopback-Start bleiben. Gemeinsame Auth-/Transportmodule werden nicht entfernt. Dieser Schritt behauptet keine neue allgemeine Remote-Sperre. |
| Diktat/TTS, Push/APNS, Quoten/Modellmetadaten, Themes/PWA | Behalten bis zur gesonderten Trennung | Sprache benötigt eigene Produktentscheidung; Push teilt Statuskomposition, Metadaten/Themes werden im Chat verwendet. Keine pauschale Entfernung ihrer Abhängigkeiten. |
| Goal/Assist/Work, Routing/Small Model/Magic Prompts, Scheduler, Permission-Auto-Accept | Spätere Einzelprüfung | Keine neue Automatik aktiviert. Die bestehende eingebettete Sperre der interaktiven Auto-Accept-Bedienung bleibt. Agentenlauf und manuelle Werkzeugfreigaben bleiben unabhängig davon erhalten. |
| Agent Memory, Project Context, Session Knowledge, Nachrichtensuche | Behalten bis Daten-/Nutzungsprüfung | Kein Löschen vorhandener Persistenz. Session Knowledge ist nicht der Beckhoff-Knowledge-MCP. |
| Guests/Extensions, Control-API, verwaltetes Agentenwerkzeug, Plugins | Später schrittweise trennen | Gemeinsame Konfigurations-/Sitzungsdienste und TwinCAT-Referenztransport erhalten. |

## Benennung und Herkunft

Eigene Modulzuständigkeiten heißen TwinCAT Assistant Core, Chat und Host gemäß
START_CONTRACT.md. Der HTML-Titel, PWA-Titel und der dynamische Fenstertitel des
Source-Chats heißen TwinCAT Assistant Chat. Eigene native Anbieter-Hinweise
verweisen auf TwinCAT Assistant Chat, der native Knowledge-Status auf TwinCAT
Assistant Core. Upstream-Paketnamen, Versionsausgaben, API- und Umgebungsnamen,
Konfigurationsschlüssel, bestehende Datenpfade und Drittanbieter-Lizenzen bleiben
unverändert. Insbesondere sind OpenCode und OpenChamber weiterhin die korrekten
Herkunftsnamen; Funktionen wie createOpenCodeConfig schreiben deren Vertrag und
sind keine umzubenennenden Produktmarken.

## Reproduktion und Nachweisgrenzen

- `node scripts/build-source-runtime.mjs` baut beide gepinnten Quellen wirklich.
  Nach Overlay-Änderungen ist ein neuer Source-Build erforderlich. `--ui-only`
  darf nur den durch Receipt und Binärhash geprüften unveränderten Core wiederverwenden.
- `node --test tests/delivery-scope.test.mjs` prüft ausgeschlossene und erhaltene
  Routen, kodierte Pfade, Paketentfernung, Lizenz-/Produktmodulerhalt und die
  Verweigerung bei neuen CLI-Verbrauchern.
- `npm test` prüft die bestehenden Node-Verträge einschließlich Daten-/Policy-
  und Prozessbesitztests. Vorher WindowsHost mit SDK 8.0.425 bauen.
- `node --test tests/integration/native-chat.test.mjs` prüft den echten Source-
  Listener, GET/POST-Sperren mit Cookie, Anbieterregistrierung mit synthetischem
  Schlüssel, Modellkatalog/Einwilligung und sauberes Ende der Prozesskette.
- `node scripts/build-application-package.mjs <absoluter neuer Pfad>` prüft
  Receipt, MCP-Manifeste und Versionen, baut das vollständige Anwendungspaket.
- `node scripts/test-built-application.mjs <Paketpfad>` prüft geschützten Start,
  Routensperren, beide echten MCPs mit synthetischen Dateien sowie Neustart und
  unveränderte Integrität. Keine Modellanfrage, PLC-/ADS-/Compileraktion.

Lockfiles im Paket dokumentieren die npm-Installationsinputs vor der expliziten
Reduktion. Das abschließende Anwendungsmanifest dokumentiert die wirklich
verbleibenden Dateien. PTY-/Sprachpakete bleiben vorerst in der gesperrten
Upstream-Abhängigkeitsinstallation; ihr pauschales Entfernen wäre keine geprüfte
Entkopplung. Die entfernte npm-Core-Distribution ist dagegen vollständig durch
den expliziten Source-Core ersetzt.

Fixtures belegen keine echten Anbieter-OAuth-Verfahren, Modellantworten,
Streaming-Abnahme am bezahlten Modell oder installierte IDE-Nutzung. Reale
Host-Abnahme, Update/Rollback und diese Anbieterprüfungen bleiben externe Gates
beim Parent. Punkte 5 bis 7 werden nicht begonnen. Ein Paketbau schließt diese
externen Gates nicht durch Umbenennen oder Fixture-Ergebnisse.
## Lokaler Prüflauf am 05.11.2026

- Vollständiger Source-Build von OpenCode 2.0.23 und OpenChamber 2.1.1,
  anschließend geprüfter UI-Rebuild mit der endgültigen Titeländerung.
  Receipt `artifacts/source-runtime/runtime.json`, SHA-256
  `8471c25df8800dacf185e7b4999bbf90094fa0349d2b2a74a757304c2e5fbeb4`.
- Isoliertes SDK 8.0.425; WindowsHost und x86-IdeHost erfolgreich gebaut,
  151 native Policy-/Kontext-/Chat-Regressionen bestanden. Die bestehenden
  VSTHRD010-Warnungen in Knowledge-/Diagnosecode bleiben sichtbar.
- 131 Node-Tests seriell bestanden, ohne übersprungene Tests. Ein vorausgehender
  paralleler Lauf bestand 130 von 131 Tests; die Prozess-Fixture erreichte unter
  Last ihr Bereitschaftsfenster nicht. Der serielle Gesamtlauf löst diesen
  offenen Teststand auf. Frühere fehlende Worktree-Abhängigkeiten und Helper
  wurden eingerichtet und sind keine externen Blocker.
- Vier Integrationsfälle bestanden, ohne Skip: synthetischer Paketbuilder,
  echter nativer Source-Listener mit Routensperren und Anbieter-/Modellkatalog,
  native Eingabegrenze sowie getrennte Session-/Credential-Datenkopie zwischen
  Referenzdistribution und Source-Runtime.
- Zwölf originale Upstream-Tests zu Event-Stream und Startup-Pipeline bestanden.
  UI- und Web-Typechecks bestanden. Syntaxprüfung des Server-Einstiegs bestanden.
- `oxlint` über die drei geänderten Upstream-Module meldet 60 bestehende Fehler.
  Ein Vergleich mit den unveränderten Dateien aus dem gepinnten Git-Stand meldet
  dieselben 60 Fehler. Sie betreffen vorhandene `typeof`-Prüfungen und bedingte
  Objekt-Spreads; die neue Routensperre fügt diese Muster nicht hinzu. Lint wird deshalb nicht als grün
  behauptet. Dead-code-Analyse ausgeführt, vorhandenen Backlog geprüft; der
  Terminal-Parser bleibt wegen des Event-Stream-Imports erforderlich.
- Vollständiges Anwendungspaket unter `artifacts/scope-delivery-application`,
  22.085 inventarisierte Dateien, 690.756.123 Dateibytes. Kein npm-CLI-Paket
  `@opencode/cli` oder dessen Plattformpakete, keine Chat-Server-Testdatei.
  Anwendungsmanifest SHA-256
  `c33a4b3deb4f26b7a014cdb63a87a7cfcd0f496bc2828510b9e016ce3384a583`.
- Vollständiger Paket-Smoke bestanden: geschützter Loopback-Start, echte
  GET/POST-Anmeldung und POST-Routensperren, gespeichertes synthetisches POU über
  den Engineering-MCP gelesen, Knowledge-Korpusstatus über den echten Knowledge-
  MCP abgefragt, Neustart mit erhaltener synthetischer State-Datei und erneute
  vollständige Paketintegrität. Knowledge meldet ohne Korpus korrekt
  `not_available`; daraus folgt keine Abnahme einer produktiven Wissenssuche.
  Der gesamte Lauf dauerte lokal etwa vier Minuten einschließlich mehrfacher
  vollständiger Dateiverifikation. Dies ist keine installierte Host-Startmessung.

Die lokalen Logs sind ignorierte Buildausgaben im Worktree:
`local-node-serial.log`, `local-native-build.log`, `local-scope-integration.log`,
`local-upstream-regressions.log`, `local-web-typecheck.log`,
`local-ui-typecheck.log`, `local-overlay-lint.log`, `local-baseline-lint.log`,
`local-dead-code.log`, `local-package-build.log` und
`local-built-package-smoke.log`. Der Parent erzeugt die endgültigen integrierten
Artefakte neu; dieses lokale Paket wurde weder gepusht noch installiert.
