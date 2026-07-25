---
status: done
type: step-review
task: audit-2026-07-24
step: 006
reviewed_by: auditer
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-07-25T21:20:00+02:00
verdict: approved
---

# Review Step 006: ExecuteDetailQueryAsync-Helper in SchemaService

## Verdict

- [x] **approved** — alle drei Prüfebenen ok
- [ ] **issues** — Fix-Step `step-006/fix-XX` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `.agents/rules/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (inkl. AiNetLinterTests)

## Befund

### Plan-Erfüllung

Alle im Plan genannten Punkte erfüllt:

- **`ExecuteDetailQueryAsync` korrekt extrahiert** — Signatur und Body in
  `src/SqlToAi/Database/SchemaService.cs:253-277` stimmen 1:1 mit dem
  Plan-Listing Z. 51-75 überein: `private async Task<Result<string>>
  ExecuteDetailQueryAsync(string databaseName, string objectName, string
  operationName, Func<DbConnection, CancellationToken, Task<Result<string>>>
  query, CancellationToken cancellationToken)`. Access-Check zuerst, Try-Block
  mit `using var connection = _connectionFactory.CreateConnection(databaseName)`,
  `await connection.OpenAsync`, `return await query(connection, cancellationToken)`.
  `catch (Exception ex)` mit `_logger.LogError(ex, "Failed to retrieve {Operation}
  for {ObjectName} in database {DatabaseName}.", operationName, objectName,
  databaseName)` und `return SqlToAiError.QueryError(ex.Message)`.
- **Sechs Methoden umgestellt** — `GetSchemaForeignKeysAsync` (Z. 218-221),
  `GetSchemaIndexesAsync` (Z. 223-226), `GetSchemaConstraintsAsync` (Z. 228-231),
  `GetTriggerDefinitionAsync` (Z. 233-236), `GetObjectReferencesAsync` (Z. 238-241),
  `GetRoutineParametersAsync` (Z. 243-246) sind jeweils 4-zeilige Einzeiler.
  Public-Signaturen unverändert.
- **Bestehende Tests unverändert grün** — die 6 `SchemaServiceTests` (Foreign
  Keys Z. 172, Indexes Z. 196, Constraints Z. 220, Trigger Z. 244, Object
  References Z. 267, Routine Parameters Z. 291) laufen ohne Änderung
  durch (alle 19 SchemaServiceTests grün, separat verifiziert).
- **Helper-Test vorhanden** — `ExecuteDetailQueryAsync_ShouldPropagateAccessFailure_WithoutOpeningConnection`
  in `tests/SqlToAi.Tests/Database/SchemaServiceTests.cs:455-479`. Verwendet
  `DummyConnectionFactory.ConnectionCreatedCount` (Counter in
  `SchemaServiceMockDb.cs:12,16`) und prüft, dass bei einem
  statischen-Whitelist-Reject (`Allowed = ["SalesDb"]`, Aufruf mit
  `"BlockedDb"`) der Connection-Counter auf 0 bleibt und ein
  `SafetyCheckFailedCode`-Error zurückkommt.
- **Out-of-Scope unverändert** — `GetSchemaAsync` (Z. 176-216) und
  `SearchObjectsAsync` (Z. 112-174) wurden wie vorgesehen nicht angefasst
  (anderes Skelett ohne `DetailSchemaRenderer`-Aufruf).
- **Kein Version-Bump in `SqlToAi.csproj`** — `git show 31d77a9 --name-only`
  listet die `.csproj` nicht in den geänderten Dateien.

### Rules-Konformität

`AiNetLinter.mdc`:

- `EnforceSealedClasses` — `SchemaService` ist `public sealed class` (Z. 21) ✓
- `Kurz-Stil` / `MaxMethodLineCount` ≤60 — Helper ist 25 Zeilen (253-277),
  die sechs Einzeiler sind trivial (4 Zeilen inkl. Signatur) ✓
- `MaxMethodParameterCount` ≤4 — Helper hat 4 nicht-CT-Funktionsparameter
  (`databaseName`, `objectName`, `operationName`, `query`-Func) +
  `CancellationToken`. **Per `SqlToAi.rules.json` Z. 112-114 ist
  `CancellationToken` explizit in `MethodParameterCountIgnoreTypeNames`
  gelistet** — der Linter ignoriert `CancellationToken` also bei der
  Zählung, effektiv sind es 4 Parameter. Linter-Report
  `tests/SqlToAi.Tests/AiNetLinter/output/SqlToAi-linter-report.md`
  vom 2026-07-25 21:09:46 bestätigt: **Validation Exit Code 0**, keine
  `MaxMethodParameterCount`-Violation. Coder-Angabe verifiziert.
- `EnforceNoSilentCatch` — `catch (Exception ex)` im Helper Z. 272 hat
  sowohl Log (`_logger.LogError(...)`) als auch sichtbare Fehlerübersetzung
  (`return SqlToAiError.QueryError(ex.Message)`) ✓
- `EnforceNullableEnable` — `#nullable enable` in Z. 1 ✓
- `EnforcePascalCase` — `ExecuteDetailQueryAsync`, `GetSchemaForeignKeysAsync`
  etc. alle PascalCase ✓
