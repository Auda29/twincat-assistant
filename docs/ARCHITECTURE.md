# Architektur und Integrationsgrenzen

## Grundentscheidung

OpenChamber und OpenCode werden als zusammengehörige Grundlage wiederverwendet.
Es soll zunächst weder ein neuer Chat noch ein neues Agent-Harness entstehen.
Die TwinCAT-spezifische Arbeit liegt im IDE-Host, in der Engineering-Bridge und
in der Erweiterung des bestehenden Monorepo-MCPs.

Der konkrete Upstream-Stand und die Form der Wiederverwendung — bestehende
Distribution, Fork oder eingebundener Quellstand — werden nach dem ersten
Einbettungstest festgelegt. Dieses Repository enthält zunächst nur Planung,
keine vendorten Quellen und keine Submodule.

## Komponenten

```text
TwinCAT-Shell / Visual Studio
|
+-- C#-Plugin
|   +-- WPF-Toolwindow mit WebView2
|       +-- OpenChamber-Weboberfläche
|
+-- IDE-Bridge
    +-- Solution-/Projektbindung und Objektkontext
    +-- kontrolliertes Lesen und Schreiben von PLC-Objekten
    +-- Build-Ausführung und Diagnoseerfassung
                 ^
                 | lokale, authentisierte Verbindung
                 v
OpenChamber + OpenCode außerhalb der IDE
+-- Sitzungen, Modellzugriff, Streaming und Agent-Ausführung
+-- twincat-knowledge-mcp
+-- twincat-mcp-mono
    +-- gespeicherte Projektdateien und ADS-Diagnose
    +-- neues Live-Engineering-Backend -> IDE-Bridge
```

## Zuständigkeiten

### IDE-Plugin und Bridge

- Toolwindow bereitstellen; WebView2 und IDE-Lebenszyklus verwalten.
- Die konkrete IDE-Instanz, Solution und das Zielprojekt eindeutig identifizieren.
- DTE und TwinCAT Automation Interface nur in einem geeigneten IDE-/COM-
  Threading-Kontext aufrufen; keine UI-Thread-Blockierung durch Modellaufrufe.
- Zielobjekt, Dirty-Status, Ausgangsrevision und Freigabe prüfen.
- Deklaration und ST-Implementierung lesen und kontrolliert ändern.
- Build-Abschluss und zugehörige Diagnosen erfassen.
- Zustandsänderungen und unbekannte Ausgänge an das Backend melden.

### OpenChamber und OpenCode

- OpenChamber übernimmt die vertraute Thread-, Aktivitäts- und Review-Oberfläche.
- OpenCode übernimmt Agent-Ausführung, Modellzugriff und MCP-Client-Funktionen.
- Der Agent entscheidet, welche Lese-Tools für die Frage erforderlich sind.
- Schreib- und Build-Aktionen müssen den Engineering-Freigabeablauf respektieren.
- Allgemeine Werkzeuge dürfen keine unkontrollierten Umgehungspfade eröffnen.

### Bestehende MCPs

`twincat-knowledge-mcp` bleibt eine lesende Dokumentationsschnittstelle. Der
Betreiber stellt einen autorisierten, validierten lokalen Corpus bereit; ohne
diesen bleibt Dokumentationswissen nicht verfügbar.

`twincat-mcp-mono` behält seine Core-/Adapter-Aufteilung. Ein neues Backend bindet
die laufende IDE an. Bestehende Lese- und Build-Verträge sollen soweit fachlich
passend erhalten bleiben. Engineering-Schreiboperationen benötigen zusätzliche,
explizite Verträge; sie sind keine Umdeutung von ADS-Variablenschreibzugriffen.

## Aktueller Bestand und Grenzen

Recherchebasis: 8. Oktober 2026. Die tatsächlich eingesetzten Revisionen müssen
vor der Implementierung erneut geprüft und festgehalten werden.

| Bestandteil | Nachgewiesener Bestand | Grenze |
| --- | --- | --- |
| Knowledge-MCP | Vier Tools, lokale Pakete/Snapshots, Suche und Topic-/Hierarchieabruf | Kein Corpus gebündelt; keine Engineering- oder Runtime-Steuerung |
| Monorepo-MCP | Gespeicherte Projektanalyse und ADS-Diagnose | Kein implementierter Live-Engineering-Schreibbackend |
| Build-Tools im Monorepo | Registrierte Toolverträge | Dateibackend meldet Build-/Compilerdiagnosen als nicht verfügbar |
| OpenChamber | Turn-basierte Threads und Extension-SDK | Kein fertiger TwinCAT-Host; enge Kopplung an OpenCode |
| Automation Interface | Dokumentierte PLC-Lese-/Schreibschnittstellen | Verhalten mit ungespeicherten Editoren, Undo und konkreten Versionen praktisch prüfen |

## Oberflächenstrategie

1. Zunächst die lokale OpenChamber-Weboberfläche in WebView2 anzeigen.
2. Projektkontext, Navigation und Tooldarstellungen ergänzen.
3. Das OpenChamber-Extension-SDK verwenden, wo es ausreicht.
4. Erst bei nachgewiesenem Bedarf einen stärker angepassten Frontend-Host bauen.

