# TwinCAT Assistant

Ein KI-Agent direkt in der TwinCAT-Shell beziehungsweise Visual Studio: Projekte
verstehen, passende InfoSys-Dokumentation finden, kontrolliert ST-Code bearbeiten
und echte Build-Ergebnisse auswerten. Die Bedienung orientiert sich an den Threads
und Agent-Aktivitäten von OpenChamber.

**Status:** Repository-Initialisierung und MVP-Planung. Es gibt noch keine
ausführbare Anwendung, IDE-Erweiterung oder implementierte Engineering-Bridge.
Das Repository wird zunächst privat geführt.

## Ansatz

- OpenChamber und OpenCode wiederverwenden, statt Chat und Agent-Unterbau neu zu
  entwickeln. Zunächst möglichst wenige Änderungen am bestehenden System.
- Ein C#-Plugin stellt ein angedocktes Toolwindow mit WebView2 bereit.
- Modellaufrufe und MCP-Prozesse laufen außerhalb der IDE.
- Eine kleine IDE-Bridge verbindet das Agent-System mit der geöffneten Solution,
  PLC-Objekten und der Build-Ausführung.
- Die beiden bestehenden TwinCAT-MCP-Projekte bleiben die fachliche Grundlage.

## Bestehende Projekte

| Projekt | Rolle |
| --- | --- |
| [twincat-knowledge-mcp](https://github.com/Auda29/twincat-knowledge-mcp) | Lokale, quellenbasierte Suche und Abruf von TwinCAT-/Beckhoff-Dokumentation |
| [twincat-mcp-mono](https://github.com/Auda29/twincat-mcp-mono) | Projektanalyse und ADS-Diagnose; soll ein Live-Engineering-Backend erhalten |
| [OpenChamber](https://github.com/openchamber/openchamber) | Chatoberfläche, Threads, Aktivitäten und Änderungsreview |
| [OpenCode](https://github.com/anomalyco/opencode) | Agent-Ausführung, Modellanbindung und MCP-Client |

## Erster MVP

Ein dauerhafter Chat in einer konkret festgelegten TwinCAT-Shell-Version, der das
richtige Projekt untersucht, beide MCPs verwendet und nach Freigabe Deklaration
und ST-Implementierung eines bestehenden Bausteins ändern und kompilieren kann.

Kein Download, Online Change, Forcing oder Runtime-Schreiben. Eine verbundene PLC
ist für den Kernablauf nicht erforderlich.

## Dokumentation

- [Dokumentationsübersicht](docs/README.md)
- [MVP: Umfang und Abnahme](docs/MVP.md)
- [Architektur und Integrationsgrenzen](docs/ARCHITECTURE.md)
- [Umsetzungsplan und technische Prüfschritte](docs/ROADMAP.md)
- [Engineering-Sicherheit und Änderungsablauf](docs/ENGINEERING_SAFETY.md)

## Entwicklung und Lizenzen

Build- und Installationsanweisungen folgen nach dem ersten technischen Prototyp.
Die genaue Zielversion der IDE und die kompatiblen Upstream-Versionen sind noch
festzulegen. In diesem Initial-Commit wird kein Upstream-Code übernommen.

OpenChamber und OpenCode stehen zum Recherchezeitpunkt unter MIT. Bei einer
Übernahme müssen deren Copyright- und Lizenzhinweise sowie die Lizenzen der
Drittkomponenten erhalten beziehungsweise geprüft werden. Für dieses eigene
Projekt ist noch keine Softwarelizenz festgelegt; Zugriff auf das Repository
begründet keine allgemeine Nutzungs- oder Weiterverteilungserlaubnis.

Dieses Projekt ist unabhängig und kein offizielles Beckhoff-Produkt. Beckhoff-
Dokumentation und reale Maschinenprojekte werden nicht mitgeliefert.
