# TwinCAT Assistant 0.2.0 Preview 4

Enthält den Fix aus PR #18 für fehlgeschlagene Projekteinrichtungen.
Vorhandene Projektsperren werden ausdrücklich gemeldet. Ein Einrichtungsfehler
beendet die Ladeanimation und wird nicht mehr als unterbrochene Chatverbindung
angezeigt. Die weiterhin begrenzte Einrichtungsfrist beträgt jetzt 120 Sekunden,
damit beim Versionswechsel beide Anwendungspakete vollständig geprüft werden
können. Sperren werden nicht automatisch gelöscht oder übernommen.

## Installation und Update

TcXaeShell regulär schließen. Setup-EXE und `SHA256.txt` herunterladen, Prüfsumme
vergleichen und die EXE öffnen. Die UAC-Abfrage mit demselben Windows-Konto
bestätigen, das den Assistant verwendet. **Installieren / aktualisieren** und
den bestehenden Installationsordner wählen, normalerweise
`C:\Program Files\TwinCatAssistant`.

Preview 3 muss nicht deinstalliert werden. Preview 4 verwendet eine neue
Anwendungsversion `0.2.0-preview.4` und eine neue native Assembly-/Datei-/VSIX-Version
`0.2.0.4`. Damit ist das Update getrennt von der bereits vorhandenen Preview 3.
Bestehende Chats, Anmeldungen und Projektbindungen werden erhalten.

Bereits zurückgebliebene Projektsperren benötigen weiterhin eine geprüfte
Wiederherstellung bei Prozessruhe. Auf dem betroffenen Arbeitsrechner wurden
die zwei festgestellten Sperren separat gesichert; der Installer löscht solche
Dateien nicht selbst. [Befund und Grenzen](../PROJECT_SETUP_FAILURE_20261109.md)
erklärt die ursprüngliche Diagnose.

Ziel bleibt TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 auf Windows x64.
.NET 8 Runtime, .NET Framework und WebView2 sind lokale Voraussetzungen.
Der Installer ist nicht signiert und startet oder beendet weder IDE noch PLC.

## Umfang und Nachweise

Dieser Release enthält ausschließlich den gemergten Main-Stand mit PR #18.
Die Produktbereinigung aus PR #16 und die vollständige Sprachumstellung aus
PR #17 sind nicht enthalten. Der erste Menüaufruf-Fix und der grafische Installer
aus Preview 3 bleiben erhalten.

Der Chat wird aus dem zu diesem Stand gehörenden gepinnten Quellstand neu gebaut;
der unveränderte Core und die geprüften MCP-Eingaben werden weiterverwendet.
Host, vollständiges Anwendungspaket und Setup-EXE werden lokal unter Windows
gebaut. Dies ist kein neuer hosted-CI-Installerlauf. `BUILD_PROVENANCE.json` und
`SHA256.txt` begleiten die EXE.

Geschützter Paketstart, beide echten MCPs an synthetischen Dateien, Neustart,
Datenerhalt und unverändertes Paket sowie die eingebetteten Installerdateien
werden vor Veröffentlichung geprüft. Die sichtbare Abnahme in der installierten
IDE bleibt ein anschließender Bedienertest. Die Knowledge-Verbindungszeit und
die Untersuchung des Kaltstarts mit wiederhergestellten Editoren bleiben offen.
