---
status: done (pending audit)
type: step-plan
task: audit-try-magicvalues
step: 001
corrects: null
title: "EPIC-01 Konstanten-Zentralisierung & Boilerplate-Cleanup (Quick Wins, batch)"
epic: EPIC-01
estimated_risk: low
step_type: batch
items:
  - id: item-01
    title: "SqlServerErrorCode einführen und rohe SQL-Fehlernummern ersetzen (MV-1)"
    source: "audit-dry-magicvalues.md#MV-1"
  - id: item-02
    title: "BenchmarkVerdict einführen und rohe Verdict-Strings ersetzen (MV-2)"
    source: "audit-dry-magicvalues.md#MV-2"
  - id: item-03
    title: "Gewichtungskonstanten in AnonymizationRuleProvider (MV-3)"
    source: "audit-dry-magicvalues.md#MV-3"
  - id: item-04
    title: "FNV-1a-Konstanten in Anonymizer (MV-4)"
    source: "audit-dry-magicvalues.md#MV-4"
  - id: item-05
    title: "SecurityConstants.DefaultRegexTimeout einführen und Timeouts bündeln (MV-5)"
    source: "audit-dry-magicvalues.md#MV-5"
  - id: item-06
    title: "AnonymizationMode einführen und Modus-Strings ersetzen (MV-6)"
    source: "audit-dry-magicvalues.md#MV-6"
  - id: item-07
    title: "SqlServerObjectType einführen und Objekttyp-Strings ersetzen (MV-7)"
    source: "audit-dry-magicvalues.md#MV-7"
  - id: item-08
    title: "DdlUnavailableNote konsolidieren (DRY-2)"
    source: "audit-dry-magicvalues.md#DRY-2"
  - id: item-09
    title: "OptionalStringParam entfernen und auf StringParam umstellen (DRY-3)"
    source: "audit-dry-magicvalues.md#DRY-3"
  - id: item-10
    title: "BuildObjectDetailTool-Helper für die 5 Detail-Tool-Builder (DRY-4)"
    source: "audit-dry-magicvalues.md#DRY-4"
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-15T21:42:00+02:00
related_to: []
---

# Step 001: EPIC-01 Konstanten-Zentralisierung & Boilerplate-Cleanup (Quick Wins, batch)

## Bezug

- **Task:** `audit-try-magicvalues`
- **Epic:** `EPIC-01` aus `roadmap.md` — *Konstanten-Zentralisierung (Phase 1, Quick Wins)*.
  Sämtliche Befunde MV-1 bis MV-7 sowie DRY-2, DRY-3 und DRY-4 sollen in einem
  einzigen, kohärenten Konstanten- und Boilerplate-Refactoring umgesetzt werden.
- **Konzept-Referenz:** `konzept.md` §"Muss-Haven" Pkt. 1 (Phase 1), im Detail
  belegt durch `audit-dry-magicvalues.md` Abschnitte 3 (MV-1..MV-7) und 2
  (DRY-2/3/4). Risiko laut Audit: durchgehend **niedrig bis mittel** (keine
  Architekturänderung, keine Verhaltensänderung, keine API-Vertragsänderung).

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen des Bestandscodes habe ich folgende Strukturen vorgefunden, die den
Plan beeinflussen:

- **MV-1 (`SqlServerErrorCode`)** — die rohen Codes stehen an drei Stellen, die
  der Audit nennt, aber in unterschiedlichen syntaktischen Formen: in
  `SqlToAiErrorMapper.cs:48` als boolesches ODER im `IsTimeoutException`
  (`sqlEx.Number == -2 || sqlEx.Number == 121 || sqlEx.Number == 258`), in
  `SqlToAiErrorMapper.cs:75` als `switch` über `sqlEx.Number`
  (`20 or 40 or 53 or 233 or 10054 or 10060 or 10061 or 18456`), in
  `PerformanceMeasurementService.cs:168, 230, 260` als Argument
  `IsPermissionError(ex, 262, "SHOWPLAN")`, und in `IndexSuggestionService.cs:291-292`
  als Argument `IsPermissionError(ex, 300|297, "VIEW SERVER STATE")`. Die
  Konstante muss also sowohl in `if`/`switch`-Pfaden als auch als
  Funktionsargument einsetzbar sein — eine reine `int`-Konstante (`public const int`)
  erfüllt beides.
