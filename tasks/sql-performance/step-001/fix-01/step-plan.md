---
status: done (pending audit)
type: step-plan
task: sql-performance
step: 001/fix-01
title: "Gültigkeits-Erkennung für Min/Max strukturell statt über Wert-Schwellenwert bestimmen"
epic: EPIC-01
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-03T16:00:00+02:00
related_to:
  - tasks/sql-performance/step-001/step-review.md
---

# Step 001/fix-01: Gültigkeits-Erkennung für Min/Max strukturell statt über Wert-Schwellenwert bestimmen

## Bezug

- **Task:** `sql-performance`
- **Epic:** `EPIC-01` aus `roadmap.md` — Fix zum bereits umgesetzten Step-001
  (PerformanceMetrics min/avg/max), behebt einen von `step-001/step-review.md`
  gemeldeten MAJOR-Finding, ohne den Epic-Status selbst zu verändern.
- **Review-Referenz:** `tasks/sql-performance/step-001/step-review.md`,
  Abschnitt „Findings", Eintrag 1 (Logische Korrektheit / Konzept-Treue).

## Aktueller Projektzustand (JIT-Kontext)

`src/SqlToAi/Database/PerformanceMetricsCalculator.cs` (vollständig gelesen,
Stand nach Step-001):

- `Compute` (Zeile 28-65) iteriert `perRunMessages`, ruft pro Run
  `ParseRunMessages` auf und akkumuliert `totalCpu`/`totalElapsed`/etc. für
  den Avg-Wert (unverändert korrekt, nicht Teil dieses Fixes).
- Die Gültigkeits-Prüfung für Min/Max steht in Zeile 43: `if (runCpu > 0 ||
  runElapsed > 0)`. Nur wenn diese Bedingung zutrifft, fließt der Run in
  `minCpu`/`maxCpu`/`minElapsed`/`maxElapsed` ein.
- `ParseRunMessages` (Zeile 74-96) gibt aktuell ein 5-Tupel
  `(long Cpu, long Elapsed, long Logical, long Physical, long ReadAhead)`
  zurück. Ob `CpuTimeRegex` für diesen Run überhaupt gematcht hat, wird
  **nicht** nach außen gegeben — die aufrufende Seite (`Compute`) hat also
  keine Möglichkeit, zwischen „kein Match" und „Match mit Wert 0" zu
  unterscheiden, und behilft sich stattdessen mit dem Wert-Schwellenwert
  `runCpu > 0 || runElapsed > 0`. Das ist exakt der im Review gemeldete
  Fehler: ein Run mit echtem `CPU time = 0 ms, elapsed time = 0 ms` (Regex
  hat gematcht, Werte sind einfach `0`) wird fälschlich wie ein Run ohne
  STATISTICS-Match behandelt und aus Min/Max ausgeschlossen.
- `OrNullIfSingleRun` (Zeile 67-72) selbst braucht **keine** Änderung: Sie
  prüft nur, ob `value != sentinel` (also ob überhaupt ein Run die
  Sentinel-Werte `long.MaxValue`/`long.MinValue` überschrieben hat). Wird
  die Gating-Bedingung in `Compute` korrekt auf `HasMatch` umgestellt,
  bleibt dieser Sentinel-Vergleich weiterhin richtig — auch bei
  durchgängig `0 ms`-Runs wird `minCpu`/`maxCpu` dann korrekt auf `0`
  gesetzt und ist `!= long.MaxValue`.
- `PerformanceMetricsCalculatorTests.cs` (vollständig gelesen) hat aktuell
  4 Tests, keiner deckt eine Mischung aus `0`-Runs und Nicht-`0`-Runs oder
  durchgängig `0`-Runs bei `execRuns > 1` ab — exakt die vom Review
  geforderten Testfälle (a) und (b) fehlen.

**Wiederverwendung:** Kein neuer Typ/keine neue Struktur nötig — die
bestehende `ParseRunMessages`-Signatur wird um ein `bool HasMatch` im
Rückgabe-Tupel erweitert (kein neuer Record, kein neues Klassen-Design),
und `Compute` nutzt dieses Flag statt des Wert-Schwellenwerts.

## Intention

