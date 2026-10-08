# Umsetzungsplan

Alle Implementierungsmeilensteine sind offen. Es werden noch keine Termine oder
Aufwandsschätzungen zugesichert. Die Reihenfolge reduziert zuerst die Risiken
der Shell-Einbettung und des Engineering-Zugriffs.

## M0: Repository und Zielumgebung

**Ergebnis:** Klare Ausgangsbasis ohne bereits behauptete Produktfunktionen.

- [x] Privates Repository vorbereiten und MVP-/Architekturplan ablegen.
- [ ] Konkrete TwinCAT-, TcXaeShell64- und Windows-Version erfassen.
- [ ] Installierte Automation-Interface-, SDK- und WebView2-Komponenten prüfen.
- [ ] Bestehende MCP-Installationen und verfügbaren Dokumentationscorpus erfassen.
- [ ] Geeignetes synthetisches ST-Testprojekt und manuelle Prüffälle festlegen.
- [ ] Upstream-Versionen und Lizenzen der übernommenen Komponenten festhalten.

Reale Kunden-/Maschinenprojekte, Corpus-Daten, Zugangsdaten und Benutzerzustand
bleiben außerhalb des Repositorys.

## M1: Technische Machbarkeit in der Shell

**Ergebnis:** Die zwei größten Integrationsrisiken sind praktisch überprüft.

- [ ] Minimales klassisches C#-Toolwindow im gewählten Shell-Host installieren.
- [ ] Lokale OpenChamber-Weboberfläche mit WebView2 laden.
- [ ] Docking, Tastaturfokus, Größenänderung und Toolwindow-Neustart testen.
- [ ] Über die aktuelle IDE-Instanz das richtige PLC-Projekt und einen
  bestehenden ST-Baustein ermitteln.
- [ ] Deklaration und Implementierung lesen; Verhalten ungespeicherter Editor-
  Änderungen und Dirty-Erkennung untersuchen.
- [ ] Auf einem synthetischen Testprojekt eine ausdrücklich freigegebene
  Änderung über die Bridge ausführen und einen echten Build auswerten.
- [ ] Prüfen, ob die eigene Änderung kontrolliert wiederherstellbar ist.

Die Schreibprobe ist ein isolierter, manuell freigegebener Integrationstest,
noch keine Freigabe für allgemeine Agent-Schreiboperationen. Vorher-/Nachher-
Stände werden gesichert. Keine Verbindung zu einer Produktionsruntime.

Bei fehlender zuverlässiger Dirty-Erkennung oder Konfliktkontrolle bleibt das
Agent-Schreiben deaktiviert. Eine funktionierende Chatansicht allein erfüllt
den vereinbarten MVP nicht. Ein möglicher Ausweichhost wird ausdrücklich
entschieden, nicht stillschweigend als Shell-Kompatibilität dargestellt.

## M2: Projektgebundener Chat und beide MCPs

**Ergebnis:** Ein nutzbarer, zunächst lesender Assistant in der IDE.

- [ ] Lokalen Agent-Unterbau starten oder mit einer kompatiblen laufenden
  Instanz verbinden; keine unbeabsichtigten Doppelstarts.
- [ ] Thread dauerhaft der konkreten Projekt-/IDE-Bindung zuordnen.
- [ ] Beide MCPs verbinden, Toolkataloge und tatsächliche Aufrufe testen.
- [ ] Protokollprofil und `structuredContent` des Knowledge-MCP prüfen.
- [ ] Aktuellen Objektkontext sichtbar anhängen; gespeicherten und Live-Kontext
  unterscheiden.
- [ ] Aktivitätsdarstellung, Quellen, Rückfragen und Abbrechen prüfen.
- [ ] Thread schließen und wieder öffnen; Bindung erneut validieren.
- [ ] Solution-Wechsel und mehrere IDE-Instanzen testen.

## M3: Kontrolliertes ST-Schreiben

**Ergebnis:** Ein Agent kann genau den zugesicherten Schreibumfang verwenden.

- [ ] Live-Engineering-Backend im Monorepo anbinden.
- [ ] Schreibverträge für ein vorhandenes ST-Zielobjekt definieren.
- [ ] Objektbezogenen Diff und Freigabe integrieren.
- [ ] Ausgangsstand unmittelbar vor der Übernahme erneut prüfen.
- [ ] Dirty-Zielobjekte blockieren; keine implizite Speicherung fremder Änderungen.
- [ ] Eigene Vorher-/Nachher-Stände und tatsächlichen Ausgang protokollieren.
- [ ] Kontrollierte Rücknahme mit Folgestandsprüfung implementieren.
- [ ] Allgemeine Datei-/Shellwerkzeuge für das Engineering-Agent-Profil
  einschränken und Umgehungsversuche testen.
- [ ] Abbruch, Timeout und Prozessabbruch ohne automatische Schreibwiederholung
  behandeln.

## M4: Build und vollständige MVP-Abnahme

**Ergebnis:** Chat, MCPs, Schreiben und echte Prüfung bilden einen vollständigen
Engineering-Ablauf.

- [ ] Build-Ausführung an die konkrete Projektbindung koppeln.
- [ ] Build-Abschluss, Timeout und frische Fehler-/Warnungsmeldungen erfassen.
- [ ] Navigierbare Diagnosen und Build-Ausgabe im Thread bereitstellen.
- [ ] Erfolgreichen Build und absichtlich fehlerhaften Build testen.
- [ ] Optional vorhandene Runtime-Diagnose ausschließlich lesend integrieren.
- [ ] Sämtliche [MVP-Abnahmekriterien](MVP.md#abnahmekriterien) prüfen.
- [ ] Installation, Start, Einschränkungen und getestete Versionen dokumentieren.

## Nach dem MVP

Erst nach Abnahme und ausdrücklicher Priorisierung:

- Methoden, Properties, Actions und neue Bausteine bearbeiten.
- Mehrere Objekte als kontrolliertes Change Set behandeln.
- Ungespeicherte Editorstände live integrieren und Konflikte besser unterstützen.
- Semantische Referenz-/Aufrufanalyse verbessern.
- Weitere Visual-Studio-/TwinCAT-Hosts unterstützen.
- HMI- und I/O-Engineering ergänzen.
- Erweiterte Runtime-Funktionen mit separatem Sicherheitskonzept prüfen.

## Test- und Evidenzprinzip

Jeder Machbarkeitsnachweis hält Host-/Upstream-Version, Eingabe, erwartetes und
beobachtetes Ergebnis fest. Synthetische Tests beweisen keinen vollständigen
TwinCAT-Produktumfang. Eine praktische Shell-Prüfung wird nicht durch einen
gewöhnlichen Webbrowser-Test ersetzt.
