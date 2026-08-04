---
status: blocked
type: step-plan
task: sql-index-suggestions
step: 006/fix-01
title: "Fix: versionsabhängige DMV-Query-Konstruktion (SQL Server 2019/2022 + 2025) in IndexSuggestionService.LoadSuggestionsAsync"
epic: EPIC-04
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: claude-sonnet-5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-05T18:00:00+02:00
related_to:
  - tasks/sql-index-suggestions/step-006/step-result.md
  - tasks/sql-index-suggestions/step-006/step-plan.md
  - tasks/sql-index-suggestions/tech-debt.md#TD-004
---

# Step 006/fix-01: Versionsabhängige DMV-Query-Konstruktion in `IndexSuggestionService.LoadSuggestionsAsync`

## Bezug

- **Task:** `sql-index-suggestions`
- **Epic:** `EPIC-04` aus `roadmap.md` — Post-Completion Tech-Debt Cleanup
  Round 2, TD-004.
- **Auslöser:** `step-006` (Commit `2011331`) hat die DMV-Query exakt
  wie geplant auf reine 2019/2022-Syntax (`migs.index_group_handle`,
  `INNER JOIN sys.dm_db_missing_index_columns ... ON mic.index_handle`)
  umgestellt. Der Step wurde `blocked` gemeldet: alle vier
  Integrationstests gegen die reale Test-DB (SQL Server 2025 RTM
  17.0.1000.7) schlagen mit `SqlException: Ungültiger Spaltenname
  "index_group_handle"` fehl — die im ursprünglichen Plan akzeptierte
  Annahme „SQL Server 2025 führt `index_group_handle` als
  Rückwärtskompatibilitäts-Alias weiter" ist damit empirisch widerlegt
  (siehe `step-006/step-result.md`, Abschnitte „Zusammenfassung" und
  „Bekannte Unschärfen").
- **Bindende Nutzer-Entscheidung (2026-08-05):** SQL-Server-2019-
  Kompatibilität bleibt Pflicht, UND die Integrationstests gegen die
  reale SQL-Server-2025-Test-Instanz müssen weiterhin grün laufen. Ein
  reiner Revert auf die alte 2025-only-Syntax ist explizit **nicht**
  die gewünschte Lösung — gefordert ist eine **versionsabhängige
  Query-Konstruktion** (Server-Versionserkennung zur Laufzeit).
- **Scope-Disziplin (Fix-Modus):** Dieser Plan behandelt ausschließlich
  den in `step-006/step-result.md` dokumentierten Blocker (SQL-Fehler
  gegen die reale Test-DB) plus den dort unter „Beobachtungen"
  gemeldeten, real-zurechenbaren `MaxLineCount`-Verstoß in
  `IndexSuggestionServiceTests.cs` (506/500 Zeilen), der ohne
  Gegenmaßnahme durch die neuen Tests dieses Fixes weiter wachsen
  würde. Die im `step-result.md` ebenfalls gemeldete AiNetLinter-
  Nichtdeterminismus-Anomalie (5 Violations in step-006-fremden
  Dateien, reproduzierbar an-/abwesend je nach Arbeitsbaum-Zustand,
  variierende Baseline-Hashes) ist **nicht** Teil dieses Fix-Scopes —
  sie betrifft keine Datei, die dieser Fix ändert, ist nicht auf
  step-006/fix-01 zurückführbar und bleibt dem Kritiker/Nutzer zur
  Bewertung überlassen (kein automatisches Tech-Debt-Anlegen durch den
  Planer).

## Aktueller Projektzustand (JIT-Kontext)

Gelesen: `src/SqlToAi/Database/IndexSuggestionService.cs` (aktueller
Stand nach Commit `2011331`, 345 LOC),
`tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs` (506
LOC), `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs`,
`tests/SqlToAi.Tests/TestSupport/FakeDbConnection.cs`,
`tests/SqlToAi.Tests/TestSupport/FakeDbCommand.cs`, sowie der exakte
Vorher/Nachher-Diff von Commit `2011331`
(`git show 2011331 -- src/SqlToAi/Database/IndexSuggestionService.cs`).

**Wichtigster Fund — die Versionserkennungs-Mechanik existiert bereits
im Projekt, sie wird nur noch nicht produktiv genutzt:**

- `IDatabaseConnectionFactory.CreateConnection(...)` liefert ein
  `System.Data.Common.DbConnection` (`src/SqlToAi/Database/IDatabaseConnectionFactory.cs`
  Zeile 17). `DbConnection` hat die **Standard-ADO.NET-Property**
  `ServerVersion` (`string`, geerbt, kein SQL-Server-spezifisches
  API). Für `Microsoft.Data.SqlClient.SqlConnection` liefert diese
  Property die Server-Produktversion im Format
  `"<major>.<minor>.<build>"` (z. B. `"15.00.4153"` für SQL Server
  2019, `"16.00.xxxx"` für 2022, `"17.0.xxxx"` für 2025 — konsistent
  mit der in `step-003`/`step-006` beobachteten RTM-Versionsangabe
  „SQL Server 2025 RTM 17.0.1000.7"). Kein zusätzlicher DB-Roundtrip
  nötig — `ServerVersion` ist nach `OpenAsync` sofort verfügbar (aus
  dem Login-Handshake), genau der Punkt, an dem `LoadSuggestionsAsync`
  die bereits offene `connection` erhält.
- **Diese Property ist bereits Teil der Test-Infrastruktur, nicht neu
  zu erfinden:** `tests/SqlToAi.Tests/TestSupport/FakeDbConnection.cs`
  Zeile 18–22 (`FakeDbConnectionOptions.ServerVersion`, Default
  `"1.0"`) und Zeile 50 (`public override string ServerVersion =>
  _options.ServerVersion;`) — die generische `DbConnection`-Fake-Klasse,
  die **alle** ADO.NET-Test-Doubles im Projekt gemeinsam nutzen,
  spiegelt exakt diese Property. Die private
  `DmvMockConnectionFactory` in `IndexSuggestionServiceTests.cs`
  (Zeile 450–476) setzt bereits `ServerVersion: "16.0"` beim Aufbau
  jeder Fake-Connection (Zeile 471) — bisher nur als Fülldaten,
  ungenutzt vom Code unter Test. Das ist der fertige Haken, mit dem
  sich die Versionserkennung ohne echten SQL Server unit-testen lässt.
- **Keine andere Versionserkennungs-Mechanik im Projektcode
  gefunden:** Suche nach `SERVERPROPERTY`, `@@VERSION`,
  `ProductMajorVersion`, `EngineEdition` in `src/**` und `tests/**`
  ergab keine Treffer. `McpConstants.ServerVersion`/`McpModels.cs`
  betreffen die **MCP-Server-Version** (unabhängige, unverwandte
  Konstante `"1.0.0"`), nicht die SQL-Server-Version — nicht zu
  verwechseln, keine Wiederverwendung möglich/nötig.
- **`LoadSuggestionsAsync` (Zeile 118–204):** erhält die bereits
  geöffnete `connection` als Parameter (Zeile 119) — `ServerVersion`
  ist dort ohne zusätzlichen Code direkt lesbar.
- **`IndexSuggestionServiceTests.cs`, Struktur der Fake-Plumbing**
  (Zeile 429–505, Abschnitt „Fake DB plumbing"): `DmvColumn`- und
  `DmvRow`-Records (Zeile 433–443) sowie die private
  `DmvMockConnectionFactory`-Klasse (Zeile 450–504) sind aktuell als
  private verschachtelte Typen **innerhalb** der Testklasse
  untergebracht — anders als das Projekt-Muster in
  `tests/SqlToAi.Tests/TestSupport/`, wo jeder gemeinsam nutzbare
  ADO.NET-Fake (`FakeDbConnection`, `FakeDbCommand`,
  `FakeDbDataReader`, `FakeDbParameter`, `FakeDbParameterCollection`,
  `FakeDbTransaction`) eine eigene Datei mit `internal`-Sichtbarkeit
  hat. `DmvMockConnectionFactory` ist aktuell nur von
  `IndexSuggestionServiceTests.cs` selbst genutzt, folgt aber exakt
  demselben Bauplan (Delegation an `FakeDbConnection`/
  `FakeDbCommandHandlers`) wie die bereits ausgelagerten Fakes.
  **Das ist die bestehende Struktur, die dieser Fix wiederverwendet
  statt neu zu bauen:** Auslagerung nach
  `tests/SqlToAi.Tests/TestSupport/DmvMockConnectionFactory.cs` löst
  gleichzeitig den `MaxLineCount`-Verstoß (506/500 Zeilen, siehe
  `step-006/step-result.md` „Beobachtungen") und folgt dem
  Projekt-Konsistenzmuster — keine Ad-hoc-Lösung.
- **`BuildService(DmvMockConnectionFactory factory)`-Überladung**
  (Zeile 47–56) existiert bereits und wird von den bestehenden Tests
  7/8/TD-004-Test genutzt (Zeile 224–225, 255–256, 284–285) — neue
  Tests für die Versionserkennung können dieselbe Überladung nutzen,
  keine neue `BuildService`-Variante nötig.

## Intention

Nach diesem Fix wählt `LoadSuggestionsAsync` die DMV-Query-Syntax zur
Laufzeit anhand der tatsächlich verbundenen SQL-Server-Hauptversion
(`connection.ServerVersion`, geparst auf den führenden Versions-Teil
vor dem ersten Punkt): Server mit Hauptversion **≥ 17** (SQL Server
2025+) erhalten die 2025-Syntax (`migs.group_handle`, `CROSS APPLY
sys.dm_db_missing_index_columns(...)`), alle anderen (inkl. der
Mindestversion 2019, Hauptversion 15, und 2022, Hauptversion 16, sowie
jeder nicht parsebare/unbekannte Versions-String als sicherer
Default) erhalten die stabile 2019/2022-Syntax
(`migs.index_group_handle`, `INNER JOIN ... ON mic.index_handle`).
Beide SQL-Text-Varianten existieren vollständig und unabhängig
voneinander als Konstanten (kein fragiles String-Ersetzen), analog zum
bisherigen Stil eines vollständigen `const string sql`-Blocks. Die
Versionsauswahl selbst ist in eine eigene, kleine private statische
Methode ausgelagert, damit `LoadSuggestionsAsync` selbst flach bleibt
(keine neue Verzweigung in der bereits 82-LOC-langen Methode). Ein
neuer Kommentarblock über der SQL-Konstruktion dokumentiert Mechanik
und Schwellenwert. Vier neue/angepasste Unit-Tests verifizieren beide
Zweige (2019/2022-Default, 2025-Branch bei Hauptversion 17,
Fallback-Verhalten bei nicht parsebarem Versions-String) über das
bereits etablierte `CommandText`-Inspektionsmuster — ohne echten SQL
Server. Die vier bestehenden Integrationstests laufen unverändert
gegen die reale SQL-Server-2025-Test-Instanz und müssen nach diesem
Fix wieder grün sein (der eigentliche Blocker-Beweis: die Query wählt
jetzt automatisch die zur Instanz passende Syntax). Gleichzeitig wird
`DmvMockConnectionFactory` samt `DmvRow`/`DmvColumn` aus
`IndexSuggestionServiceTests.cs` nach
`tests/SqlToAi.Tests/TestSupport/DmvMockConnectionFactory.cs`
ausgelagert, um den bereits gemeldeten `MaxLineCount`-Verstoß (506/500)
zu beheben und Raum für die neuen Tests zu schaffen, ohne das Limit
erneut zu reißen.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/IndexSuggestionService.cs`

**a) Kommentarblock über der SQL-Konstruktion (aktuell Zeile 123–143)
ersetzen:**

Der bestehende „Minimum supported SQL Server version: 2019 …
presumably via a backward-compatibility alias"-Block beschreibt nach
diesem Fix nicht mehr den tatsächlichen Code (die Annahme ist
widerlegt, es gibt jetzt zwei SQL-Text-Varianten statt einer) und muss
ersetzt werden. Neuer Inhalt (Coder darf umformulieren, Inhalt
maßgeblich):
- Minimum unterstützte Version bleibt SQL Server 2019 (Policy,
  EPIC-04/TD-004).
- Die step-006-Annahme „SQL Server 2025 akzeptiert die alten
  2019/2022-DMV-Namen als Rückwärtskompatibilitäts-Alias" wurde durch
  die Integrationstests in `step-006` widerlegt (`SqlException:
  Ungültiger Spaltenname "index_group_handle"` gegen SQL Server 2025
  RTM 17.0.1000.7) — Verweis auf `step-006/step-result.md`.
- Deshalb: versionsabhängige Query-Konstruktion zur Laufzeit über
  `connection.ServerVersion` (Standard-`DbConnection`-Property, kein
  zusätzlicher Roundtrip). Hauptversion ≥ 17 → 2025-Syntax
  (`group_handle`, `CROSS APPLY`-TVF); alles andere (inkl. 2019/2022
  und unbekannt/nicht parsebar) → 2019/2022-Syntax
  (`index_group_handle`, `INNER JOIN`-View).
- Verweis auf `tech-debt.md`/TD-004 als Herkunft der
  Mindestversions-Policy (wie im bisherigen Kommentarstil).

**b) SQL-Text — zwei vollständige Varianten statt einer, als
`private const string`-Felder (Klassenebene, oberhalb von
`LoadSuggestionsAsync`, analog zum bisherigen `RestartHint`-Feld-Stil
Zeile 30–33):**

```csharp
// Threshold: SqlConnection.ServerVersion reports "<major>.<minor>.<build>";
// major version 17 is the first SQL Server 2025 release. Below this
// threshold (2019 = 15, 2022 = 16, and any unparseable/unknown version)
// the stable 2019/2022 DMV schema is used — see TD-004 for why this is a
// fixed technical constant, not an AppSettings-tunable value (same
// reasoning as the hardcoded SQL error numbers 300/297 in
// IsViewServerStatePermissionError below).
private const int Sql2025MinMajorVersion = 17;

private const string Sql2019CompatibleQuery = """
    WITH Scored AS (
        SELECT
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
    ),
    TopIndexes AS (
        SELECT TOP (@Top)
            Statement,
            IndexHandle,
            UserSeeks,
            UserScans,
            LastUserSeek,
            AvgTotalUserCost,
            AvgUserImpact,
            ImprovementScore
        FROM Scored
        WHERE (@TableName IS NULL OR Statement LIKE '%' + @TableName + '%')
          AND (@MinScore IS NULL OR ImprovementScore >= @MinScore)
        ORDER BY ImprovementScore DESC, Statement
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
        ON mic.index_handle = ti.IndexHandle
    ORDER BY ti.ImprovementScore DESC, ti.Statement, mic.column_id
    """;

private const string Sql2025Query = """
    WITH Scored AS (
        SELECT
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
            ON migs.group_handle = mig.index_group_handle
        INNER JOIN sys.dm_db_missing_index_details AS mid
            ON mig.index_handle = mid.index_handle
        WHERE mid.database_id = DB_ID()
    ),
    TopIndexes AS (
        SELECT TOP (@Top)
            Statement,
            IndexHandle,
            UserSeeks,
            UserScans,
            LastUserSeek,
            AvgTotalUserCost,
            AvgUserImpact,
            ImprovementScore
        FROM Scored
        WHERE (@TableName IS NULL OR Statement LIKE '%' + @TableName + '%')
          AND (@MinScore IS NULL OR ImprovementScore >= @MinScore)
        ORDER BY ImprovementScore DESC, Statement
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
    CROSS APPLY sys.dm_db_missing_index_columns(ti.IndexHandle) AS mic
    ORDER BY ti.ImprovementScore DESC, ti.Statement, mic.column_id
    """;
```

`Sql2025Query` ist wortgleich mit der Syntax vor `step-006`
(verifiziert per `git show 2011331 -- src/SqlToAi/Database/IndexSuggestionService.cs`,
„Vorher"-Seite des Diffs) — kein neu erfundener Text, exakter
Rückgriff auf die bereits gegen die reale Test-DB in `step-003`
verifizierte Fassung.

**c) Zwei neue kleine private statische Helper-Methoden** (halten
`LoadSuggestionsAsync` selbst frei von neuer Verzweigung/Cognitive
Complexity):

```csharp
private static int GetServerMajorVersion(DbConnection connection)
{
    string version = connection.ServerVersion;
    int dotIndex = version.IndexOf('.', StringComparison.Ordinal);
    string majorPart = dotIndex >= 0 ? version[..dotIndex] : version;
    return int.TryParse(majorPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int major)
        ? major
        : 0;
}

private static string SelectSql(int serverMajorVersion) =>
    serverMajorVersion >= Sql2025MinMajorVersion ? Sql2025Query : Sql2019CompatibleQuery;
```

`GetServerMajorVersion` parst nicht-parsebare/leere Strings defensiv
auf `0` (→ `SelectSql` wählt dann die 2019/2022-Syntax, konsistent mit
der Mindestversions-Policy: unbekannte/leere Version ⇒ konservativer
Default, nicht der neuere, spezifischere 2025-Zweig). `using
System.Globalization;` ist bereits vorhanden (Datei-Kopf, für
`CultureInfo` in `GroupRows`/`RenderMarkdown`).

**d) `LoadSuggestionsAsync` — SQL-Auswahl statt fester Konstante**
(aktuelle Zeile 144–191 `const string sql = """ ... """;` entfällt,
ersetzt durch):

```csharp
string sql = SelectSql(GetServerMajorVersion(connection));
```

Direkt oberhalb der bestehenden `parameters`-Objekt-Zuweisung (bisher
Zeile 193). Kein sonstiger Code-Change an `LoadSuggestionsAsync`:
`parameters`-Objekt, `connection.QueryAsync<SuggestionRawRow>`,
`GroupRows`-Aufruf bleiben unverändert (nur `const string sql = ...`
wird durch die eine Zeile oben ersetzt).

**e) Kein sonstiger Code-Change:** `GroupRows`, `RenderMarkdown`,
`RenderPermissionNote`, `IsViewServerStatePermissionError`, DTOs
(`SuggestionRawRow`/`MissingIndexRow`) — alle unverändert, sie kennen
die konkrete SQL-Syntax nicht (Dapper mappt per Spaltenname, beide
Query-Varianten liefern dieselben `AS`-Aliase).

### Datei 2 (neu): `tests/SqlToAi.Tests/TestSupport/DmvMockConnectionFactory.cs`

Auslagerung von `DmvColumn`, `DmvRow` und `DmvMockConnectionFactory`
aus `IndexSuggestionServiceTests.cs` (aktuell Zeile 433–504) in eine
eigene Datei, exakt nach dem Muster der übrigen Dateien in
`TestSupport/` (`internal`-Sichtbarkeit, `#nullable enable`,
`namespace SqlToAi.Tests.TestSupport;`, XML-Doku-Kommentare
übernehmen). **Einzige inhaltliche Änderung dabei:** ein neuer
optionaler Konstruktor-Parameter `serverVersion` (Default `"16.0"` —
der bisher hardcodierte Wert bleibt der Default, damit alle
bestehenden Aufrufer ohne Änderung weiterhin dieselbe Fake-Version wie
bisher bekommen), durchgereicht an
`FakeDbConnectionOptions(ServerVersion: serverVersion, ...)` statt des
bisher fest verdrahteten `"16.0"`-Literals:

```csharp
internal sealed record DmvColumn(int ColumnId, string ColumnUsage);

internal sealed record DmvRow(
    string Statement,
    long IndexHandle,
    long UserSeeks,
    long UserScans,
    DateTime? LastUserSeek,
    double AvgTotalUserCost,
    double AvgUserImpact,
    IReadOnlyList<DmvColumn> Columns);

/// <summary>
/// A <see cref="DbConnection"/> fake that returns the given DMV rows from a single
/// reader. If <see cref="_throwOnExecuteReader"/> is set, it is thrown on
/// <c>ExecuteReaderAsync</c> to simulate server-side failures (e.g. permission errors).
/// <paramref name="serverVersion"/> feeds <see cref="FakeDbConnection.ServerVersion"/>
/// so tests can exercise IndexSuggestionService's version-dependent DMV query selection
/// (TD-004 / step-006/fix-01) without a real SQL Server.
/// </summary>
internal sealed class DmvMockConnectionFactory(
    IReadOnlyList<DmvRow> rows,
    Exception? throwOnExecuteReader,
    string serverVersion = "16.0") : IDatabaseConnectionFactory
{
    // ... Rest 1:1 wie bisher (LastConnection, LastReaderCommand, CreateConnection,
    // ExecuteReader), nur ServerVersion: "16.0" → ServerVersion: serverVersion
    // in der FakeDbConnectionOptions-Konstruktion.
}
```

Namespace `SqlToAi.Tests.TestSupport` ist in
`IndexSuggestionServiceTests.cs` bereits importiert (Zeile 12,
`using SqlToAi.Tests.TestSupport;`) — kein neuer `using`-Zusatz nötig.

### Datei 3: `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs`

**a) Entfernen:** Abschnitt „Fake DB plumbing" (aktuell Zeile
429–505: `DmvColumn`, `DmvRow`, `DmvMockConnectionFactory`) — nach
Datei 2 ausgelagert.

**b) Bestehender Test
`SuggestIndexesAsync_GeneratedSql_UsesSqlServer2019CompatibleSyntax`
(Zeile 281–309): unverändert.** Die Mock-Factory nutzt weiterhin den
Default `serverVersion: "16.0"` (SQL Server 2022, Hauptversion 16 <
17) — der Test bleibt gültig und beweist weiterhin den
2019/2022-Standardpfad, jetzt zusätzlich als Beleg, dass der neue
Versions-Schwellenwert für „normale" (nicht-2025-)Versionen korrekt
den bekannten Zweig wählt.

