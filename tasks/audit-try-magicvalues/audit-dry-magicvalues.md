---
title: "360-Grad-Audit: DRY-Verstöße & Magic Values (SqlToAi)"
status: ready
date: "2026-08-15"
scope: Vollständige Solution (Produktion src/SqlToAi + Test-Suite tests/SqlToAi.Tests)
tooling: AiNetLinter MCP-Server (find_duplicates [maxResults=500, minTokens=10/15], find_magic_values [maxResults=1000], get_violations)
rules_dir: .agents/rules
---

# 360-Grad-Audit: DRY & Magic Values (SqlToAi Solution)

Dieser Bericht liefert eine **vollständige, unbegrenzte 360-Grad-Bestandsaufnahme** aller Code-Duplikate (DRY), Refactoring-Drifts und hartkodierten Werte (Magic Values / Numbers / Strings) über die gesamte Solution `SqlToAi.slnx` hinweg.

Die Analyse stützt sich auf ungekappte Scans des MCP-Servers `AiNetLinter` über alle 157 Quell- und Testdateien (757 analysierte Methoden/Funktionen, 404 Magic-Value-Fundstellen) sowie manuelle Tiefenanalyse gegen die Richtlinien ([SqlToAiRichtlinien.mdc](../../.agents/rules/SqlToAiRichtlinien.mdc) und [AiNetLinter.mdc](../../.agents/rules/AiNetLinter.mdc)).

---

## 1. Executive Summary & Gesamtübersicht

### A. Produktionscode (`src/SqlToAi`)

| ID | Kategorie | Bereich / Thema | Priorität | Empfehlung |
|:---|:---|:---|:---:|:---|
| **DRY-1** | DRY / Refactoring-Drift | Guardrail- & Validierungs-Pipeline über 4 Services | **Hoch** | **Anpassen:** Zentrale `IQuerySafetyValidator`-Pipeline extrahieren |
| **DRY-2** | DRY / Text-Duplikat | `DdlUnavailableNote` in `TableSchemaRenderer` & `DetailSchemaRenderer` | **Niedrig** | **Anpassen:** In gemeinsame `internal const` auslagern |
| **DRY-3** | DRY / Boilerplate | `StringParam` vs. `OptionalStringParam` in `ToolRegistry.cs` | **Niedrig** | **Anpassen:** `OptionalStringParam` entfernen / konsolidieren |
| **DRY-4** | DRY / Boilerplate | 5 strukturidentische Detail-Tool-Definitionen in `ToolRegistry.cs` | **Niedrig** | **Anpassen:** Parameterisierten Builder-Helper einführen |
| **DRY-5** | DRY / Konsistenz | Timeout & Exception-Handling in `GlobMatcher` vs. `LikePatternMatcher` | **Mittel** | **Anpassen:** Gemeinsame Timeout-Konstante nutzen |
| **DRY-P1** | DRY (False Positive) | `SchemaService` 6 Forwarder-Methoden (Cluster 1) | **Info** | **Nicht anpassen:** Interface-API, bereits optimal an Skeleton delegiert |
| **DRY-P2** | DRY (False Positive) | `Program.cs` Enum-Switch-Ausdrücke (Cluster 2) | **Info** | **Nicht anpassen:** Idiomatisches, typsicheres Switch-Pattern |
| **DRY-P3** | DRY (False Positive) | `McpHost.cs` Response-Writer (`WriteResult` vs `WriteError`) | **Info** | **Nicht anpassen:** Durch AOT/Source-Generator-Serialisierung bedingt |
| **MV-1** | Magic Numbers | SQL Server Error Codes (`262`, `297`, `300`, `121`, `258`, `233`, `18456`) | **Hoch** | **Anpassen:** `SqlServerErrorCode`-Konstantenklasse einführen |
| **MV-2** | Magic Strings | Benchmark-Verdicts (`"Recommended"`, `"NotRecommended"`, `"Neutral"` etc.) | **Mittel** | **Anpassen:** `BenchmarkVerdict`-Konstanten/Enum einführen |
| **MV-3** | Magic Numbers | Gewichtungsfaktoren `1000, 100, 10` in `AnonymizationRuleProvider` | **Mittel** | **Anpassen:** Benannte Gewichtungskonstanten deklarieren |
| **MV-4** | Magic Numbers | FNV-1a Hash Constants (`2166136261`, `16777619`) in `Anonymizer.cs` | **Mittel** | **Anpassen:** Benannte `FnvOffsetBasis32` und `FnvPrime32` |
| **MV-5** | Magic Numbers | Verstreute `200 ms` Regex-Timeouts | **Mittel** | **Anpassen:** Zentrales `DefaultRegexTimeout` deklarieren |
| **MV-6** | Magic Strings | Anonymisierungs-Modi (`"Hash"`, `"Scramble"`) in `Anonymizer.cs` | **Niedrig** | **Anpassen:** `AnonymizationMode`-Konstantenklasse anlegen |
| **MV-7** | Magic Strings | SQL Server Objekttypen (`"U"`, `"V"`) in `DetailSchemaRenderer.cs` | **Niedrig** | **Anpassen:** `SqlServerObjectType.UserTable`/`View` einführen |
| **MV-8** | Magic Strings | ShowPlan XML Element- & Attribut-Namen | **Niedrig** | **Optional:** `ShowPlanXml`-Konstanten bündeln |
| **MV-P1** | MV (False Positive) | Options Defaults in `SqlToAiOptions.cs` | **Info** | **Nicht anpassen:** Konform mit Richtlinie §4 (einziger erlaubter Ort) |
| **MV-P2** | MV (False Positive) | JSON-Property-Schlüssel (`"Password"`) in `AppSettingsMigrator` | **Info** | **Nicht anpassen:** Maskierungs-Target, kein Klartext-Secret |
| **MV-P3** | MV (False Positive) | `SqlToAiError` Fehlerkatalog-Codes (`SQL-AI-*`) | **Info** | **Nicht anpassen:** Bereits als `internal const` gekapselt |

