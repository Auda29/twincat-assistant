# TwinCAT Assistant 0.2.0 Preview 6

Behebt den Startfehler mit der Meldung „Zuerst eine Unterhaltung öffnen.“ bei
gleichzeitig sichtbarem Ladebildschirm. Eine neue Unterhaltung kann bereits
bestätigt sein, bevor React ihr Eingabefeld rendert. Der native Host bewertete
diesen gültigen Zustand bisher als nicht bereit und lehnte die Zuordnung ab.

Bestätigte Chatmetadaten erlauben jetzt die Zuordnung unabhängig vom DOM-Aufbau.
Die Anzeige wartet weiterhin auf den passenden Verlauf und das gerenderte
Eingabefeld. Das WebView2-Composition-Control rendert hinter dem undurchsichtigen
WPF-Ladebildschirm. Maus und Tastatur bleiben bis zur Freigabe deaktiviert.
Projekt-, Sitzungs-, Dokument- und Berechtigungsprüfungen bleiben erhalten.

## Prüfung

`node scripts/test-native-chat-startup.mjs` startet ausschließlich einen eigenen
synthetischen Backendzustand und ein eigenständiges WPF-WebView2-Control. Der
Test verwendet den tatsächlichen nativen Chatstart, die eingebettete Integration
und den gebauten Chat. Ein absichtlich verzögertes DOM-Signal reproduziert das
beobachtete Zeitfenster sowohl für den allgemeinen als auch den Projektchat.
Geprüft werden Zuordnung vor dem Mount, gesperrte Eingabe während des Ladens,
anschließende Darstellung und ein weiterer Automationsdurchlauf ohne Duplikat.
Der MCP-Vorbereitungsschritt wird in diesem UI-Test durch synthetische Metadaten
ersetzt. Der separate Paketsmoke prüft beide echten MCPs an synthetischen Dateien.

Das gleiche Startszenario lehnt der bisherige Preview-5-Host noch vor der
Darstellung mit „Der neue allgemeine Chat ist nicht verfügbar.“ ab. Der neue
Host besteht alle zwölf Prüfungen. Die Prüfung startet keine IDE, öffnet kein
Engineeringprojekt, sendet keinen Modellprompt und verändert kein Operatorprofil.
Eine anfangs fehlerhafte Anmeldung innerhalb der Testseite wurde auf den echten
nativen Login vor der Navigation umgestellt. Fehlgeschlagene Fixtureläufe zählen
nicht als weitere Produktfehler oder erfolgreiche Nachweise.

## Installation

TcXaeShell regulär schließen und Setup mit **Installieren / aktualisieren** im
vorhandenen Installationsordner ausführen. Anwendungsversion ist
`0.2.0-preview.6`, Assembly-/Datei-/VSIX-Version ist `0.2.0.6`.

Ziel bleibt TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 auf Windows x64.
Die Fixes aus PR #18 und #19 sind enthalten. Die separaten PRs #16 und #17
sind nicht enthalten. Der lokale Paketbau wird im `BUILD_PROVENANCE.json`
erfasst. Die sichtbare Abnahme in der installierten IDE bleibt ein Bedienertest.
