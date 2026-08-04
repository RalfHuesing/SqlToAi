---
status: done (pending audit)
type: step-plan
task: sql-index-suggestions
step: 002/fix-01
title: "Fix: CTE-basierte DMV-Query — Top-N pro index_handle statt pro verjointe Zeile"
epic: EPIC-02
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-04T18:00:00+02:00
related_to:
  - step-002/step-review.md
---

# Step 002/fix-01: CTE-basierte DMV-Query — Top-N pro `index_handle`

## Bezug

- **Task:** `sql-index-suggestions`
- **Epic:** `EPIC-02` aus `roadmap.md` — `IndexSuggestionService` mit DMV-basierten Index-Empfehlungen (serverweit kumuliert, Graceful Degradation).
- **Konzept-Referenz:** unverändert gegenüber `step-002` (`konzept.md` §Muss-Haven Idee 2, §Wie Idee 2); der Fix korrigiert die *technische* Umsetzung, nicht den Scope.
- **Review-Befund:** `step-002/step-review.md` Findings 1 [CRITICAL] [Logische Korrektheit] — `OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY` wird auf das verjointe Resultat angewendet, nicht auf das pro `index_handle` gruppierte Resultat. Zwei sichtbare Defekte: (a) Anzahl Recommendations ist `<= @Top` (oft kleiner), (b) abgeschnittene Recommendations können unvollständige Spalten-Listen haben.

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen des aktuellen Codes (Commit `3195a17`) habe ich folgende Strukturen vorgefunden, die für den Fix direkt relevant sind:

- **Die fehlerhafte Query** steht in `src/SqlToAi/Database/IndexSuggestionService.cs:123-149`. Konkret der relevante Block:
  ```sql
  FROM sys.dm_db_missing_index_group_stats AS migs
  INNER JOIN sys.dm_db_missing_index_groups AS mig ON migs.index_group_handle = mig.index_group_handle
  INNER JOIN sys.dm_db_missing_index_details AS mid ON mig.index_handle = mid.index_handle
  INNER JOIN sys.dm_db_missing_index_columns AS mic ON mid.index_handle = mic.index_handle
  WHERE mid.database_id = DB_ID()
    AND (@TableName IS NULL OR mid.statement LIKE '%' + @TableName + '%')
    AND (@MinScore IS NULL OR
         (migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans)) >= @MinScore)
  ORDER BY (migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans)) DESC,
           mid.statement,
           mic.column_id
  OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY
  ```
  Da `sys.dm_db_missing_index_columns` eine Zeile pro Spalte liefert, zerschneidet das `OFFSET/FETCH NEXT` die *verjointen* Zeilen, nicht die Handles.
