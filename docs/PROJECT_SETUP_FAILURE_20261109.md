# Fehlgeschlagene Projekteinrichtung, 09.11.2026

Der lokale Preview-3-Bericht zeigte einen verbundenen und angemeldeten Chat,
aber keine eingerichteten Projektwerkzeuge. Danach blieb die Ladeansicht stehen
oder meldete eine unterbrochene Chatverbindung.

## Lokaler Befund und Wiederherstellung

Die read-only Prüfung ergab:

- Beide installierten Anwendungspakete, Preview 2 und Preview 3, stimmen mit
  ihren vollständigen Manifesten überein. Beide enthalten 22.088 Dateien.
- Die neun vorhandenen Projektkonfigurationen entsprechen bekannten Policies.
  Ihre Engineering-Konfigurationen sind exakt lesend und ihre referenzierten
  Lösungs- und Projektdateien vorhanden. Es wurden keine PLC-Quellen gelesen.
- Die Projektkonfigurationen verweisen noch auf Preview 2. Der allgemeine
  Chat verwendet bereits Preview 3.
- Zwei leere Projekt-Lockdateien lagen seit 13:52 und 14:47 Uhr vor. Sie
  verhindern mit `open(..., 'wx')` die nächste Einrichtung. Es gibt keine
  automatische Übernahme vermeintlich veralteter Sperren.

Nach der Prüfung, dass keine TcXaeShell, Visual-Studio-IDE oder installierte
Assistant-Runtime lief, wurden genau diese zwei regulären, leeren Sperrdateien
exklusiv geöffnet und anschließend in eine eigene lokale Sicherung verschoben.
Die Sicherung wurde per SHA-256 geprüft. Konfigurationen, Journale, Chats,
Anmeldungen, PLC-Dateien und die installierten Binärdateien wurden nicht geändert.
Der genaue Sicherungspfad steht nur im lokalen Wiederherstellungsprotokoll unter
`local-data/project-lock-recovery-*/recovery.json`.

Der ursprüngliche Abbruch, der die Sperren hinterlassen hat, ist nicht bewiesen.
Die native Einrichtung beendete ihren Node-Prozess jedoch bisher hart nach
30 Sekunden. Ein solcher Abbruch innerhalb des Projektlocks kann dessen
`finally`-Freigabe verhindern. Der isolierte Übernahmelauf mit den tatsächlichen
installierten Paketen benötigte 21,4 Sekunden; die separaten vollständigen
Inventarprüfungen benötigten 15,7 und 10,3 Sekunden. Unter I/O-Last ist die alte
Grenze knapp. Diese Messungen beweisen keinen konkreten Timeout im Operatorlauf.

## Korrektur im Quellstand

- Eine vorhandene Projektsperre liefert einen festen Fehlercode
  `project_locked`. Der Kontrollkanal enthält weiterhin keine Pfade, Inhalte,
  Zugangsdaten oder Exceptiontexte. Unbekannte Fehler bleiben `setup_failed`.
- Der Host unterscheidet Sperre, Timeout und allgemeine Einrichtungsfehler.
  Die Einrichtungsfrist ist weiterhin begrenzt, jetzt auf 120 Sekunden, und
  berücksichtigt die vollständige Prüfung beider Paketversionen.
- Während dieser Einrichtung pausiert der separate WebView-Bereitschaftstimer.
  Ein Einrichtungsfehler beendet die Ladeanimation, lässt native Aktionen
  erreichbar und zeigt eine bleibende Meldung mit erneutem Versuch.
- Die Diagnose zeigt den Einrichtungsfehler ausdrücklich bei Projektwerkzeugen
  und Wissenssuche. Ein fertiger Dokumentationsindex wird nicht als bestehende
  MCP-Verbindung gewertet. Der Chat-Verbindungsstatus bleibt davon getrennt.
- Die automatische Sperrenlöschung bleibt ausgeschlossen. Es gibt keine
  Policy-Erweiterung und keine automatische Reparatur unbekannter Konfigurationen.

## Nachweise und Grenzen

- Tatsächliche installierte Preview-2- und Preview-3-Node-Komponenten richteten
  ein synthetisches Projekt ein und übernahmen dessen Zustand erfolgreich.
  Dabei wurde kein Backend, Provider oder MCP-Server gestartet.
- 24 bestehende MCP-/Prompt-/Profil-Recovery-Tests bestanden. Der neue Test
  bestand nach einer Korrektur seines CLI-Dateipfads separat: Eine aktive
  Projektsperre bleibt erhalten, die Projektpolicy bleibt bytegleich, und der
  echte CLI-Prozess gibt ausschließlich den festen Fehlercode zurück.
- x86-Host-Build und 151 native Regressionen bestanden. Der Build verwendete
  den vorhandenen EmbeddedChat-Eingang aus dem vorherigen Branchbuild; es wird
  damit kein neuer Source-Runtime-, Paket- oder Releasebau behauptet.
- `scripts/Test-ProjectSetupFailure.ps1` prüft das tatsächliche gebaute WPF-Control
  außerhalb der IDE in einem frischen privaten Testprofil. Alle neun Checks
  bestanden: sichtbarer Fehler statt Ladeanimation, erreichbare Aktionen,
  Fehlerdiagnose und kein falscher Verbindungsabbruch bei gesperrter oder noch
  laufender Projekteinrichtung. Keine IDE-/Backendinstanz wurde gestartet.

Der Quellfix wurde nicht installiert. Der nächste echte Projektstart nach der
lokalen Sperrenwiederherstellung muss vom Bediener geprüft werden. Für einen
Release sind Adapter, Host und Anwendungspaket koordiniert neu zu bauen. Bei
Integration der Sprach-PR sind die neuen Hosttexte in deren DE/EN-Katalog
aufzunehmen.