- **MV-2 (`BenchmarkVerdict`)** — die vier Strings existieren in
  `OptimizationBenchmarkService.cs:104, 115, 123, 129` als Tupel-Feld `Verdict:`.
  Der Test `OptimizationBenchmarkServiceTests.cs:51, 68, 83` greift sie per
  `Assert.Equal("Recommended", …)` ab. **Zusatzbefund (nicht im Audit, aber
  gleicher Befundcharakter):** `ToolDispatcherTestFakes.cs:185` enthält ein
  hartkodiertes `"Recommended"` beim Aufbau eines `OptimizationBenchmarkResult` —
  der Coder sollte diese Stelle in derselben Aktion mit auf
  `BenchmarkVerdict.Recommended` umstellen, damit der Fake konsistent bleibt.
  Die in `ToolRegistry.cs:288-291` zitierte Beschreibung im `Description`-String
  muss interpoliert werden, etwa über
  `… $"… one of \"{BenchmarkVerdict.Recommended}\" — …"`. Damit bleibt die
  Doku-Lesefluß erhalten und gleichzeitig ist die Wahrheit an genau einer Stelle
  definiert.
- **MV-3 (`WeightedScore`)** — der Aufruf in
  `AnonymizationRuleProvider.cs:193` (`OrderByDescending(m => WeightedScore(m.Scores))`)
  ist nach dem Kommentarblock in Zeile 285-287 ein *bewusst* nur als
  deterministischer Tie-Break gehaltener Restposten — Sicherheitsbedeutung: 0.
  Konstanten für `1000, 100, 10` schaden der Lesbarkeit, ohne Logik zu ändern.
- **MV-4 (FNV-1a)** — die zwei rohen `uint`-Literale
  (`2166136261` und `16777619`) stehen in `Anonymizer.GetStableHashCode` an
  Zeile 126 und 130. Sie sind exakt die 32-Bit-FNV-1a-Konstanten; benannte
  Konstanten mit Suffix `u` machen die Absicht sofort sichtbar.
- **MV-5 (Regex-Timeout)** — der Audit listet drei Vorkommen
  (`ReadOnlyGuard.cs:24`, `GlobMatcher.cs:17`, `LikePatternMatcher.cs:15`).
  Ein **`grep` hat eine vierte Stelle aufgedeckt:**
  `src/SqlToAi/Database/QueryTokenResolver.cs:77`
  (`return new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));`).
  Diese Stelle ist im Audit-Abschnitt 3 (MV-5) nicht aufgeführt, hat aber
  exakt dieselbe ReDoS-Schutz-Semantik. Da `SecurityConstants.DefaultRegexTimeout`
  die Single Source of Truth für *alle* ReDoS-Timeouts des Servers werden soll,
  ist es inkonsistent, diese vierte Stelle unberührt zu lassen. Der Coder
  **soll** sie im selben Schritt mit umstellen, sonst entsteht sofort ein
  neuer MV-5-Fund. Diese Empfehlung geht über den Orchestrator-Auftrag hinaus,
  ist aber im Sinne der Konzept-Definition "eine zentrale ReDoS-Schutzgrenze".
- **MV-6 (`AnonymizationMode`)** — die beiden Strings existieren ausschließlich
  in `Anonymizer.cs:88` (`string.Equals(mode, "Hash", StringComparison.OrdinalIgnoreCase)`);
  `"Scramble"` ist als impliziter Fallback kodiert (alles, was nicht `"Hash"` ist,
  wird gescrambled). Der Coder muss beide Konstanten deklarieren, in der
  `if`-Bedingung `AnonymizationMode.Hash` referenzieren und den Fallback
  explizit auf `!= AnonymizationMode.Hash` (oder symmetrisch
  `== AnonymizationMode.Scramble`) umstellen, damit das Verhalten 1:1
  erhalten bleibt.
- **MV-7 (`SqlServerObjectType`)** — die Strings `"U"` und `"V"` stehen
  in `DetailSchemaRenderer.cs` an **zwei** Stellen: Zeile 30
  (im Helper `ValidateTableOrViewAsync`) und Zeile 251 (in
  `GetObjectReferencesAsync`). Der Audit-Abschnitt 3 nennt nur Zeile 30; der
  Coder muss beide ersetzen, sonst bleibt eine rohe String-Duplikation
  bestehen.
- **DRY-2 (`DdlUnavailableNote`)** — die Konstante existiert
  wortgleich in `DetailSchemaRenderer.cs:11-12` und `TableSchemaRenderer.cs:13-14`.
  Da `DetailSchemaRenderer` bereits `internal static` ist und die Konstante
  produktionsweit nur in zwei Dateien verwendet wird, ist die im Orchestrator-
  Auftrag gewählte Lösung (Sichtbarkeit auf `internal const string` erhöhen,
  in `TableSchemaRenderer` referenzieren, lokale Kopie entfernen) die minimal-
  invasive Variante und führt **keine** neue Datei ein.
