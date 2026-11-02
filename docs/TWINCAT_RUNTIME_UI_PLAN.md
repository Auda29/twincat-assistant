# OpenCode und OpenChamber für den TwinCAT Assistant zuschneiden

Stand: 29.10.2026. Arbeitsbranch: `feat/twincat-runtime-ui`.
Ausgangspunkt ist Release `native-v0.1.9`, Commit `d9d62d0`.

Dieses Dokument beschreibt den gemeinsam besprochenen Umbau. Die ersten beiden
Funktionsgruppen werden auf diesem Branch mit festgelegten Quellständen und
einer gezielten TwinCAT-Anbindung umgesetzt. Den Stand beschreibt
[Runtime integration](RUNTIME_INTEGRATION.md). Die Auswahl entfallender Module
bleibt ein späterer gemeinsamer Schritt.

Die nächste Umbauphase auf `backend` beginnt mit der
[Bestandsaufnahme der Serveraufgaben](BACKEND_SERVER_INVENTORY.md). Sie erfasst
den auf `main` zusammengeführten Stand `de4eef8`, trennt heutige Serveraufgaben
von der Zielzuordnung zu Core, Chat, Host und beiden MCPs und hält offene
Reduktionsentscheidungen fest. Diese Bestandsaufnahme ändert noch kein
Laufzeitverhalten.

Nach Übernahme des fertigen Frontend-Designs hält der
[gemeinsame Startvertrag](START_CONTRACT.md) die vorhandenen Eingänge und
Lebenszyklen sowie die Anforderungen an die nächste Konsolidierung fest.
Themes und Commands müssen vor dem ersten Chat-Bootstrap vorbereitet sein.

## Vereinbarte Richtung

Wir übernehmen die für den TwinCAT Assistant relevanten Funktionen aus OpenCode
und OpenChamber mit ihrer bestehenden Implementierung und ihrem bisherigen
Verhalten. Anschließend schneiden wir Unterbau und Chatoberfläche auf die
Einbettung in TwinCAT zu. Anpassungen erfolgen dort, wo Projektkontext,
IDE-Lebenszyklus oder die gewünschte Bedienung sie erfordern.

Die bewährte Agentenlogik, Modellkommunikation und Chatdarstellung bilden die
Basis. Unser eigener Code verbindet diese Funktionen mit TwinCAT und definiert
verlässliche Zustände für Projekte, Gespräche und Anhänge.

Das Ergebnis soll sich als zusammenhängender TwinCAT Assistant bedienen lassen.
Welche allgemeinen Funktionen dafür entfallen können, entscheiden wir anhand
des tatsächlichen Quellcodes und seiner Abhängigkeiten.

## Ausgangslage

Aktuell verwenden wir die festgelegten npm-Distributionen OpenCode `2.0.23` und
OpenChamber `2.1.1`. Der native C#-Host stellt das Toolfenster, WebView2 und den
lokalen IDE-Kontext bereit. Beide MCPs sind angebunden; der Engineering-MCP
liest gespeicherte Projektdateien und der Knowledge-MCP die lokale Dokumentation.

Die eingebettete Oberfläche wird über einen versionsgebundenen Adapter an
interne OpenChamber-Stores angepasst. Dabei werden ausgelieferte JavaScript-
Dateien anhand ihrer Hashes geprüft. Die Zuordnung zu diesen internen Exporten
ist im [Integrationsvertrag](CHAT_INTEGRATION.md) dokumentiert.

Der geplante Umbau führt zu eigenen, nachvollziehbaren Quellcode-Builds beider
Projekte. Die heutige Installation und der bestehende Datenbestand bleiben der
Vergleichsstand für die schrittweise Umstellung.

## Funktionen, die wir übernehmen wollen

### OpenCode-Unterbau

- Bestehende Anbieteranbindungen, Anmeldeverfahren und Modellverwaltung.
- Nachrichtenverarbeitung, Antwort-Streaming und Abbruch laufender Anfragen.
- Agentenablauf mit Werkzeugauswahl, Werkzeugaufrufen und Verarbeitung der
  Ergebnisse in weiteren Modellschritten.
