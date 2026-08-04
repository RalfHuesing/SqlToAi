---
status: done (pending audit)
type: step-result
task: sql-index-suggestions
step: 007
epic: EPIC-04
step_type: single
coded_by: coder
coded_by_model: claude-sonnet-5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T00:00:00+02:00
code_commit_hash: 0a71e9b
status_after: done
blocker_category: n/a
---

# Result Step 007: TD-006 — Test 1 Graceful-Degradation-Toleranz

## Zusammenfassung

Die Assertion in Test 1
(`SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase`)
akzeptiert jetzt zusätzlich den Graceful-Degradation-Pfad
(`"VIEW SERVER STATE"`-Permission-Notiz), analog zur bereits bestehenden
Assertion in Test 4. 1:1-Übernahme des Plans, kein Produktionscode
betroffen.

## Geänderte Dateien

- `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs` —
  Assertion in Test 1 um dritte Bedingung
  (`result.Value.Contains("VIEW SERVER STATE", ...)`) erweitert,
  Kommentar und Failure-Message auf drei Pfade angepasst.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` —
  automatisch durch `AiNetLinterTests.RecreateBaseline` aktualisiert
  (SHA-256-Hash der geänderten Testdatei).

## Commit

- **Code-Commit-Hash:** `0a71e9b`
- **Message:**
  ```
  test(index-suggestions): Test 1 akzeptiert Permission-Notiz als validen Pfad [sql-index-suggestions]

  Erweitert die Assertion in
  SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase
  um den dritten laut Architektur gültigen Output-Pfad
  ("VIEW SERVER STATE"-Permission-Notiz), analog zu Test 4. Test 1 ist
  damit setup-tolerant und erzwingt kein implizites GRANT VIEW SERVER
  STATE mehr als Testvoraussetzung.

  Refs: tasks/sql-index-suggestions/step-007
  ```
- **Branch:** `main`
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier
  drin — Selbstbezug, siehe `git log`).

## Build-/Test-Output

```
dotnet build → grün (0 Warnungen, 0 Fehler)
dotnet test  → grün (526 Tests, 0 Fehler; AiNetLinterTests.RecreateBaseline aktualisierte Baseline automatisch)
```

Die lokale Test-Instanz hat aktuell `VIEW SERVER STATE` (per manuellem
GRANT) — Test 1 und Test 4 liefen daher über den Markdown-Tabellen-Pfad
(`"| Score |"`), nicht über den neu ergänzten Permission-Notiz-Pfad. Der
neue Assert-Zweig ist unbenutzt-aber-verifiziert grün (keine
Kompilierfehler, keine Laufzeit-Exceptions in der Bedingungskette) und
wirkt als Absicherung für Umgebungen ohne dieses Recht.

## Abweichungen vom Plan

Keine — Plan 1:1 umgesetzt.

## Beobachtungen

Keine neuen Beobachtungen über die im Plan bereits dokumentierten hinaus.

## Bekannte Unschärfen

- Der neue dritte Pfad (`"VIEW SERVER STATE"`) konnte in diesem Lauf
  nicht durch tatsächliches Fehlschlagen der Berechtigung verifiziert
  werden, da die lokale Test-Instanz aktuell `VIEW SERVER STATE` besitzt
  (siehe Auftrags-Kontext). Die Bedingung ist strukturell identisch zur
  bereits produktiv laufenden Assertion in Test 4 (dort ebenfalls nicht
  über den Permission-Pfad verifizierbar), das Risiko eines Tippfehlers
  im String-Literal ist daher gering, aber nicht durch einen grünen Lauf
  über genau diesen Zweig belegt.
