---
status: done (pending audit)
type: step-plan
task: dry-refactor
step: step-001
corrects: null
title: "Baseline-Eliminierung & Zero-Warning-Setup"
epic: EPIC-01
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Gemini 3.7 Flash (High)
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-15T18:20:00+02:00
related_to: []
---

# Step 001: Baseline-Eliminierung & Zero-Warning-Setup

## Bezug

- **Task:** `dry-refactor`
- **Epic:** `EPIC-01` aus `roadmap.md`
- **Konzept-Referenz:** [Konzept.md](tasks/dry-refactor/Konzept.md) Abschnitt „Scope > Muss-Haben"

## Aktueller Projektzustand (JIT-Kontext)

In `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` existiert eine Baseline mit 144 Dateihashes. In `AiNetLinterTests.cs` existiert ein Test `RecreateBaseline`, der diese Datei aktualisiert, sowie `RunLinterShouldBeCleanOrBaselineMatch`. In `.agents/rules/SqlToAiRichtlinien.mdc` Abschnitt 5 wird auf die automatische Aktualisierung der Baseline hingewiesen.

## Intention

Entfernen der Baseline-Datei, Bereinigen des Linter-Tests und Aktualisieren der Richtlinien. Das System stellt damit die Weichen für eine 100% baseline-freie Codequalität.

## Konkrete Änderungen

### Datei 1: `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json`
- **Was:** Datei löschen.
- **Warum:** Es soll keine Baseline mehr existieren.

### Datei 2: `tests/SqlToAi.Tests/AiNetLinter/AiNetLinterTests.cs`
- **Was:** 
  - Entfernen der Methode `RecreateBaseline()`.
  - Methode `RunLinterShouldBeCleanOrBaselineMatch()` in `RunLinterShouldBeClean()` umbenennen.
- **Warum:** Linter-Test prüft nur noch strikt auf Clean-Check.

### Datei 3: `.agents/rules/SqlToAiRichtlinien.mdc`
- **Was:** Abschnitt 5 („Baseline-Aktualisierung") umschreiben auf strikte Zero-Warning- und Linter-Konformität ohne Baseline.
- **Warum:** Dokumentation und Linter-Regeln synchron halten.

## Tests

- [ ] `dotnet build` läuft ohne Fehler/Warnungen.
- [ ] `dotnet test` läuft erfolgreich.

## Definition of Done

- [ ] Alle Änderungen umgesetzt
- [ ] Build & Test grün
- [ ] Commit erfolgt
- [ ] `step-result.md` geschrieben
