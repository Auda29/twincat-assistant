# Gemeinsamer Startvertrag für Core, Chat und Host

Stand der Bestandsaufnahme: 29.10.2026, integrierter Stand `57fb2cc`.
Der Frontend-Commit `ddc9dce6b2662090b223149e0b4d032ef47b2b83` ist darin
konfliktfrei übernommen. Die Angaben zur vorausgehenden Designprüfung beziehen
sich auf diesen Stand; die Startvorbereitung wurde am 30.10.2026 konsolidiert.

Dieser Vertrag konkretisiert den nächsten Abschnitt der
[Server-Bestandsaufnahme](BACKEND_SERVER_INVENTORY.md). Er beschreibt den
vorhandenen Startpfad und die Anforderungen an dessen gemeinsame Verwaltung
im Assistant Core. Die erste Konsolidierung ist seit 30.10.2026 in
[runtime-start.mjs](../src/runtime-start.mjs) umgesetzt. Dieses Dokument führt
weder einen neuen Dienst noch eine
neue API, Protokollversion oder einen zusätzlichen Prozess ein.

## Zuständigkeiten

| Modul | Verantwortung beim Start |
| --- | --- |
| Assistant Host | Native Konfiguration, lokales Dienstpasswort, Lebensdauer des eigenen Starts, WebView, Anmeldung, Navigation und Anzeige des Ladezustands |
| Assistant Core | Profil und freigegebene Konfiguration prüfen, Chat-Ressourcen vorbereiten, Runtime-Artefakte auswählen, Prozesse kontrolliert starten und beenden, vorhandene Serveraufgaben weiterverwenden |
| Assistant Chat | Bestehende Oberfläche laden, Runtime-/Source-Vertrag initialisieren, Composer und Sitzungen laden, Status anzeigen |
| Engineering MCP | Projektbezogene Werkzeuge über den vorhandenen MCP-Vertrag anbieten; aktuell gespeicherte PLC-Dateien lesen |
| Knowledge MCP | Dokumentationskorpus und Suchwerkzeuge bereitstellen; sein gemeinsamer Broker gehört zur lokalen Laufzeitverwaltung |

OpenCode bleibt Eigentümer von Modellanbindung, Agentenablauf, Werkzeugausführung
und Gesprächsdatenbank. OpenChamber übernimmt im aktuellen Stand weiterhin
seine vorhandenen Serveraufgaben. Die Namen Core, Chat und Host legen die
Zuständigkeit fest, nicht die Zahl der laufenden Prozesse.

## Eingaben und Konfigurationsbesitz

Die native Einrichtung in `native-chat.json` enthält die bestehenden Felder
`NodePath`, `RepositoryRoot`, `StateRoot`, `DotNetRoot` und optional
`McpSettingsPath`. Der Host prüft absolute, normalisierte Pfade und die
benötigten ausführbaren Dateien. Das lokale Dienstpasswort liegt getrennt per
DPAPI geschützt im nativen Datenbereich. Anbieter-Credentials gehören zu OpenCode.

Quelle: [NativeChatConfiguration und NativeChatCredentials](../native/IdeHost/NativeChatRuntime.cs).

Der bestehende Startauftrag an `src/native-chat.mjs` besteht aus einer
UTF-8-JSON-Zeile auf stdin. Der Kanal hat derzeit kein `version`-Feld.

| Feld | Vorhandene Validierung / Bedeutung |
| --- | --- |
| `state` | Erforderlicher absoluter Pfad zum Operator-Profil, höchstens 2048 Zeichen, kein NUL |
| `port` | Erforderliche ganze Zahl von 1024 bis 65535; Bindung ausschließlich an `127.0.0.1` |
| `password` | Erforderlicher String mit 12 bis 1024 Zeichen, nicht ausschließlich Leerraum, kein NUL; unverändert übergeben, damit Unicode erhalten bleibt |
| `mcpConfig` | Optionaler absoluter Pfad zur nativen MCP-Konfiguration, höchstens 2048 Zeichen, kein NUL; die Datei wird gesondert geprüft |

Unbekannte Felder werden abgewiesen. Der Parser akzeptiert genau einen führenden
UTF-8-BOM. Der Node-Eingang begrenzt die gesamte Eingabe auf 32768 Bytes und
wartet höchstens zehn Sekunden auf den ersten Auftrag. Weitere Eingabezeilen
beenden den laufenden Auftrag. Anbieter, Modell und PLC-Quelltext werden nicht
als zusätzliche Startfelder angenommen.

Quelle: [validateNativeChatRequest, parseNativeChatRequest und main](../src/native-chat.mjs).

