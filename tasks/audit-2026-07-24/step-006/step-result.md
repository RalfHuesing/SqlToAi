---
status: done
type: step-result
task: audit-2026-07-24
step: 006
coded_by: coder
coded_by_model: MiniMax-M3
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-07-25T21:10:00+02:00
code_commit_hash: 31d77a9057f0e59e706e39e4f87634e9d728218a
status_after: done
---

# Result Step 006: ExecuteDetailQueryAsync-Helper in SchemaService

## Zusammenfassung

In `src/SqlToAi/Database/SchemaService.cs` wurden sechs strukturell identische
Delegationsmethoden (`GetSchemaForeignKeysAsync`, `GetSchemaIndexesAsync`,
`GetSchemaConstraintsAsync`, `GetTriggerDefinitionAsync`,
`GetObjectReferencesAsync`, `GetRoutineParametersAsync`) auf einen privaten
Helper `ExecuteDetailQueryAsync` reduziert, der Access-Check, Connection-
Aufbau, Try/Catch, Logging und `QueryError`-Übersetzung einmal kapselt.
Jede öffentliche Methode ist jetzt ein Einzeiler, der den passenden
`DetailSchemaRenderer`-Aufruf als Lambda übergibt. Ergänzt um einen
Helper-Test, der sicherstellt, dass bei fehlgeschlagenem Access-Check
kein `CreateConnection`-Aufruf erfolgt. Die externe Semantik aller
sechs Methoden ist unverändert — alle bestehenden Tests bleiben grün.

## Geänderte Dateien

- `src/SqlToAi/Database/SchemaService.cs` — 6 Methoden zu Einzeilern reduziert, neuer privater Helper `ExecuteDetailQueryAsync` (Access-Check + Try/Catch + Logging) hinzugefügt; Nettoreduktion 71 Zeilen
- `tests/SqlToAi.Tests/Database/SchemaServiceTests.cs` — Neuer Test `ExecuteDetailQueryAsync_ShouldPropagateAccessFailure_WithoutOpeningConnection` (nutzt vorhandenen `DummyConnectionFactory.ConnectionCreatedCount`-Counter)
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — SHA-256-Hashes für `SchemaService.cs` und `SchemaServiceTests.cs` automatisch vom AiNetLinter neu berechnet

## Commit

- **Code-Commit-Hash:** `31d77a9057f0e59e706e39e4f87634e9d728218a`
- **Message:**
  ```
  refactor(schema): extrahiere ExecuteDetailQueryAsync-Helper in SchemaService

  Sechs strukturell identische Delegationsmethoden (GetSchemaForeignKeysAsync,
  GetSchemaIndexesAsync, GetSchemaConstraintsAsync, GetTriggerDefinitionAsync,
  GetObjectReferencesAsync, GetRoutineParametersAsync) in SchemaService
  wurden auf einen gemeinsamen privaten Helper reduziert, der Access-Check,
  Connection-Aufbau, Try/Catch, Logging und QueryError-Uebersetzung einmal
  kapselt. Jede oeffentliche Methode ist jetzt ein Einzeiler, der den
  passenden DetailSchemaRenderer-Aufruf als Lambda uebergibt.

  Zusaetzlich ein Helper-Test, der sicherstellt, dass bei fehlgeschlagenem
  Access-Check kein CreateConnection-Aufruf erfolgt.

  - SchemaService.cs: -71 Zeilen
  - SchemaServiceTests.cs: +1 Helper-Test (389/389 gruen)
  - SqlToAi-baseline.json: Hashes automatisch neu berechnet

  Refs: tasks/audit-2026-07-24/step-006
  ```
- **Branch:** `main`
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit für diese Datei +
  `step-plan.md`-Status (siehe Coder-Skill Schritt 7) — dessen Hash steht
  nicht hier drin (Selbstbezug), sondern in `git log`.

## Build-Output

```
dotnet build SqlToAi.slnx
→ Ergebnis: grün — 0 Warnungen, 0 Fehler (Verstrichene Zeit 00:00:04.28)
```

## Test-Output

```
dotnet test --filter "Category!=Integration" --nologo --no-build
→ Ergebnis: grün — Fehler: 0, erfolgreich: 389, übersprungen: 0, gesamt: 389, Dauer: 14 s
→ AiNetLinterTests: Validation Exit Code 0 (grün)
```

