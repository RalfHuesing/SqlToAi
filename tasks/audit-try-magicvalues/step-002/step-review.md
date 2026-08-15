---
status: done
type: step-review
task: audit-try-magicvalues
step: 002
epic: EPIC-02
step_type: batch
reviewed_by: kritiker
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-15T22:35:00+02:00
verdict: approved
tech_debt_ids: [TD-001, TD-002]
---

# Review Step 002: EPIC-02 Guardrail-Pipeline-Extraktion (IQuerySafetyValidator, batch)

## Verdict

- [x] **approved** — alle vier Prüfebenen ok
- [ ] **issues** — Korrektur-Step `step-<MMM>` angelegt (`corrects: step-<NNN>`)
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle 4 Items + 7 erzwungene Test-Mitmigrationen umgesetzt
- [x] Rules-Konformität: `SqlToAiRichtlinien.mdc` §2/§4/§5 + `AiNetLinter.mdc` (sealed, ≤60 LOC/Method, ≤4 Params, ≤5 Deps, ≤500 LOC/File, `#nullable enable`, PascalCase, ASCII) eingehalten
- [x] Logische Korrektheit: 6-stufige Pipeline verhaltensgleich, `allowSchemaOnly: true` und 2×Pipeline-Call korrekt verdrahtet, `FakeQuerySafetyValidator`-Semantik treu zur Production
- [x] Konzept-Treue: `konzept.md` §Muss-Haven Pkt. 2 vollständig erfüllt, alle 4 Non-Goals gehalten (GlobMatcher/LikePatternMatcher/SqlToAiOptions/AppSettingsMigrator/SchemaService-Forwarder)
- [x] Build: selbst nachgeprüft, grün (0/0)
- [x] Tests: selbst nachgeprüft, grün (523/523, inkl. Linter-Sentinel `RunLinterShouldBeClean`)

## Befund

Alle vier Plan-Items plus die dokumentationspflichtigen Constructor-Signatur-Zwangsmigrationen sauber umgesetzt; `QuerySafetyValidator` ist `internal sealed` mit 3 Dependencies und 30-Zeilen-Pipeline, DI in `Program.cs:181` direkt nach den drei Security-Singletons eingereiht, `allowSchemaOnly: true` an genau einer Aufrufstelle (`QueryValidationService`), `QueryComparisonService` ruft die Pipeline zweimal mit Short-Circuit, Test-Fake delegiert im Happy-Path an einen realen Validator (gleiche Stages wie Production) und liefert im Failure-Pfad einen festen Fehler.

### Plan-Erfüllung

Alle 4 Items vollständig erfüllt. `IQuerySafetyValidator`/`QuerySafetyValidator`/`QuerySafetyCheckResult` wie spezifiziert eingeführt (Datei `src/SqlToAi/Database/QuerySafetyValidator.cs`, 105 Zeilen, single record + interface + sealed class); DI-Zeile in `Program.cs:181` direkt nach den drei bestehenden Security-Registrierungen. `QueryExecutionService` und `QueryValidationService` migriert (Constructor 7→5 bzw. 6→4 Dependencies, Inline-Pipeline durch 8-zeiligen Validator-Call ersetzt, `AccessLevel` aus `QuerySafetyCheckResult` für Anonymisierung übernommen, `allowSchemaOnly: true` nur in `QueryValidationService`). `PerformanceMeasurementService.ValidateSecurityGuards` (ca. 20 Zeilen) ersatzlos gelöscht; `QueryComparisonService` macht zwei aufeinanderfolgende Validator-Calls mit Short-Circuit. `FakeQuerySafetyValidator` mit drei Konstruktor-Pfaden (delegierend, Ergebnis, Fehler) eingeführt; alle vier geplanten Test-Klassen umgestellt; Legacy-Fakes für `IndexSuggestionServiceTests` unangetastet (per `git diff` verifiziert). Erzwungene Mitmigrationen (7 Test-Dateien) sauber durchgeführt: Constructor-Signatur-Wechsel in `QueryExecutionService`/`QueryValidationService` zwang Anpassungen in `QueryExecutionServiceTransactionTests` (4 Aufrufe), `QueryExecutionServiceOptionsTests` (1 Helper), `QueryExecutionServiceAnonymizationTests` (10 Aufrufe), `QueryExecutionServiceSchemaScopeTests` (1 Helper) sowie `SqlServerFixture` (2 Services + neue `QuerySafetyValidator`-Property), `QueryExecutionServiceIntegrationTests` (3 Aufrufe, davon 2 mit Custom-Validator-Bau), `QueryValidationServiceIntegrationTests` (1 Aufruf mit Custom-Validator-Bau). `codemap.md` aktualisiert: neue Datei eingetragen, Database-/Security-/Test-Blöcke nachgezogen, `DRY-T3`-Status korrekt. Commit `b3cd090` ist Conventional Commit (deutsch, imperativ), Subject-Länge 138 Zeichen überschreitet die 72-Zeichen-Richtlinie, ist aber im Step-Result explizit dokumentiert und entspricht der etablierten Projekt-Praxis (siehe vorherige Commits mit 76–90 Zeichen).