Für den Operator-CLI bleiben `init`, `doctor` und `start` erhalten.
`start` verwendet dasselbe Profil und dieselben Launch-/Supervisor-Module.
Explizite Binary-Pfade sind dort weiterhin Operator-Optionen; sie werden nicht
zu Feldern des nativen Browser- oder Startkanals. Der CLI erwartet das
Dienstpasswort über `OPENCHAMBER_UI_PASSWORD` und prüft im heutigen
`prepareLaunch` nur, ob es nicht leer ist. Die strengere native Längenprüfung
darf bei der Konsolidierung nicht stillschweigend als bereits gemeinsames
Verhalten beider Eingänge vorausgesetzt werden.

Quelle: [cli.mjs](../src/cli.mjs), [prepareLaunch](../src/launcher.mjs).

## Reihenfolge eines eigenen Starts

1. **Auftrag prüfen und freien Port bestätigen.** `startNativeChat` weigert sich,
   einen belegten Port zu übernehmen. Diese Prüfung findet vor der Anlage eines
   neuen Profils statt. Ein Host kann stattdessen einen bereits erreichbaren,
   kompatiblen geschützten Dienst verwenden, ohne ihn zu besitzen.
2. **Profil prüfen oder neu anlegen.** Ein fehlendes Profil wird mit den
   bestehenden Initialisierungsfunktionen angelegt. Ein vorhandenes Profil wird
   gegen seine verwaltete Policy und seine Verzeichnisse geprüft. Beschädigte
   oder abweichende Einstellungen werden nicht als leeres Profil ersetzt.
3. **Optionale MCP-Einrichtung prüfen.** Die globale native Konfiguration
   bereitet den Knowledge-Zugang vor. Engineering wird erst über die genaue
   Solution-/PLC-Identität im verwalteten Projekt-Workspace konfiguriert.
4. **Runtime-Artefakte auswählen und prüfen.** Der Source-Receipt bindet
   Quellrevision, Plattform, Vertragsversion, Eingaben und Artefakthashes.
   Bei expliziter Source-Auswahl oder automatischer Auswahl über einen
   vorhandenen Receipt führt eine Abweichung zum Fehler. Die ausdrückliche
   Auswahl der Referenzdistribution bleibt erhalten. Ein defekter Quellbuild
   wird nicht still durch die Distribution ersetzt.
5. **Chatprofil vollständig vorbereiten.** `prepareLaunch` ruft
   `prepareChatWorkspace` auf. Beide benötigten Chat-Verzeichnisse, die beiden
   TwinCAT-Theme-Dateien, die bisherige Default-Auswahl und die TwinCAT-Commands
   sind vorbereitet, bevor der Chatserver startet. Eigene Themes und vorhandene
   Projekte bleiben erhalten. Wiederholte Vorbereitung erhält vorhandene Chats.
6. **Umgebung und Launchplan herstellen.** Der bestehende Plan enthält
   `command`, `args`, `cwd` und `env`. HOME/XDG/OpenChamber-Pfade zeigen auf das
   isolierte Operator-Profil. Relay bleibt ausgeschaltet. Beliebige IDE-Secrets
   und fremde Konfigurationsinjektionen werden nicht weitergereicht.
7. **Versionen prüfen und Prozessbesitz übernehmen.** Beide tatsächlichen
   Versionsausgaben müssen zu OpenCode `2.0.23` und OpenChamber `2.1.1` passen.
   Der Supervisor erwirbt `launcher.lock` und startet nur eigene Prozesse.
   Bei Knowledge-Konfiguration betreibt der vorhandene Wrapper genau einen
   gemeinsamen Broker und startet OpenChamber als Kindprozess. OpenChamber
   startet das separate OpenCode-Binary weiterhin selbst.
8. **Geschützten Dienst bestätigen.** Ein nicht angemeldeter GET auf
   `/auth/session` muss HTTP 401 und die erwarteten Felder
   `authenticated: false`, `locked: true` liefern. Erst dann meldet der native
   Launcher `ready`. Der gestartete Prozess muss dabei noch laufen.
9. **Nativ anmelden und Chat laden.** Der Host übernimmt nur das HttpOnly-Cookie
   in WebView2, navigiert zum vorgesehenen Origin und initialisiert den Adapter.
   Bei einem Source-Build wird der bestehende Source-Vertrag abgewartet.
   Erst Anmeldung und gemounteter Chat beziehungsweise der vorgesehene manuelle
   Login-Fallback beenden den Ladebildschirm.
