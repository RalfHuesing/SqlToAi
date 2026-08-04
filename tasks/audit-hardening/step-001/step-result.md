---
status: done
type: step-result
task: audit-hardening
step: "001"
epic: EPIC-01
step_type: single
coded_by: coder
coded_by_model: Claude Sonnet 5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T00:00:00+02:00
code_commit_hash: 32d1aab
status_after: done
blocker_category: n/a
---

# Result Step 001: CommandTimeout-Konfigurierbarkeit & Umbenennung

## Zusammenfassung

`SqlServerOptions.CommandTimeoutSeconds` zu `ConnectTimeoutSeconds` umbenannt (Options-Klasse,
`appsettings.json`, `SqlConnectionFactory.cs`). Neue `QueryExecutionOptions.CommandTimeoutSeconds`
ergänzt (Default 30, `appsettings.json`-Eintrag) und in `QueryExecutionService` an
`command.CommandTimeout` gebunden statt des bisherigen hartkodierten `= 0`. README.md
(Config-Tabelle) synchronisiert. Zwei neue Tests ergänzt.

## Geänderte Dateien

- `src/SqlToAi/Configuration/SqlToAiOptions.cs` — `SqlServerOptions.CommandTimeoutSeconds` →
  `ConnectTimeoutSeconds` umbenannt (mit XML-Doku); `QueryExecutionOptions.CommandTimeoutSeconds`
  (Default 30) neu ergänzt.
- `src/SqlToAi/Database/SqlConnectionFactory.cs` — `ConnectTimeout` liest jetzt
  `_options.SqlServer.ConnectTimeoutSeconds`.
- `src/SqlToAi/Database/QueryExecutionService.cs` — `command.CommandTimeout = 0` →
  `_options.CommandTimeoutSeconds`.
- `src/SqlToAi/Database/QueryValidationService.cs` — **nicht im Plan enthalten**, aber notwendig:
  3 Stellen (`_dbOptions.CommandTimeoutSeconds` → `_dbOptions.ConnectTimeoutSeconds`), siehe
  „Abweichungen vom Plan".
- `src/SqlToAi/appsettings.json` — `SqlServer.CommandTimeoutSeconds` → `ConnectTimeoutSeconds`;
  `QueryExecution.CommandTimeoutSeconds: 30` neu.
- `README.md` — Config-Tabelle: `SqlServer`-Zeile auf `ConnectTimeoutSeconds` korrigiert,
  `QueryExecution`-Zeile um `CommandTimeoutSeconds` ergänzt.
- `tests/SqlToAi.Tests/Database/SqlConnectionFactoryTests.cs` (neu) —
  `CreateConnection_ShouldUseConfiguredConnectTimeout`: prüft, dass
  `options.SqlServer.ConnectTimeoutSeconds` als `Connect Timeout=` in der Connection-String landet.
- `tests/SqlToAi.Tests/Database/QueryExecutionServiceTests.cs` (neu) —
  `ExecuteQueryAsync_ShouldApplyConfiguredCommandTimeout_ToCommand`: prüft, dass
  `QueryExecutionOptions.CommandTimeoutSeconds` tatsächlich auf `FakeDbCommand.CommandTimeout`
  ankommt (Kernkriterium: `= 0` ist weg).
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — automatisch aktualisierte
  Baseline (unverändertes Verhalten, keine manuelle Anpassung).

## Commit

