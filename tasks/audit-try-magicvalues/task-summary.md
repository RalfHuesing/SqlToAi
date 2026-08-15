---
task: audit-try-magicvalues
completed_at: 2026-08-15T23:30:00+02:00
final_status: done
total_iterations: 4
total_commits: 19
total_epics: 3
total_tech_debt_entries: 2  # TD-001 + TD-002 offen; TD-003 erledigt
---

# Task Summary: audit-try-magicvalues

## Ergebnis

Die drei in `konzept.md` definierten Muss-Haven-Punkte sind vollständig addressiert: (1) **Konstanten-Zentralisierung** mit 7 neuen Konstanten-Klassen (`SqlServerErrorCode`, `BenchmarkVerdict`, `AnonymizationMode`, `SecurityConstants`, `SqlServerObjectType`) plus benannten FNV-1a/Gewichtungs-Konstanten und Konsolidierung von `DdlUnavailableNote`/`OptionalStringParam`/`BuildObjectDetailTool`; (2) **Guardrail-Pipeline** durch `IQuerySafetyValidator` / `QuerySafetyValidator` (Single Source of Truth für die 6-stufige Validierung) mit Migration der 4 Services (`QueryExecutionService`, `QueryValidationService`, `PerformanceMeasurementService`, `QueryComparisonService`) und Constructor-Reduktion von 6-7 auf 3-5 Dependencies; (3) **Test-Suite-Konsolidierung** mit `QuerySafetyValidatorTests` als zentraler Test-Quelle, `ShowPlanTestHelper` + `ColumnSpec` für 7 von 8 ShowPlan-XML-Blöcken, `LegacySecurityFakes` + `McpTrailTestHelper`-Erweiterung in `TestSupport/`, plus 9 nachgereichte 2-Query-Service-Tests via Korrektur-Step. Build und Test-Suite sind grün (533/533, 0/0), AiNetLinter (`RunLinterShouldBeClean`) ist real durchgelaufen und grün. Der `audit-dry-magicvalues.md`-Befundkatalog ist vollständig im Scope adressiert; die Konzept-Non-Goals sind nicht verletzt.

## Roadmap-Status

Alle 3 Epics abgehakt:

- [x] **EPIC-01** Konstanten-Zentralisierung (Phase 1, Quick Wins) — `step-001`, verdict `approved`
- [x] **EPIC-02** Guardrail-Pipeline (Phase 2, DRY-1) — `step-002`, verdict `approved`
- [x] **EPIC-03** Test-Suite-Konsolidierung (Phase 3, DRY-T1..T3) — `step-003` verdict `issues` → `step-004` (Korrektur) verdict `approved`

Keine offenen oder obsoleten Epics; die Roadmap ist final konsistent mit `konzept.md` §"Muss-Haven" (1+1+1) und §"Non-Goals" (alle 4 gehalten).

## Steps-Übersicht

| Step | Epic | Status | Title | Commit(s) | Notiz |
|------|------|--------|-------|-----------|-------|
| step-001 | EPIC-01 | done | Konstanten-Zentralisierung & Boilerplate-Cleanup (10-Item batch) | `0f6f99a` (code) + `be4a0f0` (doku) | approved; 3 JIT-Zusatzfunde (`QueryTokenResolver.cs:77`, `DetailSchemaRenderer.cs:251`, `ToolDispatcherTestFakes.cs:185`); Coder hat zusätzlich die 5 vom Plan empfohlenen SQL-Fehlercodes `20/40/53/10060/10061` mit aufgenommen (positiv über Plan hinaus) |
| step-002 | EPIC-02 | done | Guardrail-Pipeline-Extraktion (IQuerySafetyValidator, 4-Item batch) | `b3cd090` (code) + `139f775` (doku) | approved; 7 erzwungene Test-Mitmigrationen sauber durchgeführt; `FakeQuerySafetyValidator` mit 3 Konstruktor-Pfaden (Happy-Path delegiert an realen Validator mit Legacy-Fakes); TD-001 + TD-002 angelegt |
| step-003 | EPIC-03 | done (Korrektur) | Test-Suite-Konsolidierung (DRY-T1/T2/T3/MV-T1, 4-Item batch) | `267cbfb` (code) + `bb1fc47` (doku) | verdict `issues` (1 [MAJOR] Finding: `QueryComparisonServiceTests` war 44-Zeilen-Skelett); TD-003 angelegt; Test-Anzahl 523 = 523 (reine Umverteilung); Plan-Inkonsistenz 9/31 vs. 13/25 sauber aufgelöst |
| step-004 | EPIC-03 (Korrektur) | done | 9 2-Query-Service-Tests in `QueryComparisonServiceTests` ergänzen | `a0c8c60` (code) + `a3c0b26` (doku) + `e001314` (doku-korrektur) | approved; TD-003 inhaltlich behoben; Test-Anzahl 523 → 533 (+10 Cases aus 9 Methoden) |