10. **Sitzung und Werkzeuge gesondert herstellen.** Der vorhandene Verlauf wird
    vollständig und projektbezogen geladen. Knowledge und Engineering melden
    ihre tatsächliche Verfügbarkeit unabhängig vom Chat-Mount. Ein Fehler beim
    Laden des Verlaufs erzeugt keinen neuen Chat als vermeintlichen Leerstand.

Quellen: [nativer Einstieg](../src/native-chat.mjs),
[Profilvorbereitung](../src/state.mjs), [Themes](../src/themes.mjs),
[Commands](../src/commands.mjs), [Runtime-Auswahl](../src/runtime-paths.mjs),
[Launchplan](../src/launcher.mjs), [Supervisor](../src/supervisor.mjs),
[Knowledge-Wrapper](../src/shared-chat-runtime.mjs),
[Host-Verbindung](../native/IdeHost/AssistantControl.cs),
[Source-Vertrag](../upstream/overlays/openchamber/twincat.ts) und
[Chat-Integration](../native/IdeHost/ChatIntegration.cs).

Die Voraussetzungen sind keine übergreifende Transaktion. Vor einem späteren
Startfehler können verwaltete Theme-/Command-Dateien oder MCP-Einstellungen
bereits aktualisiert sein. Diese Vorbereitung bleibt wiederholbar. Der Start
verspricht keinen automatischen Rollback des gesamten Operator-Profils.

## Bereitschaft ist mehrteilig

Ein gemeinsamer Status muss folgende Aussagen unterscheiden. Die Namen in
dieser Tabelle beschreiben fachliche Zustände; sie sind keine bereits
implementierten zusätzlichen Felder des stdin-Protokolls.

| Bereich | Nachweis für Bereitschaft | Was daraus noch nicht folgt |
| --- | --- | --- |
| Vorbereitetes Profil | Policy geprüft, notwendige Verzeichnisse, Themes und Commands vorhanden | Kein Prozess und keine Anbieteranmeldung sind damit bestätigt. |
| Lokaler Dienst | Eigener Prozess lebt; geschützter Auth-Endpunkt antwortet erwartungsgemäß | OpenCode und Knowledge können noch starten. |
| Chat | Navigation erfolgreich, Adapter initialisiert, Anmeldung und bestehender Chat-Mount bestätigt | Ein Modell oder Dokumentationskorpus muss noch nicht eingerichtet sein. |
| OpenCode | OpenChambers tatsächliche OpenCode-Bereitschaft beziehungsweise API-Probe erfolgreich | Ein ausgewählter Anbieter kann fehlen, unfreigegeben oder nicht erreichbar sein. |
| Projektchat | Vollständiger Verlauf geladen und exakte Projekt-/Sitzungsbindung hergestellt | Aktiver Editorinhalt ist damit nicht automatisch angehängt. |
| Engineering | Verwaltetes PLC-Projekt geprüft; MCP-Verbindung und benötigter Leseaufruf erfolgreich | Keine Live-IDE-, Compiler-, ADS- oder Maschinenprüfung. |
| Knowledge | Gültiger Korpus und tatsächlicher verbundener Knowledge-MCP; Status-/Suchaufrufe liefern auswertbare Ergebnisse | Eine vorhandene Indexdatei allein bestätigt noch keine benutzbare MCP-Verbindung. |

Ein langsamer Knowledge-Kaltstart hält den normalen Chat nicht bis zum Ende der
Korpusprüfung zurück. Ein allgemeiner Chat benötigt kein PLC-Projekt und erhält
keinen Engineering-MCP. Fehlender Anbieter oder fehlende Dokumentation bleiben
gezielte Einrichtungszustände. Sie sind kein Grund, Gesprächsdaten neu anzulegen.

Quellen: [Verbindungszustand](../native/Shared/ChatConnectionState.cs),
[Ladebildschirm](../native/IdeHost/SplashIntegration.cs),
[Knowledge-Anbindung](../native/IdeHost/KnowledgeIntegration.cs),
[Projektvorbereitung](../src/native-mcp.mjs),
[Broker](../src/knowledge-broker.mjs) und [Runtime-Integration](RUNTIME_INTEGRATION.md).

## Rückmeldung und Beenden

Die erfolgreiche native Kontrollantwort lautet weiterhin
`{"status":"ready","port":3123}`. Fehler an der Node-Eingangsgrenze werden
als `{"status":"failed"}` ohne Zugangsdaten oder Upstream-Fehlertext gemeldet.
`ready` bestätigt den geschützten Dienst, nicht den Abschluss des gesamten
Starts bis zur Modell- oder MCP-Bereitschaft. Nach der Meldung bleibt der
Launcher zur Überwachung am Leben.