AiNetLinter-Report (`tests/SqlToAi.Tests/AiNetLinter/output/SqlToAi-linter-report.md`)
zeigt `Validation Exit Code: 0`. Die 2 verbleibenden Violations
(`MaxBoolParameterCount` in `AccessLevelProviderTests.cs` Z. 217/261) sind
vorbestehend und nicht durch diesen Step verursacht — sie betreffen die
Test-Klassen für `AccessLevelProvider` und sind außerhalb des Scopes.

## Abweichungen vom Plan

- **Log-Message-Wortlaut** (geringfügig): Die Vereinheitlichung im Helper
  nutzt `"Failed to retrieve {Operation} for {ObjectName} in database {DatabaseName}."`
  — die ursprünglichen Methoden hatten teils `"for table {TableName}"`
  (Foreign Keys, Indexes, Constraints) statt nur `"for {ObjectName}"`.
  Da die Methoden-Signaturen unverändert sind und kein Test den
  Log-Wortlaut prüft, ist das ein rein observability-relevanter Drift.
  Der Plan hat diese Variante explizit als bevorzugt markiert
  ("strukturierte Properties sind konsistenter mit dem `LoggerMessage`-Pattern-Stil").
- **Parameter-Record nicht eingeführt:** Der Helper hat 4 funktionale
  Parameter + `cancellationToken` (5 total). Der AiNetLinter hat den Code
  ohne Record akzeptiert (Exit Code 0, keine `MaxMethodParameterCount`-
  Violation in den Test-Output-Berichten). Der Plan hatte eine
  `DetailQueryRequest`-Record-Umstellung als Fallback vorgesehen, falls
  der Linter meckert — das war hier nicht nötig.

## Beobachtungen

- **`GetSchemaAsync` (Z. 176-216) und `SearchObjectsAsync` (Z. 112-174)**
  sind explizit aus dem Scope dieses Steps ausgeklammert. Beide haben
  ein anderes Skelett (kein `DetailSchemaRenderer`-Aufruf, sondern direkt
  `QueryAsync`/`_tableSchemaRenderer`-Aufrufe). Falls in einem Folge-Step
  ein zweiter, weiter gefasster Helper für diese Methoden extrahiert
  werden soll, könnte die Datei weiter schrumpfen — wäre aber ein
  separater Punkt-20-Plus-Refactor.
- **`ValidateTableOrViewAsync` (DetailSchemaRenderer.cs:21-37)** ist
  weiterhin dreifach dupliziert: einmal in `GetSchemaForeignKeysAsync`,
  `GetSchemaIndexesAsync`, `GetSchemaConstraintsAsync` (jeweils via
  `ValidateTableOrViewAsync` aufgerufen) und implizit ähnlich in
  `GetObjectReferencesAsync` (Z. 243-255) und `GetRoutineParametersAsync`
  (Z. 285-297) mit jeweils eigenem `SELECT RTRIM(type) FROM sys.objects`-Block.
  Der Audit-Bericht nennt das als optionalen Punkt — wäre ein eigener
  Renderer-interner Refactor.
- **Test-Count:** 388 → 389 (+1 Helper-Test).

## Bekannte Unschärfen

- **Log-Format-Drift:** Wie oben dokumentiert, hat sich der Wortlaut der
  Fehler-Log-Messages leicht geändert (z. B. "for table X" → "for X").
  Der Auditer sollte explizit prüfen, ob das ein akzeptabler Drift ist
  oder die ursprünglichen Texte beibehalten werden sollen (dann wäre
  String-Interpolation statt strukturierte Properties im Helper nötig).
- **AiNetLinter-Parameter-Zählung:** Ob der Linter `Func<...>` als 1
  oder 4 Parameter zählt, ist aus dem Report nicht 100% ersichtlich
  (es gab keine entsprechende Violation). Falls ein zukünftiges
  Linter-Update die Zählung ändert, müsste der Helper auf den
  `DetailQueryRequest`-Record umgestellt werden.
- **Linter-Violations im Test-Output** (2× `MaxBoolParameterCount` in
  `AccessLevelProviderTests.cs`) sind im Report sichtbar, aber die
  Test-Assertion prüft nur den Exit Code (0), nicht die Anzahl
  Violations. Der Auditer sollte entscheiden, ob das ein zu adressierender
  Befund ist (siehe Punkt-15/Tabelle der Linter-Issues im
  `03-code-qualitaet-architektur.md`).
