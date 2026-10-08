# MVP: Umfang und Abnahme

## Ziel

Ein Agent im angedockten TwinCAT-Toolwindow beantwortet Projektfragen, verwendet
quellenbasierte Dokumentation und bearbeitet auf Auftrag einen bestehenden
ST-Baustein. Änderungen werden geprüft, ausdrücklich freigegeben und anschließend
mit einem echten Build kontrolliert.

Der Kernablauf benötigt keine verbundene PLC. Zunächst wird genau eine konkrete
TcXaeShell64-Version unterstützt. TwinCAT-Build, Shell-Version und installierte
Komponenten werden vor der Implementierung dokumentiert; VS2022 bleibt ein
möglicher zusätzlicher Host, aber keine zugesicherte MVP-Plattform.

## Beispielablauf

1. Benutzer öffnet eine Solution und das Assistant-Toolwindow.
2. Der Thread zeigt seine Projektzuordnung sichtbar an.
3. Benutzer fragt: „Erkläre die Timeout-Behandlung in FB_Station.“
4. Der Agent sucht und liest Projektcode über das Monorepo-MCP und ruft bei Bedarf
   passende Dokumentation über das Knowledge-MCP ab.
5. Benutzer beauftragt: „Ergänze einen konfigurierbaren Timeout.“
6. Der Agent erstellt einen objektbezogenen Diff für Deklaration und
   ST-Implementierung. Die IDE-Bridge prüft Zielobjekt und Ausgangsstand.
7. Benutzer bestätigt genau diese Änderung. Ein veralteter Vorschlag wird nicht
   angewendet, sondern muss neu gelesen und geprüft werden.
8. Die Bridge übernimmt die Änderung und führt den freigegebenen Build-Ablauf aus.
9. Der Thread zeigt tatsächliches Build-Ergebnis, Fehler, Warnungen und die eigene
   Änderung. Fehlgeschlagene Builds bleiben ausdrücklich fehlgeschlagen.

Der Agent darf selbstständig geeignete Lese-Tools auswählen. Dieser Ablauf ist
ein Abnahmebeispiel und keine starre Bedienungsmaske.

## Funktionsumfang

| Bereich | Mindestumfang |
| --- | --- |
| Oberfläche | Angedocktes Toolwindow, bestehende OpenChamber-Threads und Aktivitätsdarstellung |
| Unterhaltung | Streaming, Abbrechen, Rückfragen, Freigaben, dauerhafte Sitzungen |
| Projektbindung | Sichtbare Zuordnung zur konkreten Solution und IDE-Instanz; kein stiller Zielwechsel |
| Kontext | Projektstruktur, Code-Suche und POU-Inhalte über das Monorepo-MCP |
| Dokumentation | Suche und Topic-Abruf mit Quellen über das Knowledge-MCP |
| Schreiben | Deklaration und ST-Implementierung eines bestehenden Bausteins pro Änderungsauftrag |
| Prüfung | Echte Build-Ausführung mit eindeutigem Abschluss und zugehörigen Diagnosen |
| Review | Objektbezogener Diff, ausdrückliche Freigabe und Änderungsprotokoll |
| Rücknahme | Eigene Änderung bei unverändertem Folgestand kontrolliert wiederherstellen |
| Runtime | Optional vorhandene ADS-Diagnose verwenden, ausschließlich lesend |

Ein fortgesetzter Thread muss erneut validieren, ob seine IDE- und Projektbindung
noch verfügbar ist. Die Fortsetzung eines Gesprächs bedeutet nicht automatisch
die Wiederaufnahme eines abgebrochenen Schreibauftrags.

## Nicht im MVP

- Neue POUs, Methoden, Actions, Properties oder Interfaces erstellen.
- Methoden-/Property-Inhalte bearbeiten oder mehrere Objekte in einem Auftrag
  ändern.
- Automatische Zusammenführung ungespeicherter Benutzeränderungen.
- Download, Online Change, Login/Logout, PLC-Start/-Stop, Forcing oder
  Runtime-Schreibzugriffe.
- Safety-Projekte und automatische I/O-Konfiguration.
- HMI-Generierung, HMI-Deployment oder HMI-Preview-Steuerung.
- Eigene Implementierung von Modellrouter, allgemeinem Agent-Harness oder Chat.
- Multi-run, Worktree-Automatisierung, Zeitplanung und Remote-Zugriff als
  zugesicherte Produktfunktionen.
- Unterstützung aller TwinCAT-/Visual-Studio-Versionen.

Vorhandene zusätzliche Upstream-Funktionen können ausgeblendet werden. Eine
ausgeblendete Funktion ist jedoch nicht automatisch technisch gesperrt.

## Abnahmekriterien

- [ ] Toolwindow läuft im festgelegten Shell-Host; Docking, Fokus und erneutes
  Öffnen funktionieren ohne hängende IDE.
- [ ] Thread bleibt nach Schließen und erneutem Öffnen verfügbar.
- [ ] Geöffnete Solution und angesprochenes Projekt werden korrekt und sichtbar
  zugeordnet, auch bei mehreren gleichzeitig laufenden IDE-Instanzen.
- [ ] Projektwechsel invalidiert alte Schreibfreigaben und wird nicht als
  stiller Wechsel des Zielprojekts behandelt.
- [ ] Beide MCPs verbinden sich mit den festgelegten Versionen.
- [ ] Der tatsächliche Dokumentationsinhalt aus `structuredContent` erreicht das
  Modell; Quellen sind in der Antwort nachvollziehbar.
- [ ] Der Agent findet und erklärt relevante Stellen im Testprojekt, ohne einen
  vollständigen semantischen Aufrufgraphen vorzutäuschen.
- [ ] Ein erlaubtes ST-Zielobjekt wird nach Freigabe korrekt geändert.
- [ ] Ungespeicherte Zielobjekte und Änderungen seit dem Lesen blockieren die
  Übernahme zuverlässig.
- [ ] Ein echter Build liefert den korrekten Status und frische Diagnosen;
  Erfolg und Fehler werden nicht aus alten IDE-Meldungen abgeleitet.
- [ ] Ein bewusst eingebauter Compilerfehler erscheint als fehlgeschlagener Build
  mit nachvollziehbarer Diagnose.
- [ ] Eigene Änderung lässt sich bei unverändertem Folgestand zurücknehmen;
  neuere Benutzeränderungen werden dabei nicht überschrieben.
- [ ] Abbruch, Timeout oder Verbindungsabbruch führen nicht zu automatischem
  Wiederholen einer möglicherweise bereits ausgeführten Änderung.
- [ ] Direkte Datei- und Shellwerkzeuge umgehen die Engineering-Freigaben nicht.
- [ ] Kein MVP-Ablauf aktiviert, lädt oder verändert eine Runtime.

## Fertigstellung

Alle Kernkriterien werden auf einem synthetischen Testprojekt und danach auf
einem geeigneten realen Projekt überprüft. Ergebnisse halten exakte Versionen,
Testfall und beobachtetes Verhalten fest. Ein erfolgreicher Build beweist
Kompilierbarkeit, nicht die funktionale Korrektheit oder Maschinensicherheit.
