---
status: open
type: step-plan
task: sql-file-execution
step: 003
corrects: null
title: "Local SQL script file intake and encoding contract"
epic: EPIC-01
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: GPT-5
created_by_model_knowledge_cutoff: not provided by runtime
created_at: 2026-08-29T08:00:05+02:00
related_to: ["step-002"]
---

# Step 003: Local SQL script file intake and encoding contract

## Context

- **Task:** `sql-file-execution`
- **Epic:** `EPIC-01` from `roadmap.md` — the GO/batch foundation is covered
  by `step-001` and `step-002`; the validated local SQL file intake remains
  open.
- **Concept reference:** `konzept.md` sections “Dateipfad-Auflösung &
  Sicherheits-Checks”, “Multi-Batch & GO-Unterstützung”, and the file-error
  criteria in the Definition of Done.

## Current Project State (JIT Context)

After the approved `step-002`, `SqlBatch` and `SqlScriptBatchSplitter` are the
reusable GO-aware batch foundation. They already accept text and preserve
source lines and repeat counts; this step therefore supplies only the
preceding file content and does not change the splitter.

`QueryExecutionOptions` in `src/SqlToAi/Configuration/SqlToAiOptions.cs` is
the existing configuration anchor. It currently contains row limits and the
command timeout; `MaxScriptFileSizeBytes` belongs in this type and in the
embedded `src/SqlToAi/appsettings.json` template.

`Result<T>` is the established error-boundary type. `SqlToAiError` is the
central catalog, is used by many database and MCP components, and is already
at its public-member limit. The new file factory methods therefore remain
`internal`; the existing public contract and all existing error factories
remain unchanged.

`ConfigurationResolver.ResolveValue` is not a suitable replacement: it only
loads configured metadata SQL files, resolves relative paths against
`AppContext.BaseDirectory`, and throws `FileNotFoundException`. The new reader
requires `Environment.CurrentDirectory`, extension and size validation,
encoding detection, and `Result<T>` errors. Changing that existing metadata
path would unnecessarily affect the single-query/configuration path.

The existing temporary-directory and configuration-binding tests in
`tests/SqlToAi.Tests/Configuration` and the centralized catalog tests in
`tests/SqlToAi.Tests/Domain` will be extended. A new reader test remains under
`tests/SqlToAi.Tests/Database`, close to the existing database helpers.

## Intention

Establish an internal, reusable file reader that fully validates a local
`.sql` file before any later batch execution and returns an immutable file
object containing the resolved path, text, and detected encoding. All expected
file errors are represented through the existing `Result<T>`/`SqlToAiError`
path.

This step includes no database connection, safety or transaction logic, batch
execution, MCP registration, or Markdown report generation. A later
`ScriptExecutionService` can call this reader and then the existing
`SqlScriptBatchSplitter` without rebuilding file access or batch splitting.

## Concrete Changes

### File 1: `src/SqlToAi/Database/SqlScriptFile.cs` (new)

- **What:** Add an internal, sealed immutable `SqlScriptFile` record with
  `ResolvedPath`, `Text`, and `EncodingName`.
- **Why:** The later execution service needs the already validated absolute
  path and decoded content; a named value object prevents parallel tuple or
  metadata structures.

### File 2: `src/SqlToAi/Database/SqlScriptFileReader.cs` (new)

- **What:** Implement an internal static `SqlScriptFileReader` with
  `Read(string? filePath, QueryExecutionOptions options)` returning
  `Result<SqlScriptFile>`.
- **What:** Return `InvalidParameters` for empty or invalid local paths,
  resolve relative paths against `Environment.CurrentDirectory`, and reject
  non-local URL/UNC targets. Check the `.sql` extension case-insensitively.
- **What:** Open the file through a read stream, check
  `MaxScriptFileSizeBytes` before reading its content, and report an exceeded
  limit with `FileTooLarge`. Report missing files as `FileNotFound` and
  unreadable files as a catalogued infrastructure error.
- **What:** Detect UTF-8 with and without BOM and UTF-16 little/big endian with
  BOM. For BOM-less bytes, first decode strictly as UTF-8 and fall back to the
  Windows ANSI code page when the UTF-8 sequence is invalid. Register the
  available `System.Text.Encoding.CodePages` provider once; do not silently
  suppress decoding errors.