- **DRY-3 (`OptionalStringParam`)** — die Methode wird in `ToolRegistry.cs:95`
  (`ArgObjectType`) und `:332` (`ArgTableName`) aufgerufen. Beide Aufrufe sind
  in ihrer Signatur und ihrem `Required`-Set-Handling identisch zu
  `StringParam` — die Optionalität kommt ausschließlich aus dem
  `Required`-Array, nicht aus dem Parameter-DTO. Löschen der Methode und
  Umbenennen beider Aufrufe auf `StringParam(...)` ist verhaltensneutral.
- **DRY-4 (`BuildObjectDetailTool`)** — die fünf Builder
  (`BuildGetSchemaForeignKeys`, `BuildGetSchemaIndexes`,
  `BuildGetSchemaConstraints`, `BuildGetObjectReferences`,
  `BuildGetRoutineParameters`) an den Zeilen 119-208 sind tatsächlich
  strukturidentisch: jeweils `Name = McpConstants.Tool…`, jeweils zwei
  Properties (`ArgObjectName` + `ArgDatabase`), jeweils dieselbe
  `Required`-Liste. Ein Helper
  `BuildObjectDetailTool(string name, string description)` reduziert ca.
  60 Zeilen redundanten DTO-Code auf 5 Helper-Aufrufe. `BuildGetTriggerDefinition`
  hat **drei** Parameter (zusätzlich `ArgTriggerName`) und bleibt deshalb
  unberührt — der Audit-Abschnitt 2 nennt sie auch nicht.
- **Sonstiges:** `AppSettingsMigrator.cs:194, 251` enthält den String
  `"Password"` (MV-P2 False Positive — JSON-Schlüsselname, kein Secret) und
  `SqlToAiOptions.cs` enthält Default-Property-Initializer (MV-P1 False
  Positive — Richtlinie §4 erlaubt genau dort Defaults). Diese bleiben
  unangetastet.

## Intention

Nach diesem Step besitzt `SqlToAi` eine zentrale, benannte Konstante
für jeden im Audit identifizierten Magic Value: SQL-Server-Fehlernummern,
Benchmark-Verdicts, Anonymisierungs-Modi, FNV-1a-Parameter, Gewichtungen,
Regex-Timeouts und SQL-Server-Objekttypen. Die redundante `OptionalStringParam`-
Methode und die wortgleiche `DdlUnavailableNote`-Konstante sind beseitigt, und
die fünf strukturgleichen Detail-Tool-Builder sind auf einen
`BuildObjectDetailTool`-Helper reduziert. **Kein beobachtbares Verhalten ändert
sich** — die Konstante hat überall denselben Wert, den vorher das Literal hatte.

## Konkrete Änderungen

### item-01: SqlServerErrorCode einführen (MV-1) — neue Datei `src/SqlToAi/Database/SqlServerErrorCode.cs`

- **Was:** Neue Datei `src/SqlToAi/Database/SqlServerErrorCode.cs` mit
  `internal static class SqlServerErrorCode` und benannten
  `public const int`-Feldern: `ShowplanPermissionMissing = 262`,
  `ActionPermissionDenied = 297`, `InsufficientPermission = 300`,
  `ClientQueryTimeout = -2`, `SemaphoreTimeout = 121`, `WaitTimeout = 258`,
  `ConnectionInitializationError = 233`, `ConnectionReset = 10054`,
  `LoginFailed = 18456`. Ersetze in `SqlToAiErrorMapper.cs:48` die drei
  Vergleiche `sqlEx.Number == -2 || … == 121 || … == 258` durch
  `sqlEx.Number == SqlServerErrorCode.ClientQueryTimeout || … SemaphoreTimeout || … WaitTimeout`
  und in `SqlToAiErrorMapper.cs:75` das `switch`-Arm-Set
  (`20 or 40 or 53 or 233 or 10054 or 10060 or 10061 or 18456`)
  durch die passenden Konstanten. Ersetze in `PerformanceMeasurementService.cs:168, 230, 260`
  das `IsPermissionError(ex, 262, "SHOWPLAN")` durch
  `IsPermissionError(ex, SqlServerErrorCode.ShowplanPermissionMissing, "SHOWPLAN")`
  und in `IndexSuggestionService.cs:291, 292` die
  `IsPermissionError(ex, 300|297, "VIEW SERVER STATE")` durch
  `IsPermissionError(ex, SqlServerErrorCode.InsufficientPermission | ActionPermissionDenied, "VIEW SERVER STATE")`.
  Die im Audit-Abschnitt 3 aufgeführten Codes `20, 40, 53, 10060, 10061` sind
  **nicht** in der vom Orchestrator vorgegebenen Konstantenliste und bleiben
  deshalb vorerst als Magic-Number-Literale stehen — sie sind im selben
  Switch-Arm, und eine fehlende Konstante würde nur eine halbe Umstellung
  produzieren. **Empfehlung:** der Coder soll die fünf fehlenden Codes
  (`ConnectionInstanceNotFound = 20`, `StatementTooComplex = 40` o. ä.,
  `ServerNotFound = 53`, `ConnectionTimedOut = 10060`,
  `ConnectionRefused = 10061`) **in derselben Datei ergänzen**, damit der
  Switch vollständig auf Konstanten läuft; das ist ein reiner
  Konstanten-Hinzufüge-Schritt ohne semantische Auswirkung. Falls der Coder
  die zusätzlichen fünf Konstanten nicht im selben Schritt ergänzen will,
  ist das akzeptabel, muss aber als bewusst zurückgestellt dokumentiert
  werden.
