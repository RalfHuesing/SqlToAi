---
status: done
type: step-plan
task: audit-2026-07-24
step: 006
title: "Punkt 20 — ExecuteDetailQueryAsync-Helper in SchemaService extrahieren"
created_by: planer
created_at: 2026-07-25T18:30:00+02:00
coded_by: coder
coded_at: 2026-07-25T21:10:00+02:00
code_commit_hash: 31d77a9057f0e59e706e39e4f87634e9d728218a
reviewed_at: 2026-07-25T21:40:00+02:00
verdict: approved
related_to:
  - tasks/audit-2026-07-24/03-code-qualitaet-architektur.md (DRY-Impact Mittel #4)
  - tasks/audit-2026-07-24/00-summary.md (Punkt 20)
---

# Step 006: Punkt 20 — SchemaService Helper für sechs Delegationsmethoden

## Bezug

- **Task:** `audit-2026-07-24`
- **Quelle:** `03-code-qualitaet-architektur.md` Teil B „Sechs strukturell identische Delegationsmethoden in `SchemaService`" (DRY-Impact Mittel #4)
- **Phase / Priorität:** Phase 4 — Architektur-Aufräumarbeit, Punkt 20

## Intention

`SchemaService` enthält sechs öffentliche Methoden (`GetSchemaForeignKeysAsync`, `GetSchemaIndexesAsync`, `GetSchemaConstraintsAsync`, `GetTriggerDefinitionAsync`, `GetObjectReferencesAsync`, `GetRoutineParametersAsync` in `src/SqlToAi/Database/SchemaService.cs:218-348`), die alle exakt dasselbe Skelett haben:
1. `VerifyDatabaseAccessAsync` aufrufen, bei Failure den Error zurückgeben
2. `try`-Block öffnen
3. `using var connection = _connectionFactory.CreateConnection(databaseName)`
4. `await connection.OpenAsync(cancellationToken)`
5. `DetailSchemaRenderer.XyzAsync(connection, ..., cancellationToken)` aufrufen
6. `catch (Exception ex)` mit nahezu identischem Log-Text (`"Failed to retrieve … for … in database {DatabaseName}."`)
7. `SqlToAiError.QueryError(ex.Message)` zurückgeben

Nur der aufgerufene Renderer-Methode und die Log-Message unterscheiden sich. Geschätzte Reduktion: ~130 Zeilen → ~40 Zeilen, plus eine siebte künftige Detail-Query wird zur Ein-Zeilen-Ergänzung statt einer weiteren 15-Zeilen-Kopie.

Ziel: Einen privaten Helper `ExecuteDetailQueryAsync` extrahieren, der Access-Check/Connection/Try-Catch/Logging einmal kapselt. Die sechs Methoden werden zu Einzeilern, die nur noch den passenden `DetailSchemaRenderer`-Aufruf als Lambda übergeben.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/SchemaService.cs`

- **Was:** Im unteren Drittel der Klasse (nach den sechs Methoden, vor `RenderMarkdownTable`) einen neuen privaten Helper einfügen:
  ```csharp
  /// <summary>
  /// Common skeleton for the six detail-query delegations: verify access, open a
  /// connection, run a single DetailSchemaRenderer call inside a try/catch, log
  /// and translate any exception to SqlToAiError.QueryError.
  /// </summary>
  private async Task<Result<string>> ExecuteDetailQueryAsync(
      string databaseName,
      string objectName,
      string operationName,
      Func<DbConnection, CancellationToken, Task<Result<string>>> query,
      CancellationToken cancellationToken)
  {
      var accessCheck = await VerifyDatabaseAccessAsync(databaseName, cancellationToken);
      if (accessCheck.IsFailure)
      {
          return accessCheck.Error;
      }

      try
      {
          using var connection = _connectionFactory.CreateConnection(databaseName);
          await connection.OpenAsync(cancellationToken);
          return await query(connection, cancellationToken);
      }
      catch (Exception ex)
      {
          _logger.LogError(ex, "Failed to retrieve {Operation} for {ObjectName} in database {DatabaseName}.", operationName, objectName, databaseName);
          return SqlToAiError.QueryError(ex.Message);
      }
  }
  ```
- Dann die sechs Methoden auf jeweils einen Aufruf reduzieren, z. B.:
  ```csharp
  public Task<Result<string>> GetSchemaForeignKeysAsync(string databaseName, string tableName, CancellationToken cancellationToken = default) =>
      ExecuteDetailQueryAsync(databaseName, tableName, "foreign keys",
          (connection, ct) => DetailSchemaRenderer.GetSchemaForeignKeysAsync(connection, tableName, databaseName, ct),
          cancellationToken);
  ```
  (analog für die fünf anderen Methoden).
- **Linter-Konformität:** Der Helper hat 4 Funktionsparameter + `this` = 5. AiNetLinter erlaubt ≤4, aber hier ist `this` als Methoden-Receiver nicht zu den Funktionsparametern gezählt (siehe `AiNetLinter.mdc#MaxMethodParameterCount`). Sicherheitshalber `executeDetailQueryAsync` so umbauen, dass die Funktionsparameter ≤4 bleiben — z. B. `databaseName`/`objectName` zu einem `DetailQueryRequest` Record bündeln, falls Linter meckert. **Pragmatischer:** ersten Wurf ohne Record, im Auditer-Step ggf. anpassen.
- **Warum:** Sechs identische Methoden-Skelette auf einen einzigen Helper reduzieren.

### Datei 2: `tests/SqlToAi.Tests/Database/SchemaServiceTests.cs`

- **Was:** Bestehende Tests für die sechs Detail-Methoden **nicht** ändern — sie testen das beobachtbare Verhalten (Mock-Connection wirft → `SqlToAiError.QueryError`; Mock-Connection liefert Daten → Markdown-String). Die Methoden-Signaturen bleiben identisch. Intern ändert sich nur die Implementierung.
- **Optional:** Einen neuen Test `ExecuteDetailQueryAsync_ShouldPropagateAccessFailure_WithoutOpeningConnection` ergänzen, der prüft, dass bei `AccessLevel.None` kein `CreateConnection`-Aufruf erfolgt (über einen Fake-`IDatabaseConnectionFactory` mit `CreateConnection`-Counter). Erhöht die Test-Tiefe für den Helper.

## Tests

- [ ] Bestehende `SchemaServiceTests` (Foreign Keys, Indexes, Constraints, Trigger, Object References, Routine Parameters) bleiben grün
- [ ] Optional: `ExecuteDetailQueryAsync_ShouldPropagateAccessFailure_WithoutOpeningConnection` (neuer Test, deckt die Access-Check-Reihenfolge im Helper ab)
- [ ] `dotnet build SqlToAi.slnx` 0 Warnungen, 0 Fehler
- [ ] `dotnet test --filter "Category!=Integration"` grün

## Definition of Done

- [ ] Alle „Konkreten Änderungen" umgesetzt
- [ ] Build-Command grün (0 Warnings, 0 Errors)
- [ ] Test-Command grün (Ausnahmen siehe „Bekannte Ausnahmen")
- [ ] Commit auf aktuellem Branch (`refactor(schema): extrahiere ExecuteDetailQueryAsync-Helper in SchemaService`)
- [ ] `step-006/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „Kurze, flache Methoden (≤60 Zeilen); ab 5 Parametern ein Input-`record`" (AiNetLinter.mdc) — der Helper hat 4 Funktionsparameter + `cancellationToken`; falls Linter meckert, in `DetailQueryRequest(string DatabaseName, string ObjectName, string OperationName)` Record bündeln
- `.agents/rules/AiNetLinter.mdc#agent-resilience/EnforceNoSilentCatch` — der `catch (Exception ex)` im Helper hat Log + sichtbare Fehlerübersetzung (`SqlToAiError.QueryError`), kein leerer Catch

## Bekannte Ausnahmen

- `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` — vorbestehend, **nicht** Teil dieses Tasks. `src/SqlToAi/Database/SchemaService.cs` ändert sich substantiell (sechs Methoden werden zu Einzeilern, Helper kommt hinzu) — Hash in `SqlToAi-baseline.json` muss neu berechnet und aktualisiert werden. Wenn `DetailQueryRequest` als neuer Record hinzukommt, ggf. zusätzlicher Eintrag nötig.
- `QueryExecutionServiceIntegrationTests.ExecuteQueryAsync_ShouldRespectDatabaseExclusions_AgainstRealTable` — vorbestehende Integrations-Ausnahme, unverändert.

## Notes

- **Optionale Erweiterung (nicht im Scope):** Der Audit-Bericht nennt zusätzlich `ValidateObjectTypeAsync(connection, name, allowedTypes, errorFactory, ct)` als weitere Konsolidierungsmöglichkeit in `DetailSchemaRenderer.cs:21-37` (`ValidateTableOrViewAsync`) und Zeile 244-255, 286-297 (duplizierte `SELECT RTRIM(type) FROM sys.objects …`-Abfragen). Diese sind innerhalb des **Renderers**, nicht des `SchemaService`, und gehören daher konzeptionell in einen Folge-Refactor, nicht in diesen Step. Falls der Auditer das anders sieht, kann er einen Folge-Step anlegen.
- **`GetSchemaAsync` und `SearchObjectsAsync` nicht im Scope:** Diese zwei Methoden in `SchemaService.cs:97-216` haben ein anderes Skelett (kein `DetailSchemaRenderer`-Aufruf, sondern `QueryAsync` direkt). Sie bleiben unverändert.
- **Logging-Format:** Das bestehende Log-Format `"Failed to retrieve {X} for {Y} in database {Z}."` wird im Helper durch strukturierte Properties (`{Operation}`, `{ObjectName}`, `{DatabaseName}`) ersetzt — das ist konsistenter mit dem `LoggerMessage`-Pattern-Stil, den das Projekt teilweise nutzt. Falls die exakte Log-Message aus Drift-Gründen beibehalten werden soll, im Helper manuell formatieren (String-Interpolation statt strukturierte Properties).
- **Linter-Parameterzahl:** Der Helper hat 4 Funktionsparameter (`databaseName`, `objectName`, `operationName`, `query`, `cancellationToken`). AiNetLinter-Limit ist 4, `cancellationToken` ist dabei umstritten (manche Linter zählen CT nicht mit). Sicherheitshalber im Auditer-Step prüfen und ggf. auf Record umstellen.
- **Reihenfolge im Commit:** Erst Helper hinzufügen, dann die sechs Methoden umstellen — in **einem** Commit, damit `dotnet build` nie in einem Hybrid-Zustand ist. Alternativ: zwei Commits (Helper hinzufügen + Methoden umstellen), aber das wäre Overkill für einen rein internen Refactor.
- **Bonus-Möglichkeit:** Falls die `GetSchemaAsync`-Methode (Zeile 176-216) in einem Folge-Step auch in einen `ExecuteDetailQueryAsync`-ähnlichen Helper überführt werden soll, könnte das die Datei von ~491 Zeilen auf ~350 senken — aber das wäre Punkt-20-Scope-Erweiterung, daher zurückhaltend.
