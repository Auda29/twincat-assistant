# Produktbereinigung vom 09.11.2026

Die hier dokumentierte Umsetzung ist seit dem 10.11.2026 in
[Beta 1](releases/native-v0.2.0-beta.1.md) enthalten. Die folgenden Paketnummern
und Befunde beschreiben den damaligen Prüfstand. Der Beta-Release besitzt einen
eigenen vollständigen Paketbau und `BUILD_PROVENANCE.json`.

Dieser Stand gehört zum Branch `feat/lean-product-runtime`. Er baut auf dem
veröffentlichten Preview 3 auf, verändert aber weder dessen Release-Artefakte
noch die lokale Installation. Die Verifikationspakete tragen noch die bestehende
Versionsnummer; sie sind kein neuer Preview-3-Download und kein neuer Release.
Vor einer Veröffentlichung ist eine eigene Version und Paketabnahme nötig.

## Tatsächlicher Rückbau

| Teil | Änderung |
| --- | --- |
| Serverstart | Browser-Broker, Dev-Server-Scanner, Terminal/Diktat, Remote-/Relay-/Spaces-Komposition und ihre Start-/Stopdienste entfallen aus der Produktzusammenstellung. Der Listener verlangt Loopback. |
| Automatik | Assist-, Goal-, Work-, Routing-, Scheduler-, Dispatch- und Auto-Accept-Dienste werden nicht mehr erzeugt oder gestartet. Neue Goal-/Worktree-Anforderungen im gemeinsamen Sitzungsdienst werden vor Nebenwirkungen verweigert. |
| Routen | Git-/Repository-Accounts, Linear, Walkthroughs, Tracked Items, Gäste/Extensions und Control-Routen werden nicht registriert. Die bestehenden FS-Schreib-/Ausführungsrouten sind aus dem Routenmodul entfernt. Explizite Sperren verhindern den Durchfall zur Core-Proxyroute. |
| Chat-Ereignisse | `/api/openchamber/events` bleibt als eigener authentifizierter SSE-Kanal erhalten. Metadatenübertragung benötigt dadurch keinen Scheduler. Ereignisnamen und Verbindungs-IDs bleiben kompatibel. |
| CLI | Nur Versionsprobe, Hilfe und passwortgeschütztes `serve --foreground --host 127.0.0.1 --port …`. Keine Daemon-, Service-, Tunnel- oder Updater-Befehle. Die Versionsprobe bleibt der gepinnte Abhängigkeitsvertrag. |
| Client-Start | Globale Push-, Browser-Provider-, Memory- und Routing-Synchronisation, Repository-/Linear-Accountabfrage und Terminal-Keepalive entfallen aus App/MainLayout. Chat-/Statusereignisse und Enterprise-Policy bleiben. |
| Einstellungen | Produktnavigation enthält Anbieter, Darstellung, Chat, Sitzungen/Aufbewahrung, Tastenkürzel und Über. Andere persistierte Einstellungs-Slugs fallen auf die Produktnavigation zurück. Native Projekt-/MCP-Einrichtung bleibt beim Host. |
| Eingabebereich | Datei-Anhänge, Modellwahl, Senden, Queue, Stop und Fokus bleiben. Mikrofon, Goal und automatische Berechtigungsfreigabe entfallen aus dem Footer. Der normale Editor und seine Anhänge bleiben erhalten. |
| Anwendungspaket | Eine konservative Importprüfung entfernt nur nicht mehr erreichbare Dateien der benannten Funktionsfamilien und nicht mehr benötigte Pakete. Gemeinsame Verbraucher verhindern eine Paketlöschung. Unbekannte berechnete Imports halten ihre lokalen Module vorsorglich im Paket. |

Der erste vollständige Paketbau entfernte 241 Server-/CLI-Dateien und 24 Pakete,
darunter `node-pty`, `bun-pty`, `sherpa-onnx-node` samt Windows-Laufzeit,
`web-push`, `@clack/prompts` und `qrcode-terminal` mit exklusiven transitiven
Abhängigkeiten. Dieser erste Bau lag vor der letzten SSE-/Client-Korrektur und
ist nur ein Größen-/Entfernungsnachweis. Der endgültige Paketstart ist separat
nachzuweisen.

## Produktname und Herkunft

Die Sprachdateien verwenden TwinCAT Assistant Core bzw. Chat für unsere
Produktmodule. Auch die zusätzlichen Feature-Wörterbücher sind berücksichtigt.
Das betrifft 2.731 ursprüngliche und 686 weitere Texte; bekannte externe
Angebotsnamen OpenCode Go, Zen und Console bleiben erhalten. Schlüssel und
Platzhalter werden nicht umbenannt. Das Produkt erhält ein TC-Zeichen und ein
passendes SVG-Favicon. Ladebildschirm und Fenstertitel nennen TwinCAT Assistant.

Die Über-Seite liest die tatsächliche Anwendungsversion über den geschützten
Produktendpunkt. Sie zeigt keine OpenChamber-Version als Produktversion und
bietet keine Upstream-Selbstaktualisierung an. OpenCode und OpenChamber bleiben
als Herkunft mit ihren Projektlinks sichtbar. Lizenzen und Notices bleiben
erhalten. Technische Paket-, API-, Ereignis-, Umgebungs- und Persistenznamen
bleiben kompatibel; diese Namen sind weiterhin Teil der Verträge.