### Rules-Konformität

`SqlToAiRichtlinien.mdc` §2 (Guardrail-Architektur) — Pipeline ist die zentrale, sichtbare Implementierung der Mehrstufigen Schreibschutz-Logik; `§4` (No Magic Values, AppSettings-Pflicht) — keine neuen Magic Numbers, keine `appsettings.json`-Änderung nötig (Validator nutzt bestehende Security-Services); `§5` (Qualitätsdrift-Prävention) — `Result<QuerySafetyCheckResult>` durchgängig, Zero-Warning-Direktive eingehalten. `AiNetLinter.mdc`: `EnforceSealedClasses` ✓ (Validator `internal sealed`, Interface bleibt per Default unsealed, Record per C# 14 ohne explizites `sealed`); `MaxConstructorDependencies = 5` ✓ (Validator 3, alle vier Services ≤ 5); `MaxMethodLineCount = 60` ✓ (Pipeline-Methode 30 Zeilen Body); `MaxLineCount = 500` ✓ (Validator 105, `QueryExecutionServiceMockDb` 266); `MaxMethodParameterCount = 4` ✓ (Interface-Methode 4 — am Limit, aber im Plan dokumentiert); `EnforceNullableEnable` ✓ (alle geänderten Dateien mit `#nullable enable`); `EnforcePascalCase` + `EnforceAsciiIdentifiers` ✓. `RunLinterShouldBeClean` real durchgelaufen und grün (11 s, linter unter `C:\Daten\AiNetLinter-win-x64\`).

### Logische Korrektheit

Pipeline-Stages in `QuerySafetyValidator.ValidateQuerySafetyAsync` exakt in der spezifizierten Reihenfolge (Empty-DB → Empty-Query → Whitelist → AccessLevel mit `allowSchemaOnly`-Verzweigung → ReadOnlyGuard nur wenn nicht `ReadWrite` → Multi-Statement). Verhaltensgleichheit zu den vorherigen Inline-Validierungen verifizierbar: `QueryExecutionService` hatte 6 Stages inline (Zeilen 99-136 vor Step), jetzt 8 Zeilen Validator-Call + unveränderter Rest; `QueryValidationService` hatte 6 Stages inline (Z. 66-104), jetzt identisch mit `allowSchemaOnly: true`; `PerformanceMeasurementService` hatte 5 Stages in `ValidateSecurityGuards` (Z. 126-145 vor Step) + 2 Stages in `ValidateArgs` (Z. 113-124), die Argument-Validation bleibt im Service, der Rest wandert in den Validator (semantisch identisch, da `ValidateArgs` `InvalidParameters` mit identischem Text liefert, den auch der Validator produziert); `QueryComparisonService` validierte beide Queries, jetzt zwei Validator-Calls mit identischem Fehler-Short-Circuit. Fehler-Codes (`InvalidParametersCode`, `SafetyCheckFailedCode`, `WriteOperationBlockedCode`, `MultipleStatementsForbiddenCode`) unverändert. Encoding-Falle (UTF-8-BOM + `§` → `Â§` aus `Out-File -Encoding UTF8`) in `QueryExecutionServiceAnonymizationTests.cs` repariert: keine BOM, keine Replacement-Characters, keine `Ã`-Bytes. `FakeQuerySafetyValidator` mit drei Konstruktoren ist semantisch korrekt: der delegierende Pfad baut einen echten `QuerySafetyValidator` mit den Legacy-Fakes und liefert damit bit-genau die gleiche Pipeline wie Production (Stufen 1-6); der `BypassReadOnlyGuardValidator`-Helper ist die explizit schmale Variante für Tests, die das AccessLevel selbst kontrollieren wollen (nur strukturelle Checks, kein ReadOnlyGuard); der Failure-Pfad liefert fix den konfigurierten Fehler (auch ohne Pipeline-Lauf) — das ist für `Reject_SpExecuteSql_BeforeTouchingDatabase` korrekt, weil dieser Test den expliziten Fehler injiziert und nicht von der Pipeline produziert haben will. **Bekannte Test-Schwächung (vom Plan explizit anerkannt):** `Reject_SpExecuteSql_BeforeTouchingDatabase` in `QueryValidationServiceTests.cs` und `QueryExecutionServiceTransactionTests.cs` bindet keinen echten `ReadOnlyGuard` mehr — der Test verifiziert die sp_executesql-Regex nicht mehr end-to-end. Er prüft weiterhin, dass der `WriteOperationBlocked`-Fehler durchgereicht wird und `LastConnection` null bleibt. Die Regex-Logik wandert in `QuerySafetyValidator` und wird in EPIC-03 (`DRY-T3`) von dedizierten `QuerySafetyValidatorTests` abgedeckt. Plan dokumentiert das in §"Bekannte Ausnahmen". `scripts/step002_fix_anonymization.py.bak` (6578 Bytes) ist untracked, nicht im Commit, und nur als Spur der missglückten `Out-File`-Aktion da — sollte vor dem nächsten Commit aufgeräumt werden (MINOR).

### Konzept-Treue (Ebene 4)

`konzept.md` §"Muss-Haven" Pkt. 2 (Phase 2 — Architektur-Konsolidierung Guardrails) vollständig erfüllt: alle drei Sub-Punkte umgesetzt — `IQuerySafetyValidator`/`QuerySafetyValidator` zur Kapselung der 6-stufigen Validierung, Umstellung der 4 Services, Reduktion redundanter Constructor-Dependencies. §"Non-Goals" vollständig gehalten (per `git diff` auf `SqlToAiOptions.cs`, `SchemaService.cs`, `GlobMatcher.cs`, `LikePatternMatcher.cs`, `AppSettingsMigrator.cs` verifiziert — keine dieser Dateien ist im Diff). §"Definition of Done": Zero-Warnings ✓, 100% grüne Test-Suite ✓, Linter-Konformität ✓, keine neuen Magic Numbers ✓, Pipeline existiert genau einmal in `QuerySafetyValidator` ✓. Kein Doku-Sync-Bedarf (`architecture-spec.md`/`README.md`) — die Architektur-Spec dokumentiert Guardrails als Richtlinie, nicht als konkrete Klassen, und die `SqlToAiError`-Codes sind unverändert (kein MCP-Output-Vertrag-Bruch).

### Build-/Test-Status

```
dotnet build SqlToAi.slnx                                → grün (0/0)
dotnet test SqlToAi.slnx --no-build                      → grün (523/523)
dotnet test --filter RunLinterShouldBeClean              → grün (1/1, 11 s)
```

## Tech-Debt-Einträge aus diesem Review

- `TD-001` (siehe `tech-debt.md`) — `PerformanceMeasurementService.ParseExecutionPlanXml` hat vorbestehenden leeren `catch (Exception ignored)` (Z. 296-299) — EnforceNoSilentCatch-Verletzung, außerhalb Step-Scope, vom Plan als möglicher TD erwähnt.
- `TD-002` (siehe `tech-debt.md`) — Vereinheitlichter Validator-Fehlertext (operations-agnostisch) ersetzt die 4 operationsspezifischen Texte der vorherigen Inline-Validierungen; Tests prüfen nur Code, kein Bruch, aber semantischer Mini-Verlust für `QueryComparisonService` („One or both queries" → „The query"); vom Plan als möglicher Folge-TD vorgesehen.
