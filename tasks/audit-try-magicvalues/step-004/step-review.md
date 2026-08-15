---
status: done
type: step-review
task: audit-try-magicvalues
step: 004
corrects: step-003
epic: EPIC-03
step_type: single
verdict: approved
tech_debt_ids:
  - TD-003
reviewed_by: kritiker
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-15T23:25:00+02:00
---

# Review Step 004: EPIC-03 Korrektur — 2-Query-Service-Tests in `QueryComparisonServiceTests`

**Korrektur von** `step-003` (Finding 1 [MAJOR] — `QueryComparisonServiceTests` war 44-Zeilen-Skelett ohne Testmethoden). TD-003 wird durch diesen Review auf erledigt gesetzt (SKILL.md §"Umgekehrter Fall": `auto_fixable: nein`-Eintrag, aber Korrektur-Step löst ihn inhaltlich; `tech-debt.md` wird unten aktualisiert).

## Verdict

- [x] **approved** — alle vier Prüfebenen ok
- [ ] **issues** — Korrektur-Step `step-<MMM>` angelegt (`corrects: step-<NNN>`)
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `<rules_dir>/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Konzept-Treue: passt die Umsetzung zu `konzept.md` (Scope, Non-Goals, Muss-Haven)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (oder Baselines beachtet)

## Befund

### Plan-Erfüllung

Alle neun im `step-plan.md` §"Tests" gelisteten Methoden vorhanden (1 × `[Theory]` mit 2 InlineData für leere DB-Namen, 8 × `[Fact]` für EmptyQueryA/EmptyQueryB, DatabaseNotAllowed, AccessLevelNone, MutatingInQueryA/B, MultipleStatementsInQueryA/B); Doc-Kommentar am Klassenkopf Zeilen 13-21 ersetzt (verweist nicht mehr fakten-falsch auf nicht-existente `QueryComparisonServiceIntegrationTests`); `BuildService`-Helper Zeilen 26-45 byte-genau unverändert gegen `step-003`-Stand (`git diff 267cbfb HEAD` zeigt null Diffs am Helper). DoD-Abweichung `532 → 533` ist reine xUnit-Zähl-Konvention (1 Theory × 2 InlineData = 2 Cases), im `step-result.md` offen dokumentiert; Methoden-Anzahl 9/9 stimmt.

### Rules-Konformität

`#nullable enable` Zeile 1, `public sealed class` (Test-Override entbindet, Konvention hält), alle 9 Methoden PascalCase und ASCII-only, 12-15 Zeilen pro Testmethode (Test-Override `MaxMethodLineCount=100` klar unterschritten), Datei 159 Zeilen (`MaxLineCount=500`), 1 bool-Parameter in `BuildService` (`MaxBoolParameterCount=1` eingehalten), keine `catch`-Blöcke, keine nested types, `TestContext.Current.CancellationToken` durchgängig, `TestConstants.DatabaseName` statt Magic Literal. Konventioneller Commit `a0c8c60` auf Deutsch (`test(audit): ergaenze 9 2-Query-Pipeline-Tests …`).

### Logische Korrektheit

Tests 1-3 pinnen `ValidateArgs` (DB → QueryA||QueryB-Reihenfolge, kombinierter Oder-Check liefert gleichen Code für leere QueryA oder QueryB); Tests 4-5 pinnen Pipeline Stages 3-4 (Whitelist-Reject, AccessLevel-Reject); Tests 6-9 sind die 2-Query-spezifische Kernpruefung: asymmetrische Anordnung (Mutating/Multi-Statement in Q_A vs. Q_B) beweist implizit die QueryA-first/QueryB-second-Short-Circuit-Reihenfolge, weil Test 6/8 nur greifen wenn QueryA validiert wird und Test 7/9 nur greifen wenn QueryA sauber ist und QueryB der Verursacher. Real-Pipeline-Pfad (Tests 1-9) nutzt `FakeQuerySafetyValidator` mit `FakeSecurityGuard`/`FakeAccessLevelProvider` aus `TestSupport/LegacySecurityFakes.cs` (step-003 DRY-T1) plus echter `ReadOnlyGuard` für regex-basierte Mutating-Detection — keine erneute Fabrikation. Failure-Pin-Pfad wird im aktuellen Step nicht gebraucht (alle 9 Tests nutzen real-Pipeline), aber `BuildService` haelt die Pin-Variante als Reserve. Happy-Path bewusst ausgeschlossen mit nachvollziehbarer Mock-Infrastruktur-Begruendung (~80-120 Zeilen `QueryComparisonMockConnectionFactory` ausserhalb "low risk"-Scope).

### Konzept-Treue (Ebene 4)

`konzept.md` Muss-Haven Pkt. 3 (Phase 3, Test-Suite-Bereinigung) erfuellt: `QueryComparisonServiceTests` ist nicht mehr Skelett, Service-Identitaet (2-Query-Behavior, Short-Circuit, Mutating/Multi-Statement in Q_A vs. Q_B) ist auf Unit-Ebene abgedeckt. Non-Goals nicht verletzt: keine Aenderung an `SchemaService`-Forwardern, `SqlToAiOptions`-Defaults, `McpHost`/`McpJsonContext`, `GlobMatcher`/`LikePatternMatcher`, `AppSettingsMigrator`. Scope-Disziplin: nur eine Datei (`QueryComparisonServiceTests.cs`) geaendert, `roadmap.md` unangetastet (Fix-Modus-Regel §6.2.1). Der `BuildService`-Helper wurde 1:1 wiederverwendet, was die DRY-Philosophie (kein neuer Helper) und die step-003 DRY-T1/T3-Konsolidierung respektiert.

### Build-/Test-Status

```
dotnet build SqlToAi.slnx                   → grün (0 Warnungen, 0 Fehler)
dotnet test  SqlToAi.slnx --no-build        → grün (533 Tests, 0 Fehler, 0 skip, 13 s)
dotnet test  SqlToAi.slnx --no-build --filter "FullyQualifiedName~QueryComparisonServiceTests"
                                              → grün (10 Cases aus 9 Methoden)
dotnet test  SqlToAi.slnx --no-build --filter "FullyQualifiedName~AiNetLinter"
                                              → grün (1/1)
```

## Tech-Debt-Einträge aus diesem Review

- `TD-003` (siehe `tech-debt.md`) — **erledigt in step-004**: 9 Service-Level-Tests in `QueryComparisonServiceTests.cs` ergänzt (Pre-Pipeline Args, Pipeline Stages 3-4, 2-Query-spezifische Mutating/Multi-Statement-Verzweigungen in QueryA vs. QueryB); der fakten-falsche Doc-Kommentar am Klassenkopf wurde durch eine korrekte Service-Identitäts-Beschreibung ersetzt. Happy-Path-Execution (Schema/Count/EXCEPT-Diff) bleibt bewusst ungetestet auf Unit-Ebene (Mock-Infrastruktur außerhalb dieses Step-Scopes).
