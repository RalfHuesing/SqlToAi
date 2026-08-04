---
status: blocked
type: step-result
task: sql-index-suggestions
step: 003
epic: EPIC-02
step_type: single
coded_by: coder
coded_by_model: MiniMax-M3
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-05T08:25:00+02:00
code_commit_hash: 2ac3668
status_after: blocked
blocker_category: content
---

# Result Step 003: EPIC-02 Integrationstest für `sql_suggest_indexes` gegen echte Test-DB

## Zusammenfassung

`SqlServerFixture` um `IndexSuggestionService`-Property + Konstruktor-Zeile erweitert (Pattern 1:1 zu `SchemaService`). Neue Integrationstest-Datei mit 4 Tests gegen die reale Test-DB `DemoDB` auf `%COMPUTERNAME%\MSSQLSERVER2022` (Agent-Login) angelegt. Test-Infrastruktur kompiliert sauber, Build grün, 11/11 Unit-Tests für `IndexSuggestionService` grün, alle anderen Integration-Tests grün. **Die 4 neuen Integration-Tests scheitern** an einem echten SQL-Server-Fehler in der `step-002`-CTE (Alias `ImprovementScore` in `WHERE`-Klausel referenziert — in T-SQL nicht zulässig). Test-Code selbst ist korrekt und wird grün, sobald die CTE korrigiert ist. Status `blocked`, `blocker_category: content`, weil der `step-002`-Produktionscode außerhalb dieses Step-Scopes liegt.

## Geänderte Dateien

- `tests/SqlToAi.Tests/Integration/SqlServerFixture.cs` — neues Property `IndexSuggestionService` (Zeile 31) und Konstruktor-Zeile (Zeile 63) ergänzt; `using Microsoft.Extensions.Logging.Abstractions;` war bereits vorhanden (Zeile 5).
- `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs` (neu, 84 Zeilen) — 4 Tests: Happy-Path mit Markdown-Header + Restart-Hinweis, Top-Parameter-Smoke, Table-Name-Filter-Smoke, Permission-Graceful-Degradation-Probe.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — automatisch durch `AiNetLinterTests.RecreateBaseline` aktualisiert: neuer Hash für `SqlServerFixture.cs` (war grandfathered, Hash ändert sich) und neuer Eintrag für `IndexSuggestionServiceIntegrationTests.cs` (Datei war neu, kein Baseline-Eintrag vorhanden). Kein manuelles Hash-Rechnen.

## Commit

- **Code-Commit-Hash:** `2ac3668`
- **Message:**
  ```
  test(integration): ergaenzt Integrationstest fuer sql_suggest_indexes [sql-index-suggestions]

  Ref: tasks/sql-index-suggestions/step-003
  Refs: tasks/sql-index-suggestions/step-002/fix-01/step-review.md
  - SqlServerFixture um IndexSuggestionService-Property + Konstruktor-Zeile
    erweitert (Pattern 1:1 zu SchemaService in Zeile 57).
  - Neue Integrationstest-Datei mit 4 Tests gegen die reale Test-DB
    (DemoDB auf %COMPUTERNAME%\MSSQLSERVER2022, Agent-Login):
    Happy-Path, Top-Parameter-Smoke, Table-Name-Filter-Smoke,
    Graceful-Degradation-Probe.
  - AiNetLinter-Baseline automatisch aktualisiert (neuer Hash fuer
    SqlServerFixture.cs, neuer Eintrag fuer die neue Test-Datei).
  - Hinweis: Tests scheitern aktuell an einem SQL-Defekt in der CTE aus
    step-002 (Alias 'ImprovementScore' in WHERE-Klausel referenziert, was
    in T-SQL nicht zulaessig ist) - siehe step-result.md, Blocked-Status
    mit blocker_category: content.
  ```
- **Branch:** `main`
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — Selbstbezug, siehe `git log`).

## Build-/Test-Output

```
dotnet build SqlToAi.slnx   → grün (0 Warnungen, 0 Fehler, TreatWarningsAsErrors=true)
dotnet test  SqlToAi.slnx   → rot (518 vorhandene Tests grün + 11/11 IndexSuggestionService-Unit-Tests grün,
                                4/4 neue IndexSuggestionService-Integration-Tests rot, ~6 s)
```

Auszug aus dem fehlgeschlagenen Test-Output (alle 4 Tests, identische Fehlermeldung):

