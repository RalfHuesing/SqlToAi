---
status: open
type: step-plan
task: sql-index-suggestions
step: 006
title: "TD-004 — SQL-Server-2019/2022-kompatible Syntax in IndexSuggestionService.LoadSuggestionsAsync"
epic: EPIC-04
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: claude-sonnet-5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-05T15:30:00+02:00
related_to:
  - tasks/sql-index-suggestions/tech-debt.md#TD-004
  - tasks/sql-index-suggestions/step-003/step-result.md
  - tasks/sql-index-suggestions/step-003/step-review.md
  - tasks/sql-index-suggestions/roadmap.md#EPIC-04
---

# Step 006: TD-004 — SQL-Server-2019/2022-kompatible Syntax in `IndexSuggestionService.LoadSuggestionsAsync`

## Bezug

- **Task:** `sql-index-suggestions`
- **Epic:** `EPIC-04` aus `roadmap.md` — Post-Completion Tech-Debt Cleanup
  Round 2 (Nutzer-Anordnung 2026-08-05). Konkret: TD-004 in
  `tech-debt.md` (Status: „in Bearbeitung (step-006)").
- **Tech-Debt-Referenz:** `tech-debt.md` Eintrag TD-004 — der
  Reopen-CTE-Fix aus `step-003` (Commit `0348e9d`) hat zwei
  SQL-Server-2025-spezifische Schema-Änderungen in die DMV-Query
  eingebaut: (1) `sys.dm_db_missing_index_group_stats.index_group_handle`
  heißt ab SQL Server 2025 `group_handle`; (2)
  `sys.dm_db_missing_index_columns` ist ab SQL Server 2025 eine
  Table-Valued Function (Parameter `index_handle`), aufgerufen via
  `CROSS APPLY`, statt einer View mit `index_handle`-Spalte (aufrufbar
  via `INNER JOIN`). Beide Änderungen brechen die Abwärtskompatibilität
  zu SQL Server 2019/2022.
- **Nutzer-Vorgabe (2026-08-05, EPIC-04-Direktive):** Die minimal
  unterstützte SQL-Server-Version für dieses Projekt ist **2019**. Die
  CTE soll auf die stabile 2019/2022-Syntax zurückgeführt werden:
  `migs.index_group_handle` statt `migs.group_handle`,
  `INNER JOIN sys.dm_db_missing_index_columns ... ON mic.index_handle`
  statt `CROSS APPLY sys.dm_db_missing_index_columns(...)`.
  **Akzeptierte Annahme:** SQL Server 2025 behält die alten Spalten-
  /Objektnamen als Aliase bei (Microsoft pflegt bei DMV-Umbenennungen
  traditionell Rückwärtskompatibilitäts-Aliase), sodass die
  2019/2022-Syntax weiterhin unverändert gegen die aktuelle Test-DB
  (SQL Server 2025 RTM 17.0.1000.7, siehe `step-003`/TD-004-Volltext)
  funktioniert. Diese Annahme wird durch den bestehenden
  Integrationstest-Lauf (`step-006`-DoD) empirisch verifiziert — schlägt
  er fehl, ist das ein Blocker, siehe „Risiken" unten.
- **Vorgänger-Kontext:**
  - `step-003` hat die aktuelle (2025-spezifische) CTE-Form eingeführt,
    um einen CTE-Alias-Bug zu beheben — die 2025-Spezifika waren dabei
    ein Nebenprodukt, kein bewusstes Scope-Ziel (siehe TD-004-Volltext
    „Warum nicht sofort gefixt").
  - `step-005` (TD-002, unmittelbar vorheriger Step) betraf eine
    andere Datei (`PerformanceMeasurementService.cs`) und ist
    inhaltlich unabhängig von diesem Step.

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen von `src/SqlToAi/Database/IndexSuggestionService.cs`
(341 LOC gesamt) vorgefunden — bestätigt den Tech-Debt-Befund 1:1,
mit exakten Zeilenangaben für den Coder:

- **`LoadSuggestionsAsync` (Zeile 118–199):** private static Methode,
  82 LOC (überschreitet formal `MaxMethodLineCount 60`, ist aber bereits
  seit `step-003` so — der Linter zählt vermutlich den mehrzeiligen
  `const string sql = """ ... """`-Rohstring anders, oder die Methode
  fällt unter die Compound-Suppression `CC ≤ 3 AND Cognitive ≤ 5 → 150`
  (die Methode hat nur einen linearen Ablauf: SQL bauen, Parameter
  bauen, `QueryAsync`, `GroupRows` aufrufen — CC/Cognitive vermutlich
  ≤ 3/≤ 5). **Kein neues Risiko durch diesen Step:** die SQL-Text-
  Änderung ändert die LOC-Zahl kaum (±1–2 Zeilen), keine neue
  Kontrollstruktur. Falls der Linter dennoch anschlägt, war das schon
  vor diesem Step der Fall (Baseline würde es zeigen) — kein
  step-006-Scope-Punkt.
- **Kommentarblock Zeile 123–139:** dokumentiert explizit die
  SQL-Server-2025-Kompatibilitätsentscheidung aus `step-003`
  („SQL Server 2025 compatibility notes … verified against the test
  instance in step-003 … `index_group_handle` was renamed to
  `group_handle` … `sys.dm_db_missing_index_columns` is now a
  table-valued function … invoke it via CROSS APPLY"). Dieser
  Kommentarblock muss durch die neue 2019/2022-Erklärung ersetzt
  werden — sonst widerspricht der Kommentar dem tatsächlichen Code
  nach der Änderung (Doku-Code-Drift).
- **`Scored`-CTE, Zeile 141–157:**
  ```sql
  FROM sys.dm_db_missing_index_group_stats AS migs
  INNER JOIN sys.dm_db_missing_index_groups AS mig
      ON migs.group_handle = mig.index_group_handle
  ```
  Zeile 153 (`migs.group_handle`) ist der 2025-spezifische Spaltenname.
  **Änderung:** `migs.group_handle` → `migs.index_group_handle`
  (die 2019/2022-Spalte). `mig.index_group_handle` (rechte Seite des
  Joins, aus `sys.dm_db_missing_index_groups`) bleibt unverändert — laut
  TD-004-Befund war nur `sys.dm_db_missing_index_group_stats` von der
  2025-Umbenennung betroffen, die anderen DMVs behalten
  `index_group_handle`.
- **Finales `SELECT`, Zeile 173–186:**
  ```sql
  FROM TopIndexes AS ti
  CROSS APPLY sys.dm_db_missing_index_columns(ti.IndexHandle) AS mic
  ORDER BY ti.ImprovementScore DESC, ti.Statement, mic.column_id
  ```
  Zeile 184 ist die 2025-TVF-Syntax. **Änderung:**
  `CROSS APPLY sys.dm_db_missing_index_columns(ti.IndexHandle) AS mic`
  → `INNER JOIN sys.dm_db_missing_index_columns AS mic ON
  mic.index_handle = ti.IndexHandle` (2019/2022: View mit
  `index_handle`-Spalte, klassischer Join statt TVF-Aufruf). Die
  `SELECT`-Liste (`mic.column_id AS ColumnId, mic.column_usage AS
  ColumnUsage`, Zeile 181–182) bleibt unverändert — beide DMV-Formen
  (View und TVF) exponieren dieselben Spaltennamen `column_id` /
  `column_usage`.
- **Rest der Methode unverändert:** `parameters`-Objekt (Zeile
  188–193), `connection.QueryAsync<SuggestionRawRow>` (Zeile 195–196),
  `GroupRows`-Aufruf (Zeile 198) — reine SQL-Text-Änderung, keine
  API-, Parameter- oder Rückgabetyp-Änderung.
- **`GroupRows` (Zeile 201–235), `RenderMarkdown` (237–269),
  `RenderPermissionNote` (271–280), `IsViewServerStatePermissionError`
  (290–292):** unberührt — verarbeiten nur das Ergebnis der Query,
  kennen die konkrete SQL-Syntax nicht.
- **`SuggestionRawRow`/`MissingIndexRow`-DTOs (Zeile 294–339):**
  unverändert — Dapper mappt weiterhin per Spaltennamen (`Statement`,
  `IndexHandle`, `UserSeeks`, …, `ColumnId`, `ColumnUsage`), die durch
  `AS`-Aliase im SQL bereits stabil sind und durch die Syntaxänderung
  nicht betroffen sind.

**Tests — bestätigter IST-Zustand:**

- `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs`
  (12 Unit-Tests, alle gegen `DmvMockConnectionFactory`, einer
  privaten `IDatabaseConnectionFactory`-Fake-Implementierung
  am Dateiende, Zeile 416–470): **Kein Test validiert aktuell den
  SQL-Text selbst.** Die Fakes liefern vorkonfigurierte Zeilen aus
  einem `FakeDbDataReader`, unabhängig vom `CommandText` — echtes
  T-SQL-Parsing findet nie statt (das ist der bereits dokumentierte,
  bewusst nicht behobene systemische Gap TD-007, „grundsätzlich nicht"
  laut EPIC-04-Klassifizierung, also **kein Scope-Punkt** hier). Aber:
  `FakeDbCommand.CommandText` (siehe
  `tests/SqlToAi.Tests/TestSupport/FakeDbCommand.cs` Zeile 34–35,
  `[AllowNull] public override string CommandText { get; set; }`) wird
  von der Service-Implementierung befüllt und ist über
  `DmvMockConnectionFactory.LastReaderCommand.CommandText` in Tests
  auslesbar (bereits genutzt in Test 7/8 für Parameter-Assertions,
  Zeile 221–275). **Das ist der Haken, mit dem sich die 2019/2022-Syntax
  ohne echten SQL Server testen lässt** — ein neuer Unit-Test kann den
  `CommandText` string-basiert auf die erwarteten Fragmente prüfen.
- `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs`
  (4 Tests, laufen gegen die echte Test-DB, `[Trait("Category",
  "Integration")]`): führen die Query tatsächlich aus — das ist der
  Beweis, dass die neue 2019/2022-Syntax auch gegen die aktuelle
  SQL-Server-2025-Test-Instanz funktioniert (Kern der akzeptierten
  Annahme aus der Nutzer-Vorgabe). Test 1
  (`SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase`,
  Zeile 26–42) ist aktuell laut `step-005/step-review.md` bekannt rot
  (TD-006, wird erst in `step-007` behoben) — **das ist ein
  Bestandszustand, keine durch diesen Step verursachte Regression**;
  siehe „Risiken" unten für die Abgrenzung.

## Intention

Nach diesem Step baut `IndexSuggestionService.LoadSuggestionsAsync` die
DMV-Query mit der SQL-Server-2019/2022-kompatiblen Syntax
(`migs.index_group_handle`, `INNER JOIN … ON mic.index_handle`) statt
der 2025-spezifischen Syntax (`migs.group_handle`, `CROSS APPLY` als
TVF). Der Kommentarblock im Code dokumentiert die neue Mindestversion
(SQL Server 2019) und die Annahme, dass SQL Server 2025 die alten
Namen als Aliase weiterführt. Ein neuer Unit-Test verifiziert per
`CommandText`-Inspektion, dass der generierte SQL-Text die
2019/2022-Fragmente enthält und die 2025-spezifischen Fragmente nicht
mehr. Die vier bestehenden Integrationstests laufen unverändert gegen
die reale Test-DB und beweisen empirisch, dass die neue Syntax
weiterhin gegen SQL Server 2025 funktioniert (Annahme-Verifikation).
Alle 12 bestehenden Unit-Tests bleiben grün — sie sind SQL-Text-
agnostisch (Mock-Zeilen kommen unabhängig vom `CommandText`).

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/IndexSuggestionService.cs`

**a) SQL-Text, `Scored`-CTE (Zeile 151–153):**

Vorher:
```sql
FROM sys.dm_db_missing_index_group_stats AS migs
INNER JOIN sys.dm_db_missing_index_groups AS mig
    ON migs.group_handle = mig.index_group_handle
```

Nachher:
```sql
FROM sys.dm_db_missing_index_group_stats AS migs
INNER JOIN sys.dm_db_missing_index_groups AS mig
    ON migs.index_group_handle = mig.index_group_handle
```

Nur `migs.group_handle` → `migs.index_group_handle`. Alles andere in
der `Scored`-CTE unverändert (`mig.index_handle = mid.index_handle`,
`WHERE mid.database_id = DB_ID()`).

**b) SQL-Text, finales `SELECT` (Zeile 183–185):**

Vorher:
```sql
FROM TopIndexes AS ti
CROSS APPLY sys.dm_db_missing_index_columns(ti.IndexHandle) AS mic
ORDER BY ti.ImprovementScore DESC, ti.Statement, mic.column_id
```

Nachher:
```sql
FROM TopIndexes AS ti
INNER JOIN sys.dm_db_missing_index_columns AS mic
    ON mic.index_handle = ti.IndexHandle
ORDER BY ti.ImprovementScore DESC, ti.Statement, mic.column_id
```

Die `SELECT`-Liste oberhalb (Zeile 173–182,
`mic.column_id AS ColumnId, mic.column_usage AS ColumnUsage`) bleibt
unverändert.

**c) Kommentarblock (Zeile 123–139) ersetzen:**

Der bestehende Block „SQL Server 2025 compatibility notes … verified
against the test instance in step-003 …" beschreibt nach der Änderung
nicht mehr den tatsächlichen Code (Doku-Code-Drift) und muss ersetzt
werden durch einen Block, der:
- die neue Mindestversion **SQL Server 2019** benennt,
- kurz erklärt, warum `index_group_handle`/`INNER JOIN` statt
  `group_handle`/`CROSS APPLY` verwendet wird (2019/2022-Schema),
- die Annahme dokumentiert, dass SQL Server 2025 die alten Namen als
  Alias weiterführt (empirisch durch die Integrationstests bestätigt,
  siehe `step-006/step-result.md` nach Testlauf),
- auf `tech-debt.md`/TD-004 als Herkunft der Entscheidung verweist
  (analog zum bisherigen „see step-003 for the original bug
  analysis"-Verweisstil im bestehenden Kommentar über der `WITH`-CTE,
  Zeile 123–128 — dieser obere Teil des Kommentars, der die
  Nested-CTE-Architektur erklärt, bleibt unverändert, nur der untere
  „SQL Server 2025 compatibility notes"-Teil wird ersetzt).

Vorschlag für den Ersatztext (Coder darf umformulieren, Inhalt
maßgeblich):
```
// Minimum supported SQL Server version: 2019 (per project policy,
// EPIC-04/TD-004). The DMV query therefore uses the stable pre-2025
// schema:
//   * `sys.dm_db_missing_index_group_stats.index_group_handle` — this
//     is the column name on SQL Server 2019/2022. SQL Server 2025
//     renamed it to `group_handle`, but empirically still accepts
//     `index_group_handle` (verified against the SQL Server 2025 test
//     instance, see step-006 integration test results) — presumably
//     via a backward-compatibility alias.
//   * `sys.dm_db_missing_index_columns` is a view here (not a
//     table-valued function), joined via INNER JOIN on `index_handle`.
//     SQL Server 2025 turned it into a TVF, but again accepts the
//     classic view-style INNER JOIN in the same backward-compatible
//     way.
```

**d) Kein sonstiger Code-Change:** `parameters`-Objekt, `QueryAsync`,
`GroupRows`, DTOs, `RenderMarkdown`, `RenderPermissionNote`,
`IsViewServerStatePermissionError` — alle unverändert. Keine neue
Methode, keine neue Klasse, keine Signaturänderung.

### Datei 2: `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs`

**Neuer Test, der die generierte SQL-Syntax verifiziert** (Kern-DoD
dieses Steps — „neue Tests, die die 2019/2022-Syntax validieren").
Nutzt exakt das bereits etablierte `DmvMockConnectionFactory
.LastReaderCommand`-Pattern (siehe Test 7/8, Zeile 221–275) — kein
neuer Test-Helper nötig.

```csharp
[Fact]
public async Task SuggestIndexesAsync_GeneratedSql_UsesSqlServer2019CompatibleSyntax()
{
    var factory = new DmvMockConnectionFactory([], throwOnExecuteReader: null);
    var service = BuildService(factory: factory);

    await service.SuggestIndexesAsync(new IndexSuggestionArgs("DemoDB"), TestContext.Current.CancellationToken);

    var cmd = factory.LastReaderCommand;
    Assert.NotNull(cmd);
    string sql = cmd!.CommandText;

    // 2019/2022-compatible column name (SQL Server 2025 renamed this to
    // "group_handle" on sys.dm_db_missing_index_group_stats only).
    Assert.Contains("migs.index_group_handle", sql, StringComparison.Ordinal);

    // 2019/2022-compatible join (sys.dm_db_missing_index_columns is a
    // view pre-2025, joined on index_handle — not a table-valued
    // function invoked via CROSS APPLY).
    Assert.Contains(
        "INNER JOIN sys.dm_db_missing_index_columns AS mic",
        sql, StringComparison.Ordinal);
    Assert.Contains("ON mic.index_handle = ti.IndexHandle", sql, StringComparison.Ordinal);
    Assert.DoesNotContain("CROSS APPLY", sql, StringComparison.Ordinal);

    // Regression guard: migs.group_handle (2025-only column name) must
    // not reappear as a bare, unqualified fragment.
    Assert.DoesNotContain("migs.group_handle", sql, StringComparison.Ordinal);
}
```

**Wichtig — Vorsicht bei der `DoesNotContain`-Assertion für
`migs.group_handle`:** `migs.index_group_handle` enthält
`group_handle` als Teilstring. Die Assertion prüft daher explizit
`"migs.group_handle"` (mit Punkt, ohne `index_`-Präfix) als
zusammenhängenden String — das matcht nicht versehentlich gegen
`migs.index_group_handle`. Der Coder soll das beim Schreiben des Tests
verifizieren (z. B. kurz im Kopf durchspielen oder lokal laufen
lassen), da ein falsch-negativer Test hier den ganzen Sinn der
Regressionsprüfung unterläuft.

Bestehende 12 Tests bleiben unverändert — sie prüfen nicht den
SQL-Text (außer Test 7/8, die nur Parameter-Bindings prüfen, nicht die
Syntax-Fragmente selbst) und sind daher von der SQL-Änderung nicht
betroffen.

### Datei 3: `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs`

**Keine Änderung.** Die vier bestehenden Integrationstests laufen die
Query unverändert gegen die reale Test-DB aus — sie sind der
empirische Beweis, dass die neue 2019/2022-Syntax weiterhin gegen SQL
Server 2025 funktioniert (die vom Nutzer akzeptierte Annahme). Test 1
ist laut `step-005/step-review.md` bekannt rot (TD-006-Ursache, siehe
„Risiken" unten) — das ist kein step-006-Scope-Punkt (Test-Fix folgt
in `step-007`).

### Datei 4: `docs/architecture-spec.md` / `README.md` / `ToolRegistry.cs`

**Keine Änderung.** Gleiche Begründung wie in `step-005`: Diese
Änderung betrifft ausschließlich die interne SQL-Konstruktion in
`LoadSuggestionsAsync` — keine API-, Parameter-, Rückgabetyp- oder
Markdown-Output-Änderung. Die Tool-Beschreibung in §4 Nr. 16 bleibt
korrekt. Eine Mindestversions-Notiz wäre laut TD-004-Volltext
„ergänzend wünschenswert", aber laut EPIC-04-Scope-Definition in
`roadmap.md` (Zeile 184–191: „Code-/Test-Änderung in einer Datei")
nicht Pflichtbestandteil dieses Steps — die Doku-Sync-Pflicht-
Entkräftung greift analog zu `step-005`. Falls der Kritiker eine
Mini-Notiz für sinnvoll hält, kann sie optional als Nice-to-have in
§4 Nr. 16 ergänzt werden (nicht Pflicht-Finding).

## Tests

- [ ] Neuer Test
      `SuggestIndexesAsync_GeneratedSql_UsesSqlServer2019CompatibleSyntax`
      (siehe Code-Skizze oben) — verifiziert `migs.index_group_handle`,
      `INNER JOIN sys.dm_db_missing_index_columns AS mic ON
      mic.index_handle = ti.IndexHandle`, Abwesenheit von
      `CROSS APPLY` und `migs.group_handle`.
- [ ] Alle 12 bestehenden Tests in `IndexSuggestionServiceTests.cs`
      bleiben grün (SQL-Text-agnostisch).
- [ ] Alle 4 Integrationstests in
      `IndexSuggestionServiceIntegrationTests.cs` laufen gegen die
      echte Test-DB (Kategorie `Integration`) — Test 1 bleibt
      voraussichtlich rot (bekannter, unabhängiger TD-006-Zustand, kein
      Regressions-Fehler dieses Steps). Tests 2–4 bleiben grün — sie
      sind der empirische Nachweis, dass die neue Syntax gegen SQL
      Server 2025 funktioniert.
- [ ] `AiNetLinterTests.RecreateBaseline` läuft automatisch im
      `dotnet test`-Lauf und aktualisiert die Baseline — kein
      manueller Eingriff.

## Definition of Done

- [ ] `LoadSuggestionsAsync` in `IndexSuggestionService.cs` verwendet
      `migs.index_group_handle` (statt `migs.group_handle`) und
      `INNER JOIN sys.dm_db_missing_index_columns AS mic ON
      mic.index_handle = ti.IndexHandle` (statt `CROSS APPLY
      sys.dm_db_missing_index_columns(ti.IndexHandle) AS mic`).
- [ ] Kommentarblock über der SQL-Konstante beschreibt die neue
      Mindestversion (SQL Server 2019) und die Annahme zur
      Rückwärtskompatibilität auf SQL Server 2025 — kein
      Doku-Code-Drift zum alten „SQL Server 2025 compatibility
      notes"-Text.
- [ ] Neuer Unit-Test verifiziert den generierten `CommandText` auf
      die 2019/2022-Fragmente und die Abwesenheit der
      2025-spezifischen Fragmente (siehe „Tests").
- [ ] Alle 12 bestehenden Unit-Tests in `IndexSuggestionServiceTests.cs`
      bleiben grün.
- [ ] Die 4 Integrationstests laufen gegen die reale Test-DB (Beweis
      der Annahme „2019/2022-Syntax funktioniert auch gegen SQL Server
      2025"); Tests 2–4 grün, Test 1 darf am bereits bekannten
      TD-006-Zustand scheitern (kein neuer Fehler durch diesen Step —
      im `step-result.md` explizit von etwaigen neuen Fehlern
      abzugrenzen, analog zur Dokumentation in
      `step-005/step-review.md`).
- [ ] `dotnet build` grün, keine neuen Compiler-Warnungen
      (`TreatWarningsAsErrors=true`).
- [ ] `dotnet test` grün (unter Berücksichtigung des bekannten,
      unabhängigen Test-1-Zustands), inkl.
      `AiNetLinterTests.RecreateBaseline` (aktualisiert Baseline
      automatisch — kein manuelles Hash-Rechnen).
- [ ] `IndexSuggestionService.cs` bleibt unter den
      AiNetLinter-Grenzwerten (`MaxLineCount 500`; Datei hat aktuell
      341 LOC, Änderung ist ±1–2 Zeilen SQL-Text plus Kommentar-
      Austausch, bleibt deutlich unter Limit).
- [ ] **Keine** Änderung an `docs/architecture-spec.md`, `README.md`
      oder `ToolRegistry.cs` (interne SQL-Konstruktion, keine API-/
      Output-Änderung — siehe „Rules-Refs" Entkräftung; optionale
      Mini-Notiz ist Nice-to-have, kein Pflicht-Punkt).
- [ ] Commit auf Branch `main` (Conventional Commit, Deutsch,
      imperativ, Subject ≤ 72 Zeichen, Suffix
      `[sql-index-suggestions]`). Subject-Vorschlag:
      `fix(dmv): SQL-Server-2019/2022-Syntax in IndexSuggestionService [sql-index-suggestions]`
      (73 Zeichen — **zu lang**, Coder muss kürzen, z. B.
      `fix(dmv): 2019/2022-kompatible Syntax in IndexSuggestionService [sql-index-suggestions]`
      prüfen und ggf. weiter kürzen; siehe „Bekannte Ausnahmen").
- [ ] `step-006/step-result.md` geschrieben.

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` —
  **Dokumentations-Synchronisation (Pflicht)**: „Bei jeder
  Entwicklung und Änderung an Features/Optionen müssen die
  Dokumentationen in `docs/architecture-spec.md` und `README.md`
  zwingend aktuell gehalten und synchronisiert werden (ohne
  Aufforderung)."
  - **Entkräftung:** Die Pflicht zielt auf Änderungen, die API-,
    Tool-Verhalten, Felder oder Markdown-Output betreffen. Diese
    Änderung ersetzt nur die interne SQL-Konstruktion einer privaten
    Methode; das öffentliche Verhalten des Tools
    (`sql_suggest_indexes`) bleibt unverändert — gleiche Argumente,
    gleiches Markdown-Format, gleiche Fehlerbehandlung. Kein Verstoß
    gegen eine bestehende Doku-Aussage. **Konsequenz:** keine Änderung
    an `architecture-spec.md`, `README.md` oder `ToolRegistry.cs`
    erforderlich (optionale Mini-Notiz zur Mindestversion ist
    Nice-to-have, siehe TD-004-Volltext „Vorschlag").
- `.agents/rules/SqlToAiRichtlinien.mdc#4` —
  **Commits (Pflicht)**: Conventional Commit, Deutsch, imperativ,
  Suffix `[sql-index-suggestions]`, autonom in sinnvollen Abständen,
  Subject ≤ 72 Zeichen. Siehe DoD — Subject-Vorschlag im Plan
  überschreitet das Limit knapp und muss vom Coder final geprüft/
  gekürzt werden (analog zur Subject-Kürzung in `step-005`).
- `.agents/rules/SqlToAiRichtlinien.mdc#5` —
  **AiNetLinter-Hinweis**: `RecreateBaseline` läuft automatisch in
  jedem `dotnet test`, kein manuelles Hash-Rechnen.
  **Zero-Warning-Direktive:** `TreatWarningsAsErrors=true` — neue
  Compiler-Warnungen sind Build-Fehler.
- `.agents/rules/AiNetLinter.mdc` — **Grenzwerte Produktion**:
  `MaxLineCount 500` (Datei hat aktuell 341 LOC, bleibt weit unter
  Limit), `MaxMethodLineCount 60` (`LoadSuggestionsAsync` ist bereits
  vor diesem Step 82 LOC — siehe „Aktueller Projektzustand" für die
  Einordnung; diese Änderung fügt keine neue Kontrollstruktur hinzu
  und ändert die LOC-Zahl kaum, ist also kein neu durch step-006
  eingeführtes Risiko), `sealed class` (Klasse bleibt `sealed`),
  `#nullable enable` (Dateianfang, bleibt). Kein `dynamic`, keine
  leeren `catch`-Blöcke, kein `out` außerhalb `Try*`.
- `.agents/rules/SqlToAiRichtlinien.mdc#3` — **Build & Test**:
  `dotnet build`/`dotnet test` wie gewohnt; Integrationstests
  benötigen die konfigurierte Test-DB-Verbindung (bereits vorhanden
  aus vorangegangenen Steps).

## Risiken

- **Kernrisiko dieses Steps — die akzeptierte Annahme könnte falsch
  sein:** Falls SQL Server 2025 `migs.index_group_handle` NICHT als
  Alias akzeptiert (harter SQL-Fehler „Invalid column name") oder
  `sys.dm_db_missing_index_columns` als klassischer Join-Partner nicht
  mehr aufrufbar ist (harter SQL-Fehler, da es dort nur noch die TVF
  gibt), schlagen die Integrationstests 2–4 (aktuell grün) mit einem
  **neuen** SQL-Fehler fehl — das wäre ein echter Regressions-Befund
  dieses Steps, klar unterscheidbar vom bekannten TD-006-Test-1-
  Zustand (der eine Assertion-Verletzung ist, kein SQL-Fehler). In
  diesem Fall: Step **nicht** als erfolgreich melden, sondern als
  `blocked` an den Nutzer eskalieren — die Nutzer-Vorgabe „SQL Server
  2025 behält alte Namen als Aliase" wäre empirisch widerlegt, und die
  Entscheidung, wie weiter zu verfahren ist (z. B. versionsabhängige
  Query-Konstruktion), liegt beim Nutzer, nicht beim Coder.
- **Test-1-Fehlschlag (TD-006) darf nicht mit diesem Risiko verwechselt
  werden:** Test 1 schlägt aktuell an einer Markdown-Text-Assertion
  fehl (`No missing-index recommendations found` ODER `| Score |`
  wird erwartet, aber die Graceful-Degradation-Notiz kommt zurück —
  eine `Assert.True`-Verletzung, kein `SqlException`). Der Coder soll
  im `step-result.md` explizit dokumentieren, welcher der beiden
  möglichen Fehlerarten (bekannter TD-006-Assertion-Fehler vs. neuer
  SQL-Syntax-Fehler) bei Test 1/2/3/4 jeweils auftritt, damit der
  Kritiker die Abgrenzung nachvollziehen kann.
- **Kein Risiko für den Unit-Test-Pfad:** Die Mocks führen kein echtes
  SQL aus, daher kann der neue `CommandText`-basierte Test nicht durch
  die SQL-Engine widerlegt werden — er prüft nur, dass der C#-Code den
  beabsichtigten String erzeugt, nicht, dass der String syntaktisch
  gültiges T-SQL ist. Die Integrationstests sind der einzige Beweis
  für syntaktische Gültigkeit (siehe TD-007 — bewusst nicht behoben,
  „grundsätzlich nicht", außerhalb dieses Scopes).

## Bekannte Ausnahmen

- **Subject-Zeichenlänge:** der im Plan vorgeschlagene Commit-Subject
  ist als Vorschlag zu verstehen, nicht als Wort-für-Wort-Vorgabe —
  der Coder muss die finale Zeichenlänge prüfen (≤ 72) und ggf. kürzen,
  wie in `step-005` bereits einmal nötig.
- **`LoadSuggestionsAsync`-Methodenlänge (82 LOC):** vorbestehender
  Zustand seit `step-003`, kein step-006-Scope-Punkt (siehe
  „Rules-Refs"/AiNetLinter.mdc).

## Notes

- **Wiederverwendete Strukturen:** keine neue Klasse, keine neue
  Schnittstelle, kein neuer Test-Helper — der `CommandText`-basierte
  Test folgt exakt dem bestehenden Pattern aus Test 7/8 in
  `IndexSuggestionServiceTests.cs`.
- **Nach `approved`-Verdict:** TD-004-Eintrag aus `tech-debt.md`
  entfernen (per Status-Policy). Kein neues Epic in `roadmap.md`
  anlegen — EPIC-04 wird mit `approved` von step-006 noch nicht
  abgeschlossen (step-007/TD-006 ist noch offen).
- **Abgrenzung zu step-007 (nicht Teil dieses Plans):** step-007
  (TD-006, Test-1-Erweiterung in
  `IndexSuggestionServiceIntegrationTests.cs`) wird laut JIT-Prinzip
  erst nach `approved`-Verdict dieses Steps geplant — auch wenn Test 1
  hier bereits als bekanntes Problem sichtbar wird, ist er **nicht**
  Teil der Konkreten Änderungen dieses Plans.