- **Why:** This deterministically handles the concept's local path, size, and
  encoding contract before splitting, while preserving the source text for
  `SqlScriptBatchSplitter`.
- **Note:** Keep private helpers for path resolution, byte reading, and
  encoding selection small and flat; add no public interface and do not change
  `ConfigurationResolver`.

### File 3: `src/SqlToAi/Configuration/SqlToAiOptions.cs` (around line 164)

- **What:** Extend `QueryExecutionOptions` with the documented property
  `long MaxScriptFileSizeBytes { get; set; }`, defaulting to 10 MB
  (10,485,760 bytes).
- **Why:** The limit must be bindable through `IOptions<SqlToAiOptions>` and
  must not be a magic value in the file reader.

### File 4: `src/SqlToAi/appsettings.json` (around line 50)

- **What:** Add `QueryExecution.MaxScriptFileSizeBytes` with the same 10 MB
  default to the embedded and copied factory template.
- **Why:** Startup migration can then add the new option automatically to
  existing configurations.

### File 5: `src/SqlToAi/Domain/SqlToAiError.cs` (around lines 10–58)

- **What:** Add the three catalogue codes `FileNotFoundCode`,
  `FileTooLargeCode`, and `InvalidFileExtensionCode` as the next available
  error codes `SQL-AI-0111` through `SQL-AI-0113`, together with matching
  `internal` factory methods carrying path/size context.
- **Why:** File validation needs stable, testable codes and must not leak
  unstructured exceptions through the later tool boundary. Keep the methods
  internal so `SqlToAiError` remains within its existing public-member budget.

### File 6: `Directory.Packages.props` and `src/SqlToAi/SqlToAi.csproj`

- **What:** Centrally version and reference the direct
  `System.Text.Encoding.CodePages` dependency at the .NET 10 package level.
- **Why:** The required Windows ANSI code page must be explicitly available;
  the dependency should not be assumed only through a transitive package.

### File 7: `tests/SqlToAi.Tests/Database/SqlScriptFileReaderTests.cs` (new)

- **What:** Add an xUnit v3 test type marked with a `// @covers` sentinel and
  an isolated temporary test directory.
- **What:** Cover empty paths, relative resolution against the current working
  directory, absolute paths, and case-insensitive `.SQL` extensions.
- **What:** Cover `FileNotFound`, `InvalidFileExtension`, the size boundary
  (exactly at the limit succeeds, one byte over the limit fails), and rejection
  of non-local path forms; assert stable codes and meaningful context.
- **What:** Add separate or data-driven tests for UTF-8 without BOM, UTF-8 with
  BOM, UTF-16 little-endian, UTF-16 big-endian, and Windows ANSI bytes; assert
  the returned content, path, and encoding metadata.
- **Why:** These tests secure the complete file-intake contract without a
  database connection and prevent later execution steps from reimplementing
  path or encoding logic.

### File 8: `tests/SqlToAi.Tests/Configuration/SqlToAiOptionsTests.cs`

- **What:** Test the default value of `MaxScriptFileSizeBytes` and binding of
  an overridden `QueryExecution.MaxScriptFileSizeBytes` JSON value.
- **Why:** Configuration and the reader limit must use the same bindable
  anchor.

### File 9: `tests/SqlToAi.Tests/Domain/SqlToAiErrorTests.cs`

- **What:** Add one catalogue test each for `FileNotFound`, `FileTooLarge`,
  and `InvalidFileExtension`, including code and context assertions.
- **Why:** Direct assertions protect the new stable codes against accidental
  code or message drift.

### File 10: `README.md` and `docs/architecture-spec.md`

- **What:** Extend the `QueryExecution` configuration description with
  `MaxScriptFileSizeBytes` and its 10 MB default. Add the three new file errors
  `SQL-AI-0111` through `SQL-AI-0113`, their causes, and their meanings to the
  architecture error catalogue.
- **Why:** Configuration and error contracts must stay synchronized with the
  source and embedded template under the project rules.
- **Boundary:** Do not document the complete, not-yet-registered
  `sql_execute_file` MCP tool; its transport and report contract belongs to
  later roadmap epics.