- **Warum:** `SqlServerErrorCode` bündelt das SQL-Server-Fehler-
  Nummernuniversum an einer Stelle; jede zukünftige Verwendung eines
  weiteren Codes verlangt zwingend einen Konstanten-Eintrag. Erfüllt
  Richtlinie §4 ("No Magic Values").

### item-02: BenchmarkVerdict einführen (MV-2) — neue Datei `src/SqlToAi/Database/BenchmarkVerdict.cs`

- **Was:** Neue Datei `src/SqlToAi/Database/BenchmarkVerdict.cs` mit
  `internal static class BenchmarkVerdict` und `public const string Recommended = "Recommended"`,
  `NotRecommended = "NotRecommended"`,
  `UnsafeDueToDataMismatch = "UnsafeDueToDataMismatch"`,
  `Neutral = "Neutral"`. Ersetze in
  `OptimizationBenchmarkService.cs:104, 115, 123, 129` jedes
  `Verdict: "X"` durch `Verdict: BenchmarkVerdict.X`. Ersetze in
  `ToolRegistry.cs:288-291` den Beschreibungstext so, dass die vier
  Verdict-Literale via String-Interpolation aus den Konstanten
  zusammengesetzt werden
  (z. B. `$"verdict (one of \"{BenchmarkVerdict.Recommended}\" — equivalent and …; \"{BenchmarkVerdict.NotRecommended}\" — …; \"{BenchmarkVerdict.Neutral}\" — …; \"{BenchmarkVerdict.UnsafeDueToDataMismatch}\" — …)"`).
  Ersetze in `tests/SqlToAi.Tests/Database/OptimizationBenchmarkServiceTests.cs:51, 68, 83`
  die `Assert.Equal("Recommended"|"UnsafeDueToDataMismatch"|"NotRecommended", result.Value.Verdict)`
  durch `Assert.Equal(BenchmarkVerdict.Recommended|UnsafeDueToDataMismatch|NotRecommended, result.Value.Verdict)`.
  **Zusatzfund** (s. JIT-Kontext): `tests/SqlToAi.Tests/Mcp/ToolDispatcherTestFakes.cs:185`
  enthält zusätzlich ein hartkodiertes `"Recommended"` als Literal im
  `OptimizationBenchmarkResult`-Konstruktor — ersetze diese Stelle im selben
  Schritt durch `BenchmarkVerdict.Recommended`, damit der zentrale
  Test-Fake denselben Vertrag verwendet.
- **Warum:** Vertragsrelevante Strings (MCP-Output-Vertrag
  `sql_benchmark_optimization.verdict`) müssen zentral und referenzierbar
  sein — der Linter-Audit nennt sie explizit als MV-2.

### item-03: Gewichtungskonstanten in AnonymizationRuleProvider.cs:290 (MV-3)

- **Was:** Füge am Kopf der Klasse (oder in einem
  privaten static-Block, gefolgt vom `WeightedScore`-Body) drei
  `private const int`-Deklarationen ein:
  `DatabaseDimensionWeight = 1000; SchemaDimensionWeight = 100; TableDimensionWeight = 10;`.
  Schreibe `WeightedScore` um auf
  `(scores[0] * DatabaseDimensionWeight) + (scores[1] * SchemaDimensionWeight) + (scores[2] * TableDimensionWeight) + scores[3]`.
- **Warum:** Die Multiplikatoren entsprechen der dokumentierten
  Dimensonshierarchie (DB > Schema > Table > Column) und müssen beim Tunen
  der Gewichtung an einer Stelle stehen. Erfüllt Richtlinie §4.

