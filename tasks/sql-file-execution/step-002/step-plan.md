---
status: open
type: step-plan
task: sql-file-execution
step: 002
corrects: step-001
title: "Fix nested block-comment depth and AddBatch parameter budget"
epic: EPIC-01
estimated_risk: high
step_type: single
items: []
created_by: planer
created_by_model: GPT-5
created_by_model_knowledge_cutoff: not provided by runtime
created_at: 2026-08-29T07:37:25+02:00
related_to:
  - tasks/sql-file-execution/step-001/step-review.md
---

# Step 002: Fix nested block-comment depth and AddBatch parameter budget

## Bezug

- **Task:** `sql-file-execution`
- **Epic:** `EPIC-01` from `roadmap.md` — local SQL script batches with stable source lines and `GO` semantics.
- **Corrected step:** `step-001`, whose implementation and review are the only scope inputs for this fix step.
- **Review reference:** `tasks/sql-file-execution/step-001/step-review.md`, complete `Findings` section.
- **Concept reference:** The `Multi-Batch & GO support` scope carried forward by `tasks/sql-file-execution/step-001/step-plan.md`.

## Aktueller Projektzustand (JIT-Kontext)

`src/SqlToAi/Database/SqlScriptBatchSplitter.cs` is the existing internal splitter introduced by step-001. The review identifies two defects in that file: the scanner treats the first `*/` as the end of a block comment, and `AddBatch` currently has five effective parameters. `TryReadTrailingComments` must receive the same nesting-aware treatment as the line scanner.

The AiNetLinter semantic check confirms that `AddBatch` is at `src/SqlToAi/Database/SqlScriptBatchSplitter.cs:48-62`, has the five-parameter signature reported by the review, and has only two production call sites in the splitter. The splitter is otherwise consumed by the existing `SqlScriptBatchSplitterTests` class. The existing `SqlBatch` data shape and single-query path are outside this correction and must remain unchanged.

## Intention

Make block-comment scanning correct for nested T-SQL comments across line boundaries. A `GO` line is eligible as a separator only while the tracked block-comment depth is zero, and trailing comments use the same nesting semantics.

Resolve the parameter-count finding with one concrete design: introduce a private `readonly record struct BatchMetadata` inside `SqlScriptBatchSplitter` and pass it to `AddBatch`, reducing that method to three effective parameters without changing batch text, source ranges, or repeat counts.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/SqlScriptBatchSplitter.cs` (existing splitter and `AddBatch`)

- **Was:** Replace the boolean/equivalent single-level block-comment state used by `AdvanceState`/`AdvanceBlockComment` with an integer depth that is carried from one source line to the next. Increment for every nested `/*`, decrement for its matching `*/`, and inspect a candidate separator only when the resulting lexical state is at block-comment depth zero. Preserve the existing handling of string literals, escaped quotes, single-line comments, source text, line ranges, and repeat counts.
- **Was:** Apply the same integer-depth scan in `TryReadTrailingComments`. A suffix such as `GO /* outer /* nested */ */` must be accepted as a separator only after both block-comment levels close and only whitespace remains; an unterminated or otherwise invalid suffix must retain the existing non-separator behavior. A `GO` encountered while the depth is positive must never be treated as a separator.
- **Was:** Add a private `readonly record struct BatchMetadata(int StartLine, int EndLine, int RepeatCount)` in the existing splitter, change `AddBatch` to accept `List<SqlBatch>`, `StringBuilder`, and `BatchMetadata`, and update its two existing call sites to construct that metadata. Do not add an overload, public API, or a fifth effective parameter.
- **Warum:** This directly fixes the critical nested-comment regression and the major `MaxMethodParameterCount` violation while reusing the step-001 splitter and preserving all approved behavior.

### Datei 2: `tests/SqlToAi.Tests/Database/SqlScriptBatchSplitterTests.cs` (existing splitter tests)

- **Was:** Add a focused nested-comment regression test using this exact seven-line shape, with the line endings supplied by the existing test convention:

  ```text
  SELECT 1;
  GO
  /* outer
     /* nested
     */
     GO
  */
  ```

  Assert that the top-level `GO` on line 2 creates the first batch, the `GO` on line 6 remains comment text in the second batch, the second batch spans source lines 3-7, and no malformed extra batch is emitted.
- **Was:** Add or extend a focused trailing-comment test for `GO /* outer /* nested */ */`, asserting that it is recognized as one valid separator and that the following SQL starts a new batch with the expected one-based source range.
- **Was:** Keep the existing `// @covers SqlToAi.Database.SqlScriptBatchSplitter` sentinel and all step-001 tests; do not broaden the test file to unrelated SQL execution behavior.
- **Warum:** The first regression protects depth carried across lines and the second protects the matching trailing-comment parser path named by the review.

## Tests