## Tests

- [ ] `SqlScriptFileReaderTests` — relative/absolute local paths, case-insensitive
  `.sql`, empty/invalid paths, and non-local path forms.
- [ ] `SqlScriptFileReaderTests` — missing file, wrong extension, and size limit
  with stable `SqlToAiError` codes.
- [ ] `SqlScriptFileReaderTests` — UTF-8, UTF-8 BOM, UTF-16 LE, UTF-16 BE, and
  Windows ANSI decoding with preserved script text.
- [ ] `SqlToAiOptionsTests` — default and JSON binding of
  `MaxScriptFileSizeBytes`.
- [ ] `SqlToAiErrorTests` — codes and message context for the three file
  errors.
- [ ] After all changes, the coder runs the complete test command exactly once
  before the code commit: `dotnet test SqlToAi.slnx`; the green evidence is
  recorded in `step-result.md`.

## Definition of Done

- [ ] All concrete changes are implemented without anticipating execution,
  guardrail, transaction, MCP, or report logic.
- [ ] The file reader returns a complete `SqlScriptFile` for valid local files
  and a `Result<T>` with `SqlToAiError` for expected intake failures.
- [ ] `MaxScriptFileSizeBytes` is synchronized in the options type, JSON
  template, and documentation.
- [ ] Build command from the Tech-Stack Note is green:
  `dotnet build SqlToAi.slnx`.
- [ ] Test command from the Tech-Stack Note is green:
  `dotnet test SqlToAi.slnx`, run exactly once after all changes and before the
  code commit, with evidence in the Step Result.
- [ ] A commit is made on the current branch using a German imperative and the
  `[sql-file-execution]` suffix.
- [ ] `tasks/sql-file-execution/step-003/step-result.md` is written.
- [ ] `status` in `step-plan.md` is changed from `open` to
  `done (pending audit)`.
- [ ] With green coder evidence, the critic does not repeat the complete test
  run; the critic reviews independently and runs only the focused
  `SqlScriptFileReaderTests` subset if the encoding/path risk justifies a
  targeted execution.

## Rules-Refs

- `.agents/rules/AiNetLinter-McpWorkflow.mdc#Verbindliche Priorität` — perform
  semantic C# analysis and linter checks through the matching AiNetLinter MCP
  before text inspection or after changes.
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — keep concrete classes sealed,
  nullable enabled, and error paths visible; use input records from five
  parameters onward.
- `.agents/rules/AiNetLinter.mdc#Grenzwerte (Produktion)` — respect file,
  method, parameter, coupling, and public-member budgets.
- `.agents/rules/AiNetLinter.mdc#agent-resilience` — avoid silent catches,
  blocking tasks, and invisible error paths.
- `.agents/rules/SqlToAiRichtlinien.mdc#2. Architektur- & Guardrail-Konzepte` —
  use local, standardised error handling and security-oriented input limits.
- `.agents/rules/SqlToAiRichtlinien.mdc#3. Windows-Umgebung & Tool-Regeln` —
  use PowerShell/Windows-compatible path and test assumptions.
- `.agents/rules/SqlToAiRichtlinien.mdc#4. Updates, Dokumentation & Sprachen` —
  provide xUnit v3 coverage, English repository artefacts, JSON defaults, and
  synchronized documentation.
- `.agents/rules/SqlToAiRichtlinien.mdc#5. Qualitätsdrift-Prävention & Tech Debt` —
  maintain zero warnings, the Result pattern, and the AiNetLinter quality
  gate.

## Known Exceptions

- None known. This step needs no SQL Server integration; its tests use local
  temporary files and exercise only the intake boundary.

## Code Sketch (Optional)

```csharp
internal sealed record SqlScriptFile(string ResolvedPath, string Text, string EncodingName);

internal static class SqlScriptFileReader
{
    public static Result<SqlScriptFile> Read(string? filePath, QueryExecutionOptions options);
}
```

## Notes

The existing `SqlScriptBatchSplitter` remains the only location for GO
semantics; the new reader returns decoded source text and file metadata only.
`TD-001` is not automatically planned because the Tech-Debt index marks it as
not auto-fixable and outside this file area. No further steps are planned.