Die VS-Code-Erweiterung ist eine Architekturvorlage, aber keine in Visual Studio
installierbare Erweiterung. Das OpenChamber-Extension-SDK unterstützt laut
Recherche Web und Desktop; eine Nutzung in der eingebetteten Weboberfläche ist
praktisch zu validieren.

MCP-basierte Objektänderungen sind nicht automatisch normale OpenCode-Dateipatches.
POU-Diffs, Änderungsprotokolle und Rücknahme brauchen eine explizite Integration.
Die erste Darstellung kann ein eigenes Review-Panel sein; eine tiefe Integration
in sämtliche Upstream-Diffansichten ist keine MVP-Voraussetzung.

## Kommunikationsgrenzen

- Agent-/MCP-Prozesse bleiben außerhalb des IDE-Prozesses.
- Die Bridge soll vorzugsweise eine lokale Named Pipe verwenden; Transport und
  Nachrichtenvertrag werden im Machbarkeitstest festgelegt.
- Endpunkte werden auf den vorgesehenen Benutzer begrenzt. Zusätzlich braucht
  die Verbindung eine geprüfte Identität beziehungsweise Sitzungsauthentisierung.
- WebView2 akzeptiert nur die vorgesehene lokale Oberfläche. Herkunft,
  Navigation und Nachrichten werden geprüft; keine generische COM- oder
  Shellschnittstelle für Webinhalte.
- Die Kopplung muss IDE-Schließen, Solution-Wechsel, mehrere IDE-Instanzen und
  abgestürzte Hilfsprozesse ausdrücklich behandeln.
- Solution, PLC-Projekt und optionales ADS-Ziel sind getrennte Identitäten.
  Gleiche Namen beweisen keine Übereinstimmung von Source und Runtime.

## MCP-Kompatibilität

Das Knowledge-MCP verwendet laut aktuellem Adapter das Profil `2026-07-28` und
liefert Dokumentationsinhalt in `structuredContent`, während der Textblock eine
kurze Statuszusammenfassung enthält.

Die aktuelle OpenCode-V2-Dokumentation beschreibt `legacy` als Standard und
unterstützt `auto` beziehungsweise `2026-07-28` als serverspezifische Auswahl.
Der Knowledge-Server braucht deshalb eine bewusst passende Protokolleinstellung.
Die Auswahl wird nicht ungeprüft auf den Monorepo-Server übertragen.

Verbindung und Toolkatalog allein reichen nicht: Ein echter Such-/Topic-Aufruf
muss den Dokumentationsinhalt bis in den Modellkontext transportieren. Die
MCP-Konfigurationsoberfläche von OpenChamber muss die benötigte Einstellung
erhalten, statt sie bei Änderungen zu verlieren.

## Offene Entscheidungen

- Exakte TwinCAT-/TcXaeShell64-Version und installierte Komponenten.
- Passender klassischer Visual-Studio-SDK-, .NET- und WebView2-Stand.
- Kompatible, festgehaltene OpenChamber-/OpenCode-Revisionen und Update-Strategie.
- Form und Ablage der Upstream-Wiederverwendung; kein unnötiges Duplizieren.
- Konkreter Bridge-Vertrag, Authentisierung und Freigabezuordnung.
- Eindeutige Objektidentität und zuverlässige Dirty-/Revisionsprüfung.
- Verhalten von Build, Diagnosen und Persistenz in der gewählten Shell.
- Nachweisbarer Rücknahmeablauf ohne Annahme eines universellen IDE-Undo.
- Eigene Softwarelizenz und spätere Distribution.

## Quellen

- [OpenChamber](https://github.com/openchamber/openchamber)
- [OpenChamber Extension-SDK](https://github.com/openchamber/openchamber/blob/main/packages/sdk/README.md)
- [OpenChamber Turn-Projektion, geprüfter Stand](https://github.com/openchamber/openchamber/blob/866dffd863c98a25aa6577e90cca46b01a66237e/packages/ui/src/components/chat/lib/turns/projectTurnRecords.ts)
- [Monorepo EngineeringService, geprüfter Stand](https://github.com/Auda29/twincat-mcp-mono/blob/3c303837e6d6f72bec1b9d38e778a7c32c61ae5c/packages/core/src/engineering.ts)
- [Monorepo Engineering-Backend-Dokumentation](https://github.com/Auda29/twincat-mcp-mono/blob/main/docs/engineering-backends.md)
- [Knowledge-MCP Adaptervertrag](https://github.com/Auda29/twincat-knowledge-mcp/blob/main/docs/implementation/topic-mcp-adapter.md)
- [OpenCode V2 MCP und Protokollauswahl](https://opencode.ai/v2/docs/mcp-servers)
- [Beckhoff: Automation Interface API](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21905744267.html)
- [Beckhoff: Anschluss an laufende IDE über DTE](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842572555.html)
- [Microsoft: Toolwindow-Erweiterung](https://learn.microsoft.com/en-us/visualstudio/extensibility/creating-an-extension-with-a-tool-window?view=vs-2022)
- [Microsoft: Kommunikation mit WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/communicate-btwn-web-native)
- [Microsoft: WebView2-Sicherheit](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
