# TwinCAT Assistant 0.2.0 Preview 5

Behebt einen weiteren Fehler bei der nativen Projekteinrichtung: Node liefert
seine JSON-Antwort als UTF-8. Der Host verwendete beim Lesen bisher die
Windows-/Konsolen-Zeichenkodierung. Dadurch können Umlaute und `ß` in den
zurückgegebenen Lösungs- und Projektpfaden verändert werden. Die unverändert
strenge Prüfung des Projektbezugs lehnt diese verfälschten Pfade anschließend ab.

Der MCP-Prozess legt jetzt UTF-8 ausdrücklich für seine Ausgabekanäle fest.
Die JSON-Antwort wird strikt dekodiert. Projektprüfung, Paketprüfung, lesende
Berechtigungen und Sperrenverhalten werden nicht gelockert.

## Nachweise

`scripts/Test-NativeMcpEncoding.ps1` ruft den tatsächlichen nativen Prozessstarter
mit einem synthetischen Node-Protokollpartner auf. Derselbe Unicode-Pfadtest
scheitert mit dem bisherigen Host unter Codepage 850 und besteht mit dem Fix
unter 850, 1252 und 65001. Es werden keine echten Lösungs- oder PLC-Dateien gelesen.
Der vierte Check bestätigt dies für die ausschließlich synthetischen Dateinamen.

Zusätzlich bestand der echte installierte Preview-4-Backendaufruf über den
korrigierten nativen Prozessstarter mit einer isolierten Kopie der bekannten
Konfigurationsmetadaten und erzwungener Codepage 850. Originalkonfigurationen,
Chats und Zugangsdaten blieben unverändert. Eine anfängliche PowerShell-Probe
las ihre eigene UTF-8-Fixture mit der falschen Standardkodierung; sie wurde
korrigiert und zählt nicht als Nachweis eines Produktfehlers. Der anschließende
gezielte Vorher-/Nachher-Test isoliert die Antwortdekodierung.

## Installation

TcXaeShell regulär schließen, Setup-EXE und `SHA256.txt` herunterladen, Prüfsumme
vergleichen und **Installieren / aktualisieren** im vorhandenen Installationsordner
wählen. Die UAC-Abfrage mit demselben Windows-Konto bestätigen. Eine Deinstallation
von Preview 3 oder 4 ist nicht nötig. Anwendungsversion ist `0.2.0-preview.5`,
native Assembly-/Datei-/VSIX-Version ist `0.2.0.5`.

Ziel bleibt TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 auf Windows x64 mit vorhandener
.NET-8-Runtime, .NET Framework und WebView2. Der Installer ist nicht signiert.
Bereits vorhandene Projektsperren benötigen weiterhin eine geprüfte
Wiederherstellung bei Prozessruhe. Die Erweiterung löscht sie nicht automatisch.

Der Fix aus PR #18 bleibt enthalten. Die noch separaten PRs #16 und #17 sind
nicht enthalten. Der lokale Windows-Paketbau und seine Prüfungen werden im
beiliegenden `BUILD_PROVENANCE.json` erfasst. Eine sichtbare Abnahme der neuen
Version in der installierten IDE bleibt ein anschließender Bedienertest.
