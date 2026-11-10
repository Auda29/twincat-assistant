# TwinCAT Assistant 0.2.0 Beta 1

Diese Beta vereint die Produktbereinigung aus PR #16 und die gespeicherte
Deutsch-/Englisch-Auswahl aus PR #17 mit allen Korrekturen aus Preview 6.

- Host und Chat teilen eine Sprachauswahl. Sie ist im Host-Menü unter
  **… → Sprache / Language** und in den Chat-Einstellungen verfügbar und bleibt
  nach einem Neustart erhalten. Quelltext, Nachrichten und Entwürfe bleiben im
  Original. Die Antwortsprache des Modells richtet sich nach der Nutzernachricht.
- Eigene Produkttexte, Logo und Versionsanzeige verwenden TwinCAT Assistant.
  Ungenutzte Chat-Serverdienste, Routen und zugehörige Abhängigkeiten wurden
  entfernt. Benötigte Datenverträge und Drittanbieterhinweise bleiben erhalten.
  Gemeinsame Hilfen und weitere Client-Reste sind im
  [Bereinigungsumfang](../PRODUCT_CLEANUP_20261109.md) dokumentiert.
- Enthält den korrigierten ersten Fensteraufruf, die UTF-8-Projekteinrichtung,
  klare Fehleranzeigen und die Chatzuordnung vor dem Mount des Eingabefelds.
  Der Bediener hat den Chatstart mit Preview 6 in der installierten IDE bestätigt.

## Installation

TcXaeShell regulär schließen. Setup-EXE und `SHA256.txt` von diesem Release
herunterladen, Prüfsumme vergleichen und **Installieren / aktualisieren** im
vorhandenen Installationsordner wählen. Die UAC-Abfrage mit demselben
Windows-Konto bestätigen. Eine vorherige Deinstallation ist nicht erforderlich.
Vorhandene Chats, Anmeldungen und Projektbindungen verwenden die bisherigen
Datenpfade. Anwendungsversion ist `0.2.0-beta.1`, native Datei-/Assembly-/VSIX-
Version ist `0.2.0.7`.

Ziel bleibt TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 auf Windows x64 mit .NET 8,
.NET Framework und WebView2. Andere Hosts sind weiterhin nicht abgenommen.
Der Installer ist nicht signiert. Bestehende Sperren werden nicht automatisch
gelöscht. Der Editor bleibt lesend; Editor-Profil und Inline-Vervollständigung
sind nicht Bestandteil dieser Beta.

## Nachweise und Grenzen

Der lokale Windows-Build erzeugt das vollständige Anwendungspaket und die
Setup-EXE aus dem integrierten Quellstand. `BUILD_PROVENANCE.json` erfasst die
Quellrevision, Komponenten, Prüfsummen und tatsächlich bestandenen Prüfungen.
Die Installerprüfung liest den eingebetteten Payload, ohne Setup auszuführen.
Der Paket-Smoke verwendet beide echten MCPs ausschließlich an synthetischen
Dateien. WPF-/WebView-Start und Sprachprüfungen verwenden eigene Testprofile,
keine Operatorprofile und keine Modellanfragen.

Die sichtbare Sprachabnahme dieser Beta in der installierten IDE, echte
Modellantworten, Member-/Paste-/First-after-logon-Abnahmen und ein vollständiger
realer Update-/Rollback-Lauf bleiben offen. Die Beta wird auf Wunsch als GitHub
**Latest** geführt. Dafür ist sie dort nicht als „Pre-release“ markiert;
Produktname und Version kennzeichnen sie weiterhin ausdrücklich als Beta.