- Fehlerbehandlung, Wiederholungen und Verwaltung langer Gesprächskontexte,
  soweit diese Funktionen in der ausgewählten Quellversion vorhanden sind.
- MCP-Unterstützung mit den jeweils passenden Protokollprofilen.
- Gesprächsspeicherung und Verlauf einschließlich der dafür benötigten
  Datenmodelle und Datenbankmechanismen.
- Bestehende Berechtigungsmechanismen als Grundlage unserer überprüften
  TwinCAT-Werkzeugfreigaben.

Die Umstellung darf vorhandene Anmeldemöglichkeiten oder Modellfunktionen
nicht stillschweigend einschränken. Die konkrete Übernahme wird für die aktuell
verwendeten Anbieter praktisch geprüft.

### OpenChamber-Oberfläche

- Chatdarstellung und Eingabefeld mit dem vom Nutzer akzeptierten Verhalten.
- Anzeige gestreamter Antworten, Abbruch und verständliche Fehlerzustände.
- Gesprächsverlauf und Wechsel zwischen Unterhaltungen.
- Modell- und Anbieterauswahl sowie die zugehörige Anmeldung.
- Anhänge und Referenzen einschließlich Hinzufügen, Entfernen und Absenden.
- Markdown, Codeblöcke, Kopieraktionen, Werkzeuganzeigen und Quellenangaben.
- Darstellung und Einstellungen, die für die verbleibenden Funktionen benötigt
  werden. Weitere vorhandene Funktionen prüfen wir einzeln.

Die vorhandenen Chat-Komponenten bleiben die funktionale Grundlage. Ihre
Einbettung, Navigation und Verbindung zum nativen Kontext werden angepasst.

## TwinCAT-spezifische Anpassungen

### Projekt und Gespräch dauerhaft verbinden

Ein Gespräch gehört zu einem eindeutig identifizierten PLC-Projekt. Die
dauerhafte Projektzuordnung wird von der kurzlebigen IDE-Instanz und der
jeweiligen Laufzeitkonfiguration getrennt. Ein IDE-Neustart oder Modellwechsel
darf allein keinen neuen Projektchat erzwingen.

Beim Öffnen eines Projekts wird der vorhandene Verlauf vollständig geladen und
der passende Chat wieder ausgewählt. Ein noch laufender Ladevorgang muss als
solcher behandelt werden. Für neue Gespräche bleibt eine explizite Aktion.
Gleichnamige Projekte, mehrere PLCs in einer Solution und mehrere IDE-Instanzen
werden getrennt geprüft. Die Regeln für verschobene oder umbenannte Projekte
legen wir vor einer Datenmigration fest.