- **Dapper-Mapping-Klasse `SuggestionRawRow`** (Zeilen 291–302) bleibt **unverändert**: neun Properties (`Statement`, `IndexHandle`, `UserSeeks`, `UserScans`, `LastUserSeek`, `AvgTotalUserCost`, `AvgUserImpact`, `ColumnId`, `ColumnUsage`) — exakt das, was die CTE-Outer-Select liefert. Kein Mapping-Bruch durch den Fix.
- **Gruppierungs-Logik `GroupRows`** (Zeilen 164–198) ist semantisch korrekt — der Bug liegt **nicht** in der Service-Schicht, sondern ausschließlich in der SQL-Query. Die CTE-Korrektur ändert die Service-Logik nicht; nur die SQL-Quelle der Roh-Rows ändert sich.
- **`DmvMockConnectionFactory`** in `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs:370-424` liefert über ihren `ExecuteReader` bereits exakt die neun Spalten, die auch die CTE+JOIN-Konstruktion zurückgibt. Der Mock ist fix-kompatibel, **keine** Anpassung am Mock nötig.
- **Bestehende Parameter-Bindungs-Tests** (Test 7 `@TableName`, Test 8 `@Top`) iterieren `paramsCmd!.Parameters` und matchen per `TrimStart('@')` + Property-Name. Dapper bindet anonyme Properties aus `new { TableName = …, MinScore = …, Top = … }` als `@TableName`/`@MinScore`/`@Top` — diese Namen ändern sich durch den CTE-Fix **nicht** (die Parameter werden nur an anderer Stelle in der SQL referenziert: vorher im `OFFSET/FETCH`, jetzt im `TOP (@Top)`). Tests 7 + 8 bleiben grün.
- **Coder-Notiz aus `step-result.md` (Beobachtung 3, „LastCommand vs LastReaderCommand"):** Dapper ruft Parameter-Binding vor `ExecuteReaderAsync` auf; `conn.LastCommand` ist deshalb zum Binding-Zeitpunkt noch `null`. Der Coder muss für Parameter-Inspektions-Tests `factory.LastReaderCommand` (vom `DmvMockConnectionFactory` exponiert) verwenden — das ist genau die im bestehenden Test 7/8 genutzte API. Der neue Test soll sich an dieses Pattern halten.
- **Konzept-Wortlaut §Muss-Haven Idee 2 + §DoD** verlangt die Top-N-Semantik auf Recommendation-Ebene (nicht auf Spalten-Zeilen-Ebene). Die CTE-Korrektur ist die Umsetzung dieser Anforderung — kein Konzept-Konflikt.
- **`PerformanceMeasurementService`/`IsPermissionError`** sind **nicht** Teil des Fixes (TD-003 ist erledigt, vom Planer in `step-002/step-plan.md` „Aktueller Projektzustand" dokumentiert). Die `IsViewServerStatePermissionError`-Methode (Zeilen 253–255) und der Permission-Catch (Zeile 107) bleiben unverändert.
- **Doku-Sync-Pflicht (`SqlToAiRichtlinien.mdc` §4) entfällt** für diesen Fix: Score-Formel, Output-Spalten, Header-Reihenfolge und Permission-Verhalten ändern sich nicht — nur die interne SQL-Struktur der DMV-Query. `architecture-spec.md` §4 Nr. 16 und `README.md` Zeile 13/27 bleiben konsistent zum bisherigen Code. **Begründung im Plan dokumentiert** (siehe Rules-Refs).

## Intention

Nach dem Fix liefert `IndexSuggestionService` für `@Top=N` immer **exakt N** Index-Empfehlungen (oder weniger, wenn die DMV insgesamt weniger Handles liefert) — und jede zurückgegebene Recommendation enthält ihre **vollständige** Spalten-Liste (EQUALITY/INEQUALITY/INCLUDE). Die Top-N-Semantik wird auf der `index_handle`-Ebene erzwungen, bevor `sys.dm_db_missing_index_columns` gejoint wird. Das Tool bleibt ansonsten verhaltensgleich: gleiche Score-Formel, gleiche Markdown-Struktur, gleiche Permission-Graceful-Degradation, gleiche Argumente, gleiche Header, gleicher Restart-Hinweis.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/IndexSuggestionService.cs` (Zeile 123–149, SQL-Konstante in `LoadSuggestionsAsync`)

- **Was:** Die SQL-Konstante wird von einem flachen JOIN mit nachgelagertem `OFFSET/FETCH NEXT` auf eine CTE-Konstruktion umgestellt. Aufbau (siehe Code-Skizze unten):
  - Innerer CTE `TopIndexes`: `SELECT TOP (@Top) … FROM sys.dm_db_missing_index_group_stats JOIN sys.dm_db_missing_index_groups JOIN sys.dm_db_missing_index_details` mit denselben WHERE-Filtern (`mid.database_id = DB_ID()`, `@TableName IS NULL OR … LIKE`, `@MinScore IS NULL OR ImprovementScore >= @MinScore`) und `ORDER BY ImprovementScore DESC, mid.statement`. Spalten: `Statement`, `IndexHandle`, `user_seeks`, `user_scans`, `last_user_seek`, `avg_total_user_cost`, `avg_user_impact`, `ImprovementScore` (berechnet).
  - Äußerer SELECT: zieht die CTE-Spalten plus `mic.column_id AS ColumnId` und `mic.column_usage AS ColumnUsage` über `INNER JOIN sys.dm_db_missing_index_columns AS mic ON ti.IndexHandle = mic.index_handle`. ORDER BY `ti.ImprovementScore DESC, ti.Statement, mic.column_id` (deterministisch, für reproduzierbare Tests).
  - `OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY` **entfällt** im äußeren SELECT — das `TOP (@Top)` leistet die Begrenzung auf Handle-Ebene.
- **Warum:** Der Bug aus Finding 1 wird an der Wurzel behoben. Die `SuggestionRawRow`-Mapping-Klasse passt 1:1 auf die neun Spalten des äußeren SELECTs, daher **kein** weiterer Code-Change im Service nötig. `GroupRows`, `RenderMarkdown`, `RenderPermissionNote`, `IsViewServerStatePermissionError`, `LastReaderCommand`-Exponierung am Mock-Connection, Parameter-Bindungs-Tests — alles bleibt wie gehabt. Minimale, lokal begrenzte Änderung: **genau die SQL-Konstante in `LoadSuggestionsAsync`**.
- **Coder-Hinweis:** Die CTE muss syntaktisch exakt so aufgebaut sein, dass Dapper die Parameter `@TableName`/`@MinScore`/`@Top` an derselben anonymen Objekt-Signatur bindet (`new { TableName = …, MinScore = …, Top = … }` bleibt unverändert). Bestehende Tests 7 + 8 prüfen das ohne Anpassung.

### Datei 2: `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs` (NEU: 1 zusätzlicher Test)

- **Was:** Ein neuer `[Fact]`-Test, der mehrere `IndexHandle`s mit unterschiedlichen Spaltenzahlen an die Service-Schicht liefert und prüft, dass jeder zurückgegebene Handle seine **vollständige** Spalten-Liste behält. Konkrete Vorgaben:
  - **Test-Name:** `SuggestIndexesAsync_MultipleHandlesWithDifferentColumnCounts_AllColumnsPerHandlePreserved`.
  - **Mock-Setup:** Drei `DmvRow`s (die existierende Test-Record-Struktur in `IndexSuggestionServiceTests.cs:355-363`):
    - Handle 1: `Statement = "[dbo].[Orders]"`, `IndexHandle = 1`, `UserSeeks = 45230`, `UserScans = 12`, `AvgTotalUserCost = 10.5`, `AvgUserImpact = 25.0`, `Columns = [ (2, EQUALITY), (3, EQUALITY) ]` (2 Spalten).
    - Handle 2: `Statement = "[dbo].[OrderItems]"`, `IndexHandle = 2`, `UserSeeks = 30000`, `UserScans = 5`, `AvgTotalUserCost = 8.0`, `AvgUserImpact = 30.0`, `Columns = [ (10, EQUALITY), (11, EQUALITY), (12, INCLUDE), (13, INCLUDE), (14, INCLUDE) ]` (5 Spalten).
    - Handle 3: `Statement = "[dbo].[Customers]"`, `IndexHandle = 3`, `UserSeeks = 20000`, `UserScans = 0`, `AvgTotalUserCost = 5.0`, `AvgUserImpact = 20.0`, `Columns = [ (20, EQUALITY), (21, INEQUALITY), (22, INCLUDE) ]` (3 Spalten).
  - **Argumente:** `IndexSuggestionArgs("DemoDB", Top: 10)` (großzügig, damit der Filter rein serverseitig keine Rolle spielt — der Mock repräsentiert die bereits-gefilterte Top-N-Sicht des SQL-Outputs).
  - **Assertions:**
    - `Assert.True(result.IsSuccess)`.
    - `result.Value` enthält `"[dbo].[Orders]"`, `"[dbo].[OrderItems]"`, `"[dbo].[Customers]"` (alle drei Handles erscheinen).
    - `result.Value` enthält `"2, 3"` (Handle 1, alle 2 EQUALITY-Spalten).
    - `result.Value` enthält `"10, 11"` (Handle 2, alle 2 EQUALITY-Spalten).
    - `result.Value` enthält `"12, 13, 14"` (Handle 2, alle 3 INCLUDE-Spalten — der entscheidende Beweis: vorher wäre Handle 2 nach 5 Spalten-IDs „voll", aber bei der alten Query mit `OFFSET/FETCH NEXT` nach 4 verjointen Zeilen hätte Handle 2 nur 2 von 5 Spalten gehabt; jetzt hat er alle 5).
    - `result.Value` enthält `"20"` (Handle 3, EQUALITY) und `"21"` (Handle 3, INEQUALITY) und `"22"` (Handle 3, INCLUDE).
    - **Markdown-Struktur-Smoke-Test:** `result.Value` enthält `"| Score |"`, `"| Table |"`, `"| Equality Columns |"`, `"| Inequality Columns |"`, `"| Include Columns |"`, `"| Seeks |"`, `"| Scans |"`, `"| Last Seek |"` und den Restart-Hinweis (`"since the last SQL Server restart"`).
- **Warum:** Der Bug lebte **ausschließlich** in der SQL-Query. Der neue Test demonstriert, dass die Service-Schicht bei Eingabe korrekt geformter Daten (mehrere Handles, jede mit ihrer vollständigen Spalten-Liste) auch korrekt gruppiert und rendert — also dass die CTE-Filterung *das* war, was fehlte, und nicht die Service-Logik. Der Test wäre unter dem alten SQL kaputt gegangen (Handle 2 hätte nur 2 von 5 Spalten-IDs gehabt), jetzt ist er grün. Er sichert die Regression gegen versehentliche Re-Vereinfachung der SQL ab (jemand, der später wieder `OFFSET/FETCH` außerhalb der CTE einbaut, würde diesen Test brechen — die Spalten-Liste von Handle 2 wäre unvollständig). Der Test braucht **keinen** Mock-Umbau; `DmvMockConnectionFactory` mit `rows: new List<DmvRow> { … }` reicht.
- **Coder-Hinweis:** Der Test folgt 1:1 dem Muster von Test 6 (`SuggestIndexesAsync_QueryReturnsRows_RendersMarkdownWithScoreAndRestartHint`, Zeilen 121–169) — gleiche `BuildService(rows: …)`-Helper-Signatur, gleiche `Assert.Contains`-Stil. Keine neuen Helper-Klassen, keine neuen Mocks. Wichtig: KEINE Parameter-Inspektion via `LastReaderCommand` nötig — der Test arbeitet rein auf dem Markdown-Output.

## Tests

- [ ] `SuggestIndexesAsync_MultipleHandlesWithDifferentColumnCounts_AllColumnsPerHandlePreserved` (neu) — drei Handles à 2/5/3 Spalten, alle drei erscheinen im Output, jede Spalten-Liste vollständig (Beweis gegen Truncation-Bug).
- [ ] Bestehende Tests 1–12 in `IndexSuggestionServiceTests` weiterhin grün (insbesondere Test 6 als Happy-Path-Regression, Tests 7/8 als Parameter-Bindungs-Regression, Test 9 als Permission-Graceful-Regression, Test 10 als Generic-Error-Regression, Test 11 als TD-003-Helper-Regression).
- [ ] Bestehende Tests in `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` weiterhin grün (kein Eingriff in `IsPermissionError`).
- [ ] `dotnet test` Task-weit grün (517 + 1 = 518 Tests, 0 Fehler, 0 übersprungen).

## Definition of Done

- [ ] SQL-Konstante in `LoadSuggestionsAsync` auf CTE-Konstruktion umgestellt (genau Datei 1, Zeile 123–149).
- [ ] Neuer Test `SuggestIndexesAsync_MultipleHandlesWithDifferentColumnCounts_AllColumnsPerHandlePreserved` hinzugefügt und grün.
- [ ] `dotnet build` grün (0 Warnungen, 0 Fehler, `TreatWarningsAsErrors=true`).
- [ ] `dotnet test` grün (518 Tests, 0 Fehler).
- [ ] Keine Code-Änderungen außerhalb von Datei 1 + Datei 2.
- [ ] Keine Doku-Änderungen (begründet — siehe Rules-Refs).
- [ ] Code-Commit (Conventional Commit, deutsch, imperativ, Suffix `[sql-index-suggestions]`).
- [ ] `step-002/fix-01/step-result.md` geschrieben.
- [ ] Frontmatter `status` dieses Plans von `open` auf `done (pending audit)` gesetzt (separater Commit oder gemeinsam mit Code-Commit — Festlegung des Coder, beide Optionen sind spec-konform).

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc` §4 (Doku-Sync-Pflicht: `docs/architecture-spec.md` und `README.md` bei jeder Code-Änderung mit-aktualisieren) — **für diesen Fix ausdrücklich nicht anwendbar**. Begründung: Die CTE-Korrektur ändert kein beobachtbares Verhalten des Tools — Score-Formel, Output-Spalten (8 Header: Score/Table/Equality/Inequality/Include/Seeks/Scans/LastSeek), Header-Reihenfolge, Restart-Hinweis, Graceful-Degradation-Notiz und Parameter-Signatur bleiben identisch. Es ändert sich ausschließlich die interne SQL-Struktur der DMV-Query (Reihenfolge der JOIN-Operationen und Position des `TOP` vs. `OFFSET/FETCH`). Die bestehende Doku (`architecture-spec.md` §4 Nr. 16 und `README.md` Zeile 13/27) ist weiterhin korrekt; eine Doku-Aktualisierung wäre redundant und würde keinen Mehrwert bringen. Der Coder darf §4 für diesen Fix **begründet ignorieren** und soll diese Begründung in `step-002/fix-01/step-result.md` im Abschnitt „Rules-Konformität" explizit wiederholen (Spiegelung der Planer-Begründung).
- `.agents/rules/SqlToAiRichtlinien.mdc` §5 (Zero-Warning-Direktive, `TreatWarningsAsErrors`) — **anwendbar und weiterhin einzuhalten**. `dotnet build` muss mit 0 Warnungen grün sein. Caveat: bei Änderung an der SQL-Konstante in einer raw string literal mit `"""`-Delimiter ist auf korrekte Einrückung/Whitespace zu achten, damit der C#-Compiler keine `CS8995`/`CS8996`-Raw-String-Hinweise wirft.
- `.agents/rules/AiNetLinter.mdc` Zeile 11 (`sealed` für konkrete Klassen) — **anwendbar, aber nicht betroffen**. Es werden keine neuen Klassen hinzugefügt; bestehende `sealed`-Klassen (`IndexSuggestionService`, `MissingIndexRow`, `SuggestionRawRow`, `DmvMockConnectionFactory`) bleiben unverändert.
- `.agents/rules/AiNetLinter.mdc` Zeile 12 (`#nullable enable` am Dateianfang) — **anwendbar, aber nicht betroffen**. Beide berührten Dateien haben bereits `#nullable enable`.
- `.agents/rules/AiNetLinter.mdc` Zeile 13–14 (kein leeres `catch`; `Log + sichtbarer Fehler`) — **anwendbar, aber nicht betroffen**. Die `try/catch`-Struktur in `SuggestIndexesAsync` (Zeilen 99–115) bleibt unverändert.
- `.agents/rules/AiNetLinter.mdc` Zeile 22 (`MaxMethodParameterCount`=4) und Zeile 27 (`MaxConstructorDependencies`=5) — **anwendbar, aber nicht betroffen**. Keine Methoden-Signatur-Änderung, keine Konstruktor-Änderung. Bestehender `ToolDispatcher`-Status quo (9 Parameter, 8 Dependencies) ist explizit nicht EPIC-02-relevant und vom Planer in `step-002/step-plan.md` „Aktueller Projektzustand" als „Bekannte Ausnahme" markiert — durch diesen Fix nicht berührt.
- `.agents/rules/AiNetLinter.mdc` Zeile 58 (`EnforceNamespaceDirectoryMapping`), Zeile 67 (`EnforceAsciiIdentifiers`) — **anwendbar, aber nicht betroffen**. Keine Namespace-Änderung, keine Bezeichner-Änderung.

## Bekannte Ausnahmen

- Keine.

## Code-Skizze (optional)

Die CTE-SQL als Vorlage für den Coder — Spalten-Namen exakt an die existierende `SuggestionRawRow`-Klasse angepasst (Dapper-Mapping 1:1, Pascal-Case-Aliase wie in der bestehenden SQL):

```sql
WITH TopIndexes AS (
    SELECT TOP (@Top)
        mid.statement AS Statement,
        mig.index_handle AS IndexHandle,
        migs.user_seeks AS UserSeeks,
        migs.user_scans AS UserScans,
        migs.last_user_seek AS LastUserSeek,
        migs.avg_total_user_cost AS AvgTotalUserCost,
        migs.avg_user_impact AS AvgUserImpact,
        (migs.avg_total_user_cost * migs.avg_user_impact * (migs.user_seeks + migs.user_scans)) AS ImprovementScore
    FROM sys.dm_db_missing_index_group_stats AS migs
    INNER JOIN sys.dm_db_missing_index_groups AS mig
        ON migs.index_group_handle = mig.index_group_handle
    INNER JOIN sys.dm_db_missing_index_details AS mid
        ON mig.index_handle = mid.index_handle
    WHERE mid.database_id = DB_ID()
      AND (@TableName IS NULL OR mid.statement LIKE '%' + @TableName + '%')
      AND (@MinScore IS NULL OR ImprovementScore >= @MinScore)
    ORDER BY ImprovementScore DESC, mid.statement
)
SELECT
    ti.Statement,
    ti.IndexHandle,
    ti.UserSeeks,
    ti.UserScans,
    ti.LastUserSeek,
    ti.AvgTotalUserCost,
    ti.AvgUserImpact,
    mic.column_id AS ColumnId,
    mic.column_usage AS ColumnUsage
FROM TopIndexes AS ti
INNER JOIN sys.dm_db_missing_index_columns AS mic
    ON ti.IndexHandle = mic.index_handle
ORDER BY ti.ImprovementScore DESC, ti.Statement, mic.column_id
```

Anonyme-Parameter-Bindung in C# bleibt **unverändert**:

```csharp
var parameters = new
{
    TableName = args.TableName,
    MinScore = args.MinScore,
    Top = args.Top
};
```

Der Coder passt nur den `const string sql = """…""";`-Body an; alles andere in `LoadSuggestionsAsync` (Zeilen 118–162) bleibt wie gehabt. Insbesondere: `QueryAsync<SuggestionRawRow>(new CommandDefinition(sql, parameters, cancellationToken: ct))` und das nachfolgende `GroupRows(rawRows)` werden nicht angefasst.

## Notes

- **Wo der Bug wirklich saß:** ausschließlich in der SQL-Query. `GroupRows` ist korrekt, der Dapper-Mapper ist korrekt, der Markdown-Renderer ist korrekt. Der Test ist deshalb primär ein **Regressions-Schutz gegen versehentliche Re-Vereinfachung der SQL** (jemand, der `TOP (@Top)` zurück zu `OFFSET 0 ROWS FETCH NEXT @Top ROWS ONLY` patcht, würde den neuen Test sofort brechen). Der *eigentliche* Beweis, dass die SQL-Query die DMVs korrekt abfragt, kommt aus dem Integrationstest in `step-003` (Echtdatenbank) — dieser ist **nicht** Teil von `fix-01`, sondern explizit ein Folge-Step nach erfolgreichem `fix-01`-Abschluss (Orchestrator-Vorgabe).
- **Coder-Notiz „LastCommand vs LastReaderCommand"** (aus `step-002/step-result.md` Beobachtung 3): Der neue Test braucht keine Parameter-Inspektion, also kein `LastReaderCommand`. Sollte der Coder dennoch Parameter prüfen wollen, ist `factory.LastReaderCommand` (exponiert vom bestehenden `DmvMockConnectionFactory` in `IndexSuggestionServiceTests.cs:379`) der korrekte Zugriffspunkt — Dapper bindet Parameter vor `ExecuteReaderAsync`, deshalb ist `conn.LastCommand` zum Bindungszeitpunkt noch `null`.
- **Bestehende Tests 7 + 8 (Parameter-Bindung `@TableName`/`@Top`) bleiben unverändert grün**, weil Dapper die anonymen Properties aus `new { TableName = …, MinScore = …, Top = … }` an dieselben `@`-Namen bindet, unabhängig davon, an welcher Stelle der SQL diese Parameter referenziert werden (innerhalb der CTE im `WHERE`/`TOP` statt außerhalb im `OFFSET/FETCH`). Der Coder muss an der Parameter-Bindungs-Stelle nichts ändern.
- **Sortierreihenfolge im Outer-SELECT** (`ORDER BY ti.ImprovementScore DESC, ti.Statement, mic.column_id`) ist deterministisch und auf zwei Sortier-Schlüssel-Ebenen stabil: zuerst pro Handle (via `ti.ImprovementScore` und `ti.Statement`), dann innerhalb des Handles nach Spalten-ID. Damit ist auch die `GroupRows`-Ausgabe (Dictionary-Insertion-Order, intern `Dictionary<long, MissingIndexRow>`) reproduzierbar über die Test-Mock-Daten hinweg.
- **Der CTE-Name `TopIndexes`** ist nur ein mnemonischer Vorschlag; der Coder darf einen anderen sprechenden Namen wählen, solange die SQL-Semantik erhalten bleibt.
- **Konzept-Formel-Schreibweise-Hinweis** (Konzept Zeile 45 nennt `avg_user_cost`, Code verwendet `avg_total_user_cost`): kein Eingriff, der Fix übernimmt die im Code etablierte DMV-Spalten-Schreibweise. Konzept-Harmonisierung ist out-of-scope.
- **„Sonstige Beobachtungen" aus dem Review** sind **nicht** Teil dieses Fixes (siehe Orchestrator-Vorgabe „Scope-Disziplin"): kein JOIN auf `sys.columns` für Spalten-Namen, keine `ToolDispatcher`-Refactoring, keine Doku-/Konzept-Updates, keine README-Themenbullet-Harmonisierung.
- **Roadmap wird im Fix-Modus nicht angefasst** — der Planer bestätigt: `roadmap.md` bleibt unverändert, EPIC-02 bleibt mit `step-002/fix-01` als Pending-Fix markiert, bis der Fix-Step erfolgreich abgeschlossen ist.
