# Deutsch und Englisch

Implementiert am 09.11.2026 auf `feat/de-en-localization`, aufbauend auf der
Produktbereinigung in `feat/lean-product-runtime`. Die Umsetzung ist seit dem
10.11.2026 in [Beta 1](releases/native-v0.2.0-beta.1.md) enthalten. Die ursprünglichen
Prüfungen unten bleiben datierte Nachweise; die Integration ergänzt die jüngsten
Projektfehlertexte in beiden Sprachen und wurde erneut geprüft.

## Bedienung und Speicherung

Im Host-Menü **… → Sprache / Language** stehen **Deutsch** und **English** zur
Auswahl. Der Menüpunkt funktioniert auch ohne verbundenen Chat. Im Chat ist
dieselbe Auswahl unter **Einstellungen → Erscheinungsbild → Sprache** erreichbar.
Host und Chat übernehmen die Auswahl voneinander, ohne Chat, Entwurf oder
Anhänge neu anzulegen. Eine ältere, bereits gestartete Sprachbeobachtung darf
eine inzwischen getroffene native Auswahl nicht zurücksetzen.

Der Host speichert `language: "de" | "en"` in der bestehenden `preferences.json`
neben `autoAttach`. Der vorhandene Credential-Verzeichnisresolver bestimmt den
Pfad; der Prozess-Testoverride verwendet weiterhin ausschließlich sein privates
Profil. Die Sprache wird weder in Zugangsdaten noch in Projektkonfigurationen
geschrieben. Die vorhandene atomare Präferenzspeicherung bleibt erhalten.

Beim nächsten Öffnen erhält der Chat die gespeicherte native Auswahl. Damit
bleibt sie auch erhalten, wenn der lokale Backend-Port und damit der
Browser-Origin wechseln. Im Chat bleibt zusätzlich dessen bestehender
LocalStorage-Eintrag `openchamber.i18n.v1` erhalten. Ohne native Sprachpräferenz
wird eine vorhandene deutsche oder englische Chat-Auswahl übernommen, sonst
Deutsch. Alte Präferenzdateien brauchen keine Migration. Ein unbekannter Wert
wird beim Lesen ignoriert; bloßes Lesen schreibt die Datei nicht um.

## Umfang

- Native Menüs, Picker, Dialoge, Status, Quellzustandsanzeigen, Benachrichtigungen,
  bekannte Hostdiagnosen und Fenstertitel verwenden zentrale `.resx`-Kataloge.
- Der Chat verwendet seine bestehenden vollständigen DE/EN-Wörterbücher und den
  vorhandenen reaktiven Locale-Store. Der Produkt-Sprachpicker bietet diese zwei
  Sprachen an. Der neue native Leerzustand ist ebenfalls ein Katalogeintrag;
  für alle 13 weiterhin im Quellstand vorhandenen Wörterbücher liegt er vor.
- Der Projektchat-Picker formatiert relative Zeiten und Datumsangaben mit der
  ausgewählten Sprache. Die Kultur des gesamten IDE-Prozesses wird nicht geändert.
- Technische Protokollwerte, Provider-/Modellnamen, Dateipfade, Fehlercodes und
  unveränderte Meldungen externer Komponenten bleiben im Original. Die
  Installeroberfläche gehört nicht zu dieser Host-/Chat-Änderung.
- Quelltext, Objekt- und Chattitel sowie vorhandene Nachrichten bleiben im
  Original. Die Antwortsprache des Modells richtet sich weiterhin nach der
  letzten Nutzernachricht gemäß Analyse-Prompt, nicht nach der Oberflächensprache.

Das lokale Prüf-Paket enthält 21.538 Dateien; sein Manifest hat SHA-256
`85d1f27549932bf4406324aa576ceb20e5860b4d1567a919c559a558a2953c41`.
Die englische Satellite-Assembly hat SHA-256
`7b4b4238263a121538ada6e1cff3048bc67d53ba8f254b030a3e90a6f7ae56ea`.
Diese Prüfarbeitskopie verwendet weiterhin die bestehende Versionsnummer; sie
wurde nicht als Release veröffentlicht.

