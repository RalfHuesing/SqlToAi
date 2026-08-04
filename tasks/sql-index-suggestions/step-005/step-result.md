---
status: done (pending audit)
type: step-result
task: sql-index-suggestions
step: 005
epic: EPIC-04
step_type: single
coded_by: coder
coded_by_model: MiniMax-M3
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-05T15:00:00+02:00
code_commit_hash: a1492c6
status_after: done
blocker_category: n/a
---

# Result Step 005: TD-002 — `DESC`-Sortierung in `BuildCreateIndexStatement` korrekt rendern

## Zusammenfassung

`ExtractMissingIndexWarnings` wertet jetzt das `Descending`-Attribut der
`<Column>`-Kinder aus. Neuer privater Helper `WithDescendingSuffix` hängt
für `EQUALITY`/`INEQUALITY`-Spalten mit `Descending="True"` ein ` DESC`
an den Spaltennamen an; `INCLUDE`-Spalten bleiben defensiv unverändert
(auch wenn `Descending="True"` dort stünde). `BuildCreateIndexStatement`
ist unangetastet — `string.Join(", ", keyCols)` rendert die vorgefertigten
Strings automatisch korrekt. Vier neue Tests verifizieren den neuen Pfad
sowie Regressionsschutz und Edge-Cases.

## Geänderte Dateien

- `src/SqlToAi/Database/PerformanceMeasurementService.cs` — `ExtractMissingIndexWarnings`
  ruft `WithDescendingSuffix` für `EQUALITY`/`INEQUALITY` auf; neue private
  Helper-Methode `WithDescendingSuffix` rendert `name + " DESC"` wenn das
  passende `<Column>`-Kind `Descending="True"` (case-insensitive exakt)
  trägt. INCLUDE-Zweig und LINQ-Kette für die Namen-Extraktion bleiben
  semantisch identisch.