Die native Windows-Prozesskette trennt die begrenzte Kontrollantwort von
Upstream-Ausgaben. Der Host verwirft die abgeleiteten Serverlogs, statt sie in
Dialoge oder Gesprächsinhalte zu übernehmen. Dieser Kanal darf bei einer
gemeinsamen CLI-/Host-Implementierung nicht durch zusätzliche Logzeilen oder
eine neue Ausgabeform des CLI beschädigt werden.

| Ereignis | Erforderliches Verhalten |
| --- | --- |
| Start ohne Besitz, vorhandener kompatibler Dienst | Host meldet sich an und verbindet sich; er bekommt dadurch kein Recht, den fremden Dienst zu beenden. |
| Host stoppt seinen eigenen Start | Stdin wird geschlossen. Abbruch erreicht den Supervisor und dessen besessene Prozesskette. |
| Besitzer oder Helper stirbt | Windows Job Object beziehungsweise Prozessgruppenaufsicht beendet die eigenen Nachfahren einschließlich Knowledge. Unbeteiligte Prozesse bleiben erhalten. |
| Dienst stirbt | Host zeigt Dienstverlust und verwendet die bestehende Wiederverbindung. Ein späterer Versuch erhält eine neue Generation. |
| Navigation oder Browser scheitert | Ladezustand endet mit Fehler und Wiederverbindung; eine erfolgreiche `Navigate()`-Ausführung allein gilt nicht als geladener Chat. |
| Verspätete Rückmeldung | Generation, NavigationId und Dokumentgeneration verhindern die Übernahme in einen inzwischen gewechselten Zustand. |
| Belegter Port oder Profilsperre | Start ablehnen. Keine fremden Prozesse beenden, keine Sperre aufgrund eines geratenen PID-Zustands löschen. |

Quellen: [NativeChatSession](../native/IdeHost/NativeChatRuntime.cs),
[Windows-Supervisor](../src/windows-native.mjs),
[Job Object](../native/WindowsHost/Program.cs),
[Supervisor](../src/supervisor.mjs), [Host](../native/IdeHost/AssistantControl.cs).

Die heutige Wiederverwendungsprobe prüft das erwartete Auth-Endpunktverhalten.
Sie liefert keinen vollständigen Nachweis von RepositoryRoot, StateRoot oder
Source-Revision des bereits laufenden Dienstes. Ein späterer expliziter
Dienstidentitätsvertrag muss getrennt eingeführt und mit dem Host abgestimmt
werden; dieses Dokument behauptet diese zusätzliche Prüfung nicht.

## Bestehende Zeitgrenzen

| Grenze | Vorhandener Wert / Startpunkt |
| --- | --- |
| Erster stdin-Auftrag | 10 Sekunden ab Node-Einstieg |
| Einzelne Versionsprüfung | Je höchstens 10 Sekunden |
| Node-Bereitschaftsschleife | Standardmäßig 65 Sekunden ab gestarteter Supervision, nach Profil-/Artefaktvorbereitung und Versionsprüfungen |
| Einzelne Auth-Bereitschaftsprobe | 1,5 Sekunden, Antwort höchstens 4096 Bytes |
| Native Kontrollantwort | Höchstens 80 Sekunden ab Beginn des Lesens nach Auftragsübermittlung |
| Native automatische Anmeldung | 5 Sekunden pro Anmeldung |
| Chat-/Adapter-Ladebildschirm | 60 Sekunden ab Navigation; ein Timeout zeigt Fehler und Wiederverbindung |
| Stoppen aus dem Host | 5 Sekunden Wartezeit, danach Beenden des eigenen Launchers und weitere 2 Sekunden Wartezeit |

Diese Grenzen gehören heute verschiedenen Ebenen. Insbesondere liegen
Profilvorbereitung und Versionsprüfungen außerhalb der 65-Sekunden-Schleife,
aber innerhalb der nativen 80-Sekunden-Wartezeit. Die Konsolidierung muss diesen
Unterschied berücksichtigen. Dieses Dokument ändert keine Timeout-Defaults
und verspricht keine neue Gesamtstartzeit. Die Knowledge-Korpusprüfung hat
ihren eigenen Lebenszyklus und darf nicht in das Chat-Ladebildschirmbudget
eingerechnet werden.

## Abnahme der ersten Konsolidierung

`prepareRuntimeLaunch` führt Runtime-Auswahl, Launchplan und Versionsprüfung aus
`cli.mjs` und `native-chat.mjs` gemeinsam aus. Die erwarteten Versionen stammen
aus dem vorhandenen Source-Lock. Das Profil mit Themes und Commands wird wie
bisher vor den Versionsprozessen vorbereitet. Erst nach erfolgreicher Prüfung
geben die Eingänge den Plan an den unveränderten Supervisor weiter.