```
Fehler SqlToAi.Tests.Integration.IndexSuggestionServiceIntegrationTests.SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase
  Fehlermeldung:
   SQL-AI-0102: Query error: Ungültiger Spaltenname "index_group_handle".
Ungültiger Spaltenname "ImprovementScore".
```

## Abweichungen vom Plan

1. **Plan-Code-Skizze verwendet falschen Parameter-Namen `ct:` statt `cancellationToken:`.**
   Die Code-Skizze im `step-plan.md` Zeile 504, 523, 534, 547 ruft `SuggestIndexesAsync(_db, ct: TestContext.Current.CancellationToken)` auf. Der Compiler lehnt das ab, weil der Interface-Parameter `cancellationToken` heißt (nicht `ct` — das war ein Copy-Paste-Fehler aus dem `AccessLevelProvider`-Pattern, wo der Parameter in einem Helper-Context `ct` heißt). Korrektur: alle vier Vorkommen in `IndexSuggestionServiceIntegrationTests.cs` auf `cancellationToken:` umgestellt. Semantisch identisch zum Plan, nur der Named-Argument-Bezeichner ist korrekt.

2. **Status `blocked` statt `done (pending audit)`.**
   Plan-DoD Z. 3 verlangt: „`dotnet test` grün — bestehende Tests bleiben grün, neue Integration-Tests 1–4 grün **oder** (bei nicht verfügbarer Test-DB) die `SqlServerCollectionFixture`-Konstruktion schlägt mit einem klaren Infrastructure-Fehler fehl". Die Test-DB ist erreichbar (verifiziert: `SchemaServiceIntegrationTests.ListDatabasesAsync_ShouldIncludeConfiguredDefault` und `QueryValidationServiceIntegrationTests.ValidateQueryAsync_ShouldSucceed_ForValidQuery` laufen gegen die reale DB ohne Fehler), die Konstruktion der Fixture gelingt, aber die 4 neuen Tests scheitern an einem SQL-Server-Syntaxfehler in der `step-002`-CTE — das ist nicht der im Plan vorgesehene „Infrastructure-Fail"-Pfad und nicht „Test grün", sondern ein vom Plan nicht antizipierter Defekt im `step-002`-Produktionscode. Status daher `blocked` mit `blocker_category: content`.

3. **Code-Commit wurde trotz `blocked`-Status durchgeführt.**
   Der SKILL `coder/SKILL.md` §„Commit-Verhalten bei `blocked`" erlaubt explizit einen Code-Commit als „sinnvoller Zwischenstand". Der Test-Code ist korrekt (er macht genau das, was der Plan verlangt — Anbindung an die reale Test-DB, Smoke-Test der DMV-Query, Graceful-Degradation-Probe); er scheitert nur, weil die `step-002`-CTE einen Bug enthält, den kein Unit-Test (Mock) und keine `fix-01`-Review (kein Real-DB-Lauf) gefunden hat. Der Zwischenstand ist die fertige Test-Infrastruktur, die automatisch grün wird, sobald die CTE korrigiert ist.

## Beobachtungen

- **Echter CTE-Bug in `step-002` (außerhalb dieses Step-Scopes, aber entscheidungsrelevant).**
  Die CTE in `IndexSuggestionService.cs:123-158` (finaler Stand nach `fix-01`) referenziert in der `WHERE`-Klausel den Alias `ImprovementScore`, der im selben `SELECT`-List definiert ist:
  ```sql
  WHERE mid.database_id = DB_ID()
    AND (@TableName IS NULL OR mid.statement LIKE '%' + @TableName + '%')
    AND (@MinScore IS NULL OR ImprovementScore >= @MinScore)  -- ← BUG: Alias in WHERE
  ORDER BY ImprovementScore DESC, mid.statement               -- (hier OK: Alias in ORDER BY erlaubt)
  ```
  In T-SQL sind Column-Aliase aus dem `SELECT`-List **nicht** in der `WHERE`-Klausel desselben `SELECT` referenzierbar (erst in `ORDER BY`). Der echte SQL-Server meldet: „Ungültiger Spaltenname 'ImprovementScore'" — der zweite gemeldete Spaltenname `index_group_handle` ist ein Cascading-Parse-Fehler aus dem fehlgeschlagenen CTE-Kontext. Empfohlene Fix-Strategien für `step-002/fix-02` (vom Planer zu wählen):
  1. Expression in `WHERE` wiederholen: `AND (@MinScore IS NULL OR (migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans)) >= @MinScore)` — minimal-invasiv, keine CTE-Struktur-Änderung.
  2. Geschachtelte CTE: innere CTE `Scored` mit `ImprovementScore`-Berechnung, äußere CTE `TopIndexes` mit `WHERE`-Filter + `TOP (@Top)` — saubererer Endzustand, aber CTE wird eine Ebene tiefer.
  Dies ist genau die Bug-Klasse, die der Integrationstest laut `step-002/fix-01/step-review.md` Absatz „Logische Korrektheit" explizit aufdecken sollte („Der *eigentliche* Beweis ... kommt aus dem Integrationstest in `step-003` (Echtdatenbank)"). Der `fix-01`-Reviewer hat den CTE als korrekt markiert, weil er nur Unit-Tests (mit Mock-DB) laufen ließ — der Mock führt die SQL nicht tatsächlich aus, sondern liefert nur vorher konfigurierte Rows.