- **Code-Commit-Hash:** `32d1aab`
- **Message:**
  ```
  fix(config): CommandTimeout konfigurierbar machen, SqlServer-Option umbenennen [audit-hardening]

  SqlServerOptions.CommandTimeoutSeconds (tatsaechlich ein ConnectTimeout) zu
  ConnectTimeoutSeconds umbenannt, um die Namenskollision mit der neuen
  QueryExecutionOptions.CommandTimeoutSeconds zu vermeiden. Der bisher
  hartkodierte, unbegrenzte command.CommandTimeout = 0 in QueryExecutionService
  wird jetzt an die neue, appsettings-konfigurierbare Option gebunden.

  Refs: tasks/audit-hardening/step-001
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — Selbstbezug, siehe
  `git log`).

## Build-/Test-Output

```
dotnet build → grün (0 Fehler, 0 Warnungen)
dotnet test  → grün (488 Tests, 0 Fehler)
```

## Abweichungen vom Plan

- **Plan-Widerspruch in `QueryValidationService.cs` (nicht in „Konkrete Änderungen" gelistet):**
  Der Plan behauptet im Abschnitt „Aktueller Projektzustand (JIT-Kontext)":
  „`SqlServerOptions.CommandTimeoutSeconds` … wird ausschließlich in `SqlConnectionFactory.cs:44`
  als `ConnectTimeout` verwendet (per Grep verifiziert) — keine weiteren Referenzen im Code."
  Beim eigenen Lesen (Schritt 2) fand sich das nicht bestätigt: `QueryValidationService.cs`
  (Zeilen 143, 151, 160) liest ebenfalls `SqlServerOptions.CommandTimeoutSeconds` — dort aber als
  echtes `DbCommand.CommandTimeout` für die drei `SET NOEXEC ON/OFF`/Parse-Only-Validierungsbefehle,
  nicht als `ConnectTimeout`. Ohne Anpassung dieser drei Stellen hätte die Umbenennung nicht
  gebaut. Ich habe die drei Referenzen **rein mechanisch** auf den neuen Property-Namen
  `ConnectTimeoutSeconds` umgestellt (identischer Wert, identisches Laufzeitverhalten — reine
  Compile-Reparatur), **ohne** eine inhaltliche Korrektur vorzunehmen (z. B. Umstellung auf
  `QueryExecutionOptions.CommandTimeoutSeconds`, was semantisch näher läge, aber eine
  Scope-Erweiterung über den Plan hinaus gewesen wäre). Sonst identisch zum Plan umgesetzt.

## Beobachtungen

- **Neuer Kandidat für denselben Bezeichnungs-Fehler wie der behobene Audit-Fund:** Nach dieser
  Umbenennung heißt die Property zwar korrekt `ConnectTimeoutSeconds`, wird in
  `QueryValidationService.cs` (3 Stellen, siehe oben) aber weiterhin als **Command**-Timeout für
  Validierungsabfragen verwendet — der Feldname passt dort jetzt nicht mehr zum tatsächlichen
  Verwendungszweck (umgekehrtes Muster zum ursprünglichen Fund: nicht der Name war falsch, sondern
  die Wiederverwendung derselben Connection-Option für einen andersartigen Zweck). Das war schon
  vor diesem Step so (nur unter dem alten, zufällig passenden Namen `CommandTimeoutSeconds`
  verdeckt) und ist strukturell dasselbe Muster wie das in `step-plan.md` unter „Notes" bereits
  benannte, bewusst nicht angefasste `SecondaryConnectionSettings.CommandTimeoutSeconds`. Mögliche
  Tech-Debt-Kandidat: `QueryValidationService` sollte künftig ggf. `QueryExecutionOptions` (oder
  eine eigene Validation-Timeout-Option) statt der Connection-`SqlServerOptions` für seine
  Command-Timeouts verwenden.
- `docs/architecture-spec.md` enthält an einer Stelle (Zeile 95) `"CommandTimeoutSeconds": 30` im
  JSON-Beispiel für `AnonymizationRules` — das ist die davon getrennte, korrekt benannte
  `AnonymizationRulesOptions.CommandTimeoutSeconds` (out of scope laut Plan) und wurde bewusst
  nicht angefasst.

## Bekannte Unschärfen

- Kein neuer Test für `QueryValidationService.cs` ergänzt (Plan nennt diese Datei nicht als
  Testziel) — die drei geänderten Zeilen dort sind reine Compile-Reparatur ohne
  Verhaltensänderung; bestehende `QueryValidationService`-Tests (falls vorhanden) liefen im vollen
  `dotnet test`-Lauf grün mit.
- Der Kritiker sollte insbesondere prüfen, ob die mechanische Reparatur in
  `QueryValidationService.cs` (Abweichung oben) als angemessen minimal akzeptiert wird, oder ob
  ein eigener Folge-Step/Tech-Debt-Eintrag für die dortige Fehlbenennung sinnvoll ist.