Die Gültigkeits-Erkennung eines Runs für Min/Max soll strukturell darauf
basieren, ob die STATISTICS-Regex (`CpuTimeRegex`) für diesen Run
überhaupt gematcht hat — nicht darauf, ob der geparste Wert `> 0` ist.
Damit werden echte `0 ms`-Messwerte korrekt in Min/Max berücksichtigt
(Ergebnis `0` statt `null`), während Runs ohne jede STATISTICS-Message
(kein Match) weiterhin korrekt von Min/Max ausgeschlossen bleiben.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/PerformanceMetricsCalculator.cs`

- **Was:** `ParseRunMessages`-Rückgabetyp um `bool HasMatch` erweitern:
  `private static (long Cpu, long Elapsed, long Logical, long Physical,
  long ReadAhead, bool HasMatch) ParseRunMessages(...)`. `HasMatch` wird
  auf `true` gesetzt, sobald `CpuTimeRegex.Match(msg).Success` für
  mindestens eine Message in diesem Run zutrifft (dieselbe Stelle, an der
  aktuell `cpu`/`elapsed` aus `cpuMatch.Groups` gelesen werden — dort
  zusätzlich `hasMatch = true;` setzen, lokale Variable vor der Schleife
  mit `false` initialisieren, am Ende mit den anderen Werten
  zurückgeben).
- **Was:** In `Compute` (Zeile 36) die Dekonstruktion um `runHasMatch`
  erweitern: `var (runCpu, runElapsed, runLogical, runPhysical,
  runReadAhead, runHasMatch) = ParseRunMessages(runMessages);`
- **Was:** Zeile 43 die Gating-Bedingung von `if (runCpu > 0 || runElapsed
  > 0)` auf `if (runHasMatch)` ändern.
- **Was:** XML-Doc-Kommentar von `OrNullIfSingleRun` (Zeile 68-70)
  anpassen: statt „no run produced a non-zero CPU/elapsed time" korrekt
  „no run's STATISTICS TIME message matched" (Kommentar muss den neuen,
  strukturellen Grund widerspiegeln, nicht den alten Wert-Schwellenwert).
- **Warum:** Behebt den MAJOR-Finding aus dem Review — Gültigkeit eines
  Runs strukturell (Regex-Match) statt über einen Wert-Schwellenwert
  bestimmen, damit echte `0 ms`-Messungen nicht fälschlich als „ungültig"
  gewertet werden.

### Datei 2: `tests/SqlToAi.Tests/Database/PerformanceMetricsCalculatorTests.cs`

- **Was:** Neuer Test `Compute_MixedZeroAndNonZeroRuns_MinIsZeroNotNull`:
  `execRuns: 2` (oder 3), ein Run mit `MakeRunMessages(cpuMs: 0, elapsedMs:
  0)`, mindestens ein weiterer Run mit Nicht-Null-Werten (z. B. `cpuMs:
  50, elapsedMs: 60`). Assert: `result.MinCpuMs == 0L` (nicht `null`),
  `result.MinElapsedMs == 0L` (nicht `null`), `MaxCpuMs`/`MaxElapsedMs`
  entsprechend die Nicht-Null-Werte.
- **Was:** Neuer Test `Compute_AllRunsZero_MinMaxAreZeroNotNull`:
  `execRuns: 2`, alle Runs `MakeRunMessages(cpuMs: 0, elapsedMs: 0)`.
  Assert: `result.MinCpuMs == 0L`, `result.MaxCpuMs == 0L`,
  `result.MinElapsedMs == 0L`, `result.MaxElapsedMs == 0L` — explizit
  `Assert.Equal(0L, ...)`, nicht `Assert.Null(...)`.
- **Warum:** Deckt genau die beiden vom Review geforderten Fälle (a)
  gemischt 0/Nicht-0 und (b) durchgängig 0 bei `execRuns > 1` ab, die
  bisher fehlten und den Fehler nicht aufgedeckt hätten.

## Tests

- [ ] `Compute_MixedZeroAndNonZeroRuns_MinIsZeroNotNull` (neu)
- [ ] `Compute_AllRunsZero_MinMaxAreZeroNotNull` (neu)
- [ ] Bestehende 4 Tests in `PerformanceMetricsCalculatorTests.cs` bleiben
  grün (insbesondere `Compute_EmptyRunMessages_ReturnsZeroMetricsNullMinMax`
  — dort matcht die Regex nicht, `HasMatch` bleibt `false`, Ergebnis muss
  weiterhin `null` liefern, nicht `0`)
- [ ] `dotnet test --filter "Category!=Integration"` gesamt grün

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`roadmap.md`) grün
- [ ] Test-Command aus Tech-Stack-Notiz grün
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-001/fix-01/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/AiNetLinter.mdc` — `Compute` bleibt nach Erweiterung des
  Tupels um ein `bool`-Feld unter dem Cyclomatic-Complexity-Limit (12);
  keine neue Verzweigung wird eingeführt, nur die bestehende Bedingung
  ersetzt — CC sollte gegenüber dem aktuellen Stand nicht steigen. Sealed/
  PascalCase/ASCII/`#nullable enable` bleiben wie vorhanden unverändert
  einzuhalten.