- [ ] Run the focused regression suite after all code and test edits: `dotnet test tests/SqlToAi.Tests --filter FullyQualifiedName~SqlScriptBatchSplitterTests`; it must include the seven-line nested-comment case and the nested trailing-comment separator case.
- [ ] Use the AiNetLinter MCP before each semantic C# query and after the edits: `get_feature_context`/`metrics_lookup` for `SqlScriptBatchSplitter` and `AddBatch`, `get_impact` for the splitter, and `get_violations` scoped to `src/SqlToAi/Database/SqlScriptBatchSplitter.cs`. Verify `AddBatch` has at most four effective parameters, the expected test-only impact remains, and no relevant violations are reported.
- [ ] Run `dotnet build SqlToAi.slnx` after all changes; it must be green with zero warnings and zero errors.
- [ ] Run `dotnet test SqlToAi.slnx` once after all changes and before the code commit; record the green result in `step-002/step-result.md`.
- [ ] If the coder provides a green full-test result, the critic must not repeat `dotnet test SqlToAi.slnx`; the critic may run only the focused splitter test command when a concrete residual risk warrants it.

## Definition of Done

- [ ] The scanner tracks nested block-comment depth across lines, recognizes `GO` only at depth zero, and preserves the established literal/comment/text/range semantics.
- [ ] `TryReadTrailingComments` uses the same nesting-aware depth semantics and correctly accepts closed nested trailing block comments.
- [ ] The exact seven-line nested-comment regression and the nested trailing-comment regression are present and green.
- [ ] `AddBatch` uses the private `BatchMetadata` input record and has no more than four effective production parameters; behavior and the `SqlBatch` shape are unchanged.
- [ ] `dotnet build SqlToAi.slnx` is green with zero warnings and errors.
- [ ] `dotnet test SqlToAi.slnx` was run exactly once by the coder after all changes and before the code commit, with its green result recorded in `step-002/step-result.md`; the critic follows the no-repeat rule above.
- [ ] The targeted AiNetLinter MCP checks show no relevant rule violation and the impact remains limited to the existing splitter tests.
- [ ] No roadmap, new epic, unrelated production/documentation file, existing single-query implementation, or out-of-scope behavior is changed. The coder writes `step-002/step-result.md`, updates this plan to the workflow's completed status, and creates one German imperative Conventional Commit for this correction with the `[sql-file-execution]` suffix.

## Rules-Refs

- `.agents/rules/AiNetLinter-McpWorkflow.mdc#Verbindliche Priorität` — C# symbol, impact, and violation questions must be answered with the appropriate AiNetLinter MCP tool first; complete results must not be redundantly rechecked with text search.
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — use a private input record when parameter grouping is required, keep concrete types sealed where applicable, and retain `#nullable enable`.
- `.agents/rules/AiNetLinter.mdc#Grenzwerte (Produktion)` — `MaxMethodParameterCount` is 4; the corrected `AddBatch` must satisfy this limit and remain within the method/file budgets.
- `.agents/rules/AiNetLinter.mdc#test-coverage` — preserve the existing `// @covers` test sentinel for the splitter.
- `.agents/rules/SqlToAiRichtlinien.mdc#3. Windows-Umgebung & Tool-Regeln` — use the project solution build/test commands in the Windows PowerShell workflow.
- `.agents/rules/SqlToAiRichtlinien.mdc#4. Updates, Dokumentation & Sprachen (Updates, Documentation & Languages)` — functional changes require xUnit v3 coverage and code/documentation remains in English.
- `.agents/rules/SqlToAiRichtlinien.mdc#5. Qualitätsdrift-Prävention & Tech Debt (AiNetLinter)` — maintain zero warnings, linter conformity, and semantic MCP verification.

## Bekannte Ausnahmen

- None. The focused tests are deterministic in-memory tests; the critic's full-suite non-repeat is a workflow rule conditioned on the coder's green evidence, not a test exception.

## Code-Skizze (optional)

```csharp
private readonly record struct BatchMetadata(int StartLine, int EndLine, int RepeatCount);

// Keep the existing staticness and behavior; only the metadata is grouped.
private static void AddBatch(
    List<SqlBatch> batches,
    StringBuilder batchText,
    BatchMetadata metadata);

// Conceptual scanner state: the counter survives line boundaries.
if (blockCommentDepth > 0)
{
    if (StartsWith("/*")) blockCommentDepth++;
    else if (StartsWith("*/")) blockCommentDepth--;
}
```

## Notes

- This is a fix-only step for the two findings in `step-001/step-review.md`; do not re-plan approved step-001 behavior or observations outside the `Findings` section.
- Keep separator lines out of batch text and source ranges as established by step-001. Nested comments must remain byte-for-byte/textually preserved apart from the existing separator-line removal rule.
- The private metadata record is the single selected solution for the parameter finding; do not leave competing implementation variants for the coder to choose.
- Roadmap and task scope remain unchanged. No new Epic, file-execution service, parser integration, or SQL safety-path change belongs in this correction.
