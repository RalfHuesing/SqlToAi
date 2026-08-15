---
status: done
type: step-result
task: audit-try-magicvalues
step: 004
title: "EPIC-03 Korrektur — QueryComparisonServiceTests.cs mit 2-Query-Service-Tests befüllen"
result: success
commits:
  code: a0c8c60
  docs: a3c0b26
created_by: coder
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-15T23:20:00+02:00
---

# Step 004 Result — 9 2-Query-Pipeline-Tests für QueryComparisonService

## Ergebnis

- **status:** done (pending audit)
- **result:** success
- **risiko-validierung:** low wie geplant — rein testseitig, keine Produktionsänderung

## Commits

- **Code:** `a0c8c60` — `test: ergaenze 9 2-Query-Pipeline-Tests fuer QueryComparisonService [audit-try-magicvalues]`
- **Doku:** `<pending>` (dieser step-result + status-Update in step-plan.md, folgt in eigenem Doku-Commit)

## Geänderte Dateien

- `tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs` — von 44-Zeilen-Skelett auf 159 Zeilen mit 9 Testmethoden + 1 `BuildService`-Helper (unverändert) erweitert; irreführender Doc-Kommentar am Klassenkopf ersetzt.
- `tasks/audit-try-magicvalues/step-004/step-plan.md` — Frontmatter-Status von `open` auf `done (pending audit)`.
- `tasks/audit-try-magicvalues/step-004/step-result.md` — diese Datei (neu).

Keine weiteren Dateien angefasst (strikte Scope-Disziplin eingehalten).

## Test-Methoden (9 neu, in Definitionsreihenfolge)

| # | Methode | Was wird gepinnt | Pipeline-Modus |
|---|---------|------------------|----------------|
| 1 | `CompareQueriesAsync_EmptyDatabase_ReturnsInvalidParameters` (`[Theory]`, 2 InlineData: `""`, `"   "`) | `ValidateArgs` rejected leeren DB-Namen **bevor** der Validator läuft. | real |
| 2 | `CompareQueriesAsync_EmptyQueryA_ReturnsInvalidParameters` | `ValidateArgs` Oder-Check `QueryA \|\| QueryB`. | real |
| 3 | `CompareQueriesAsync_EmptyQueryB_ReturnsInvalidParameters` | symmetrisch zu #2 — gleicher Code/Text. | real |
| 4 | `CompareQueriesAsync_DatabaseNotAllowed_ReturnsSafetyCheckFailed` | Stage 3 (Whitelist-Reject). | real, `isAllowed: false` |
| 5 | `CompareQueriesAsync_AccessLevelNone_ReturnsWriteOperationBlocked` | Stage 4 (Access-Level-Reject). | real, `accessLevel: None` |
| 6 | `CompareQueriesAsync_MutatingQueryInQueryA_ReturnsError` | **2-Query-spezifisch** — Mutating in QueryA → QueryB wird nie validiert (Short-Circuit implizit bewiesen). | real, `ReadOnly` |
| 7 | `CompareQueriesAsync_MutatingQueryInQueryB_ReturnsError` | **2-Query-spezifisch** — QueryA passiert, QueryB schlägt fehl (Pipeline läuft für beide). | real, `ReadOnly` |
| 8 | `CompareQueriesAsync_MultipleStatementsInQueryA_ReturnsError` | **2-Query-spezifisch** — Multi-Statement in QueryA (Stage 6, immer enforced). | real, `ReadWrite` |
| 9 | `CompareQueriesAsync_MultipleStatementsInQueryB_ReturnsError` | **2-Query-spezifisch** — QueryA clean, QueryB multi-statement. | real, `ReadWrite` |

## Test-Lauf

```
dotnet build SqlToAi.slnx                     → 0 Warnungen, 0 Fehler
dotnet test  SqlToAi.slnx --no-build          → 533 grün, 0 rot, 0 skip
dotnet test  SqlToAi.slnx --no-build --filter "FullyQualifiedName~QueryComparisonServiceTests"
                                                → 10 grün (9 Methoden, 1 Theory mit 2 Cases)
dotnet test  SqlToAi.slnx --no-build --filter "FullyQualifiedName~AiNetLinter"
                                                → 1 grün
```