Alle 4 Steps `approved` durch den Kritiker; einzige Korrektur-Kette: `step-003` → `step-004` (1 Korrektur, weit unter `max_fix_rounds_per_step: 3`).

## Globale Audit-Befunde (Kritiker, Modus `global`)

### Konzept erfüllt?

**Ja, vollständig.** Alle drei Muss-Haven-Punkte aus `konzept.md` sind addressiert und durch die jeweiligen Step-Reviews verifiziert:

- **Pkt. 1 (Konstanten-Zentralisierung, EPIC-01):** `SqlServerErrorCode.cs` (10 SQL-Fehlercodes inkl. der 5 vom Plan empfohlenen Erweiterungen), `BenchmarkVerdict.cs` (4 Verdicts), `AnonymizationMode.cs` (`Hash`/`Scramble`), `SecurityConstants.cs` (`DefaultRegexTimeout` = 200 ms), `SqlServerObjectType.cs` (`UserTable`/`View`) — plus benannte `FnvOffsetBasis32`/`FnvPrime32` in `Anonymizer`, benannte Gewichtungs-Konstanten in `AnonymizationRuleProvider`, konsolidierte `DdlUnavailableNote` (1 Stelle statt 2), entfernter `OptionalStringParam`-Scheinduplikat, neuer `BuildObjectDetailTool`-Helper für 5 Tool-Builder. Verhaltensneutral, 0 Test-Bruch, 523 Tests grün nach `step-001`.

- **Pkt. 2 (Guardrail-Pipeline, EPIC-02):** `IQuerySafetyValidator` + `QuerySafetyValidator` + `QuerySafetyCheckResult` (`public sealed record` mit `AccessLevel`/`IsWriteAllowed`) in `src/SqlToAi/Database/QuerySafetyValidator.cs` (105 Z., internal sealed, 3 Dependencies, Pipeline 30 Z.); 4 Services migriert mit Constructor-Reduktion 6-7 → 3-5 Dependencies; `QueryExecutionService` greift `AccessLevel` aus `QuerySafetyCheckResult` für Anonymisierung; `QueryValidationService` reicht `allowSchemaOnly: true` durch (historische Asymmetrie explizit dokumentiert); `QueryComparisonService` ruft die Pipeline zweimal mit Short-Circuit auf; DI in `Program.cs:181` direkt nach den drei Security-Singletons. Die 6-stufige Validierung existiert exakt einmal.