- `.agents/rules/SqlToAiRichtlinien.mdc` — keine neuen Magic
  Values/Config, Zero-Warning-Direktive gilt weiterhin; falls
  `AiNetLinterTests.RecreateBaseline` die Baseline-Datei neu schreibt, ist
  das automatisch und kein manueller Eingriff (wie in Step-001 selbst
  gehandhabt).

## Bekannte Ausnahmen

- Keine.

## Code-Skizze (optional)

```csharp
private static (long Cpu, long Elapsed, long Logical, long Physical, long ReadAhead, bool HasMatch)
    ParseRunMessages(IReadOnlyList<string> messages)
{
    long cpu = 0, elapsed = 0, logical = 0, physical = 0, readAhead = 0;
    bool hasMatch = false;
    foreach (string msg in messages)
    {
        var cpuMatch = CpuTimeRegex.Match(msg);
        if (cpuMatch.Success)
        {
            hasMatch = true;
            cpu     += long.Parse(cpuMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            elapsed += long.Parse(cpuMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        var ioMatch = IoReadsRegex.Match(msg);
        if (ioMatch.Success)
        {
            logical   += long.Parse(ioMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            physical  += long.Parse(ioMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            readAhead += long.Parse(ioMatch.Groups[3].Value, CultureInfo.InvariantCulture);
        }
    }
    return (cpu, elapsed, logical, physical, readAhead, hasMatch);
}
```

In `Compute`:

```csharp
var (runCpu, runElapsed, runLogical, runPhysical, runReadAhead, runHasMatch) = ParseRunMessages(runMessages);
totalCpu      += runCpu;
totalElapsed  += runElapsed;
totalLogical  += runLogical;
totalPhysical += runPhysical;
totalReadAhead += runReadAhead;

if (runHasMatch)
{
    minCpu     = Math.Min(minCpu,     runCpu);
    maxCpu     = Math.Max(maxCpu,     runCpu);
    minElapsed = Math.Min(minElapsed, runElapsed);
    maxElapsed = Math.Max(maxElapsed, runElapsed);
}
```

## Notes

- **Scope-Grenze:** Dieser Fix behebt ausschließlich das eine MAJOR-Finding
  aus `step-001/step-review.md` (Gültigkeits-Schwelle für Min/Max). Die
  IO-Werte (`Logical`/`Physical`/`ReadAhead`) sind vom Review nicht
  beanstandet — `IoReadsRegex`/`HasMatch` für IO-Reads wird bewusst
  **nicht** angefasst, nur die CPU/Elapsed-Gültigkeitsprüfung, die laut
  Finding tatsächlich betroffen ist. Keine sonstigen Beobachtungen aus
  `step-result.md` („Bekannte Unschärfen" war bereits genau dieser Punkt)
  oder `tech-debt.md` sind Teil dieses Fixes.
- Der bestehende Test `Compute_EmptyRunMessages_ReturnsZeroMetricsNullMinMax`
  ist der Regressionstest für den „echtes kein Match"-Fall (leere
  Message-Liste → `hasMatch` bleibt `false` → weiterhin `null`, nicht `0`)
  und muss nach dem Fix weiterhin unverändert grün sein — sollte er das
  nicht mehr sein, ist die `HasMatch`-Logik falsch verdrahtet.