**c) Neuer Test — 2025-Zweig wird bei Hauptversion 17 gewählt:**

```csharp
[Fact]
public async Task SuggestIndexesAsync_GeneratedSql_UsesSqlServer2025SyntaxWhenServerReportsMajorVersion17()
{
    var factory = new DmvMockConnectionFactory([], throwOnExecuteReader: null, serverVersion: "17.0.1000");
    var service = BuildService(factory: factory);

    await service.SuggestIndexesAsync(new IndexSuggestionArgs("DemoDB"), TestContext.Current.CancellationToken);

    var cmd = factory.LastReaderCommand;
    Assert.NotNull(cmd);
    string sql = cmd!.CommandText;

    Assert.Contains("migs.group_handle", sql, StringComparison.Ordinal);
    Assert.Contains("CROSS APPLY sys.dm_db_missing_index_columns(ti.IndexHandle) AS mic", sql, StringComparison.Ordinal);
    Assert.DoesNotContain("migs.index_group_handle", sql, StringComparison.Ordinal);
    Assert.DoesNotContain("INNER JOIN sys.dm_db_missing_index_columns", sql, StringComparison.Ordinal);
}
```

**d) Neuer Test — Fallback bei nicht parsebarer/leerer
`ServerVersion` auf die 2019/2022-Syntax:**

