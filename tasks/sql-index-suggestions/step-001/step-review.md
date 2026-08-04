---
status: done
type: step-review
task: sql-index-suggestions
step: 001
epic: EPIC-01
step_type: single
reviewed_by: kritiker
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-04T13:30:00+02:00
verdict: approved
tech_debt_ids: [TD-001, TD-002, TD-003]
---

# Review Step 001: EPIC-01 — Parser-Erweiterung für vollständige CREATE NONCLUSTERED INDEX-Statements

## Verdict

- [x] **approved** — alle vier Prüfebenen ok
- [ ] **issues** — Fix-Step `step-001/fix-XX` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `<rules_dir>/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Konzept-Treue: passt die Umsetzung zu `konzept.md` (Scope, Non-Goals, Muss-Haben)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (oder Baselines beachtet)

## Befund

### Plan-Erfüllung

Alle sechs im Plan genannten Datei-Änderungen (Record-Feld, Service-`ExtractMissingIndexWarnings` + Helper, `ToolRegistry`-Description, drei neue Tests, `architecture-spec.md` §4 Nr. 14, `README.md` Zeile 13) umgesetzt, Commit `86c0e48` + Folge-Commit `4e4f6a2` (Result-Doku) auf `main` mit korrektem Conventional-Commit-Format und Suffix `[sql-index-suggestions]`, `AiNetLinter-baseline.json` automatisch durch `RecreateBaseline` mit-aktualisiert.

### Rules-Konformität

SqlToAiRichtlinien §4 (Doku-Sync: `architecture-spec.md` und `README.md` ohne Aufforderung mit-aktualisiert; Commit-Disziplin: Conventional Commit, Deutsch, imperativ, Suffix `[sql-index-suggestions]`, Subject ≤ 72 Zeichen) eingehalten; §5 (AiNetLinter-Hinweis: keine manuelle Hash-Berechnung) eingehalten. AiNetLinter-Grenzwerte eingehalten: `BuildCreateIndexStatement` (PerformanceMeasurementService.cs:373) liegt mit ~46 LOC deutlich unter 60, CC und Cognitive niedrig, Parameteranzahl 4 exakt am Limit (Planer hat das im Step-Plan so spezifiziert), `sealed class` und `#nullable enable` der Datei unverändert. Die beiden dokumentierten Abweichungen sind beide regelkonform: (1) `List<string>` statt `IReadOnlyList<string>` für die Helper-Parameter ist die direkte Reaktion auf CA1859 bei `TreatWarningsAsErrors=true` — keine Rule-Verletzung, sondern Befolgen der Linter-Vorgabe; (2) `__`-Trenner-Konvention folgt der expliziten Plan-Prose, nicht der als „optional" markierten Code-Skizze — Konzept ist hier nicht strenger (Pfeil-Beispiel ≠ Norm, siehe TD-001), Plan hat bewusst abweichend spezifiziert.

### Logische Korrektheit

`BuildCreateIndexStatement` setzt das DDL korrekt zusammen: `null`-Rückgabe wenn weder EQUALITY- noch INEQUALITY-Spalten vorhanden (Edge-Case abgedeckt — würde sonst leeres Statement liefern), Schlüsselspalten-Reihenfolge Equality→Inequality korrekt (Konzept-Vorgabe), INCLUDE-Klausel nur bei Vorhandensein, ON-Klausel in Bracket-Notation 1:1 übernommen, End-`;` immer gesetzt. Die drei neuen Tests sind inhaltlich aussagekräftig (sie prüfen die tatsächlichen Substrings, nicht nur „nicht null"), die bestehenden Tests bleiben unverändert grün (kein ColumnGroup → `MissingIndexStatement = null`, JSON-Serialisierung via `JsonIgnoreCondition.WhenWritingNull` unterdrückt das Feld, kein Breaking Change). Tests 1 (Equality-only) und 2 (alle drei ColumnGroup-Typen) entsprechen exakt dem Konzept-Beispiel in `konzept.md` Zeile 161–172, Test 3 deckt den nicht im Konzept illustrierten E+I-Fall ab.

### Konzept-Treue (Ebene 4)

Muss-Haven Idee 1 vollständig umgesetzt: `ExtractMissingIndexWarnings` liefert pro Missing-Index-Warning ein fertiges `CREATE NONCLUSTERED INDEX`-Statement als zusätzliches Feld in `PerformancePlanWarning`. Konzept-§Wie-Idee-1-Beispiel (XML → DDL) wird durch Test 2 reproduziert. Non-Goals nicht verletzt: keine Schreiboperation, keine `DTA`-Anbindung, keine `DBCC AUTOPILOT`-Anbindung. EPIC-02-Bestandteile (neues Tool `sql_suggest_indexes`, `VIEW SERVER STATE`, Tool-Count 15→16) korrekt **nicht** mit-umgesetzt — der Coder hat die EPIC-Grenzen eingehalten. Die einzige Konzept-Plan-Doku-Inkonsistenz (Index-Name-Format `IX_Table_Col_Col2` vs. `IX_Table_Col__Col2`) ist keine normative Konzept-Verletzung, sondern eine Doku-Harmonisierungs-Aufgabe — siehe TD-001.

### Build-/Test-Status

```
dotnet build  → grün (0 Warnungen, 0 Fehler)
dotnet test   → grün (505 Tests, 0 Fehler, 0 übersprungen, inkl. AiNetLinterTests.RecreateBaseline)
```

## Tech-Debt-Einträge aus diesem Review

- `TD-001` (siehe `tech-debt.md`) — Konzept-Beispiel Zeile 172 zeigt `IX_Orders_CustomerId_OrderDate` (alle einfachen Unterstriche), Plan-Prose + Implementierung verwenden `IX_Orders_CustomerId__OrderDate` (`__` zwischen Spalten); Doku-Harmonisierung an Konzept oder Plan ausstehend.
- `TD-002` (siehe `tech-debt.md`) — `DESC`-Markierung an `Column`-Elementen in `ColumnGroup` wird in `BuildCreateIndexStatement` ignoriert; bei absteigend indizierten Spalten semantisch unvollständig.
- `TD-003` (siehe `tech-debt.md`) — `IsShowplanPermissionError` (PerformanceMeasurementService.cs:264) ist `SHOWPLAN`-spezifisch; für EPIC-02 (`VIEW SERVER STATE`) ist eine Generalisierung sinnvoll, aber out-of-scope dieses Steps.