Ganze ersetzte Module liegen lesbar unter `upstream/overlays/openchamber/`.
`scripts/source-overlay.mjs` prüft vor dem Ersetzen den ursprünglichen
normalisierten SHA-256. Kleine Eingriffe bleiben exakte Einzelersetzungen.
Build-Receipt und Paketbauer binden auch die Ersatzdateien. Unbekannte
Quelländerungen werden weiterhin abgewiesen.

## Erhaltener Zustand und Grenzen

Es werden keine vorhandenen Chats, Anmeldungen, Konten, Projektbindungen oder
historischen Feature-Verzeichnisse gelöscht oder eingelesen. Das Entfernen
einer Laufzeitfunktion migriert oder bereinigt deren alte Benutzerdaten nicht.
Die gesperrten npm-Lockfiles dokumentieren weiter die Installationsinputs;
das abschließende Manifest beschreibt den tatsächlich ausgelieferten Bestand.

Der unveränderte Core ist weiterhin ein vollständiges gepinntes Engine-Binary.
Seine internen Drittanbieterbezeichnungen oder eingebauten CLI-Teile lassen sich
nicht durch Löschen von Chat-Dateien entfernen. Gemeinsame Sitzungs-, Git-,
Memory-/Knowledge- und Konfigurationshilfen bleiben teilweise erreichbar,
weil normale Gespräche, Metadaten und alte Zustände dieselben Module verwenden.
Das interne Session Knowledge ist nicht der TwinCAT-Knowledge-MCP.

Der generische Web-Client enthält noch alte Panel-/Starter-/Statistik-Einstiege
und einzelne Abfragen für entfernte Funktionen. Der native Adapter verbirgt
bereits die allgemeinen Panels, Sidebar und Starter. Der Browser-Prüflauf
ohne Adapter meldet deshalb erwartete 404 für entfernte Funktionen und ist
keine fehlerfreie Abnahme des generischen Web-Clients. Diese Client-Reste,
weitere gemeinsame Hilfen sowie ältere PWA-Rasterbilder benötigen einen
weiteren begrenzten Rückbau. Electron, VS Code und Mobile sind keine
Auslieferungsziele; ihre Quellbäume werden nicht zu TwinCAT-Produkten erklärt.

## Prüfungen

- Echter gepinnter Chat/UI-Build; unveränderter Core per Hash wiederverwendet.
- 26 gezielte Node-/Integrationsprüfungen einschließlich tatsächlichem
  geschütztem Start, Anbieteranmeldung mit künstlichem Schlüssel, Modellkatalog,
  Profil-Recovery und Paketbauer-Fixture ohne Skip bestanden.
- Nach dem Herauslösen des SSE-Kanals: echter Source-Start, SSE-Eröffnungsframe,
  Goal-/Worktree-Verweigerung und kontrolliertes Ende der Prozesskette bestanden.
- UI-/Web-Typechecks und gezielter UI-Lint; der normale Composer bleibt erhalten.
- Der Lint der acht betroffenen UI-/Kompositionsdateien meldet keine Fehler.
  Die fünf bestehenden `setLinkedReferences`-Hookwarnungen im Composer bleiben;
  die exakt rekonstruierte Ausgangsfassung meldet dieselben fünf Warnungen.
- Der projektweite Knip-Lauf wurde ausgeführt. Er meldet im gesamten
  Upstream-Monorepo 74 ungenutzte Dateien, 297 Exports, 214 exportierte Typen und
  drei Konfigurationshinweise. Das ist kein bereinigter Nullbefund; Dateien
  anderer Clients und Tests wurden deshalb nicht pauschal gelöscht.
- Isolierter Browser: Login, Eingabebereich, Produktnavigation und Über-Seite mit
  echter Anwendungsversion. Keine Modellanfrage und keine Anbieter-OAuth-Abnahme.

Lokale Prüfdateien liegen ignoriert unter `artifacts/` und
`output/playwright/lean-product/`. Das Browserprofil und alle verwendeten
Zustandsverzeichnisse sind synthetisch. Eine installierte IDE, PLC-Aktion,
Modellantwort, echter Update-/Rollback-Lauf oder neue CI-Auslieferung wird
hieraus nicht abgeleitet. Die Punkte 5–7 sind unverändert offen.

## Vollständiges Verifikationspaket

`artifacts/lean-product-verified-20261109-application` enthält 21.537 Dateien mit
630.292.500 Dateibytes. Das veröffentlichte Preview-3-Anwendungspaket enthält
22.088 Dateien mit 690.823.921 Dateibytes. Die Reduktion beträgt 551 Dateien und
60.531.421 Bytes. Das Anwendungsmanifest des Verifikationspakets hat SHA-256
`b0603c8f7f75984f554652db03e199370444e0796b013b9282b8cef58369f0da`.

Der echte Paket-Smoke bestand geschützten Start, Produktversion, Chat-Metadaten-
SSE, ausgeschlossene Routen, beide echten MCP-Probes mit synthetischen Dateien,
Neustart, Zustandserhalt und abschließende unveränderte Paketintegrität. Es gab
keine Provideranfrage oder PLC-Aktion.

Nach diesem Paketbau wurde ausschließlich die responsive Anordnung der neuen
Einstellungsansicht angepasst: schmale Desktop-WebViews verwenden ebenfalls die
kompakte Navigation. Dafür wurde der Source-UI erneut gebaut und geprüft. Das
obige Paket belegt den Stand vor dieser letzten UI-Anordnung. Vor einem Release
muss das vollständige Paket aus dem endgültigen Stand neu gebaut werden; dieser
Nachweis behauptet keine abweichende oder nachträglich umgeschriebene Manifest-
Identität.