### item-04: FNV-1a-Konstanten in Anonymizer.cs (MV-4)

- **Was:** Füge in `Anonymizer.cs` zwei `private const uint`-Deklarationen
  ein: `FnvOffsetBasis32 = 2166136261u;` und `FnvPrime32 = 16777619u;`.
  Ersetze in `GetStableHashCode` (Zeilen 126, 130) das `uint hash = 2166136261;`
  durch `uint hash = FnvOffsetBasis32;` und das `hash *= 16777619;` durch
  `hash *= FnvPrime32;`.
- **Warum:** Die Literale sind genau die 32-Bit-FNV-1a-Konstanten der
  Spezifikation; benannte Konstanten signalisieren die Quelle, vermeiden
  Tippfehler beim Re-Tippen und werden vom Linter nicht mehr als Magic
  Numbers markiert.

### item-05: SecurityConstants.DefaultRegexTimeout einführen (MV-5) — neue Datei `src/SqlToAi/Security/SecurityConstants.cs`

- **Was:** Neue Datei `src/SqlToAi/Security/SecurityConstants.cs` mit
  `public static class SecurityConstants` und
  `public static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromMilliseconds(200);`
  (alternativ `private static readonly` plus `public TimeSpan`-Property,
  falls `static readonly` für den Linter als nicht-konstant zählt; das
  Lint-Ergebnis entscheidet). Ersetze in `ReadOnlyGuard.cs:24` das
  `TimeSpan.FromMilliseconds(200)` durch `SecurityConstants.DefaultRegexTimeout`,
  in `GlobMatcher.cs:17` (`private static readonly TimeSpan RegexTimeout = …`)
  ebenfalls, und in `LikePatternMatcher.cs:15` ebenfalls.
  **Zusatzfund** (s. JIT-Kontext): ersetze zusätzlich in
  `src/SqlToAi/Database/QueryTokenResolver.cs:77` das
  `TimeSpan.FromMilliseconds(200)` durch `SecurityConstants.DefaultRegexTimeout`.
  Dies ist nicht im Orchestrator-Brief, aber semantisch identisch und
  verhindert einen neuen MV-5-Fund direkt im selben Schritt. `using SqlToAi.Security;`
  ist in `QueryTokenResolver.cs` bereits vorhanden, falls nicht — ergänzen.
- **Warum:** Die ReDoS-Schutzgrenze (200 ms) ist eine einzelne sicherheits-
  relevante Größe; ein zentraler Wert macht Anpassungen auditierbar.

### item-06: AnonymizationMode einführen (MV-6) — neue Datei `src/SqlToAi/Anonymization/AnonymizationMode.cs`

- **Was:** Neue Datei `src/SqlToAi/Anonymization/AnonymizationMode.cs` mit
  `internal static class AnonymizationMode` und
  `public const string Hash = "Hash"; Scramble = "Scramble";`.
  Ersetze in `Anonymizer.cs:88` den `string.Equals(mode, "Hash", …)`-
  Vergleich durch `string.Equals(mode, AnonymizationMode.Hash, …)`. Der
  Fallback in der `return Scramble(value);`-Zeile bleibt strukturell
  erhalten (impliziter Fallback auf Scramble für unbekannte Modi), aber
  der Coder **soll** ihn explizit machen:
  `if (string.Equals(mode, AnonymizationMode.Hash, …)) return HashValue(value); return Scramble(value);`
  — also ein symmetrischer `if/else`-Block, der beide Konstanten
  referenziert. Aktuell gibt es **keine** weiteren Vergleichsstellen mit
  `"Hash"`/`"Scramble"` (per `grep` verifiziert) — wenn der Coder beim
  Ersetzen weitere findet, sind diese genauso umzustellen.
- **Warum:** Der Modus-String ist Teil des `AnonymizerOptions.DefaultMode`-
  Vertrags (siehe Konzept §"Musterbasierte String-Anonymisierung"); ein
  zentrales Symbol macht den Vertrag referenzierbar und schützt vor
  Tippfehlern.

### item-07: SqlServerObjectType einführen (MV-7) — neue Datei `src/SqlToAi/Database/SqlServerObjectType.cs`

