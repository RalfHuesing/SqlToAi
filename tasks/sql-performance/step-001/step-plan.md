---
step: step-001
epic: EPIC-01
status: done (fix-01 pending)  # open | in_progress | done (pending audit) | blocked
step_type: single  # single | batch
estimated_risk: low
rules_refs:
  - .agents/rules/AiNetLinter.mdc
  - .agents/rules/SqlToAiRichtlinien.mdc
related_to: []
created_by_model: Claude Sonnet 4.6
created_by_model_knowledge_cutoff: 2025-04
---

# step-001: PerformanceMetrics min/avg/max erweitern

## Ziel

`PerformanceMetrics` um nullable `min_elapsed_ms`/`max_elapsed_ms`/`min_cpu_ms`/`max_cpu_ms`-Felder erweitern.
`ProcessCapturedOutput` so umbauen, dass Messages **pro Run** zugeordnet werden (nicht summiert über alle Runs).
Tests für das neue min/avg/max-Verhalten ergänzen.

## Aktueller Projektzustand (JIT-Kontext)

**PerformanceMeasurementResult.cs (L10–15):** `PerformanceMetrics` hat 5 Felder: `CpuTimeMs`, `ElapsedTimeMs`, `LogicalReads`, `PhysicalReads`, `ReadAheadReads` — alle `long`, kein nullable.

**PerformanceMeasurementService.cs — ProcessCapturedOutput (L302–337):**
- Iteriert über alle messages, summiert totalCpu/totalElapsed/etc., dividiert durch execRuns → nur avg
- Problem: bei N runs stehen alle N × messages in einer einzigen Liste; kein per-Run-Trenner

**ExecuteMeasuredRunsAsync (L234–265):**
- Schleife `for i = 0..execRuns`, ruft `RunQueryOnceAsync` auf
- `messages` ist eine gemeinsame Liste, wird **vor dem Loop** nicht und **nach jedem Run nicht** gecleart
- `messages.Clear()` passiert **vor** dem Measured-Loop (L188 in ExecuteMeasurementAsync, nach Warmup-Runs)

**McpJsonContext.cs:** `Domain.PerformanceMetrics` ist bereits registriert — wird nach Record-Erweiterung automatisch korrekt serialisiert (nullable long? wird durch `DefaultIgnoreCondition.WhenWritingNull` in null-Fall weggelassen).

**PerformanceMeasurementServiceTests.cs:** Hat Tests für Security-Guards und ParseExecutionPlanXml, aber keinen Test für das min/avg/max-Verhalten von ProcessCapturedOutput.

## Implementierungsplan

### 1. PerformanceMeasurementResult.cs — PerformanceMetrics erweitern

```csharp
public sealed record PerformanceMetrics(
    [property: JsonPropertyName("cpu_time_ms")]    long CpuTimeMs,
    [property: JsonPropertyName("elapsed_time_ms")] long ElapsedTimeMs,
    [property: JsonPropertyName("logical_reads")]   long LogicalReads,
    [property: JsonPropertyName("physical_reads")]  long PhysicalReads,
    [property: JsonPropertyName("read_ahead_reads")] long ReadAheadReads,
    [property: JsonPropertyName("min_elapsed_ms")] long? MinElapsedMs,
    [property: JsonPropertyName("max_elapsed_ms")] long? MaxElapsedMs,
    [property: JsonPropertyName("min_cpu_ms")]     long? MinCpuMs,
    [property: JsonPropertyName("max_cpu_ms")]     long? MaxCpuMs);
```

- `avg_*`-Felder = bisherige Semantik (rückwärtskompatibel): `CpuTimeMs`, `ElapsedTimeMs` etc.
- `null` wenn `execRuns == 1` (kein Min/Max bei einem einzigen Run)

### 2. PerformanceMeasurementService.cs — ProcessCapturedOutput + ExecuteMeasuredRunsAsync umbauen