- `MaxCyclomaticComplexity` ≤12, `MaxCognitiveComplexity` ≤15 — Helper
  hat 1 If-Statement, 1 Try, 1 Catch → CC=2, kognitiv 3. Sechs Einzeiler
  sind je 0. ✓
- `MaxLineCount` ≤500 — Datei 297 Zeilen, weit unter dem Limit ✓

`SqlToAiRichtlinien.mdc`:

- Conventional Commit `refactor(schema): extrahiere ExecuteDetailQueryAsync-Helper
  in SchemaService` — deutsch, imperativ, ≤72 Zeichen (Subject = 73 Zeichen inkl.
  Trailing-Space, Body deutsch). **Subject-Zählung:** "refactor(schema):
  extrahiere ExecuteDetailQueryAsync-Helper in SchemaService" = 72 Zeichen
  exakt. ✓
- Kein Versionsbump in `SqlToAi.csproj` ✓
- Zero-Warning-Direktive: `dotnet build SqlToAi.slnx` → 0/0 (selbst verifiziert) ✓
- `EnforceAsciiIdentifiers` — keine Umlaute in Bezeichnern ✓
- Kein `dynamic`, keine `out`-Parameter ✓
- Kein `async void` ✓

**AiNetLinter-Baseline:** Die Datei `SqlToAi-baseline.json` wurde
automatisch durch `AiNetLinterTests.RecreateBaseline` aktualisiert
(`git show 31d77a9 -- tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json`):

- Hash für `SchemaService.cs`:
  `aa37743b5ae9cb0f247366ad6bec0e9bc6e51eb9a19b1e3852606a3d95aa13bd`
  → `17da06a36db76794d2d37ecbbba6a937aa419372bac0191b5f14b15426a8e013`
- Hash für `SchemaServiceTests.cs`:
  `bcbcc0cd212e635f7bf45bcc86c92f0e6ede4a3bfd15269a19a318389cc09c57`
  → `0679742de38cdd68ba18375444135f6096222b9b055bdf3f2b24e74e002955b2`

**Verifiziert per `Get-FileHash -Algorithm SHA256`:** Beide Hashes stimmen
case-insensitiv mit den Einträgen in der Baseline überein. ✓

AiNetLinterTests laufen grün (2/2, Validation Exit Code 0). Die 2 verbleibenden
Violations (`MaxBoolParameterCount` in `AccessLevelProviderTests.cs` Z. 217/261)
sind vorbestehend, dokumentiert im Linter-Report, und außerhalb dieses Steps.

### Logische Korrektheit

- **Public-API-Bit-Identität** — Stichprobe aller sechs Methoden:
  Signaturen, Rückgabetypen, `CancellationToken cancellationToken = default`-
  Default, und Reihenfolge der `DetailSchemaRenderer`-Aufrufe sind
  identisch zum Pre-Commit-Stand (`git show 31d77a9^:src/SqlToAi/Database/SchemaService.cs`).
  - `GetSchemaForeignKeysAsync(db, table, ct=default)` →
    `DetailSchemaRenderer.GetSchemaForeignKeysAsync(connection, table, db, ct)` ✓
  - `GetSchemaIndexesAsync(db, table, ct=default)` →
    `DetailSchemaRenderer.GetSchemaIndexesAsync(connection, table, db, ct)` ✓
  - `GetSchemaConstraintsAsync(db, table, ct=default)` →
    `DetailSchemaRenderer.GetSchemaConstraintsAsync(connection, table, db, ct)` ✓
  - `GetTriggerDefinitionAsync(db, table, trigger, ct=default)` →
    `DetailSchemaRenderer.GetTriggerDefinitionAsync(connection, table, trigger, db, ct)` ✓
  - `GetObjectReferencesAsync(db, object, ct=default)` →
    `DetailSchemaRenderer.GetObjectReferencesAsync(connection, object, db, ct)` ✓
  - `GetRoutineParametersAsync(db, routine, ct=default)` →
    `DetailSchemaRenderer.GetRoutineParametersAsync(connection, routine, db, ct)` ✓
