# Engineering-Sicherheit und Änderungsablauf

## Sicherheitsgrenze des MVP

Der Assistant darf Projektcode untersuchen und kontrolliert ändern. Ein Build
ist erlaubt, aber kein implizites Deployment. Der MVP bietet keine Tools für
Download, Online Change, Aktivierung, Login/Logout, Start/Stop oder Forcing.
Runtime-Diagnose bleibt optional und lesend.

Die Freigabe muss in der ausführenden Bridge beziehungsweise dem Engineering-
Backend geprüft werden. Tool-Anmerkungen, Agent-Anweisungen oder ein ausgeblendeter
UI-Schalter ersetzen diese Prüfung nicht.

## Projekt- und Objektbindung

Ein Auftrag wird an die konkrete IDE-Instanz, Solution, PLC-Projekt und das
Zielobjekt gebunden. Die fachliche Identität darf nicht nur aus einem Anzeigenamen
bestehen. Projektwechsel, Objektlöschung und verlorene Verbindung invalidieren
ausstehende Freigaben.

Eine OpenCode-Session-ID kann zur Korrelation dienen, ist für sich genommen aber
kein Authentisierungsnachweis und keine Schreibberechtigung.

## Lesen und ungespeicherte Änderungen

Ergebnisse kennzeichnen, ob sie aus gespeicherten Dateien oder aus dem Live-
Engineering-Modell stammen. Beide Stände dürfen nicht unbemerkt vermischt werden.

Im MVP wird ein ungespeichertes Zielobjekt nicht bearbeitet. Der Benutzer muss
seinen Stand ausdrücklich speichern und der Agent anschließend neu lesen.
Ist Dirty-Erkennung nicht zuverlässig verfügbar, bleibt der Schreibpfad
deaktiviert. Das Plugin speichert nicht pauschal die gesamte Solution.

## Änderungsablauf

1. Erlaubtes Zielobjekt und ST-Sprache prüfen.
2. Ausgangsstand und Dirty-Status erfassen.
3. Vorschlag erstellen und einen Diff für Deklaration/Implementierung anzeigen.
4. Freigabe an genau Ziel, Ausgangsrevision und vorgeschlagenen Inhalt binden.
5. Unmittelbar vor der Übernahme Objektstand und Freigabe erneut prüfen.
6. Den eigenen Ausgangsstand lokal sichern und Änderungen serialisiert ausführen.
7. Tatsächlichen Nachher-Stand lesen und Ausgang protokollieren.
8. Den zugehörigen, freigegebenen Build-Ablauf ausführen und Ergebnis berichten.

Ein Hash oder Revisionsvergleich muss genau den zuvor gelesenen, relevanten
Objektstand abdecken. Eine Freigabe darf nicht für einen anderen Vorschlag
wiederverwendet werden.

## Teilfehler und unbekannter Ausgang

Deklaration und Implementierung können getrennte API-Aufrufe erfordern. Es wird
keine atomare TwinCAT-Transaktion vorausgesetzt. Bei einem Teilfehler müssen
bereits ausgeführte Schritte, beobachteter Zustand und mögliche Wiederherstellung
sichtbar sein.

Ein Timeout oder Verbindungsabbruch kann nach einer tatsächlich ausgeführten
Änderung eintreten. In diesem Fall lautet der Status zunächst „Ausgang unbekannt“.
Erst eine erneute Zustandsprüfung darf Erfolg, Fehler oder Teiländerung bestätigen.
Der Assistant wiederholt einen solchen Schreibauftrag nicht blind.

Abbrechen beendet weitere Agent-Schritte, garantiert aber nicht die Rücknahme
eines bereits gestarteten COM-Aufrufs oder Builds. Diese Grenze wird im Thread
sichtbar gemacht.

## Build und Diagnosen

- Build ist nicht Aktivierung oder Download.
- Projekt, Konfiguration und Ziel müssen nachvollziehbar ausgewählt sein.
- Erfolg wird aus einem echten Build-Abschluss ermittelt, nicht aus dem Fehlen
  zufälliger Meldungen in der Fehlerliste.
- Diagnosen werden einem aktuellen Build zugeordnet. Alte IDE-Meldungen dürfen
  keinen erfolgreichen oder fehlgeschlagenen Lauf vortäuschen.
- Falls benutzerdefinierte Build-Hooks beliebige externe Aktionen ausführen
  können, muss dies vor dem ersten Build geprüft und als Vertrauensgrenze
  offengelegt werden; „Build“ ist keine universelle Sandbox.
- Ein erfolgreicher Compilerlauf ist kein Verhaltenstest und keine Freigabe
  für Maschinenbetrieb.

## Rücknahme

Eigene Vorher-/Nachher-Stände werden lokal gespeichert. Zurücknehmen ist nur
zulässig, wenn der aktuelle Stand dem eigenen Nachher-Stand entspricht. Neuere
Benutzeränderungen blockieren die automatische Wiederherstellung.

Es werden weder ein globaler Git-Reset noch pauschales Verwerfen von IDE-
Änderungen verwendet. Die Verfügbarkeit nativer Undo-Integration wird separat
geprüft; sie wird nicht vorausgesetzt.

## Umgehungspfade und lokale Kommunikation

- Allgemeine Datei-Schreib- und Shelltools müssen im Engineering-Agent-Profil
  so eingeschränkt sein, dass sie die geschützten TwinCAT-Artefakte und Runtime-
  Aktionen nicht ohne den vorgesehenen Ablauf verändern können.
- Bei einem frei zugänglichen Shelltool wäre eine reine Pfad-Allowlist kein
  ausreichender Schutz. Solche Zugriffe bleiben im MVP gesperrt oder benötigen
  ein separat geprüftes Ausführungsmodell.
- Die IDE-Bridge bietet keine beliebigen COM-Aufrufe oder Shellbefehle an.
- Lokale Endpunkte benötigen geeignete Zugriffskontrolle und Authentisierung;
  „localhost“ allein ist kein Berechtigungsnachweis.
- WebView2-Herkunft, Navigation und Nachrichtenparameter werden validiert.
  Externe Dokumentationslinks erhalten keinen Zugang zur Host-Bridge.

Diese Regeln sind Anwendungskontrollen, keine Behauptung einer vollständigen
OS-Sandbox gegenüber beliebigem Code mit den Rechten des angemeldeten Benutzers.

## Daten und Protokollierung

Quellcode kann über den gewählten Modellanbieter das lokale System verlassen.
Provider, Datenfluss und Freigabe durch den Betreiber müssen sichtbar sein.

Reale Projekte, Dokumentationscorpus, Sitzungsdaten, Tokens und Änderungsbackups
werden nicht in dieses Repository eingecheckt. Diagnoselogs müssen sensible
Inhalte berücksichtigen und für eine Weitergabe bereinigt werden. Aufbewahrung
und lokale Speicherorte werden vor der produktiven Nutzung festgelegt.