```csharp
[Theory]
[InlineData("")]
[InlineData("not-a-version")]
public async Task SuggestIndexesAsync_GeneratedSql_FallsBackToSqlServer2019SyntaxWhenServerVersionUnparseable(string serverVersion)
{
    var factory = new DmvMockConnectionFactory([], throwOnExecuteReader: null, serverVersion: serverVersion);
    var service = BuildService(factory: factory);

    await service.SuggestIndexesAsync(new IndexSuggestionArgs("DemoDB"), TestContext.Current.CancellationToken);

    var cmd = factory.LastReaderCommand;
    Assert.NotNull(cmd);
    string sql = cmd!.CommandText;

    Assert.Contains("migs.index_group_handle", sql, StringComparison.Ordinal);
    Assert.DoesNotContain("CROSS APPLY", sql, StringComparison.Ordinal);
}
```

**e) LOC-Budget beachten:** Nach a)–d) muss die Datei unter 500 Zeilen
bleiben (`MaxLineCount`). Die Auslagerung (b) entfernt ca. 70–75
Zeilen, die beiden neuen Tests (c, d) fügen zusammen ca. 40–50 Zeilen
hinzu — rechnerisch sollte die Datei danach spürbar unter 500 Zeilen
liegen, aber der Coder muss die tatsächliche Zeilenzahl nach der
Änderung verifizieren (z. B. `(Get-Content <Datei> | Measure-Object
-Line).Lines` unter PowerShell) und bei Bedarf nachschärfen (z. B.
Kommentare straffen), **bevor** der Step als fertig gemeldet wird —
nicht erst der Kritiker soll das auffangen.