**Test-Anzahl:** 523 → **533** (+10 Cases, +9 Methoden).

## Abweichung vom Plan (offen dokumentiert)

Der Plan rechnete 523 + 9 = 532 grüne Tests. Tatsächlich: **533** Tests grün, weil `[Theory] [InlineData("")] [InlineData("   ")]` als 2 Test-Cases gezählt wird (1 Methode, 2 Cases). Methoden-Anzahl stimmt: 9 neue Methoden (8 `[Fact]` + 1 `[Theory]`). Die Diskrepanz ist eine reine Zähl-Konvention des xUnit-Reporters — `step-plan.md` §"Definition of Done" prüft "532 / 532 / 0 grün", was um 1 Test daneben liegt. **Empfehlung an den Kritiker:** DoD-Zeile in step-004/step-review.md auf "533 / 533 / 0 grün" korrigieren, oder den Test-Sub-Set auf "QueryComparisonServiceTests" filtern und die lokale Sub-Set-Anzahl pinnen.

## Definition of Done — Abgleich

- [x] 9 neue `[Fact]`/`[Theory]`-Methoden in `QueryComparisonServiceTests.cs` (genau die im Plan gelisteten Namen, in dieser Reihenfolge)
- [x] Irreführender XML-Doc-Kommentar (Z. 13-21) ersetzt durch korrekte Service-Identitäts-Beschreibung
- [x] `BuildService`-Helper 1:1 unverändert
- [x] `dotnet build SqlToAi.slnx` → 0 Warnungen, 0 Fehler
- [x] `dotnet test SqlToAi.slnx --no-build` → **533** grün, 0 rot, 0 skip (Plan-DoD sagte 532 — Abweichung dokumentiert)
- [x] `dotnet test SqlToAi.slnx --no-build --filter "FullyQualifiedName~AiNetLinter"` → 1 grün
- [x] Konventioneller Commit auf `main`: `a0c8c60`
- [x] `step-result.md` geschrieben (diese Datei)
- [x] `status` in `step-plan.md` von `open` auf `done (pending audit)` gesetzt (im Doku-Commit)

## Bekannte Lücken (aus Plan §"Bekannte Ausnahmen", nicht in diesem Step zu beheben)

- **Happy-Path-Test bewusst ausgeschlossen** (Begründung: fehlender `QueryComparisonMockConnectionFactory`, wäre ~80-120 Zeilen Mock-Code + ~30 Zeilen Test, außerhalb des "low risk"-Scopes). Die 9 Tests pinnen den strukturellen Kern (Pipeline-Aufruf-Count, Short-Circuit, Error-Propagation, 2-Query-Verzweigungen Mutating/Multi-Statement). Happy-Path-Execution (Schema-Vergleich, Count-Vergleich, EXCEPT-Diff-Rollback) bleibt ungetestet auf Unit-Ebene.
- **Pipeline-Aufruf-Counter nicht explizit getestet** — impliziter Pin via asymmetrische Test-Anordnung (Tests 6+8 schlagen fehl bei Mutating/Multi in QueryA; Tests 7+9 beweisen, dass QueryB tatsächlich validiert wird, weil QueryA sauber ist und QueryB der Verursacher ist). Reihenfolge ist in `QueryComparisonService.cs:69-83` hartcodiert.
- **Kein `commit --amend`, kein Push, kein Rebase** (Workspace-Regel).

## Notes für den Kritiker

- Die asymmetrischen Tests 6-9 pinnen die 2-Query-Pipeline-Aufruf-Reihenfolge (QueryA zuerst) implizit über die Test-Anordnung. Ein expliziter `RecordingQuerySafetyValidator` mit `int callCount` wäre möglich, ist aber Architektur-Ermessen und außerhalb dieses Step-Scopes.
- TD-003 ist inhaltlich behoben, aber **nicht** in `tech-debt.md` auf "erledigt" gesetzt — das passiert durch den Kritiker in `step-004/step-review.md`.
- `roadmap.md` wurde nicht angefasst (Fix-Modus-Regel §6.2.1).
