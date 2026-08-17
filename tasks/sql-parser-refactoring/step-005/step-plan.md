---
status: open
type: step-plan
task: sql-parser-refactoring
step: "005"
corrects: null
title: "Dokumentation synchronisieren und Gesamtabnahme"
epic: EPIC-05
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.7-flash
created_by_model_knowledge_cutoff: "2026-01"
created_at: "2026-08-17T16:42:15+02:00"
related_to: [step-001, step-002, step-003, step-004]
---

# Step 005: Dokumentation synchronisieren und Gesamtabnahme

## Bezug

- **Task:** `sql-parser-refactoring`
- **Epic:** `EPIC-05` aus `roadmap.md` — Doku-Synchronisation & Gesamtabnahme
- **Konzept-Referenz:** `konzept.md` §Definition of Done

## Aktueller Projektzustand (JIT-Kontext)

- Alle drei Komponenten (`ReadOnlyGuard`, `SqlMultiStatementDetector`, `QueryDeconstructor`) wurden erfolgreich auf Microsoft ScriptDom (`TSql150Parser`) und AST-Navigation umgestellt.
- Alle Unit- und Integrationstests (556 Tests) sind grün.
- In `docs/architecture-spec.md` und `README.md` wird der Read-Only Guard an einzelnen Stellen noch als Regex-Validierung beschrieben.

## Intention

Die Projektdokumentation (`docs/architecture-spec.md` und `README.md`) synchronisieren, sodass die Umstellung auf den Microsoft ScriptDom AST-Parser und `TSql150Parser` exakt reflektiert wird (Pflicht laut `.agents/rules/SqlToAiRichtlinien.mdc` §4). Anschließend den vollständigen Linter- und Testlauf durchführen.

## Konkrete Änderungen

### Datei 1: `docs/architecture-spec.md`

- **Was:** Abschnitt *B. Konfigurierbarer Schreibschutz (Read-Only Guard)* auf ScriptDom AST-Visitor aktualisieren.
- **Warum:** Korrekte technische Dokumentation der Sicherheitsarchitektur.

### Datei 2: `README.md`

- **Was:** Abschnitt *Read-Only Guard & Rollback Safety* von Regex- auf AST-basierte Validierung aktualisieren.
- **Warum:** Synchronisation der öffentlichen Projektbeschreibung.

## Tests

- [ ] `dotnet build` grün (Zero-Warnings)
- [ ] `dotnet test` grün (alle 556 Tests)
- [ ] `AiNetLinter` Clean-Check grün

## Definition of Done

- [ ] `docs/architecture-spec.md` und `README.md` sind synchronisiert
- [ ] Vollständiger Test- und Linter-Lauf erfolgreich
- [ ] Code- und Doku-Commits mit Suffix `[sql-parser-refactoring]` erstellt
- [ ] `step-005/step-result.md` geschrieben und Status aktualisiert

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — Dokumentations-Synchronisation (Pflicht)