- `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` —
  vier neue Tests:
  - `ParseExecutionPlanXml_MissingIndex_DescendingColumn_RendersDescSuffix`
    (Pflicht: gemischter Fall, EQUALITY ohne Descending + INEQUALITY mit
    Descending="True" + INCLUDE)
  - `ParseExecutionPlanXml_MissingIndex_DescendingFalse_IsAscendingLikeBefore`
    (Pflicht: Regressionsschutz gegen zu-permissive Auswertung)
  - `ParseExecutionPlanXml_MissingIndex_AllColumnsDescending_RendersAllDesc`
    (optional: zwei EQUALITY-Spalten mit Descending="True")
  - `ParseExecutionPlanXml_MissingIndex_DescendingInInclude_IsIgnored`
    (optional: INCLUDE-Spalte mit Descending="True" bekommt kein DESC)
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` —
  automatisch durch `AiNetLinterTests.RecreateBaseline` aktualisiert
  (SHA-256-Hashes der beiden geänderten Quell-/Test-Dateien).

## Commit

- **Code-Commit-Hash:** `a1492c6`
- **Message:**
  ```
  feat(parser): DESC-Attribut in Missing-Index-DDL [sql-index-suggestions]

  - ExtractMissingIndexWarnings wertet das Descending-Attribut der Column-Kinder aus.
  - Neuer privater Helper WithDescendingSuffix haengt ' DESC' an EQUALITY/INEQUALITY-Spalten mit Descending='True' an; INCLUDE-Spalten bleiben unveraendert.
  - BuildCreateIndexStatement bleibt unveraendert (string.Join propagiert den Suffix).
  - Vier neue Tests: Descending='True', Descending='False', AllColumnsDescending, DescendingInInclude-IsIgnored.

  Refs: tasks/sql-index-suggestions/step-005
  ```
- **Branch:** `main`
- **Push:** nein (lokal)

## Build-/Test-Output

```
dotnet build    → grün (0 Warnungen, 0 Fehler)
dotnet test     → grün (526 Tests, 0 Fehler; AiNetLinterTests.RecreateBaseline aktualisierte Baseline automatisch)
```

Gezielter Vor-Lauf der Missing-Index-Tests: 8/8 grün (4 bestehende + 4 neue).

## Abweichungen vom Plan

- **Subject gekürzt:** Der Plan-Vorschlag
  `feat(parser): berücksichtige Descending-Attribut in Missing-Index-DDL [sql-index-suggestions]`
  ist 93 Zeichen und überschreitet das Subject-Limit von 72. Verkürzt auf
  `feat(parser): DESC-Attribut in Missing-Index-DDL [sql-index-suggestions]`
  (72 Zeichen, exakt am Limit). Inhaltlich identisch, Komma/Präzision
  geht nicht verloren.
- **Helper-Variante:** Plan bot drei Hauptvarianten (A/B/C) plus
  Helper-Ansatz. Umgesetzt: Helper-Ansatz (Variante aus
  `step-plan.md` §„Code-Skizze" Variante 1, `WithDescendingSuffix` als
  private statische Methode statt lokaler Function). Begründung:
  INCLUDE-Zweig bleibt 1:1 wie zuvor, Switch-Arms bleiben flach
  (1 Zeile pro Arm), Helper ist klein (10 LOC) und gut testbar über
  die bestehende Test-Infrastruktur. Kein Konflikt mit
  `MaxMethodLineCount 60` für `ExtractMissingIndexWarnings`
  (45 LOC inkl. 2 Kommentarzeilen).

## Beobachtungen

- **`BuildCreateIndexStatement` bleibt semantisch unverändert:** Das ist
  exakt der Plan-Punkt und funktioniert wie spezifiziert. Der Plan weist
  mehrfach darauf hin, dass `string.Join(", ", keyCols)` den
  `name + " DESC"`-String 1:1 propagiert — bestätigt. Keine
  zusätzliche Render-Logik nötig.
- **Mehrere `ColumnGroup`-Kinder pro `MissingIndex`:** Die
  Test-XML-Fixtures haben je einen `ColumnGroup` pro Usage; in
  SQL-Server-XML-Plans theoretisch mehrere `EQUALITY`-Gruppen möglich
  (Schema erlaubt). Aktueller Code addiert alle in die jeweilige
  Liste, die `Descending`-Auswertung läuft pro Spalte unabhängig.
  Kein zusätzlicher Test, weil das Schema-Multiple-Fall kein
  neues Verhalten hinzufügt (gleicher Mechanismus wie
  single-Group).
- **`Descending`-Index-Name-Suffix nicht betroffen:** Der
  Index-Name `IX_Orders_CustomerId__OrderDate` (mit doppeltem
  Unterstrich für mehrere Key-Columns) kommt aus
  `BuildCreateIndexStatement` und nutzt nur die unmodifizierten
  Spaltennamen ohne `DESC`-Suffix. Auch wenn `OrderDate` mit
  Descending="True" markiert wäre, bliebe der Index-Name
  `IX_Orders_CustomerId__OrderDate` (Suffix-frei). Das ist
  konsistent zum bestehenden Verhalten und nicht TD-002-relevant,
  könnte aber ein zukünftiger Diskussionspunkt sein, falls jemand
  `IX_Orders_CustomerId__OrderDate_DESC` als lesbarer empfindet.
- **CA1859 / `List<string>` als Parametertyp:** Die Signatur
  `WithDescendingSuffix(XElement, XNamespace, List<string>)` nimmt
  `List<string>` statt `IReadOnlyList<string>`. Das ist konsistent
  zum bestehenden Stil in `BuildCreateIndexStatement` (gleiche
  `List<string>`-Parameter). Ein CA1859-Refactor wäre außerhalb
  dieses Step-Scopes.

## Bekannte Unschärfen

- **Plan-Empfehlung „Variante A/B mit Include-Spezialfall nach (iii)":**
  Der Plan empfiehlt primär den Tuple-LINQ-Ansatz, sagt aber explizit
  „einfachste robuste Lösung: `Descending`-Auswertung *innerhalb* der
  `switch (usage)`-Zweige für EQUALITY/INEQUALITY" als Alternative.
  Beide Wege sind regelkonform. Habe Helper-Variante gewählt, weil sie
  den Switch-Block am lesbarsten hält. Falls der Kritiker die
  Inline-Variante bevorzugt, ist die Migration trivial
  (Helper entfernen, `foreach` in die zwei Switch-Arms ziehen,
  ~5 Zeilen zusätzlich pro Arm).
- **`XNamespace`-Parameter im Helper:** `XNamespace` ist ein
  `XName`-artiger Wrapper, also ein „complex object", der beim
  `MaxMethodParameterCount`-Limit (4) als ein Parameter zählt. Habe
  das Limit geprüft: 3 Parameter, kein Problem. Helper könnte statt
  `ns` auch `XName` direkt nehmen (`ns + "Column"`), aber die
  bestehende Signatur spiegelt das Aufrufer-Pattern 1:1 wider.
- **Reihenfolge der Child-Element-Extraktion:** Der Helper iteriert
  die `<Column>`-Kinder des `ColumnGroup` ein zweites Mal. Das ist
  eine Allokation von ~1 Listen-Header + 1-3 `XElement`-Referenzen
  pro `ColumnGroup` — vernachlässigbar. Eine Alternative wäre,
  die Liste einmalig vorab zu materialisieren und durchzureichen,
  aber das verkompliziert die `ExtractMissingIndexWarnings`-Signatur
  für minimalen Performance-Gewinn.