---

### B. Testcode & Test-Support (`tests/SqlToAi.Tests`)

| ID | Kategorie | Bereich / Thema | Priorität | Empfehlung |
|:---|:---|:---|:---:|:---|
| **DRY-T1** | DRY / Test-Fakes | Redundante Fake-Klassen zwischen Testdateien verstreut | **Mittel** | **Anpassen:** In `TestSupport/` konsolidieren |
| **DRY-T2** | DRY / Test-Fixtures | 8 duplizierte ShowPlan-XML Stringblöcke in `PerformanceMeasurementServiceTests` | **Mittel** | **Anpassen:** `ShowPlanTestHelper`-Builder nutzen |
| **DRY-T3** | DRY / Test-Drift | 33 identische Negative-Guardrail-Tests über 5 Testklassen | **Mittel** | **Anpassen:** Nach `IQuerySafetyValidator`-Extraktion zentral testen |
| **MV-T1** | Magic Numbers | JSON-RPC Standard-Error-Codes (`-32601`, `-32700`) in Tests | **Niedrig** | **Anpassen:** In `McpConstants` als `const int` führen |

---

## 2. Detaillierte DRY-Befunde (Produktionscode)

### 🔴 Befund DRY-1: Guardrail- & Validierungs-Pipeline über 4 Services dupliziert