- **Was:** Neue Datei `src/SqlToAi/Database/SqlServerObjectType.cs` mit
  `internal static class SqlServerObjectType` und
  `public const string UserTable = "U"; View = "V";`.
  Ersetze in `DetailSchemaRenderer.cs:30` (im `ValidateTableOrViewAsync`-Helper)
  das `objectType != "U" && objectType != "V"` durch
  `objectType != SqlServerObjectType.UserTable && objectType != SqlServerObjectType.View`,
  und **zusätzlich** in `DetailSchemaRenderer.cs:251` (in `GetObjectReferencesAsync`)
  das gleiche String-Literal-Paar. Der Audit-Abschnitt 3 nennt nur Zeile 30;
  der zweite Vergleich an Zeile 251 ist gleichermaßen betroffen und muss
  mit umgestellt werden, sonst entsteht eine 1:1-Duplikation der Magic
  Strings innerhalb derselben Datei. Hinweis: die Konstanten `RoutineProcedure = "P"`,
  `ScalarFunction = "FN"`, `TableValuedFunction = "TF"`, `InlineTableValuedFunction = "IF"`
  (aus `GetRoutineParametersAsync`, Zeile 293) liegen außerhalb des
  Orchestrator-Auftrags (nicht in MV-7 genannt) und bleiben vorerst Magic
  Strings — sie zu ergänzen wäre eine sinnvolle Folge-Aktion, ist aber
  nicht Teil dieses Schritts.
- **Warum:** SQL-Server-`sys.objects.type` ist ein dokumentiertes
  Single-Character-Vokabular, das an mehreren Stellen vorkommt; benannte
  Konstanten machen den Vertrag sichtbar.

### item-08: DdlUnavailableNote konsolidieren (DRY-2) — Datei `src/SqlToAi/Database/DetailSchemaRenderer.cs` + `TableSchemaRenderer.cs`

- **Was:** Ändere in `DetailSchemaRenderer.cs:11` den Sichtbarkeits-Modifizierer
  von `private const string DdlUnavailableNote` auf `internal const string DdlUnavailableNote`
  (Wortlaut und Wert unverändert). Lösche in `TableSchemaRenderer.cs:13-14`
  die lokale `private const string DdlUnavailableNote = "…";`-Deklaration
  komplett und referenziere stattdessen `DetailSchemaRenderer.DdlUnavailableNote`.
  Beide Klassen liegen im selben Namespace `SqlToAi.Database` und der
  Default-InternalsVisibleTo-Status reicht; falls der Test-Assembly kein
  Zugriff auf `internal` hat (zu prüfen: existiert eine
  `InternalsVisibleTo`-Direktive für `SqlToAi.Tests`?), den `internal const`-
  Modifizierer beibehalten und stattdessen die Konstante über die
  public-API verfügbar machen — Standardlösung in dieser Solution: das
  `InternalsVisibleTo` ist in `SqlToAi.csproj` (oder einer
  `AssemblyInfo.cs`) einzutragen, falls noch nicht geschehen. **Prüfpflicht
  für den Coder:** `Get-ChildItem src/SqlToAi -Filter *.csproj` und
  `Select-String "InternalsVisibleTo" src/SqlToAi/SqlToAi.csproj` — wenn
  der Eintrag fehlt, vor der Umstellung ergänzen (z. B.
  `<ItemGroup><InternalsVisibleTo Include="SqlToAi.Tests" /></ItemGroup>`).
- **Warum:** Eine identische Konstante an zwei Stellen ist ein
  klassischer DRY-Verstoß; der nächste Bearbeiter, der den Text anpasst,
  vergisst sonst eine der beiden Stellen.

### item-09: OptionalStringParam entfernen (DRY-3) — Datei `src/SqlToAi/Mcp/ToolRegistry.cs`

- **Was:** Lösche in `ToolRegistry.cs:356-360` die Methode
  `private static ToolParameterDefinition OptionalStringParam(string description) => …;`
  komplett. Ersetze in `ToolRegistry.cs:95` den Aufruf `OptionalStringParam(…)`
  durch `StringParam(…)` und analog in `ToolRegistry.cs:332`. Da beide
  Aufrufstellen denselben Description-String an `StringParam` übergeben
  (Optionalität kommt aus dem `Required`-Array, nicht aus dem DTO), ist
  die Umstellung verhaltensneutral.
- **Warum:** `OptionalStringParam` ist ein Scheinduplikat; der Linter
  erkennt es als DuplicateCode und der nächste Leser fragt sich, ob
  zwischen den beiden Helpern ein subtiler Unterschied besteht, den es
  nicht gibt.

### item-10: BuildObjectDetailTool-Helper einführen (DRY-4) — Datei `src/SqlToAi/Mcp/ToolRegistry.cs`