- **Pkt. 3 (Test-Suite-Konsolidierung, EPIC-03):** `QuerySafetyValidatorTests.cs` (276 Z., 13 Methoden, 25 Pipeline-Cases via `[Theory]`/`[InlineData]`) als Single-Source-of-Truth; `ShowPlanTestHelper.cs` (61 Z.) + `ColumnSpec.cs` (12 Z.) ersetzen 7 von 8 ShowPlan-XML-Blöcken (8. Test bewusst eigenständig wegen `<RelOp>`/`<Warnings>`-Strukturen außerhalb `<MissingIndex>`); `McpTrailTestHelper.cs` erweitert um `CreateIsolatedLogRoot` + `GetDayDir`; `LegacySecurityFakes.cs` (43 Z.) als zentrale Heimat für die 3 Security-Fakes; `JsonRpcError.MethodNotFound`-Konstante statt `-32601`-Literal in `McpModelsTests`; `QueryComparisonServiceTests` mit 9 Service-Level-Tests für 2-Query-Verhalten (Pre-Pipeline-Args, Pipeline Stages 3-4, asymmetrische Mutating/Multi-Statement in QueryA vs. QueryB).

**Nicht-Muss-Haven-Items, die im Scope mit-erledigt wurden** (positiv über Konzept hinaus): `QueryTokenResolver.cs:77` (4. ReDoS-Timeout-Stelle), `DetailSchemaRenderer.cs:251` (2. `"U"`/`"V"`-Literal), `ToolDispatcherTestFakes.cs:185` (2. `"Recommended"`-Literal) — alle drei im JIT-Kontext von `step-001` aufgedeckt und im selben Schritt konsolidiert.

**Konzept-Non-Goals gehalten** (per `git diff` in den jeweiligen Reviews verifiziert): keine Zusammenlegung `GlobMatcher`/`LikePatternMatcher`, keine `SqlToAiOptions`-Änderungen, keine `AppSettingsMigrator`-Änderungen, keine `SchemaService`-Forwarder-Änderungen.

### Seiteneffekte / Regressionen

**Keine.** Build und Tests sind im globalen Re-Run grün:

```
$ dotnet build SqlToAi.slnx
  SqlToAi -> ...\SqlToAi.dll
  SqlToAi.Tests -> ...\SqlToAi.Tests.dll
  Der Buildvorgang wurde erfolgreich ausgeführt.
      0 Warnung(en)
      0 Fehler
  Verstrichene Zeit 00:00:06.33

$ dotnet test SqlToAi.slnx --no-build
  Bestanden!   : Fehler: 0, erfolgreich: 533, übersprungen: 0, gesamt: 533, Dauer: 15 s
```

Test-Anzahl-Übergang: 523 (start) → 523 (nach step-003, reine Umverteilung) → 533 (nach step-004, +9 Methoden / +10 Cases).

Verifizierte Nicht-Regressionen:

- **`IndexSuggestionService` unangetastet** — eigene Mini-Validierungskette ohne `IReadOnlyGuard` und ohne Multi-Statement-Prüfung (nicht in `IQuerySafetyValidator`-Migration enthalten). `IndexSuggestionServiceTests.cs:12` importiert die umgesiedelten Fakes via `using SqlToAi.Tests.TestSupport;` ohne weitere Änderung.
- **`SqlToAiOptions` und `McpJsonContext` nicht angefasst** — keine API-/Output-Vertragsänderung am MCP-Output.
- **DI-Reihenfolge** in `Program.cs:178-181` stabil: `ISecurityGuard` → `IAccessLevelProvider` → `IReadOnlyGuard` → `IQuerySafetyValidator`; alle als Singleton (Pipeline ist zustandslos).
- **Encoding-Falle** aus `step-002` (`§`-Bytes → `Â§` durch `Out-File -Encoding UTF8`) in `QueryExecutionServiceAnonymizationTests.cs` repariert; keine BOM, keine Replacement-Characters; `step-003` und `step-004` sind der Falle von vornherein ausgewichen (alle Edits über `edit`/`write`-Tool).

### Rules-Konformität (Stichproben)