Am 30.10.2026 ist die Navigation dafür festgelegt worden: Mehrere Chats pro
PLC-Projekt sind erlaubt. Die Uhr öffnet eine kompakte Liste mit Titel und
letzter Aktivität ausschließlich für dieses Projekt; Plus beginnt einen weiteren
Projektchat. Beim erneuten Öffnen wird der zuletzt ausgewählte Chat
wiederhergestellt. Ein Baustein- oder Memberwechsel bleibt im aktuellen Chat.
Allgemeine Chats bleiben getrennt über das Menü erreichbar. Projektwerkzeuge und
Knowledge-Verbindung werden gemeinsam genutzt, bestehende Chats bleiben erhalten.
Diese Navigation ist seit 02.11.2026 auf `feat/project-chat-navigation`
implementiert: Die kompakte Auswahl ersetzt die allgemeine Seitenleiste und
ergänzt Titelsuche, Umbenennen, Archivieren und Wiederherstellen. Die letzte
Auswahl bleibt über einen Laufzeit-Neustart erhalten. Die
[genauen Regeln und Abnahmegrenzen](RUNTIME_INTEGRATION.md#agreed-project-chat-navigation-2026-10-30)
sind festgehalten. Der lokale Stand `dfb50b1` wurde am 02.11.2026 installiert.
Der Nutzer hat Projektchat-Navigation, getrennte Entwürfe/Anhänge,
Wiederherstellung nach IDE-Neustart und den Überdeckungsfix für Properties/Toolbox
manuell abgenommen. Ein Timeout des kombinierten Theme-Tests bleibt separat
offen. Ältere Chats tragen noch den zuvor fest vergebenen Projektnamen; neue
Chats lassen die ursprüngliche automatische Titelgenerierung zu.

### Kontext und Anhänge eindeutig behandeln

Der lokale aktive Baustein, Projektmetadaten, ein vorgemerkter Snapshot und der
mit einer Nachricht tatsächlich übertragene Inhalt erhalten unterscheidbare
Zustände. Die Anzeige muss diesen tatsächlichen Zustand wiedergeben.

Wir legen gemeinsam fest, welche Metadaten eine Frage wie "dieser Baustein"
eindeutig machen sollen. Das lokale Folgen des Editors allein erteilt keine
Freigabe zur Übertragung von Quellcode. Gespeicherte Dateien, Automation-
Interface-Snapshots und ein gegebenenfalls künftig verifizierter Editorpuffer
bleiben als unterschiedliche Quellen erkennbar.

### Stabile Verbindung zwischen Oberfläche, Laufzeit und IDE

Wir schaffen eine kleine, ausdrücklich unterstützte Schnittstelle im übernommenen
Quellcode. Darüber werden Projektzuordnung, Kontext, Anhangszustände und Verlauf
angebunden. Die heutige Kopplung an minifizierte Store-Exporte kann dadurch
schrittweise entfallen.

OpenChamber und OpenCode werden dabei gemeinsam betrachtet. Änderungen an
Routen, Ereignissen oder Datenmodellen müssen zu ihren jeweiligen Verbrauchern
passen. Die native IDE-Anbindung und die bestehenden MCPs werden weiterverwendet.

### Werkzeuge und Prozesse gezielt bereitstellen

Knowledge bleibt innerhalb der lokalen Laufzeit gemeinsam nutzbar. Die
Engineering-Verbindung bleibt auf das konkrete PLC-Projekt beschränkt.
Werkzeugergebnisse müssen Suchtreffer, IDs, Artikel und Quellen vollständig
bis zum Modell transportieren.

Die bestehende Prozessaufsicht und das Beenden eigener Kindprozesse beim
Schließen oder Absturz bleiben erhalten. Der langsame erste Knowledge-Start
wird getrennt gemessen und untersucht, da die vollständige Korpusprüfung im
Knowledge-MCP liegt. Der Fork allein beseitigt diesen Aufwand nicht.
Die weiterhin sehr lange Verbindung trotz vorbereitetem Index ist als
[offene Untersuchung mit Video-Beobachtung und Abnahmekriterien](KNOWLEDGE_SETUP.md#open-investigation-slow-knowledge-connection)
festgehalten. Sie bleibt ein eigener Arbeitspunkt neben der UI-Bereinigung.

## Kandidaten für die Reduktion

Diese Auswahl ist ein Vorschlag für die gemeinsame Bestandsaufnahme:

- Allgemeine Terminal- und Shell-Funktionen.
- Git-, Worktree- und allgemeine Repository-Verwaltung.
- Generische Dateibearbeitung außerhalb der vorgesehenen TwinCAT-Verträge.
- Oberflächen, Hintergrunddienste und Verbindungsarten, die unser eingebetteter
  lokaler Assistant nicht benötigt.
- Erweiterungen und weitere Funktionen, deren Nutzen für den Assistant wir
  noch nicht bestätigt haben.

Vor dem Entfernen prüfen wir die Abhängigkeiten zu den übernommenen Funktionen.
Ein ausgeblendeter Menüpunkt reicht nicht aus, wenn seine Backend-Route oder
sein Werkzeug weiterhin unerwünscht erreichbar ist. Benötigte gemeinsame
Bibliotheken und Datenstrukturen bleiben erhalten.

## Quellcode, Daten und Wartung

Für beide Projekte wird die genaue Zuordnung von Paketversion, Quellrevision
und Build festgehalten. Zunächst muss ein unveränderter Quellcode-Build als
Referenz funktionieren. Danach werden unsere Änderungen in kleinen,
nachvollziehbaren Commits geführt.

Wie wir die Upstream-Quellen verwalten, entscheiden wir nach dieser Prüfung.
Separate Fork-Repositories, Submodule oder eine Übernahme in dieses Repository
sind noch nicht festgelegt. Lizenzen, Drittanbieterhinweise und die bestehenden
Verteilungsgrenzen werden bei dieser Entscheidung berücksichtigt.

Aktuell liegen die Gespräche in
`%LOCALAPPDATA%\TwinCatAssistant\operator-state\data\opencode\opencode.db`.
Die native Projektzuordnung liegt unter
`%LOCALAPPDATA%\TwinCatAssistant\native-chat\threads\`.
Vor Änderungen an Speicherformat oder Zuordnung wird eine Übernahme mit einer
getrennten Datenkopie geprüft. Bestehende Gespräche und Anmeldedaten dürfen durch
den Umbau nicht verloren gehen. Zugangsdaten, Projektcode, lokale Datenbanken
und Dokumentationskorpora werden nicht in die Quellcode-Repositories übernommen.

## Ablauf unserer gemeinsamen Arbeit

1. **OpenCode aufnehmen.** Funktionen und tatsächliche Abhängigkeiten der
   gewählten Quellversion erfassen. Gemeinsam festlegen, welche Funktionen
   übernommen, angepasst oder entfernt werden.
2. **OpenChamber aufnehmen.** Die zugehörigen Chat-Komponenten, Einstellungen,
   Schnittstellen und Dienste auf dieselbe Weise durchgehen.
3. **Quellen reproduzierbar bauen.** Quellrevisionen und Build-Werkzeuge
   festlegen und den funktionalen Referenzstand prüfen.
4. **Die TwinCAT-Anbindung im Quellcode einbauen.** Projekt-, Kontext- und
   Anhangsschnittstellen anschließen. Vorhandene Funktionen weiterverwenden
   und die bekannten Wiederherstellungs- und Anhangsprobleme korrigieren.
5. **Funktionsgruppen reduzieren.** Die gemeinsam ausgewählten Teile einzeln
   entfernen, ihre Abhängigkeiten bereinigen und nach jedem Schritt die
   betroffenen Funktionen prüfen.
6. **Datenübernahme und Installation prüfen.** Bestehende Gespräche übernehmen,
   den Kandidaten getrennt testen und erst danach lokal umstellen.

Nach jedem Abschnitt halten wir Ergebnis und offene Entscheidungen fest und
gehen sie gemeinsam durch. Der nächste konkrete Schritt nach diesem Dokument
ist die OpenCode-Bestandsaufnahme.

## Abnahme des umgebauten Stands

- Vorhandene Anbieteranmeldung, Modellauswahl und normaler Chat funktionieren.
- Streaming, Abbruch, Werkzeugfehler und weitere Modellschritte bleiben nutzbar.
- Beide MCPs liefern tatsächliche Ergebnisse. Ein zurückgegebener Knowledge-
  Treffer lässt sich öffnen und mit Quelle auswerten.
- Derselbe Projektchat ist nach einem vollständigen IDE-Neustart wieder da.
- Projektwechsel, gleichnamige Bausteine und mehrere IDE-Instanzen ordnen
  weder Nachrichten noch Anhänge dem falschen Kontext zu.
- Die Anhangsanzeige stimmt beim Vormerken, Entfernen, Absenden und nach
  Kontextwechseln mit dem tatsächlich verwendeten Inhalt überein.
- Bestehende Daten lassen sich übernehmen; ein Rückweg zum Referenzstand ist
  vor der lokalen Umstellung geprüft.
- Die bisherigen relevanten Regressionstests und die Linux-/Windows-CI bleiben
  grün. Messungen zu Startzeit und Speicherverbrauch werden getrennt erfasst.

Der erste Umbau behält den bisherigen lesenden Funktionsumfang. Spätere PLC-
Schreib-, Compiler- oder Runtime-Funktionen folgen weiterhin den eigenen
Meilensteinen in [MVP](MVP.md), [Roadmap](ROADMAP.md) und
[Engineering Safety](ENGINEERING_SAFETY.md). Ein erfolgreicher Build oder
Chat-Test ist keine Prüfung von PLC- oder Maschinenverhalten.
