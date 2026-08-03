---
status: done
type: step-result
task: sql-optimization-tools
step: step-002
epic: EPIC-02
step_type: single
coded_by: coder
coded_by_model: gemini-3.6-flash
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-03T10:20:00+02:00
code_commit_hash: 4c05ee4
status_after: done
blocker_category: n/a
---

# Result Step 002: Ergebnissatz- & Äquivalenzvergleich (sql_compare_queries) implementieren

## Zusammenfassung

Implementierung des neuen MCP-Tools `sql_compare_queries` zur empirischen Prüfung von zwei SQL-Abfragen auf semantische Gleichheit. Die Implementierung umfasst `QueryComparisonResult` und `QueryComparisonArgs` im Domain-Layer, `QueryComparisonService` im Database-Layer sowie die Tool-Registrierung und das Dispatching in `ToolRegistry` und `ToolDispatcher`. Der Vergleich prüft (1) Schemagleichheit via `SchemaOnly`, (2) Zeilenanzahl via `COUNT_BIG(*)`, und (3) DB-seitige Set-Differenzen via `EXCEPT`, um Differenzen ohne Übertragung großer Datenmengen präzise aufzudecken.

## Geänderte Dateien

- `src/SqlToAi/Domain/QueryComparisonResult.cs` (neu) — Modell für Äquivalenz-Status, Counts, Schema-Diffs und Zeilen-Diffs.
- `src/SqlToAi/Domain/QueryComparisonArgs.cs` (neu) — Parameter-Object für saubere Aufruf-Signaturen.
- `src/SqlToAi/Database/IQueryComparisonService.cs` & `QueryComparisonService.cs` (neu) — Core Engine für Schema-Check, Count-Check und `EXCEPT` Set-Differenzen.
- `src/SqlToAi/Mcp/McpConstants.cs` — Konstanten `ToolCompareQueries`, `ArgQueryA`, `ArgQueryB`, `ArgParametersA`, `ArgParametersB`, `ArgMaxDiffRows` hinzugefügt.
- `src/SqlToAi/Mcp/ToolRegistry.cs` — Registrierung von `sql_compare_queries` (13. Tool).
- `src/SqlToAi/Mcp/ToolDispatcher.cs` — Routing-Handler für `sql_compare_queries`.
- `src/SqlToAi/Mcp/McpJsonContext.cs` — Native AOT Serialisierungsunterstützung für `QueryComparisonResult` & `QueryComparisonArgs`.
- `src/SqlToAi/Program.cs` — DI-Registrierung für `IQueryComparisonService`.
- `tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs` (neu) — Unit-Tests für Guards, Whitelisting, AccessLevels und Multi-Statement Protection.
- `tests/SqlToAi.Tests/Mcp/ToolDispatcherTests.cs`, `ToolRegistryTests.cs`, `McpHostTests.cs` — Fakes & Tool-Count Assertions aktualisiert.

## Commit

- **Code-Commit-Hash:** `4c05ee4`
- **Message:**
  ```
  feat(database): Ergebnissatz- & Äquivalenzvergleich (sql_compare_queries) implementieren [sql-optimization-tools]

  Refs: tasks/sql-optimization-tools/step-002
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit.

## Build-/Test-Output

```
dotnet build SqlToAi.slnx -> grün
dotnet test SqlToAi.slnx  -> grün (448 Tests, 0 Fehler)
```

## Abweichungen vom Plan

Keine — Plan 1:1 umgesetzt. `QueryComparisonArgs` wurde als Parameter-Object genutzt, um Linter-Regeln bezüglich der maximalen Parameteranzahl und kognitiver Komplexität strikt einzuhalten.

## Beobachtungen

- Durch die Kombination aus exaktem Zeilen-Count-Vergleich und beidseitigem `EXCEPT` (`A EXCEPT B` & `B EXCEPT A`) werden auch Duplikat-Differenzen im Multiset-Ergebnis verlässlich erkannt.

## Bekannte Unschärfen

Keine.

## Falls Status `blocked`

n/a