**Stichprobe 1: `step-001` (EPIC-01) gegen `SqlToAiRichtlinien.mdc` §4 + `AiNetLinter.mdc`.**
- §4 *No Magic Values*: vollständig erfüllt — alle in MV-1..MV-7 genannten Stellen nutzen benannte Konstanten (verifiziert per `grep` auf die rohen Literale, kein Treffer in den im Plan adressierten Dateien).
- `EnforceSealedClasses`: alle 5 neuen Konstanten-Klassen sind `internal static class` (statisch implizit `sealed` — Linter zufrieden).
- `EnforceNullableEnable`: alle 5 neuen Dateien mit `#nullable enable` am Dateianfang.
- `MaxMethodLineCount = 60` (Produktion) / `100` (Test-Override): `BuildObjectDetailTool` ~14 Z., alle anderen Helper weit unter Limit.
- `EnforceNoSilentCatch`: keine neuen leeren `catch`-Blöcke (TD-001 betrifft vorbestehenden Code aus `step-002`).
- **Verdict: konform.**

**Stichprobe 2: `step-002` (EPIC-02) gegen `SqlToAiRichtlinien.mdc` §2 + §5 + `AiNetLinter.mdc`.**
- §2 *Guardrail-Architektur*: Pipeline ist die zentrale, sichtbare Implementierung der 6-stufigen Validierung; `QuerySafetyValidator` mit 30 Z. Body (`MaxMethodLineCount=60`).
- §5 *Zero-Warning-Direktive*: `TreatWarningsAsErrors=true` aktiv; Build 0/0.
- `MaxConstructorDependencies = 5`: Validator 3 Dependencies, alle 4 migrierten Services ≤ 5.
- `MaxMethodParameterCount = 4`: Interface-Methode hat 4 Parameter (`databaseName`, `query`, `allowSchemaOnly`, `cancellationToken`) — am Limit, im Plan explizit dokumentiert.
- `EnforceAsciiIdentifiers`: alle neuen Identifier ASCII-only.
- **Verdict: konform.**

**Stichprobe 3: `step-003` (EPIC-03) gegen `AiNetLinter.mdc` + `EnforceSealedClasses`-Override in `*.Tests`.**
- `DuplicateCode`: alle drei Befunde (DRY-T1, DRY-T2, DRY-T3) aufgelöst; `GetDayDir()`-Duplikat → `McpTrailTestHelper`; 8 ShowPlan-XML-Blöcke → 7× Builder + 1× bewusste Ausnahme; 31 Pipeline-Cases → 25 in `QuerySafetyValidatorTests` + 6 Service-Asserts mit `FakeQuerySafetyValidator(error)`.
- `MaxBoolParameterCount=1`: `McpTrailTestHelper.CreateWriter` brauchte Refactor auf `McpTrailTestWriterConfig(bool, bool)`-Record; Call-Sites der McpTrail-Tests bauen das Config-Objekt im privaten `CreateWriter`-Helper (1 bool pro Aufruf).
- `BanPublicNestedTypes`: `ColumnSpec` + `McpTrailTestWriterConfig` als Datei-Level-Records (nicht nested).
- `EnforceNoSilentCatch`: keine neuen `catch`-Blöcke in den Test-Dateien.
- **Verdict: konform** (mit der dokumentierten Abweichung `9/31 vs. 13/25`, die der Coder sauber begründet hat).