Bestehende Tests 1–8 sowie 9–12 (Permission-Handling, generische
`SqlException`, `IsPermissionError`-Refactoring-Test,
Args-Defaults-Test) bleiben unverändert und nutzen weiterhin dieselbe
`BuildService`/`DmvMockConnectionFactory`-Infrastruktur (nur aus einer
anderen Datei importiert).

### Datei 4: `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs`

**Keine Änderung.** Die vier bestehenden Integrationstests sind der
einzig belastbare Beweis, dass die Versionserkennung gegen die reale
SQL-Server-2025-Instanz tatsächlich den 2025-Zweig wählt und dieser
syntaktisch gültig ist — sie müssen nach diesem Fix wieder grün laufen
(inkl. Test 1, der laut `step-006/step-result.md` aktuell durch den
SQL-Fehler verdeckt ist und nach dem Fix in seinen bekannten,
unabhängigen TD-006-Assertion-Zustand aus `step-005/step-review.md`
zurückfallen sollte — siehe „Risiken").

### Datei 5: `docs/architecture-spec.md` / `README.md` / `ToolRegistry.cs`

**Keine Änderung.** Gleiche Begründung wie in `step-005`/`step-006`:
reine interne SQL-Konstruktionsänderung, kein API-/Output-Wechsel des
Tools `sql_suggest_indexes`.

## Tests

- [ ] Bestehender Test
      `SuggestIndexesAsync_GeneratedSql_UsesSqlServer2019CompatibleSyntax`
      bleibt grün (Default-Mock-Version 16.0 → 2019/2022-Zweig).
- [ ] Neuer Test
      `SuggestIndexesAsync_GeneratedSql_UsesSqlServer2025SyntaxWhenServerReportsMajorVersion17`
      (Mock-Version `17.0.1000`) — verifiziert `migs.group_handle`,
      `CROSS APPLY sys.dm_db_missing_index_columns(ti.IndexHandle) AS mic`,
      Abwesenheit von `migs.index_group_handle` und
      `INNER JOIN sys.dm_db_missing_index_columns`.
- [ ] Neuer Theory-Test
      `SuggestIndexesAsync_GeneratedSql_FallsBackToSqlServer2019SyntaxWhenServerVersionUnparseable`
      (`""`, `"not-a-version"`) — verifiziert Fallback auf
      `migs.index_group_handle`, Abwesenheit von `CROSS APPLY`.
- [ ] Alle 12 unveränderten Bestandstests in
      `IndexSuggestionServiceTests.cs` bleiben grün.
- [ ] Alle 4 Integrationstests in
      `IndexSuggestionServiceIntegrationTests.cs` laufen gegen die
      echte Test-DB grün (der eigentliche Blocker-Beweis dieses
      Fixes) — inkl. Dokumentation im `step-result.md`, ob Test 1 wie
      erwartet in den bekannten TD-006-Assertion-Zustand zurückfällt
      oder tatsächlich vollständig grün wird.
- [ ] `AiNetLinterTests.RecreateBaseline` läuft automatisch mit, kein
      manueller Eingriff.

## Definition of Done

- [ ] `LoadSuggestionsAsync` wählt die DMV-Query-Syntax zur Laufzeit
      über `connection.ServerVersion` (Hauptversion ≥ 17 → 2025-Syntax,
      sonst 2019/2022-Syntax) statt einer festen Konstante.
- [ ] Beide SQL-Text-Varianten (`Sql2019CompatibleQuery`,
      `Sql2025Query`) existieren vollständig als eigene Konstanten,
      `Sql2025Query` ist textgleich mit der vor `step-006` verifizierten
      Fassung (Commit vor `2011331`).
- [ ] Kommentarblock dokumentiert Mechanik, Schwellenwert (17) und
      Verweis auf die widerlegte step-006-Annahme /
      `step-006/step-result.md`.
- [ ] `DmvMockConnectionFactory`/`DmvRow`/`DmvColumn` nach
      `tests/SqlToAi.Tests/TestSupport/DmvMockConnectionFactory.cs`
      ausgelagert, mit neuem optionalen `serverVersion`-Parameter
      (Default `"16.0"`, rückwärtskompatibel zu allen bestehenden
      Aufrufern).
- [ ] `IndexSuggestionServiceTests.cs` bleibt unter der
      AiNetLinter-Grenze `MaxLineCount 500` — vom Coder nach der
      Änderung real verifiziert, nicht nur rechnerisch angenommen.
- [ ] Drei neue/angepasste Tests (siehe „Tests") verifizieren beide
      Zweige plus Fallback-Verhalten, alle grün.
- [ ] Alle 12 unveränderten Bestandstests in
      `IndexSuggestionServiceTests.cs` bleiben grün.
- [ ] Alle 4 Integrationstests in
      `IndexSuggestionServiceIntegrationTests.cs` laufen grün gegen
      die reale SQL-Server-2025-Test-Instanz — das ist der Kernbeweis,
      dass der Blocker aus `step-006` behoben ist.
- [ ] `dotnet build` grün, keine neuen Compiler-Warnungen
      (`TreatWarningsAsErrors=true`).
- [ ] `dotnet test` grün, inkl. `AiNetLinterTests.RecreateBaseline`
      (Baseline aktualisiert sich automatisch).
- [ ] `IndexSuggestionService.cs` bleibt unter `MaxLineCount 500`
      (Datei wächst um ca. 45–55 Zeilen durch die zweite SQL-Variante
      plus zwei Helper-Methoden, aktuell 345 LOC → voraussichtlich
      ca. 395–405 LOC, deutlich unter Limit).
- [ ] **Keine** Änderung an `docs/architecture-spec.md`, `README.md`
      oder `ToolRegistry.cs`.
- [ ] Commit auf Branch `main` (Conventional Commit, Deutsch,
      imperativ, Subject ≤ 72 Zeichen, Suffix
      `[sql-index-suggestions]`). Subject-Vorschlag:
      `fix(dmv): versionsabhaengige Syntax in IndexSuggestionService [sql-index-suggestions]`
      (Coder muss finale Zeichenlänge prüfen/kürzen).
- [ ] `step-006/fix-01/step-result.md` geschrieben — inkl. expliziter
      Bestätigung, dass alle 4 Integrationstests gegen die reale
      SQL-Server-2025-Instanz grün sind (der eigentliche Nachweis,
      dass der `step-006`-Blocker behoben ist), sowie einer kurzen
      Notiz zum Verhalten von Integrationstest 1 (siehe „Risiken").

## Rules-Refs

- `.agents/rules/AiNetLinter.mdc` — **`MaxLineCount 500`**: betrifft
  sowohl `IndexSuggestionService.cs` (bleibt deutlich unter Limit,
  siehe DoD) als auch `IndexSuggestionServiceTests.cs` (aktuell
  bereits über Limit — dieser Fix behebt das explizit durch
  Auslagerung, siehe „Konkrete Änderungen" Datei 3e).
  **`MaxMethodLineCount 60`** (Compound-Suppression 150 bei CC≤3/
  Cognitive≤5): `GetServerMajorVersion` und `SelectSql` sind neue,
  sehr kurze Methoden (deutlich unter 60 LOC); `LoadSuggestionsAsync`
  selbst bleibt bei der bereits vor `step-006` bekannten,
  Nicht-Scope-Länge (82 LOC, keine neue Kontrollstruktur durch diesen
  Fix — die Verzweigung wandert vollständig in `SelectSql`).
  **Kein `dynamic`, kein leeres `catch`, `out` nur in `Try*`** —
  unverändert eingehalten, keine neuen Verstöße durch diesen Fix.
- `.agents/rules/SqlToAiRichtlinien.mdc#4` — **Keine hartkodierten
  Werte (No Magic Values) & AppSettings-Pflicht:** Die Konstante
  `Sql2025MinMajorVersion = 17` ist bewusst **kein**
  `appsettings.json`-konfigurierbarer Wert — sie ist ein fixer
  technischer Fakt über SQL-Server-Produktversionierung (analog zu
  den bereits hardcodierten SQL-Fehlernummern `300`/`297` in
  `IsViewServerStatePermissionError`, die ebenfalls nie als
  AppSettings-Option modelliert wurden), kein laufzeit-abstimmbarer
  Business-Parameter. Falls der Kritiker das anders bewertet: explizit
  im `step-result.md` zur Diskussion stellen, nicht stillschweigend
  in `appsettings.json` verschieben (würde die Doku-Sync-Pflicht für
  `AppSettingsMigrator` triggern, ohne dass ein Nutzer diesen Wert
  jemals sinnvoll ändern würde).
- `.agents/rules/SqlToAiRichtlinien.mdc#4` — **Dokumentations-
  Synchronisation (Pflicht):** Entkräftung wie in `step-005`/`step-006`
  — keine API-/Output-Änderung, siehe „Datei 5".
- `.agents/rules/SqlToAiRichtlinien.mdc#4` — **Commits (Pflicht):**
  Conventional Commit, Deutsch, imperativ, Suffix
  `[sql-index-suggestions]`, Subject ≤ 72 Zeichen.
- `.agents/rules/SqlToAiRichtlinien.mdc#5` — **AiNetLinter-Hinweis:**
  `RecreateBaseline` läuft automatisch; **Zero-Warning-Direktive**
  bleibt bindend.

## Risiken

- **Restrisiko: die Versions-Schwellenwert-Logik selbst könnte falsch
  kalibriert sein** — z. B. falls eine zukünftige SQL-Server-Version
  zwischen 2022 und 2025 (hypothetisch Hauptversion 16.x mit anderem
  DMV-Schema) existiert, oder falls `ServerVersion` bei bestimmten
  Verbindungsarten (z. B. Azure SQL Database, andere Treiber-
  Konfigurationen) ein unerwartetes Format liefert. **Mitigation:**
  Die vier Integrationstests gegen die reale Test-DB sind der
  empirische Beweis für den konkret vorhandenen Fall (SQL Server 2025
  RTM). Der Fallback bei nicht-parsebarer Version auf die
  konservativere 2019/2022-Syntax minimiert das Risiko für unbekannte
  zukünftige Instanzen (schlägt im schlimmsten Fall mit demselben
  bekannten Fehlerbild wie `step-006` fehl, nicht mit einem neuen,
  schwerer diagnostizierbaren Verhalten). Dieses Restrisiko ist nicht
  vollständig ausräumbar ohne eine dritte reale Testinstanz (z. B.
  echtes SQL Server 2019) — außerhalb der Mittel dieses Fixes, wie
  bereits in `step-002`/`step-003` für die 2019/2022-Testabdeckung
  generell festgestellt (TD-007, „grundsätzlich nicht behoben").
- **Integrationstest 1 (TD-006) — Verhalten nach dem Fix unklar:**
  Laut `step-006/step-result.md` ist Test 1 aktuell nicht vom
  neuen SQL-Fehler unterscheidbar (er schlägt mit derselben
  `SqlException` fehl wie 2–4, statt an der bekannten
  TD-006-Assertion). Nach diesem Fix sollte er entweder (a) wieder in
  den bekannten TD-006-Assertion-Zustand aus `step-005/step-review.md`
  zurückfallen (wahrscheinlichster Fall, da der SQL-Fehler behoben
  ist), oder (b) überraschend vollständig grün werden. Beides ist
  **kein neuer Fehler dieses Fixes** — der Coder muss im
  `step-result.md` dokumentieren, welcher der beiden Fälle eintritt,
  damit der Kritiker die Abgrenzung zu TD-006 (separates, bereits
  bekanntes Thema, `step-007`) nachvollziehen kann. Ein
  TD-006-Assertion-Fehlschlag bei Test 1 ist **kein** Blocker für
  diesen Fix.
- **AiNetLinter-Nichtdeterminismus-Anomalie (aus `step-006/step-result.md`
  „Beobachtungen") ist explizit nicht Teil dieses Fixes:** Falls sie
  beim `dotnet test`-Lauf dieses Fixes erneut auftritt (Violations in
  step-006/fix-01-fremden Dateien, variierende Baseline-Hashes), im
  `step-result.md` erneut dokumentieren (nicht selbst beheben,
  weiterhin dem Kritiker/Nutzer zur Bewertung vorlegen) — kein
  Rückfall in denselben Fehler wie zuvor, aber auch keine
  Scope-Erweiterung dieses Fixes.

## Bekannte Ausnahmen

- **Subject-Zeichenlänge:** der vorgeschlagene Commit-Subject ist ein
  Vorschlag, keine Wort-für-Wort-Vorgabe — Coder muss die finale
  Zeichenlänge (≤ 72) prüfen und ggf. kürzen.
- **`LoadSuggestionsAsync`-Methodenlänge (82 LOC):** vorbestehender
  Zustand seit `step-003`/`step-006`, durch diesen Fix nicht
  verändert (die Zeilenanzahl bleibt gleich, da `const string sql =
  """ ... """;` 1:1 durch `string sql = SelectSql(...);` ersetzt
  wird) — weiterhin kein Scope-Punkt.

## Notes

- **Wiederverwendete Strukturen — der Kern dieses Fixes:** Die
  Versionserkennung nutzt ausschließlich bereits vorhandene
  ADO.NET-/Projekt-Infrastruktur (`DbConnection.ServerVersion`,
  `FakeDbConnectionOptions.ServerVersion`, die bestehende
  `BuildService(DmvMockConnectionFactory)`-Überladung) — keine neue
  Abstraktion, kein neuer Service, kein neues Interface für
  „Versionserkennung" eingeführt. Das hält den Fix minimal-invasiv
  und vermeidet die im Skill beschriebene Gefahr paralleler,
  unabhängig entstandener ähnlicher Strukturen.
- **`DmvMockConnectionFactory`-Auslagerung ist ein Nebenprodukt, kein
  Selbstzweck:** Sie behebt den `MaxLineCount`-Verstoß, folgt aber
  auch dem bereits etablierten `TestSupport/`-Muster — sollte künftig
  ein weiterer Test (z. B. `step-007`/TD-006) DMV-Mocking brauchen,
  ist die Factory jetzt wiederverwendbar, ohne erneut kopiert werden
  zu müssen.
- **Abgrenzung zu step-007 (nicht Teil dieses Plans):** TD-006
  (Test-1-Erweiterung um den Graceful-Degradation-Pfad) bleibt ein
  separater, künftiger Step — auch wenn dieser Fix das Verhalten von
  Test 1 indirekt beeinflusst (siehe „Risiken"), ist eine gezielte
  Test-1-Assertion-Erweiterung nicht Gegenstand dieses Fixes.
