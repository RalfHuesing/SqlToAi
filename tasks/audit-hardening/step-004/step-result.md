---
status: done
type: step-result
task: audit-hardening
step: "004"
epic: EPIC-04
step_type: single
coded_by: coder
coded_by_model: Claude Sonnet 5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T21:10:00+02:00
code_commit_hash: 7becaf3
status_after: done
blocker_category: n/a
---

# Result Step 004: QueryValidationService: Command-Timeout statt ConnectTimeoutSeconds verwenden

## Zusammenfassung

`QueryValidationService` liest jetzt `CommandTimeout` für die drei Parse-Only-Commands
(`SET NOEXEC ON`, Query, `SET NOEXEC OFF`) aus `QueryExecutionOptions.CommandTimeoutSeconds`
statt aus `SqlServerOptions.ConnectTimeoutSeconds`. Feld `_dbOptions` (Typ `SqlServerOptions`)
wurde zu `_queryExecutionOptions` (Typ `QueryExecutionOptions`), Konstruktor-Signatur
unverändert. XML-Doku der wiederverwendeten Option erweitert, ein neuer Unit-Test verifiziert
die Options-Quelle über tatsächlich unterschiedliche Werte.

## Geänderte Dateien

- `src/SqlToAi/Database/QueryValidationService.cs` — `_dbOptions`/`SqlServerOptions` →
  `_queryExecutionOptions`/`QueryExecutionOptions`; alle drei `CommandTimeout`-Zuweisungen
  in `ExecuteParseonlyValidationAsync` umgestellt.
- `src/SqlToAi/Configuration/SqlToAiOptions.cs` — XML-Doku von
  `QueryExecutionOptions.CommandTimeoutSeconds` erweitert (erwähnt jetzt auch
  `QueryValidationService`/`sql_validate_query`).
- `tests/SqlToAi.Tests/Database/QueryValidationServiceTests.cs` — neuer Test
  `ValidateQueryAsync_ShouldUseQueryExecutionCommandTimeout_NotConnectTimeout`; dafür
  `ValidationMockConnectionFactory` um `ObservedCommandTimeouts` (Liste, pro
  `ExecuteNonQuery`-Aufruf befüllt) erweitert.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — Hash-Update für die drei
  oben genannten Dateien (automatisch generierter Baseline-Hash, mechanische Folge der
  Codeänderung, kein inhaltlicher Eingriff).

## Commit

- **Code-Commit-Hash:** `7becaf3`
- **Message:**
  ```
  fix(database): QueryValidationService nutzt CommandTimeoutSeconds statt ConnectTimeoutSeconds [audit-hardening]

  QueryValidationService.ExecuteParseonlyValidationAsync bezog den
  CommandTimeout fuer SET NOEXEC ON/Query/SET NOEXEC OFF faelschlich aus
  SqlServerOptions.ConnectTimeoutSeconds (Connection-Timeout) statt aus
  QueryExecutionOptions.CommandTimeoutSeconds (Command-Timeout). Schliesst
  TD-001; keine neue Option, keine appsettings.json-Aenderung noetig.

  Refs: tasks/audit-hardening/step-004
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — Selbstbezug,
  siehe `git log`).

## Build-/Test-Output

```
dotnet build → grün (0 Warnungen, 0 Fehler)
dotnet test  → grün (500 Tests, 0 Fehler)
```

## Abweichungen vom Plan

Keine — Plan 1:1 umgesetzt (Feld-/Konstruktor-Umstellung, drei `CommandTimeout`-Zeilen,
Doku-Erweiterung, neuer Unit-Test wie in „Tests" des Plans beschrieben).

## Beobachtungen

- Der neue Test greift auf `FakeDbCommand.CommandTimeout` zurück, das bereits von der
  gemeinsamen `TestSupport`-Infrastruktur bereitgestellt wird; `ValidationMockConnectionFactory`
  musste dafür minimal erweitert werden (neue Property `ObservedCommandTimeouts`, Handler-Closure
  bekommt jetzt Zugriff auf die `FakeDbCommand`-Instanz statt sie zu ignorieren) — keine neue
  Testinfrastruktur-Datei nötig, blieb lokal in der bestehenden Testdatei.
- Der vom Plan referenzierte Beobachtungs-Hinweis zu `SecondaryConnectionBuilder.cs:54`
  (umgekehrter Timeout-Mismatch, andere Options-Klassen `AnonymizationRulesOptions`/
  `MetadataProviderOptions`) wurde bewusst nicht angefasst — steht bereits explizit als
  Out-of-Scope-Notiz im Step-Plan für den Kritiker, hier nur zur Vollständigkeit bestätigt:
  nicht berührt.

## Bekannte Unschärfen

Keine.
