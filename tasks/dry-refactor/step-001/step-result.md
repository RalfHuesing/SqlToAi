---
status: done
type: step-result
task: dry-refactor
step: step-001
epic: EPIC-01
step_type: single
coded_by: coder
coded_by_model: Gemini 3.7 Flash (High)
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-15T18:21:00+02:00
code_commit_hash: 90d7a89
status_after: done
blocker_category: n/a
---

# Result Step 001: Baseline-Eliminierung & Zero-Warning-Setup

## Zusammenfassung

Die Datei `SqlToAi-baseline.json` wurde vollständig aus dem Repository gelöscht. Der Test `RecreateBaseline` in `AiNetLinterTests.cs` wurde entfernt und der verbleibende Validierungstest auf `RunLinterShouldBeClean` umgestellt. Die Entwicklungsrichtlinie in `SqlToAiRichtlinien.mdc` wurde aktualisiert.

## Geänderte Dateien

- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` (gelöscht) — Baseline entfernt.
- `tests/SqlToAi.Tests/AiNetLinter/AiNetLinterTests.cs` — `RecreateBaseline` entfernt, `RunLinterShouldBeClean` beibehalten.
- `.agents/rules/SqlToAiRichtlinien.mdc` — Baseline-Passagen entfernt.

## Commit

- **Code-Commit-Hash:** `90d7a89`
- **Message:** `fix(linter): Entferne SqlToAi-baseline.json und stelle Tests auf Clean-Check um`
- **Branch:** `main`

## Build-/Test-Output

```
dotnet build → grün (0 Warnungen, 0 Fehler)
dotnet test  → grün (486 Tests, 0 Fehler)
```

## Abweichungen vom Plan

Keine — Plan 1:1 umgesetzt.

## Beobachtungen

Keine.

## Bekannte Unschärfen

Keine.
