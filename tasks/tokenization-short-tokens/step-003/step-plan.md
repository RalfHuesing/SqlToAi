---
status: open
type: step-plan
task: tokenization-short-tokens
step: "003"
title: "Test-Updates (AnonymizerTests & QueryTokenResolverTests)"
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-07-28T15:21:00Z
related_to:
  - step-001
  - step-002
---

# Step 003: Test-Updates (AnonymizerTests & QueryTokenResolverTests)

## Bezug

- **Task:** `tokenization-short-tokens`
- **Quelle:** [Konzept.md](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tasks/tokenization-short-tokens/Konzept.md) — Scope "Muss-Haben" Punkt 6

## Intention

Aktualisierung aller Unit- und Integrationstests. Entfernung aller Testfälle bezüglich `Secret` aus `AnonymizerTests` und `QueryTokenResolverTests`. Ergänzung neuer Testfälle für das Kurz-Token-Format und die Bi-Direktionalität.

## Konkrete Änderungen

### Datei 1: `tests/SqlToAi.Tests/Anonymization/AnonymizerTests.cs` (Zeile 106-215)

- **Was:**
  - Entfernen des `secret`-Parameters aus `BuildTokenizationOptions`.
  - Entfernen der veralteten Tests:
    - `Tokenize_ShouldProduceDifferentTokens_ForDifferentSecrets`
    - `Tokenize_ShouldFallBackToMasking_WhenSecretIsEmpty`
  - Hinzufügen/Anpassen von Tests:
    - Test für Kurz-Token-Format (z. B. `§§§T1§§§`).
    - Test für Bi-Direktionalität: Gleicher Wert liefert innerhalb der Sitzung exakt denselben Kurz-Token.
    - Test für verschiedene Werte: Verschiedene Werte liefern unterschiedliche Kurz-Tokens (`T1`, `T2`).

### Datei 2: `tests/SqlToAi.Tests/Database/QueryTokenResolverTests.cs` (Zeile 13-46)

- **Was:**
  - Entfernen des `secret`-Parameters aus `BuildResolver`.
  - Entfernen von `ResolveTokens_ShouldReturnQueryUnchanged_WhenSecretIsEmpty`.
  - Anpassen vorhandener Token-Strings im Resolver-Test an das neue Kurz-Token-Format (z. B. `§§§T1§§§`).

## Tests

- [ ] `dotnet build` kompilieren ohne Warnungen/Fehler
- [ ] `dotnet test` führt 100% aller Tests erfolgreich aus

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command (`dotnet build`) grün (0 Warnings, 0 Errors)
- [ ] Test-Command (`dotnet test`) grün (100% bestanden)
- [ ] Commit auf aktuellem Branch (`test(anonymization): update unit tests for short tokenization and secret removal`)
- [ ] `step-003/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — xUnit v3 Tests Pflicht
- `.agents/rules/AiNetLinter.mdc` — Tests Overrides (MaxMethodLineCount ≤ 100)
