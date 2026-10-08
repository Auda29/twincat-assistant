# Planungsdokumentation

Stand: 8. Oktober 2026. Diese Dokumente beschreiben den vereinbarten Zielzustand,
nicht bereits implementierte Funktionen.

| Dokument | Inhalt |
| --- | --- |
| [MVP](MVP.md) | Nutzerablauf, Funktionsumfang, Ausschlüsse und Abnahmekriterien |
| [Architektur](ARCHITECTURE.md) | Wiederverwendung, Komponenten, Kommunikation und offene Entscheidungen |
| [Roadmap](ROADMAP.md) | Technische Machbarkeitsprüfung und Reihenfolge der Umsetzung |
| [Engineering-Sicherheit](ENGINEERING_SAFETY.md) | Freigaben, Konflikte, Builds, Rücknahme und Schutz der Runtime |

## Leitentscheidungen

1. Vorhandenen OpenChamber-/OpenCode-Code verwenden, nicht nur optisch nachbauen.
2. Mit einer konkreten TwinCAT-Shell-Version beginnen, nicht mit einer allgemeinen
   Kompatibilitätszusage für alle Visual-Studio- und TwinCAT-Versionen.
3. Beide bestehenden MCPs integrieren und Engineering-Funktionen im Monorepo
   erweitern, statt parallele TwinCAT-Toolkataloge aufzubauen.
4. Der MVP ist ein frei bedienbarer Chat mit Agent-Funktionen, kein starrer
   Assistent mit vorgeschriebenen Formularschritten.
5. Offline-Engineering und Online-/Runtime-Aktionen strikt trennen.

## Umgang mit Änderungen am Plan

Neue technische Erkenntnisse werden in den betroffenen Dokumenten ergänzt.
Insbesondere sind offene Annahmen nicht stillschweigend als bewiesene Fähigkeiten
zu behandeln. Größere Architekturentscheidungen erhalten später eigene ADRs,
sobald eine konkrete Entscheidung und ihre Alternativen vorliegen.