- **Was:** Füge in `ToolRegistry.cs` einen privaten Helper ein:
  `private static ToolDefinition BuildObjectDetailTool(string name, string description) => new() { Name = name, Description = description, InputSchema = new ToolInputSchema { Properties = new Dictionary<string, ToolParameterDefinition> { [McpConstants.ArgObjectName] = StringParam("The name of the target object."), [McpConstants.ArgDatabase] = StringParam("Target database name. Required.") }, Required = [McpConstants.ArgObjectName, McpConstants.ArgDatabase] } };`.
  Ersetze die fünf Builder `BuildGetSchemaForeignKeys`, `BuildGetSchemaIndexes`,
  `BuildGetSchemaConstraints`, `BuildGetObjectReferences` und
  `BuildGetRoutineParameters` (Zeilen 119-208) jeweils durch einen
  einzeiligen Aufruf `BuildObjectDetailTool(McpConstants.Tool…, "<bisherige Description>")`.
  `BuildGetTriggerDefinition` bleibt unverändert (anderes Schema mit
  `ArgTriggerName` als drittem Pflichtfeld). Die `BuildTools()`-Liste und
  alle Konstanten in `McpConstants` bleiben unverändert.
- **Warum:** Reduziert ca. 60 Zeilen redundanten DTO-Code auf fünf
  einzeilige Aufrufe; gleichzeitig ist der Linter-DuplicateCode-Befund
  DRY-4 strukturell behoben (gleicher DTO-Aufbau an fünf Stellen
  verschwindet).

## Tests

- [ ] Bestehende `tests/SqlToAi.Tests/Database/OptimizationBenchmarkServiceTests.cs`
      läuft nach Umstellung auf `BenchmarkVerdict.*` weiterhin grün
      (drei `Assert.Equal`-Stellen, s. item-02).
- [ ] `dotnet test` läuft insgesamt grün — die Schritte sind reine
      Konstanten-Ersetzungen und ein Helper-Refactoring, sie ändern kein
      beobachtbares Verhalten.
- [ ] Keine **neuen** Tests erforderlich: alle Änderungen sind
      verhaltensneutral, der Linter-Audit hat keine Test-Coverage-Lücke
      benannt. Falls die Test-Suite einen bestehenden Test hat, der die
      exakte Schreibweise des `DdlUnavailableNote`-Strings prüft (sehr
      unwahrscheinlich, da es nur ein Anzeige-Hinweis ist), muss der
      betroffene Test mitsamt der Erwartung umgestellt werden — das ist
      vom Coder beim ersten Test-Lauf zu prüfen.

## Definition of Done

- [ ] Alle 10 `item-NN`-Blöcke umgesetzt; die in "Aktueller Projektzustand"
      dokumentierten Zusatzfunde (`QueryTokenResolver.cs:77`,
      `DetailSchemaRenderer.cs:251`, `ToolDispatcherTestFakes.cs:185`)
      sind in den jeweiligen Items mit erledigt.
- [ ] `dotnet build` (gemäß `roadmap.md` Tech-Stack-Notiz) läuft fehler- und
      **warnungsfrei** (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).
- [ ] `dotnet test` läuft vollständig grün.
- [ ] AiNetLinter (`dotnet test` → `RunLinterShouldBeClean`) meldet 0 Fehler
      und 0 Warnungen. Falls `AiNetLinter.exe` fehlt, ist
      `Assert.Skip` zulässig (siehe `roadmap.md` Tech-Stack-Notiz).
- [ ] Conventional Commit auf `main` (oder aktuellem Branch), deutsch,
      imperativ, Subject ≤ 72 Zeichen — Vorschlag:
      `refactor: zentralisiere MV-1..7 und entferne Boilerplate-Duplikate`.
- [ ] `tasks/audit-try-magicvalues/step-001/step-result.md` geschrieben
      (Coder-Pflicht) mit Verweisen auf jeden `item-NN`-Abschluss, Build-/
      Test-Output-Zusammenfassung und Commit-Hash.
- [ ] `status` in `step-plan.md` (dieser Datei) von `open` auf
      `done (pending audit)` gesetzt; `tasks/audit-try-magicvalues/task-state.md`
      Steps-Tabelle um die Zeile für `step-001` ergänzt.

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` ("No Magic Values & AppSettings-
  Pflicht") — die Items 01, 02, 03, 04, 05, 06, 07 setzen diese Richtlinie
  1:1 um; die in `SqlToAiOptions.cs` zentralisierten Defaults (MV-P1) und
  die JSON-Schlüsselnamen in `AppSettingsMigrator` (MV-P2) bleiben
  unangetastet, weil sie nach Richtlinie §4 ausdrücklich erlaubt sind.
- `.agents/rules/SqlToAiRichtlinien.mdc#5` ("Qualitätsdrift-Prävention,
  Zero-Warning-Direktive, Result-Pattern, Baseline-Freiheit") — `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  muss erhalten bleiben; das Refactoring darf **keine** Compiler-Warnung
  einführen.