- **`dotnet test` läuft alle 4 Integration-Tests vor der Fixture-Konstruktion in 1 ms — verdächtig schnell.**
  Die Tests `SuggestIndexesAsync_ShouldRespectTopParameter_…` und `…_TableNameFilter_…` und `…_PermissionNote_…` brauchen alle nur 1–4 ms, was verdächtig kurz ist. Vermutung: Die Service-Konstruktion in `IndexSuggestionService` ist im DI-Container gecached (Singleton), und der Fehler `SQL-AI-0102` wird beim ersten Aufruf geworfen — die folgenden Aufrufe sehen den gecachten Fehler-Pfad. Tatsächlicher Root-Cause: die `SqlException` wird vom Service korrekt abgefangen **nur** bei `IsViewServerStatePermissionError(ex)` (Error 300/297 + „VIEW SERVER STATE") — generische SQL-Fehler wie `SQL-AI-0102: Query error: ...` landen im `catch (Exception ex)`-Pfad und liefern `IsSuccess = false` mit dem rohen `ex.Message`. Das ist konsistent mit dem Test-Output. Kein zusätzlicher Bug, nur ein Implementierungs-Detail.

- **Linter-Baseline-Mechanismus hat wie erwartet funktioniert.**
  `AiNetLinterTests.RecreateBaseline` hat in einem einzigen `dotnet test`-Lauf zwei Hashes automatisch aktualisiert: neuen Hash für `SqlServerFixture.cs` (war grandfathered, weil der Inhalt sich durch die zwei neuen Zeilen ändert) und einen brandneuen Eintrag für `IndexSuggestionServiceIntegrationTests.cs`. Das bestätigt den in `step-002/fix-01/step-result.md` dokumentierten Mechanismus („Dateien, deren Hash in der Baseline steht, sind grandfathered; Dateien, deren Hash nicht in der Baseline steht, müssen 100% clean sein"). Kein manueller Eingriff war nötig.

- **Test-DB-Verfügbarkeit wie geplant: verfügbar.** SchemaService- und QueryValidationService-Integration-Tests laufen erfolgreich (verifiziert vorab mit `--filter`), die `SqlServerCollectionFixture`-Konstruktion gelingt ohne Infrastructure-Fail. Die `appsettings.json`-Konfiguration (`%COMPUTERNAME%\MSSQLSERVER2022`, `Agent`/`Agent!`, `DemoDB`) ist erreichbar. Der Plan-Hinweis „kein zusätzlicher `Assert.Skip` nötig" wäre nur bei nicht verfügbarer Test-DB relevant gewesen — das war hier nicht der Fall.

## Bekannte Unschärfen

- **CTE-Diagnose beruht auf Cascading-Parse-Fehler-Interpretation.** Der SQL-Server meldet zwei „Ungültiger Spaltenname"-Fehler (`index_group_handle` und `ImprovementScore`). Meine Diagnose ist: nur `ImprovementScore` ist der echte Fehler, `index_group_handle` ist ein Cascading-Report aus dem fehlgeschlagenen CTE-Scope. Möglich, dass der echte Fehler ein anderer ist (z. B. `migs.index_group_handle` würde scheitern, wenn der DMV-Alias nicht greift, aber die `sys.dm_db_missing_index_group_stats.index_group_handle`-Spalte ist im Standard-SQL-Server vorhanden). Wahrscheinlicher ist aber: der CTE-Resolution-Failure führt zu mehreren Folge-Fehlern. Falls der Planer/Kritiker eine andere Ursache vermutet, sollte er die CTE isoliert gegen die reale DB ausführen (z. B. via `QueryValidationService` mit der isolierten SQL — aber das Tool verlangt ein einzelnes Statement, und die CTE ist ein einzelnes Statement, also wäre das der direkte Verifikationsweg).

- **Permissions-Situation in der Test-Instanz ist nicht abschließend geklärt.** Da die Tests bereits in der `try`-Phase des Service an einem SQL-Syntaxfehler scheitern (nicht an `IsViewServerStatePermissionError`), lässt sich aus dem aktuellen Lauf nicht ableiten, ob der `Agent`-Login `VIEW SERVER STATE` hat oder nicht. Erst nach CTE-Fix würde Test 4 (`ShouldReturnPermissionNote_…`) den Graceful-Degradation-Pfad tatsächlich prüfen. Empfehlung an den Planer für `fix-02`: nach dem CTE-Fix einmal manuell die `RenderPermissionNote`-Variante verifizieren (z. B. durch temporäres Entziehen der Permission oder durch Mocks — die `fix-01`-Unit-Tests decken diesen Pfad bereits ab).

## Falls Status `blocked`

**Blocker-Art:** `content`

**Blockiert weil:** Die 4 neuen Integration-Tests scheitern an einem echten SQL-Syntaxfehler in der `step-002`-CTE (`IndexSuggestionService.cs` Zeile 141: `AND (@MinScore IS NULL OR ImprovementScore >= @MinScore)`). T-SQL verbietet die Referenz auf einen `SELECT`-List-Alias in der `WHERE`-Klausel desselben `SELECT`. Der Fehler wird vom echten SQL-Server mit `Ungültiger Spaltenname "ImprovementScore"` (plus Cascading-Fehler `Ungültiger Spaltenname "index_group_handle"`) quittiert. Die CTE-Syntax wurde in `step-002/fix-01` zwar reviewt und als korrekt markiert, der Review basierte aber nur auf Unit-Tests mit Mock-DB — der Mock führt die SQL nicht aus, sondern liefert vorher konfigurierte Rows. Der Integrationstest ist die im `fix-01`-Review explizit als „eigentlicher Beweis" angekündigte Validierung — er liefert nun das gegenteilige Ergebnis.

**Brauche von Nutzer:** Entscheidung, ob ein `step-002/fix-02` angelegt werden soll (mit dem oben in „Beobachtungen" skizzierten Fix), oder ob der CTE-Bug direkt hier in `step-003` mit-gefixt werden darf (Plan verbietet „Änderung an `step-002`-Code" — diese Sperre müsste der Nutzer/Orchestrator aufheben). Empfehlung: `step-002/fix-02` als neuer Fix-Step, weil die CTE-Änderung den Test-Coverage-Scope von `step-002` (Unit-Tests für `MinScore`-Pfad, insbesondere Test 7 `TopFilter_PassedAsFetchNextParameter` und Test 8 `TableNameFilter_PassedAsLikeParameter`) erneut validieren muss.

**Bisher erreicht:**
- ✅ `SqlServerFixture.cs` um `IndexSuggestionService`-Property + Konstruktor-Zeile erweitert (Pattern 1:1 zu `SchemaService`).
- ✅ Neue Integrationstest-Datei `IndexSuggestionServiceIntegrationTests.cs` mit 4 Tests angelegt (84 Zeilen, weit unter `MaxLineCount=500`).
- ✅ Build grün (0 Warnungen, 0 Fehler, `TreatWarningsAsErrors=true`).
- ✅ Linter-Baseline automatisch durch `AiNetLinterTests.RecreateBaseline` aktualisiert.
- ✅ 11/11 `IndexSuggestionService`-Unit-Tests grün.
- ✅ 514/514 andere Tests grün.
- ✅ Test-DB erreichbar (verifiziert mit `SchemaServiceIntegrationTests`/`QueryValidationServiceIntegrationTests`).
- ❌ 4/4 neue Integration-Tests rot wegen CTE-Syntaxfehler in `step-002` (außerhalb Scope).
- ❌ Konzept §DoD letzter Punkt für Idee 2 (Integrationstest gegen echte Test-DB) NICHT erfüllt — die Test-Infrastruktur steht, der Test scheitert aber, also ist der „Beweis" formal nicht erbracht.
- ❌ EPIC-02 nicht abgeschlossen (passt zur Roadmap-Notiz „in Arbeit → step-003" — bleibt in Arbeit, bis CTE korrigiert).
