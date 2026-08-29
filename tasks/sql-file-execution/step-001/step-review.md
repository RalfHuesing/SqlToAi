---
status: done
type: step-review
task: sql-file-execution
step: 001
epic: EPIC-01
step_type: single
reviewed_by: kritiker
reviewed_by_model: GPT-5
reviewed_by_model_knowledge_cutoff: not provided by runtime
reviewed_at: 2026-08-29T07:31:22+02:00
verdict: issues
tech_debt_ids: [TD-001]
---

# Review Step 001: GO-aware SQL script batch splitter foundation

## Verdict

- [ ] **approved** — all four review levels are clear
- [x] **issues** — correction required for the CRITICAL and MAJOR findings below
- [ ] **blocked** — no user or infrastructure decision is required

## Geprüft

- [x] Plan-Erfüllung: the committed files, internal surface, scope boundaries, CodeMap pointer, step documentation, and commit conventions were checked against `step-plan.md` and `step-result.md`.
- [x] Rules-Konformität: the three Rules-Refs in the plan were checked, including AiNetLinter MCP-first symbol, impact, reference, metric, violation, safeguard, and duplicate queries.
- [x] Logische Korrektheit: the normal, comment, literal, repeat-count, and source-range cases were inspected; a concrete nested-comment case was reproduced against the built assembly.
- [x] Konzept-Treue: the implementation remains an internal splitter foundation and does not change the single-query path or implement a non-goal, but its robust block-comment handling is incomplete.
- [x] Build: the Coder's green `dotnet build SqlToAi.slnx` evidence was accepted and not repeated.
- [x] Tests: the Coder's green `dotnet test SqlToAi.slnx` evidence (548/548) and focused 11-test evidence were accepted and the full command was not repeated.

## Befund

### Plan-Erfüllung

The two production files and the focused test class exist with the planned internal API, ordinary `GO` variants, repeat-count metadata, line ranges, comment/literal handling, empty-section handling, and source preservation. The CodeMap pointer, step result, status transition, code commit, and separate documentation commit are present, and unchanged single-query files are absent from the commit diff. The planned requirement that `GO` inside multi-line block comments must never split is not met for nested T-SQL block comments (Finding 1), and the test suite has no regression case for that requirement.

### Rules-Konformität

`#nullable enable`, the sealed `SqlBatch` record, the static splitter, namespace mapping, method/file size limits, and the `// @covers` sentinel comply with the referenced rules. However, AiNetLinter `metrics_lookup` reports `AddBatch` at `src/SqlToAi/Database/SqlScriptBatchSplitter.cs:48` with five effective parameters against the production limit of four (`MaxMethodParameterCount`); the general `get_violations` and `safeguard` calls did not surface this metric violation (Finding 2).

### Logische Korrektheit

The implemented state machine is correct for the covered non-nested strings, line comments, block comments, quoted identifiers, valid/invalid repeat counts, and CRLF/LF preservation. It loses block-comment nesting depth: with `/* outer`, `/* inner */`, `GO`, `*/`, `SELECT 1`, `GO`, `SELECT 2`, the built implementation returns three batches (`1-2`, `4-5`, and `7-7`) instead of two; the first `GO` is still inside the outer comment. The same missing nesting also prevents valid nested trailing block comments from being recognized as separator-line comments. The focused tests are therefore meaningful for the listed cases but incomplete for the robust T-SQL comment contract (Finding 1).

### Konzept-Treue (Ebene 4)

The result stays within the Foundation-Step scope, introduces no public API or file I/O, preserves the existing single-query safety path, and implements no declared non-goal. The intended `GO`-aware multi-batch capability from `konzept.md` is nevertheless incomplete because valid nested block comments can change batch boundaries (Finding 1).

### Build-/Test-Status

```text
dotnet build SqlToAi.slnx → green (Coder evidence: 0 warnings, 0 errors)
dotnet test tests/SqlToAi.Tests --filter FullyQualifiedName~SqlScriptBatchSplitterTests → green (Coder evidence: 11 tests, 0 failures)
dotnet test SqlToAi.slnx → green (Coder evidence: 548 tests, 0 failures, 0 skipped; not repeated)
```

### AiNetLinter-/Impact-Status

The semantic queries resolved both new symbols and found only the new splitter tests as callers; `SqlBatch` is only consumed by the new splitter. The changed-file `get_violations` result was empty and the Database-scope `safeguard` score was 10/10, while the targeted metric query independently exposed the five-parameter violation in Finding 2. The drift-audit `find_duplicates(scopeDir="src", minTokens=20)` found one exact, pre-existing constructor cluster, recorded as TD-001 below.

## Findings

1. `src/SqlToAi/Database/SqlScriptBatchSplitter.cs:210-228,251-258` — **[CRITICAL] [Logik]** `AdvanceState`/`AdvanceBlockComment` treats the first `*/` as the end of a block comment and does not count nested `/* ... */` levels. A valid T-SQL script can therefore split on a `GO` that is still inside an outer block comment and emit a malformed batch; the focused test suite does not detect it. Microsoft documents nested Transact-SQL block comments as supported ([Microsoft Learn](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/slash-star-comment-transact-sql?view=sql-server-ver17)). **Fix:** track block-comment depth across lines, increment for nested `/*`, decrement for `*/`, and only allow separator detection at depth zero; apply the same nesting-aware scan to `TryReadTrailingComments` and add a regression test with the reproduced seven-line shape.
2. `src/SqlToAi/Database/SqlScriptBatchSplitter.cs:48-53` — **[MAJOR] [Rules]** `AddBatch` has five effective parameters, violating the referenced `AiNetLinter.mdc` production `MaxMethodParameterCount` limit of four and its input-record guidance. **Fix:** group the related line/repeat metadata in a private input record/struct or otherwise reduce the method to at most four parameters without changing behavior, then rerun the targeted metric/violation checks.

## Tech-Debt-Einträge aus diesem Review

- `TD-001` (siehe `../tech-debt.md`) — the existing `PerformanceMeasurementService` and `QueryComparisonService` constructors contain an exact duplicate dependency-initialization shape; consolidation needs architectural judgment and is outside Step-001.