Die unterschiedlichen Eingänge, Rückmeldungen und Abbruchsignale bleiben an
ihrer jeweiligen Grenze. Der CLI normalisiert seine Operator-Binary-Pfade wie
bisher über `realpath`; der native Eingang verwendet die vom Runtime-Receipt
gewählten regulären Dateien. Die Referenzdistribution verwendet den geprüften
`bin.opencode`-Pfad ihres Paketmanifests, dessen `.exe`-Suffix bei der gepinnten
Version auch unter Linux und macOS gilt. Die native Portprüfung und MCP-Einrichtung bleiben
vor der gemeinsamen Vorbereitung, ihre 65-Sekunden-Bereitschaftsfrist beginnt
weiterhin erst nach der Versionsprüfung. Es gibt keinen neuen Gesamt-Timeout.

Für diese erste Konsolidierung gelten folgende Abnahmen:

- Gleiche Runtime-Artefakte und vorhandene Source-/Distribution-Auswahl.
- Beide TwinCAT-Themes, effektive bisherige Default-Auswahl und alle Commands
  vor dem ersten Chat-Bootstrap. Bewusst gewählte andere Themes bleiben erhalten.
- Bestehende Operator-Daten, Projektbindungen, Quellarten und Anhangszustände
  bleiben erhalten; kein Umzug von RepositoryRoot, Profil oder Datenbank.
- Geschützter Loopback-Dienst, ursprüngliche Anbieteranmeldung, Streaming,
  Abbruch, Queue und tatsächlicher Verlauf bleiben nutzbar.
- Ein gemeinsamer Knowledge-Broker in der besessenen Laufzeit und getrennte
  Engineering-Konfiguration je PLC-Projekt.
- Fehler-, Neustart- und Owner-Death-Verhalten einschließlich unveränderter
  nativer Kontrollantwort und CLI-Ausgabe bestehen die betroffenen Tests.

Die vorhandenen Prüfungen für diese Abnahmen sind
[Launcher](../tests/launcher.test.mjs), [nativer Eingang](../tests/native-chat.test.mjs),
[CLI](../tests/cli.test.mjs), [Profil](../tests/state.test.mjs),
[Themes](../tests/themes.test.mjs), [Commands](../tests/commands.test.mjs),
[Supervisor](../tests/supervisor.test.mjs),
[Windows-Lebenszyklus](../tests/windows-native.test.mjs),
[gemeinsamer Broker](../tests/shared-runtime.test.mjs),
[echter nativer Runtime-Eingang](../tests/integration/native-chat.test.mjs) und
[WPF/WebView-Integration](../scripts/Test-ChatIntegration.ps1).
[Startvorbereitung](../tests/runtime-start.test.mjs) prüft zusätzlich die
Reihenfolge vor der Versionsausführung, unveränderte Projektauswahl und eigene
Themes, den gemeinsamen Versionsfehlerpfad ohne Prozessbesitz sowie die feste
CLI-Fehlerantwort vor einem tatsächlichen Serverstart.

### Nachweis der vorausgehenden Designübernahme

Am kombinierten Stand `57fb2cc` wurden erneut ausgeführt:

- 81 Node-Tests und acht Browser-Tests über `npm run check`.
- Beide nativen Host-Builds und 80 native Policy-/Kontextprüfungen.
- 51 WPF-Prüfungen sowie 59 echte WebView-Prüfungen mit der gepinnten
  Referenzdistribution, einschließlich Hell/Dunkel und 390/768/1100 Pixel Breite.

Die vorhandenen Threading-Warnungen in Knowledge-/Diagnosecode blieben sichtbar.
Der unveränderte Source-Vertrag und die Anwendung sind zum übernommenen
Frontend-Commit bytegleich. Dessen protokollierter Nachweis umfasst außerdem
63 Source-WebView-Prüfungen mit beiden MCPs. Dieser Source-Lauf wurde hier nicht
erneut ausgeführt. Die genannten Prüfungen verwenden synthetische Projekte,
Editorzustände und Anmeldedaten; sie sind keine IDE-/PLC-/Compiler- oder
kostenpflichtige Modellabnahme.

Die Logs liegen ausschließlich lokal unter `local-data/backend-*.log`.
Für das Startvertragsdokument werden Verweise, konkrete Vertragsfelder und
Zeitgrenzen am Quellcode geprüft. Es werden dadurch keine neuen
Laufzeitgarantien als bereits implementiert ausgewiesen.