- **Status:** **Anpassung dringend empfohlen (Hohe Priorität — Architektur & Sicherheit)**
- **Betroffene Dateien:**
  - [src/SqlToAi/Database/QueryExecutionService.cs:100-136](src/SqlToAi/Database/QueryExecutionService.cs#L100-L136)
  - [src/SqlToAi/Database/QueryValidationService.cs:66-104](src/SqlToAi/Database/QueryValidationService.cs#L66-L104)
  - [src/SqlToAi/Database/PerformanceMeasurementService.cs:113-150](src/SqlToAi/Database/PerformanceMeasurementService.cs#L113-L150)
  - [src/SqlToAi/Database/QueryComparisonService.cs:121-153](src/SqlToAi/Database/QueryComparisonService.cs#L121-L153)

#### Code-Vergleich der Duplikation
Alle vier Services führen vor der eigentlichen Arbeit dieselbe Kette aus:
```csharp
// Schritt 1: Argumente validieren
if (string.IsNullOrWhiteSpace(databaseName)) return SqlToAiError.InvalidParameters("Database name must not be empty.");
if (string.IsNullOrWhiteSpace(query)) return SqlToAiError.InvalidParameters("Query must not be empty.");

// Schritt 2: Whitelist-Abgleich
if (!_securityGuard.IsDatabaseAllowed(databaseName)) return SqlToAiError.SafetyCheckFailed(databaseName);

// Schritt 3: Dynamischer AccessLevel-Check
var accessLevel = await _accessLevelProvider.GetAccessLevelAsync(databaseName, cancellationToken);
if (accessLevel == AccessLevel.None || accessLevel == AccessLevel.SchemaOnly)
    return SqlToAiError.WriteOperationBlocked($"Database '{databaseName}' does not permit execution (AccessLevel: {accessLevel}).");

// Schritt 4: ReadOnlyGuard (Schreibschutz)
bool writeAllowed = accessLevel == AccessLevel.ReadWrite;
if (!writeAllowed && !_readOnlyGuard.IsQuerySafe(query))
    return SqlToAiError.WriteOperationBlocked("The query contains mutating SQL keywords and was rejected.");

// Schritt 5: Multi-Statement-Detector (Batch-Schutz)
if (SqlMultiStatementDetector.ContainsMultipleStatements(query))
    return SqlToAiError.MultipleStatementsForbidden();
```

#### Warum MUSS das angepasst werden?
1. **Sicherheitsrisiko durch Refactoring-Drift:** Guardrails sind die zentrale Schutzschicht von `SqlToAi`. Änderungen am Sicherheitsmodell (z. B. neue Berechtigungsprüfungen) müssen an vier Stellen fehlerfrei synchron gehalten werden.
2. **Text-Inkonsistenzen:** Bereits heute weichen Fehlermeldungen ab (z. B. `QueryValidationService`: `$"Database '{databaseName}' has AccessLevel None."` vs. `QueryExecutionService`: `$"Database '{databaseName}' does not permit query execution (AccessLevel: {accessLevel})."`).
3. **Hohe Konstruktorkopplung:** Alle 4 Services müssen `ISecurityGuard`, `IAccessLevelProvider` und `IReadOnlyGuard` separat per Dependency Injection einbinden.

#### Konkrete Handlungsempfehlung
Extraktion eines gemeinsamen Pipeline-Dienstes:
```csharp
public sealed record QuerySafetyCheckResult(AccessLevel AccessLevel, bool IsWriteAllowed);

public interface IQuerySafetyValidator
{
    Task<Result<QuerySafetyCheckResult>> ValidateQuerySafetyAsync(
        string databaseName,
        string query,
        bool allowSchemaOnly = false,
        CancellationToken cancellationToken = default);
}
```

---

### 🟡 Befund DRY-2: Duplizierte Konstante `DdlUnavailableNote` in Schema-Renderern

- **Status:** **Anpassung empfohlen (Niedrige Priorität / Quick Fix)**
- **Betroffene Dateien:**
  - [src/SqlToAi/Database/TableSchemaRenderer.cs:13-14](src/SqlToAi/Database/TableSchemaRenderer.cs#L13-L14)
  - [src/SqlToAi/Database/DetailSchemaRenderer.cs:11-12](src/SqlToAi/Database/DetailSchemaRenderer.cs#L11-L12)

#### Beschreibung & Begründung
Beide Klassen definieren identisch:
```csharp
private const string DdlUnavailableNote =
    "*Definition not available — either the object is encrypted, or the configured login lacks VIEW DEFINITION permission on it.*";
```
Sollte zentral als `internal const string` (z. B. in `DetailSchemaRenderer.DdlUnavailableNote`) hinterlegt werden, um Textabweichungen bei künftigen Änderungen auszuschließen.

---

### 🟡 Befund DRY-3: `StringParam` vs. `OptionalStringParam` in `ToolRegistry.cs`

- **Status:** **Anpassung empfohlen (Niedrige Priorität / Cleanup)**
- **Betroffene Datei:**
  - [src/SqlToAi/Mcp/ToolRegistry.cs:350-358](src/SqlToAi/Mcp/ToolRegistry.cs#L350-L358)

#### Beschreibung
```csharp
private static ToolParameterDefinition StringParam(string description) =>
    new() { Type = "string", Description = description };

private static ToolParameterDefinition OptionalStringParam(string description) =>
    new() { Type = "string", Description = description };
```
Beide Hilfsmethoden erzeugen exakt dasselbe DTO. Im MCP JSON Schema wird die Optionalität ausschließlich über das `Required = [...]` Array des übergeordneten `ToolInputSchema` festgelegt. `OptionalStringParam` ist ein reines Scheinduplikat und sollte durch `StringParam` ersetzt werden.

---

### 🟡 Befund DRY-4: Boilerplate für 5 Standard-Tools in `ToolRegistry.cs`

- **Status:** **Anpassung empfohlen (Niedrige Priorität / Wartbarkeit)**
- **Betroffene Datei:**
  - [src/SqlToAi/Mcp/ToolRegistry.cs:119-208](src/SqlToAi/Mcp/ToolRegistry.cs#L119-L208)

#### Beschreibung
Die Methoden `BuildGetSchemaForeignKeys`, `BuildGetSchemaIndexes`, `BuildGetSchemaConstraints`, `BuildGetObjectReferences` und `BuildGetRoutineParameters` instanziieren identische Schemas mit den beiden Pflichtfeldern `object_name` und `database`.
Ein Builder-Helper `BuildObjectDetailTool(string name, string description, string objectDescription)` reduziert ca. 60 Zeilen redundanten DTO-Code.

---

### 🟡 Befund DRY-5: Timeout & Regex-Handling in `GlobMatcher` vs. `LikePatternMatcher`

- **Status:** **Teilweise Anpassung empfohlen (Mittlere Priorität)**
- **Betroffene Dateien:**
  - [src/SqlToAi/Anonymization/LikePatternMatcher.cs:15-35](src/SqlToAi/Anonymization/LikePatternMatcher.cs#L15-L35)
  - [src/SqlToAi/Domain/GlobMatcher.cs:17-50](src/SqlToAi/Domain/GlobMatcher.cs#L17-L50)

#### Begründung
- **Was angepasst werden soll:** `private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);` und das Fail-Closed Timeout-Catching existieren in beiden Klassen. Die Timeout-Konstante sollte zentral bezogen werden.
- **Was NICHT vereinigt werden soll:** Die Klassen selbst trennen sauber zwischen SQL-`LIKE` (`%`/`_` mit Spezifitäts-Scoring für Anonymisierungsregeln) und Datei-/Whitelist-`Glob` (`*`/`?`).

---

### ⚪ Positivbefunde (Warum NICHT anpassen)

- **DRY-P1 ([SchemaService.cs:220-249](src/SqlToAi/Database/SchemaService.cs#L220-L249)):** Die 6 Methoden sind 1-zeilige Delegationen auf `ExecuteDetailQueryAsync`. Sie müssen als separate Methoden im öffentlichen Interface `ISchemaService` existieren.
- **DRY-P2 ([Program.cs:265-285](src/SqlToAi/Program.cs#L265-L285)):** `ParseRollingInterval` vs. `ParseLogLevel` parsen Strings zu disjunkten Ziel-Enums. Switch-Expressions sind hier typsicher, AOT-freundlich und lesbarer als reflexionsbasierte Generics.
- **DRY-P3 ([McpHost.cs:199-219](src/SqlToAi/Mcp/McpHost.cs#L199-L219)):** `WriteResultAndCapture` vs. `WriteErrorAndCapture` serialisieren unterschiedliche DTOs über `System.Text.Json` Source Generation (`McpJsonContext.Default`). Eine Zusammenlegung würde den AOT-freundlichen Source Generator erschweren.

---

## 3. Detaillierte Magic-Values-Befunde (Produktionscode)

### 🔴 Befund MV-1: SQL Server Error Numbers verstreut im Code

- **Status:** **Anpassung dringend empfohlen (Hohe Priorität)**
- **Betroffene Dateien:**
  - [src/SqlToAi/Database/SqlToAiErrorMapper.cs:48, 75](src/SqlToAi/Database/SqlToAiErrorMapper.cs#L48-L75)
  - [src/SqlToAi/Database/PerformanceMeasurementService.cs:168](src/SqlToAi/Database/PerformanceMeasurementService.cs#L168)
  - [src/SqlToAi/Database/IndexSuggestionService.cs:291-292](src/SqlToAi/Database/IndexSuggestionService.cs#L291-L292)

#### Gefundene Magic Numbers & Bedeutung
- `262`: SHOWPLAN Berechtigung fehlt (`IsPermissionError(ex, 262, "SHOWPLAN")`)
- `300`: VIEW SERVER STATE Berechtigung ungenügend (`IsPermissionError(ex, 300, "VIEW SERVER STATE")`)
- `297`: Berechtigung zur Aktion verweigert (`IsPermissionError(ex, 297, "VIEW SERVER STATE")`)
- `-2`: Client Query Timeout
- `121`: Semaphore Timeout
- `258`: Wait Timeout
- `20`, `40`, `53`, `233`, `10054`, `10060`, `10061`: Netzwerk- & Verbindungsfehler
- `18456`: Login Failed

#### Handlungsempfehlung
Zentralisierung in `SqlServerErrorCode.cs`:
```csharp
namespace SqlToAi.Database;

internal static class SqlServerErrorCode
{
    // Permissions
    public const int ShowplanPermissionMissing = 262;
    public const int ActionPermissionDenied = 297;
    public const int InsufficientPermission = 300;

    // Timeouts
    public const int ClientQueryTimeout = -2;
    public const int SemaphoreTimeout = 121;
    public const int WaitTimeout = 258;

    // Connectivity & Auth
    public const int ConnectionInitializationError = 233;
    public const int ConnectionReset = 10054;
    public const int LoginFailed = 18456;
}
```

---

### 🟡 Befund MV-2: Benchmark-Verdicts als Magic Strings

- **Status:** **Anpassung empfohlen (Mittlere Priorität)**
- **Betroffene Dateien:**
  - [src/SqlToAi/Database/OptimizationBenchmarkService.cs:104, 115, 123, 129](src/SqlToAi/Database/OptimizationBenchmarkService.cs#L104-L129)
  - [src/SqlToAi/Mcp/ToolRegistry.cs:288-291](src/SqlToAi/Mcp/ToolRegistry.cs#L288-L291)
  - [tests/SqlToAi.Tests/Database/OptimizationBenchmarkServiceTests.cs:51, 83](tests/SqlToAi.Tests/Database/OptimizationBenchmarkServiceTests.cs#L51)

#### Gefundene Werte: `"Recommended"`, `"NotRecommended"`, `"UnsafeDueToDataMismatch"`, `"Neutral"`
- **Begründung:** Diese Werte definieren den festen Ergebnisvertrag des MCP-Tools `sql_benchmark_optimization`. Sie sollten als `BenchmarkVerdict`-Konstantenklasse definiert werden.

---

### 🟡 Befund MV-3: Gewichtungsfaktoren in `AnonymizationRuleProvider.WeightedScore`

- **Status:** **Anpassung empfohlen (Mittlere Priorität)**
- **Betroffene Datei:**
  - [src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs:290](src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs#L290)

```csharp
private static int WeightedScore(int[] scores) =>
    (scores[0] * 1000) + (scores[1] * 100) + (scores[2] * 10) + scores[3];
```
- **Begründung:** Multiplikatoren `1000, 100, 10, 1` sollten als `DatabaseDimensionWeight = 1000`, `SchemaDimensionWeight = 100`, `TableDimensionWeight = 10` deklariert werden.

---

### 🟡 Befund MV-4: FNV-1a Hash Constants in `Anonymizer.cs`

- **Status:** **Anpassung empfohlen (Mittlere Priorität)**
- **Betroffene Datei:**
  - [src/SqlToAi/Anonymization/Anonymizer.cs:126, 130](src/SqlToAi/Anonymization/Anonymizer.cs#L126-L130)

```csharp
uint hash = 2166136261;
hash *= 16777619;
```
- **Begründung:** Das sind die mathematischen 32-Bit FNV-1a Konstanten. Benannte Konstanten (`FnvOffsetBasis32 = 2166136261u;`, `FnvPrime32 = 16777619u;`) machen die Herkunft und Absicht transparent.

---

### 🟡 Befund MV-5: Regex-Timeouts (`200 ms`)

- **Status:** **Anpassung empfohlen (Mittlere Priorität)**
- **Betroffene Dateien:**
  - [src/SqlToAi/Security/ReadOnlyGuard.cs:24](src/SqlToAi/Security/ReadOnlyGuard.cs#L24)
  - [src/SqlToAi/Anonymization/LikePatternMatcher.cs:15](src/SqlToAi/Anonymization/LikePatternMatcher.cs#L15)
  - [src/SqlToAi/Domain/GlobMatcher.cs:17](src/SqlToAi/Domain/GlobMatcher.cs#L17)
- **Begründung:** Einheitliche Definition in `SecurityConstants.DefaultRegexTimeout` bündelt die ReDoS-Schutzgrenze an einer zentralen Stelle.

---

### 🟢 Befunde MV-6 & MV-7: Modus- & Objekttyp-Strings

- **Status:** **Niedrige Priorität / Code-Hygiene**
- **Betroffene Stellen:**
  - `Anonymizer.cs:88`: Modus `"Hash"` vs. `"Scramble"` -> `AnonymizationModes.Hash`
  - `DetailSchemaRenderer.cs:30`: `objectType != "U" && objectType != "V"` -> `SqlServerObjectType.UserTable = "U"`, `SqlServerObjectType.View = "V"`

---

### ⚪ Positivbefunde (Warum NICHT anpassen)

- **MV-P1 ([SqlToAiOptions.cs](src/SqlToAi/Configuration/SqlToAiOptions.cs)):** Standardwerte wie `DefaultRowLimit = 100`, `MaxRowLimit = 1000` sind Property-Initialisierer. Nach Richtlinie §4 ist dies die einzig zulässige Stelle für Defaults.
- **MV-P2 ([AppSettingsMigrator.cs:194, 251](src/SqlToAi/Configuration/AppSettingsMigrator.cs#L194)):** `"Password"` ist der Name des JSON-Schlüssels im Backup-Cleaner, kein Passwort.
- **MV-P3 ([SqlToAiError.cs:11-21](src/SqlToAi/Domain/SqlToAiError.cs#L11-L21)):** `SQL-AI-0001` bis `SQL-AI-0110` sind bereits saubere `internal const` Definitionen.

---

## 4. Detaillierte DRY- & Magic-Values-Befunde (Testcode)

### 🟡 Befund DRY-T1: Verstreute Fakes & Mock-Implementierungen

- **Status:** **Anpassung empfohlen (Mittlere Priorität / Test-Qualität)**
- **Betroffene Dateien:**
  - `tests/SqlToAi.Tests/Database/OptimizationBenchmarkServiceTests.cs:22` (`FakeComparisonService`) vs. `tests/SqlToAi.Tests/Mcp/ToolDispatcherTestFakes.cs:139` (`FakeQueryComparisonService`)
  - `tests/SqlToAi.Tests/Database/QueryExecutionServiceMockDb.cs:29` vs. `tests/SqlToAi.Tests/Database/SchemaServiceMockDb.cs:109`
  - `tests/SqlToAi.Tests/Mcp/McpTrailWriterRedactionTests.cs:34` vs. `tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs:32` (`GetDayDir()`)
- **Begründung:** Testklassen implementieren private Fakes lokal neu, anstatt die vorhandenen Fakes aus `ToolDispatcherTestFakes.cs` bzw. `TestSupport/` wiederzuverwenden.

---

### 🟡 Befund DRY-T2: Duplizierte ShowPlan-XML Test-Fixtures

- **Status:** **Anpassung empfohlen (Mittlere Priorität / Test-Wartbarkeit)**
- **Betroffene Datei:**
  - [tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs:97-374](tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs#L97-L374)
- **Beschreibung:** 8 Testmethoden enthalten jeweils 20–30 Zeilen XML-Strings mit minimalen Unterschieden (`Usage="EQUALITY"`, `Descending="True"` etc.).
- **Empfehlung:** Einführung eines XML-Builders in `TestSupport/ShowPlanTestHelper.cs`.

---

### 🟡 Befund DRY-T3: 33 redundante Guardrail-Negative-Tests

- **Status:** **Konsolidierung nach Behebung von DRY-1 (Mittlere Priorität)**
- **Betroffene Dateien:**
  - `IndexSuggestionServiceTests.cs`, `PerformanceMeasurementServiceTests.cs`, `QueryComparisonServiceTests.cs`, `QueryExecutionServiceTests.cs`, `QueryValidationServiceTests.cs`
- **Beschreibung:** 33 Testmethoden prüfen identisch `EmptyDatabase`, `EmptyQuery`, `DatabaseNotAllowed`, `AccessLevelNone`, `MutatingQuery`, `MultiStatement`.
- **Empfehlung:** Sobald `IQuerySafetyValidator` (DRY-1) extrahiert ist, werden diese Tests gebündelt in `QuerySafetyValidatorTests.cs` geführt.

---

## 5. 3-Phasen-Aktionsplan zur Behebung

```mermaid
flowchart TD
    subgraph Phase 1: Konstanten & Hygiene (Quick Wins)
        A1[SqlServerErrorCode.cs erstellen] --> A2[BenchmarkVerdict.cs & AnonymizationModes.cs erstellen]
        A2 --> A3[RegexTimeout & FNV-Konstanten bündeln]
        A3 --> A4[DdlUnavailableNote & StringParam bereinigen]
    end

    subgraph Phase 2: Core Guardrail Pipeline (DRY-1)
        B1[IQuerySafetyValidator & QuerySafetyPipeline erstellen] --> B2[Services auf Pipeline umstellen]
        B2 --> B3[Constructor-Dependencies in Services reduzieren]
    end

    subgraph Phase 3: Test-Suite Konsolidierung
        C1[ShowPlanTestHelper erstellen] --> C2[Zentrale Fakes in TestSupport bündeln]
        C2 --> C3[33 Guardrail-Tests in QuerySafetyValidatorTests bündeln]
    end

    Phase 1 --> Phase 2 --> Phase 3
```

1. **Phase 1: Konstanten & Quick Wins (Kein Architektur-Risiko)**
   - `SqlServerErrorCode.cs` für SQL-Fehlercodes (`262`, `297`, `300`, `121` etc.).
   - `BenchmarkVerdict.cs` und `AnonymizationModes.cs`.
   - `FnvOffsetBasis32` / `FnvPrime32` in `Anonymizer.cs`.
   - `SecurityConstants.DefaultRegexTimeout`.
   - `OptionalStringParam` in `ToolRegistry.cs` entfernen.

2. **Phase 2: Architektur-Konsolidierung der Guardrails (DRY-1)**
   - `IQuerySafetyValidator` Pipeline erstellen.
   - `QueryExecutionService`, `QueryValidationService`, `PerformanceMeasurementService` und `QueryComparisonService` entlasten.

3. **Phase 3: Test-Suite Konsolidierung (DRY-T1 bis T3)**
   - Fakes und ShowPlan-XML-Fixtures zentralisieren.
   - 33 duplizierte Guardrail-Tests konsolidieren.