Native WPF-Bindungen erkennen ausschließlich vom Katalog erzeugte Strings anhand
ihrer Objektidentität. Ein vom Nutzer vergebener Titel `Gespeichert` wird deshalb
beim Wechsel auf Englisch nicht zu `Saved`. Textboxen mit Quelltext oder
Benutzereingaben werden nicht als Übersetzungsziele behandelt.

Der x86-Host erzeugt eine englische Satellite-Assembly. Der Paketbauer übernimmt
sie verpflichtend als `host/x86/en/TwinCatAssistant.IdeHost.resources.dll` und
erfasst sie im vollständigen Dateimanifest. Ein Paketbau ohne diese Datei scheitert.

## Prüfung

Die Prüfungen verwenden synthetische Dateien und eigene Testprofile. Es wurden
keine installierte IDE, Operatorprofile, echten Providerzugänge oder PLC-Aktionen
verwendet.

| Prüfung | Ergebnis |
| --- | --- |
| Chat-Kataloge | Je 7.239 DE/EN-Schlüssel, keine fehlenden Schlüssel oder abweichenden Platzhalter |
| Native Kataloge | Vollständige DE/EN-Schlüssel- und Platzhaltergleichheit; englische Ressourcen ohne deutschen Fallback geladen |
| Native Katalogumfang | Je 384 Einträge in Deutsch und Englisch |
| Native Build / Regressionen | x86-Release-Build erfolgreich; 932 Prüfungen einschließlich der Katalogprüfungen bestanden; fünf bestehende Threading-Warnungen |
| Node-Tests | 162/162 bestanden, kein Skip |
| Embedded-Adapter und Paketbauer-Fixture | 29/29 bestanden; Paketfixture enthält die englische Satellite-Assembly |
| Projektchat-Picker im echten WPF-Harness | 110 Checks, DE/EN-Menüs und Zeitangaben, Titel unverändert, Rückwechsel bestanden |
| Tatsächliches AssistantControl außerhalb der IDE | 11 Checks: Umschalten, erneutes Erzeugen mit gespeicherter Sprache, Auto-Attach erhalten, Quelltext/Objektname unverändert, alte/ungültige Präferenzen gelesen |
| Gebauter Chat in Chrome | Sprachpicker zeigt nur DE/EN; Wechsel über Einstellungen und Adapter, gleiche Session/Generation und unveränderter Entwurf; Englisch nach Neuladen erhalten |
| UI-/Web-Typechecks | Beide bestanden |
| Vollständiges Anwendungspaket | Mit englischer Host-Satellite gebaut; geschützter Start, beide echten MCPs an synthetischen Dateien, Neustart, Datenerhalt und unveränderte Paketdateien bestanden |

Die standalone WPF-Prüfung ruft den gemeinsamen synchronen Übergang auf. Sie
belegt nicht den Menüdispatch über den IDE-eigenen JoinableTaskContext. Die
Browserprüfung hat keinen Prompt gesendet. Vorhandene Fehler von allgemeinen
Client-Endpunkten im bereinigten Backend bleiben sichtbar; es wird kein
fehlerfreies Browserkonsolenprotokoll behauptet.

Reproduktion:

```powershell
powershell.exe -NoProfile -File scripts/Build-Native.ps1 -DotNetRoot <SDK-8.0.425> -Architecture X86
powershell.exe -NoProfile -STA -File scripts/Test-HostLanguage.ps1
powershell.exe -NoProfile -STA -File scripts/Test-ProjectChatPicker.ps1
node --test --test-concurrency=1 tests/*.test.mjs
$env:TC_TEST_DOTNET_ROOT = 'C:\Program Files\dotnet'
node --test tests/integration/application-package-builder.test.mjs
```

Noch offen ist die sichtbare Prüfung dieses Builds in der installierten
TcXaeShell: Sprachmenü, Fenstertitel, Popups, eingebetteter Chat und Sprache nach
IDE-Neustart. Dieser PR führt keine Installation aus und schließt die anderen
Host-Abnahmepunkte der Roadmap nicht.