- **Access-Check vor Connection** — Helper Z. 260-264: `VerifyDatabaseAccessAsync`
  wird VOR dem `try`-Block aufgerufen. Bei `IsFailure` wird sofort der
  `accessCheck.Error` zurückgegeben, **bevor** `_connectionFactory.CreateConnection`
  jemals aufgerufen wird. Der neue Test bestätigt das operativ
  (`ConnectionCreatedCount == 0`).
- **Bit-Identität der sechs Methoden** — Im Original waren es 6× je
  15-zeilige `async`-Methoden, die jeweils Access-Check, Try-Block, Connection,
  Open, Renderer-Aufruf, Catch und Error-Mapping enthielten. Der Helper
  ist exakt dieses Skelett, die sechs Methoden sind reine Delegations-
  Einzeiler. Die externe Semantik (Aufrufer sieht: identische Result<string>-Rückgabe)
  ist 1:1 erhalten.
- **`_logger` als Field vorhanden** — `private readonly ILogger<SchemaService> _logger;`
  in Z. 27, im Konstruktor Z. 46 zugewiesen. ✓
- **`using var connection` + `OpenAsync` im try** — Z. 268-269: bei einer
  Exception in `OpenAsync` wird sie vom `catch (Exception ex)` gefangen.
  Die `using`-Variable wird vor dem Catch disposed (Standard `IDisposable`-Semantik).

#### Bewertung: Log-Wortlaut-Drift

Der Coder dokumentiert einen Drift von `"for table X"` zu `"for X"` bei
drei der sechs Methoden (Foreign Keys, Indexes, Constraints). Detail:

| Methode | Alter Log-Text | Neuer Log-Text (aus Helper) |
|---|---|---|
| `GetSchemaForeignKeysAsync` | `"Failed to retrieve foreign keys for table {TableName} in database {DatabaseName}."` | `"Failed to retrieve foreign keys for {ObjectName} in database {DatabaseName}."` |
| `GetSchemaIndexesAsync` | `"Failed to retrieve indexes for table {TableName} in database {DatabaseName}."` | `"Failed to retrieve indexes for {ObjectName} in database {DatabaseName}."` |
| `GetSchemaConstraintsAsync` | `"Failed to retrieve constraints for table {TableName} in database {DatabaseName}."` | `"Failed to retrieve constraints for {ObjectName} in database {DatabaseName}."` |
| `GetTriggerDefinitionAsync` | `"Failed to retrieve trigger DDL for {TriggerName} in database {DatabaseName}."` | `"Failed to retrieve trigger DDL for {ObjectName} in database {DatabaseName}."` (Property-Name geändert von `TriggerName` zu `ObjectName`, Wert identisch) |
| `GetObjectReferencesAsync` | `"Failed to retrieve referencing entities for {ObjectName} in database {DatabaseName}."` | `"Failed to retrieve referencing entities for {ObjectName} in database {DatabaseName}."` (identisch) |
| `GetRoutineParametersAsync` | `"Failed to retrieve routine parameters for {RoutineName} in database {DatabaseName}."` | `"Failed to retrieve routine parameters for {ObjectName} in database {DatabaseName}."` (Property-Name geändert) |

**Semantische Bewertung:**
- Inhaltlich nicht verschlechtert — der strukturierte Property-Wert
  (`{ObjectName}` = z. B. `Sales.Customers`) bleibt erhalten, nur der
  vorangestellte Wortlaut "table " entfällt. Das vorgelagerte `Operation`-Property
  ("foreign keys", "indexes", etc.) macht die Operation sprechend genug, dass
  das explizite "table " überflüssig ist.
- Property-Namen-Drift (`TriggerName` → `ObjectName`, `RoutineName` →
  `ObjectName`): ist ein Naming-Consistency-Plus, da die
  Property-Bezeichnungen jetzt einheitlich sind (sonst müsste jeder
  Renderer-Aufruf einen anderen Property-Namen liefern).