**Querschnitt:** AiNetLinter-Sentinel `RunLinterShouldBeClean` ist in **allen 4 Steps real durchgelaufen** (Linter unter `C:\Daten\AiNetLinter-win-x64\` installiert) und grün — keine Mock-Skips.

## Tech-Debt-Zusammenfassung

Pointer zu `tasks/audit-try-magicvalues/tech-debt.md` (Volltext dort). Aggregation:

- **Hoch:** 0 Einträge
- **Mittel:** 0 offene Einträge (1 ehemals mittel: TD-003 in `step-004` erledigt)
- **Niedrig:** 2 Einträge — `TD-001` (leerer `catch (Exception ignored)` in `PerformanceMeasurementService.ParseExecutionPlanXml:296-299`, vorbestehend, EnforceNoSilentCatch-Verletzung), `TD-002` (vereinheitlichter `WriteOperationBlocked`-Text im `QuerySafetyValidator:97-98` ersetzt 4 operationsspezifische Texte; semantischer Mini-Verlust nur für `QueryComparisonService`)

**Hinweis (keine Empfehlung, die selbst entscheidet):** beide offenen Einträge sind `auto_fixable: nein` (Architektur-Ermessen nötig). TD-001 betrifft eine vorbestehende Verletzung, die im Audit/Konzept nicht als Befund gelistet war; TD-002 wurde im Plan von `step-002` explizit als bewusste Abwägung dokumentiert. Falls TD-002 zurückgedreht werden soll (operationsspezifische Texte), wäre ein eigener Step mit Designentscheidung "optionaler `operation`-Parameter" oder "strukturiertes Feld im `SqlToAiError`" sinnvoll.

## Offene Punkte

- [x] **TD-003 erledigt** (in `step-004`, 9 Service-Level-Tests ergänzt, fakten-falscher Doc-Kommentar ersetzt)
- [ ] **TD-001 offen** (niedrig, `auto_fixable: nein`) — leerer `catch (Exception ignored)` in `PerformanceMeasurementService.cs:296-299`, vorbestehende EnforceNoSilentCatch-Verletzung
- [ ] **TD-002 offen** (niedrig, `auto_fixable: nein`) — vereinheitlichter Validator-Text ersetzt 4 operationsspezifische Texte
- [ ] **Happy-Path-Execution von `QueryComparisonService.CompareQueriesAsync`** (Schema/Count/EXCEPT-Diff) bleibt bewusst ungetestet auf Unit-Ebene — Mock-Infrastruktur (`QueryComparisonMockConnectionFactory`, ~80-120 Z.) außerhalb des "low risk"-Scopes von `step-004`; falls gewünscht, eigener Folge-Step

Keine ungetesteten Produktions-Pfade. Keine offenen Migrations-Punkte. Keine ausstehenden Build/Test-Regressionen.

## Empfehlungen

- **TD-001 in einem Folge-Task angehen, falls `PerformanceMeasurementService.ParseExecutionPlanXml` ohnehin refactored wird** — vorab klären, ob der "stillschweigende" Verlust des Plan-XMLs im Integration-Test-Pfad jemals eintritt; falls nein, `throw;` oder strukturiertes `ILogger.Warn`-Logging.
- **TD-002 nur dann angehen, wenn Downstream-Tools die Fehlertexte parsen** — sonst ist der semantische Mini-Verlust (4 operationsspezifische Texte → 1 operations-agnostischer Text) im aktuellen Stand akzeptabel; die strukturierten Felder (`Code`, `Database`, `AccessLevel`) reichen für Mensch + Maschine.
- **Happy-Path-Test für `QueryComparisonService`** als eigenständigen Schritt einplanen, falls die 2-Query-Execution-Pfade in CI laufen sollen — Aufwand ~80-120 Z. Mock + ~30 Z. Test.
- **Encoding-Falle** in Lessons-Learned aufnehmen: für Dateien mit Nicht-ASCII-Bytes (`§`, `€`, Umlaute) ausschließlich `edit`/`write`-Tool verwenden, niemals `Out-File -Encoding UTF8` in PowerShell-Pipelines (BOM + Replacement-Characters). Tritt in `QueryExecutionServiceAnonymizationTests.cs` auf.
- **Commit-Subject-Länge** im Projekt: 72-Zeichen-Empfehlung wird konsistent überschritten (step-001: 77, step-002: 138, step-003: 119, step-004: ~90). Entweder Linie schärfen (Conventional-Commit-Wrapper mit Auto-Truncate auf 72 Z.) oder Linie lockern (in `roadmap.md` von 72 auf z. B. 100 anheben). Aktueller Stand: stillschweigend gelockert.
- **Vor Push auf `origin`**: lokale Smoke-Tests gegen `NB-RALF261022\MSSQLSERVER2022` / `OLDemoReweAbfD910` als finale Verifikation — bisher nur lokale `dotnet test` ohne DB-Connect geprüft. Konvention: `dotnet test --filter "Category=Integration"` gegen Test-DB.

## Statistik

- **Anzahl Epics:** 3, davon abgehakt: **3** (EPIC-01, EPIC-02, EPIC-03)
- **Anzahl Steps:** **4** (step-001, step-002, step-003, step-004)
- **Davon approved:** **4** (alle)
- **Davon blocked:** 0
- **Davon Korrektur-Steps:** 1 (`step-004` korrigiert `step-003`, 1 Korrektur in der längsten `corrects`-Kette — weit unter `max_fix_rounds_per_step: 3`)
- **Anzahl Commits:** 19 (siehe unten)
- **Anzahl Tech-Debt-Einträge:** 2 offen (TD-001, TD-002; niedrig) + 1 erledigt (TD-003) — davon `auto_fixable: ja`: 0
- **Test-Anzahl:** 523 (start) → 533 (final) — netto +10 Cases (8 `[Fact]` + 1 `[Theory]` mit 2 InlineData = 9 Methoden / 10 Cases) durch `step-004`
- **Build-Status:** 0 Warnungen, 0 Fehler (im Re-Run verifiziert, `TreatWarningsAsErrors=true` aktiv)
- **Linter-Status:** AiNetLinter grün (Sentinel `RunLinterShouldBeClean` in allen 4 Steps real durchgelaufen)
- **Laufzeit:** ca. 1h 52min (2026-08-15T21:38:00 → 2026-08-15T23:30:00; 4 Steps inkl. 1 Korrektur, Reviews + Doku-Commits)

**Commits (19, in Reihenfolge):**

| SHA | Typ | Subject (gekürzt) |
|---|---|---|
| `0f586ed` | docs | Roadmap + CodeMap + Tech-Debt-Log für audit-try-magicvalues ableiten |
| `5c4f7bf` | docs | plane step-001 (EPIC-01 Konstanten-Zentralisierung, 10-Item batch) |
| `0f6f99a` | refactor | zentralisiere MV-1..7 Konstanten und entferne Boilerplate-Duplikate |
| `be4a0f0` | docs | step-001 (EPIC-01 Konstanten) abschließen |
| `8457b9c` | chore | step-001 Review dokumentieren (Verdict: approved) |
| `63e42e5` | docs | plane step-002 (EPIC-02 Guardrail-Pipeline, 4-Item batch) |
| `b3cd090` | feat | führe IQuerySafetyValidator-Pipeline als Single Source of Truth ein und migriere 4 Guardrail-Services |
| `139f775` | docs | step-002 (EPIC-02 Guardrail-Pipeline) abschließen |
| `6f595f4` | chore | step-002 Review dokumentieren (Verdict: approved) |
| `e2b743b` | docs | plane step-003 (EPIC-03 Test-Suite-Konsolidierung, 4-Item batch) |
| `267cbfb` | refactor | konsolidiere Test-Suite (Validator-Tests, ShowPlan-Helper, LegacyFakes, McpTrail-Helper) |
| `bb1fc47` | docs | step-003 (EPIC-03 Test-Suite-Konsolidierung) abschließen |
| `5cb1de3` | chore | step-003 Review dokumentieren (Verdict: issues) |
| `f429f7a` | docs | plane step-004 (Korrektur step-003: 2-Query-Pipeline-Tests für QueryComparisonService) |
| `a0c8c60` | test | ergänze 9 2-Query-Pipeline-Tests für QueryComparisonService |
| `a3c0b26` | docs | step-004 (2-Query-Service-Tests für QueryComparisonService) abschließen |
| `e001314` | docs | step-result.md Doku-Hash von `<pending>` auf `a3c0b26` setzen |
| `86acb3d` | chore | step-004 Review dokumentieren (Verdict: approved, korrigiert step-003) |
| `f4b3589` | docs | Roadmap EPIC-03-Verweis auf step-003 + step-004 erweitern |