- `.agents/rules/AiNetLinter.mdc` (DuplicateCode, Sealed-Klassen,
  Methodenlänge ≤ 60 Zeilen Produktion / ≤ 100 Zeilen Test) — die fünf
  neuen Konstanten-Klassen sind `internal static` (kein `sealed` nötig);
  `BuildObjectDetailTool` muss unter dem 60-Zeilen-Limit bleiben (bei
  einem voraussichtlichen Umfang von ~12 Zeilen trifft das deutlich).
- Konzept-Referenz: `konzept.md` §"Muss-Haven" Pkt. 1 (Phase 1).

## Bekannte Ausnahmen

- Keine flaky-Tests, keine zu ignorierenden Timeouts. Der Refactoring-
  Schritt verändert keinerlei Timing- oder IO-Pfad; der `200 ms`-
  Regex-Timeout bleibt exakt gleich.

## Code-Skizze (optional)

```
// src/SqlToAi/Database/SqlServerErrorCode.cs
namespace SqlToAi.Database;

internal static class SqlServerErrorCode
{
    public const int ShowplanPermissionMissing = 262;
    public const int ActionPermissionDenied = 297;
    public const int InsufficientPermission = 300;

    public const int ClientQueryTimeout = -2;
    public const int SemaphoreTimeout = 121;
    public const int WaitTimeout = 258;

    public const int ConnectionInitializationError = 233;
    public const int ConnectionReset = 10054;
    public const int LoginFailed = 18456;
}
```

## Notes

- **Verhaltensneutralität** ist die zentrale Eigenschaft dieses Schritts:
  jede Konstante hat **dieselbe** Wert-Belegung wie das vorherige
  Literal. Es gibt keine API-Änderung, keine Vertragsänderung am
  MCP-Output, keine Anpassung der `appsettings.json`. Lediglich
  `InternalsVisibleTo` für `SqlToAi.Tests` muss ggf. in
  `src/SqlToAi/SqlToAi.csproj` ergänzt werden, falls die Tests die
  `internal const string DdlUnavailableNote` referenzieren sollen (in
  der aktuellen Codebasis referenzieren sie es **nicht** — der Test-
  Code geht über die `TableSchemaRenderer`/`DetailSchemaRenderer`-
  Public-API; trotzdem zur Sicherheit prüfen).
- **Reihenfolge der Commits:** Wenn der Coder die Änderungen nicht in
  einem einzigen Commit bündeln will, ist eine Aufteilung in 2-3
  thematische Commits sinnvoll
  (z. B. `feat: führe SqlServerErrorCode und BenchmarkVerdict ein` /
  `refactor: bündele Timeouts, FNV- und Gewichtungs-Konstanten` /
  `refactor: dedupliziere ToolRegistry-Boilerplate`). Die
  `task-state.md` akzeptiert mehrere Commits pro Step; die DoD-Checkbox
  "Commit auf aktuellem Branch" bezieht sich auf den letzten
  konsolidierten Stand. Bei Aufteilung in mehrere Commits: jeder
  Commit für sich muss `dotnet build` und `dotnet test` grün halten.
- **Auto-Fixable Tech-Debt:** keine Funde in `tech-debt.md`, also
  nichts opportunistisch mitzuziehen.
- **Anti-Loop-Hinweis (codemap.md):** die Karte nennt dieselben
  Dateien, die dieser Step berührt; kein Widerspruch zu einem
  früheren Step (Task hat `total_steps: 0`, also kein Vorgänger).
- **Bewusst zurückgestellt (für spätere Schritte):**
  - Die fünf zusätzlichen SQL-Fehlercodes `20, 40, 53, 10060, 10061`
    können in einer Folge-Aktion als Konstanten ergänzt werden, wenn
    ein einheitlicher Schalter eingeführt wird, der alle Codes
    zentral führt (Konzept §"Verworfene Alternativen" ist hier nicht
    betroffen).
  - Die Routine-Typen `P, FN, TF, IF` in
    `DetailSchemaRenderer.GetRoutineParametersAsync:293` sind
    semantisch `SqlServerObjectType.Routine*`-Konstanten, aber nicht
    Teil des MV-7-Auftrags. Der Coder soll sie im selben Schritt
    **nicht** "auf eigene Faust" hinzufügen, weil das die
    Step-Größe verwässert und der Audit-Auftrag MV-7 nur `U, V`
    nennt.
  - `ShowPlanXml`-Elementnamen (MV-8) sind im Audit als "Optional"
    markiert und bleiben für eine spätere Iteration.