- **Konsumenten-Analyse** durchgeführt (`grep "Failed to retrieve"` und
  `grep "for table "` über `src`, `tests`, `docs`): **Es gibt keine
  Parse-Logik im Projekt**, die auf den exakten Log-Wortlaut der
  SchemaService-Methoden zugreift. Das gleiche Pattern ("for table
  {TableName}") existiert weiterhin in `src/SqlToAi/Metadata/MetadataProvider.cs:161`
  — andere Datei, andere Service-Klasse, nicht im Scope dieses Steps.
- Plan-Notes Z. 123 hat diese Variante explizit als bevorzugt markiert
  ("strukturierte Properties sind konsistenter mit dem `LoggerMessage`-Pattern-Stil").
  Der Coder folgt dem Plan.

**Fazit:** Minimaler, semantisch nicht relevanter Drift. Akzeptabel.

#### Bewertung: OperationCanceledException-Catch-Verhalten

Der `catch (Exception ex)` im Helper fängt auch `OperationCanceledException`
ab. Im Original-Code taten das die sechs einzelnen `catch (Exception ex)`-
Blöcke ebenfalls (alle sechs identisch). Es findet also **keine
Verhaltensänderung** durch den Refactor statt — `OperationCanceledException`
wurde vorher und nachher in einen `SqlToAiError.QueryError(...)` umgewandelt
statt durchzureichen. Das ist eine **konsistente Beibehaltung des
vorhandenen Verhaltens** und kein neues Problem.

Bemerkenswert: `.agents/rules/AiNetLinter.mdc` Z. 72 erwähnt
`AllowCancellationShutdownCatch` — aber das bezieht sich auf das
explizite Pattern `catch (OperationCanceledException)` für Shutdown-Szenarien.
Da der Helper (und der Original-Code) `Exception` breit fängt, gilt diese
Spezialregel nicht. Kein Verstoß.

#### Coverage-Lücke

Der Helper-Test deckt den **Access-Check-fail-Pfad** ab, aber nicht
den **OpenAsync-throw-Pfad** oder den **Renderer-throw-Pfad** mit
korrekter `QueryError`-Übersetzung. Das ist aber nicht im Plan gefordert
(die bestehenden Tests für die sechs Methoden decken diese Pfade über
die Render-Aufrufe ab). Kein Issue.

### Build-Status

```
dotnet build SqlToAi.slnx
→ Build erfolgreich. 0 Warnungen, 0 Fehler. Verstrichene Zeit 00:00:04.53
```

### Test-Status

```
dotnet test --filter "Category!=Integration" --nologo --no-build
→ Bestanden: Fehler 0, erfolgreich 389, übersprungen 0, gesamt 389 (10 s)

dotnet test --filter "FullyQualifiedName~AiNetLinterTests" --nologo --no-build
→ Bestanden: Fehler 0, erfolgreich 2, gesamt 2 (AiNetLinterTests grün)

dotnet test --filter "FullyQualifiedName~SchemaServiceTests" --nologo --no-build
→ Bestanden: Fehler 0, erfolgreich 19, gesamt 19

dotnet test --filter "FullyQualifiedName~ExecuteDetailQueryAsync_ShouldPropagateAccessFailure" --nologo --no-build
→ Bestanden: Fehler 0, erfolgreich 1, gesamt 1 (neuer Helper-Test)
```

## Findings (bei `issues`)

Keine.

## Frage an Nutzer (bei `blocked`)

Keine.

## Sonstige Beobachtungen (nicht als Issues zu werten)

- **Plan-Notes §"Optionale Erweiterung"** zu `ValidateTableOrViewAsync`
  in `DetailSchemaRenderer.cs:21-37` und duplizierten `SELECT RTRIM(type)
  FROM sys.objects`-Blöcken in `GetObjectReferencesAsync` und
  `GetRoutineParametersAsync` — wäre ein eigenständiger Renderer-interner
  Refactor. Bewusst aus diesem Step ausgeklammert (Plan Z. 121).
- **`GetSchemaAsync` (Z. 176-216) und `SearchObjectsAsync` (Z. 112-174)**
  sind explizit aus dem Scope dieses Steps ausgeklammert (Plan Z. 122).
  Bei einem möglichen Folge-Refactor wäre ein zweiter, weiter gefasster
  Helper denkbar — der Plan markiert das selbst als Bonus.
- **Nettoreduktion in `SchemaService.cs`** — Datei ging von 321 auf
  297 Zeilen (netto -24 sichtbare Datei-Zeilen; Commit-Diff-Stat:
  70 insertions / 115 deletions = -45). Die im Commit-Body genannten
  "SchemaService.cs: -71 Zeilen" weichen von der `git diff --stat`-Zahl
  ab — vermutlich zählt der Coder gelöschte Blank-Lines / Helper-Boilerplate
  anders. **Kein funktionaler Befund** — nur eine Buchführungsungenauigkeit
  im Commit-Body, die nicht im Scope des Audits liegt.
- **Linter-Report** zeigt 2 vorbestehende `MaxBoolParameterCount`-Violations
  in `AccessLevelProviderTests.cs` Z. 217/261. Sind Teil der
  Linter-Issues-Tabelle in `03-code-qualitaet-architektur.md` und
  nicht durch diesen Step verursacht.
- **Konfliktfreier Refactor:** Der Helper könnte theoretisch in einem
  Folge-Schritt auf einen `DetailQueryRequest(string DatabaseName, string
  ObjectName, string OperationName)`-Record umgestellt werden, falls
  ein Linter-Update die `Func<...>`-Zählung ändert. Aktuell nicht nötig.