Strategie: In `ExecuteMeasuredRunsAsync` nach jedem Run eine Snapshot-Liste (per-Run Messages) akkumulieren. `messages` wird vor jedem einzelnen Run gecleart, nach dem Run wird der Snapshot gespeichert.

**ExecuteMeasuredRunsAsync:**
```csharp
// messages-Liste vor jedem Run clearen, danach Snapshot sammeln
var perRunMessages = new List<List<string>>(execRuns);
for (int i = 0; i < execRuns; i++)
{
    messages.Clear();
    // RunQueryOnceAsync...
    perRunMessages.Add([.. messages]);
}
// perRunMessages an ProcessCapturedOutput übergeben
```

**ProcessCapturedOutput Signatur anpassen:**
```csharp
private static (PerformanceMetrics, IReadOnlyList<PerformancePlanWarning>) ProcessCapturedOutput(
    List<List<string>> perRunMessages, string? xmlPlanText, int execRuns, bool hasShowplanPermission)
```

Berechnung:
- pro Run: CpuTimeMs und ElapsedTimeMs extrahieren
- min/max über alle Runs berechnen
- avg = sum / execRuns (bisherige Semantik)
- Min*/Max* = null wenn execRuns == 1

**Achtung Linter:** `ProcessCapturedOutput` ist aktuell ~35 Zeilen. Nach Umbau auf per-Run-Verarbeitung kann sie länger werden. Komplexe Berechnungen in eine neue private Methode `ComputeRunMetrics` auslagern (≤60 Zeilen pro Methode).

### 3. McpJsonContext.cs — kein Eingriff nötig

`Domain.PerformanceMetrics` ist bereits registriert. Die neuen nullable `long?`-Felder werden automatisch unterstützt — `DefaultIgnoreCondition.WhenWritingNull` sorgt dafür, dass `null`-Felder im JSON weggelassen werden.

### 4. Tests in PerformanceMeasurementServiceTests.cs ergänzen

Neue Tests für `ProcessCapturedOutput`-Verhalten (via `ParseExecutionPlanXml` ist bereits public; ProcessCapturedOutput ist private static → via reflection oder Messages-basierter Hilfsklasse testen, oder den öffentlichen Pfad über einen Mock nutzen).

Besser: `ProcessCapturedOutput` als `internal static` markieren und `InternalsVisibleTo` nutzen — oder die Logik durch die öffentliche API testen.

Da `PerformanceMeasurementService` nur via Integration testbar ist (braucht SqlConnection), testen wir die Metriken-Berechnung durch direkten Aufruf einer extrahierten `internal static`-Hilfsmethode:

- Test: execRuns=1 → MinElapsedMs null, MaxElapsedMs null
- Test: execRuns=3 mit verschiedenen Werten → min/avg/max korrekt berechnet

## Abnahme-Kriterien

1. `dotnet build` → 0 Warnings, 0 Errors
2. `dotnet test` → alle Tests grün
3. `PerformanceMetrics` hat nullable min/max-Felder
4. Bei execRuns=1: min/max = null im JSON (weggelassen durch WhenWritingNull)
5. Bei execRuns>1: min/max korrekt berechnet
6. Keine neuen appsettings nötig (kein Config-Wert eingeführt)
7. McpJsonContext.cs: kein Eingriff nötig (Felder werden automatisch serialisiert)

## Dateien

| Datei | Änderung |
|:--|:--|
| `src/SqlToAi/Domain/PerformanceMeasurementResult.cs` | PerformanceMetrics um 4 nullable Felder erweitern |
| `src/SqlToAi/Database/PerformanceMeasurementService.cs` | ExecuteMeasuredRunsAsync + ProcessCapturedOutput umbauen |
| `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` | Tests für min/avg/max ergänzen |

## Nicht in diesem Step

- EPIC-02 (QueryExecutionService STATISTICS) → anderer Step
- EPIC-03 (ToolRegistry Descriptions) → anderer Step  
- EPIC-04 (Dokumentation) → anderer Step
- McpJsonContext.cs: kein Eingriff (Felder werden automatisch serialisiert)
