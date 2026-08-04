---
status: blocked
type: step-plan
task: sql-index-suggestions
step: 003
title: "EPIC-02 Integrationstest für sql_suggest_indexes gegen echte Test-DB"
epic: EPIC-02
estimated_risk: medium
step_type: single
items: []  # nur bei step_type: batch
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-05T08:00:00+02:00
related_to:
  - step-002/step-review.md  # CRITICAL-Fix CTE-Korrektur gegen Echtdatenbank zu validieren
  - step-002/fix-01/step-review.md  # expliziter Hinweis: "Eigentlicher Beweis ... kommt aus Integrationstest in step-003"
---

# Step 003: EPIC-02 Integrationstest für `sql_suggest_indexes` gegen echte Test-DB

## Bezug

- **Task:** `sql-index-suggestions`
- **Epic:** `EPIC-02` aus `roadmap.md` — serverweit kumulierte DMV-Index-Empfehlungen
  mit Graceful Degradation (Idee 2 aus `konzept.md`). Code, Doku und Unit-Tests sind
  bereits in `step-002` (approved nach `fix-01`) abgeschlossen; der Integrationstest
  gegen eine echte Test-DB ist der einzige verbleibende Konzept-§DoD-Punkt.
- **Konzept-Referenz:** `konzept.md` §DoD letzter Punkt für Idee 2:
  > Integrationstest gegen eine echte Test-DB in
  > `tests/SqlToAi.Tests/Integration/` (DMV-Verhalten lässt sich nicht sinnvoll
  > mocken).
- **Kritiker-Vorgabe:** `step-002/fix-01/step-review.md` Abschnitt „Logische
  Korrektheit":
  > Der *eigentliche* Beweis, dass die SQL-Query die DMVs korrekt abfragt, kommt
  > aus dem Integrationstest in `step-003` (Echtdatenbank).

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen des aktuellen Code- und Konfigurationsstands habe ich folgende
Strukturen vorgefunden, die für `step-003` wiederverwendet werden und diesen
Plan prägen:

- **Service bereits implementiert:** `src/SqlToAi/Database/IndexSuggestionService.cs`
  (312 Zeilen, `sealed`, `IndexSuggestionService : IIndexSuggestionService`).
  Service-Konstruktor: 5 Dependencies (`IDatabaseConnectionFactory`,
  `ISecurityGuard`, `IAccessLevelProvider`, `IOptions<SqlToAiOptions>`,
  `ILogger<IndexSuggestionService>`). SQL-Query ist eine CTE (`TopIndexes` →
  outer SELECT mit `sys.dm_db_missing_index_columns`-JOIN), Top-N wird auf
  `index_handle`-Ebene erzwungen — `fix-01` hat die CTE-Korrektur eingebracht
  (siehe `step-002/fix-01/step-review.md`, „Logische Korrektheit"-Block:
  CTE-Top-N-Semantik korrekt). Markdown-Output hat Header
  `# Missing Index Recommendations — {db}` + Restart-Hinweis-Block + Tabelle
  mit Headers `["Score", "Table", "Equality Columns", "Inequality Columns",
  "Include Columns", "Seeks", "Scans", "Last Seek"]`. Graceful Degradation
  via `IsViewServerStatePermissionError` (Error 300/297 + Keyword
  `VIEW SERVER STATE`).

- **Integrationstest-Fixture vorhanden:** `tests/SqlToAi.Tests/Integration/
  SqlServerFixture.cs` (89 Zeilen). Lädt `src/SqlToAi/appsettings.json` über
  `ConfigurationBuilder` (sucht in bis zu 12 Verzeichnis-Ebenen aufwärts);
  baut den kompletten DI-Graphen auf — `ConnectionFactory`, `SecurityGuard`,
  `AccessLevelProvider`, `ReadOnlyGuard`, `MetadataProvider`, `SchemaService`,
  `QueryExecutionService`, `QueryValidationService` plus Anonymizer-Suite.
  **Befund:** `IndexSuggestionService` ist im Fixture aktuell NICHT instanziert
  (kein Property, keine Konstruktion) — das ist die zentrale Lücke, die
  `step-003` schließen muss. Konsequenz: Fixture muss um genau eine Property
  + eine Konstruktor-Zeile erweitert werden (Pattern 1:1 zu `SchemaService`
  in Zeile 56).

- **Collection-Konvention vorhanden:** `SqlServerCollectionFixture` mit
  `[CollectionDefinition(Name, DisableParallelization = true)]` und
  `public const string Name = "SqlServer"` (Zeile 84–88). Alle bestehenden
  Integrationstest-Klassen nutzen `[Collection(SqlServerCollectionFixture.Name)]`
  + `[Trait("Category", "Integration")]` (siehe `SchemaServiceIntegrationTests.cs`
  Zeile 7–9, `AccessLevelProviderIntegrationTests.cs` Zeile 8–9,
  `QueryExecutionServiceIntegrationTests.cs` Zeile 12–13,
  `QueryValidationServiceIntegrationTests.cs` Zeile 9–10). `step-003` muss
  exakt diese Konvention übernehmen.

- **Test-DB-Konfiguration vorhanden:** `src/SqlToAi/appsettings.json` Zeile 12–19
  (Server: `%COMPUTERNAME%\\MSSQLSERVER2022`, User: `Agent`, Password: `Agent!`,
  IntegratedSecurity: false, ConnectTimeoutSeconds: 30) und Zeile 5–7
  (`Databases.ReadWrite: ["DemoDB"]`). Damit ist die Test-DB `DemoDB` mit
  `AccessLevel.ReadWrite` konfiguriert, und der `Agent`-Login sollte laut
  `architecture-spec.md` Zeile 168–169 (`GRANT VIEW SERVER STATE TO
  [SqlToAiUser]`) die für `sql_suggest_indexes` nötige Permission haben.
  Allerdings ist `appsettings.json` Zeile 13 ein Platzhalter
  (`%COMPUTERNAME%`), der zur Laufzeit von `Environment.ExpandEnvironmentVariables`
  aufgelöst werden muss — die `SqlServerFixture.LocateAppsettings`+`ConfigurationBuilder`
  machen das implizit (Microsoft.Extensions.Configuration expandiert Environment-Variablen
  in JSON-Werten per Default).

- **Skip-Pattern vorhanden:** `tests/SqlToAi.Tests/AiNetLinter/AiNetLinterTests.cs`
  Zeile 14–19 zeigt das `Assert.Skip` Pattern für xUnit v3:
  ```csharp
  if (!File.Exists(LinterExePath))
  {
      Assert.Skip("AiNetLinter.exe was not found at path: " + LinterExePath);
      return;
  }
  ```
  `Assert.Skip` ist die xUnit-v3-Variante (xUnit v2 hatte `Skip.IfNot` /
  `Skip.If`); das funktioniert an jeder Stelle, an der Assertions erlaubt sind
  (auch im Test-Konstruktor). Im Gegensatz zu AiNetLinterTests, wo der Skip
  *vor* dem Test steht, ist im Integrationstest-Kontext zu beachten: die
  bestehenden Integration-Tests haben **keine** Skip-Logik — wenn die
  Test-DB nicht erreichbar ist, schlägt bereits die `SqlServerFixture`-
  Konstruktion fehl und alle Tests der Collection brechen gleichzeitig ab.
  Das ist der etablierte Pattern. `step-003` braucht daher **keine** zusätzliche
  Skip-Logik im Test (der Fixture-Aufbau wirkt als impliziter Skip-Guard).
  Stattdessen: die Tests sind tolerant gegen beide Erfolgs-Varianten
  (siehe Test-Liste unten), weil `VIEW SERVER STATE` in der Standard-
  Konfiguration zwar vorhanden ist, das aber nicht hart garantiert ist
  (User-Setup-abhängig).

- **Test-Konstanten vorhanden:** `tests/SqlToAi.Tests/TestConstants.cs` Zeile 13:
  `public const string DatabaseName = "DemoDB";` — wird in allen
  Integration-Tests als `_db` verwendet.

- **Integration-Helper vorhanden:** `tests/SqlToAi.Tests/Integration/
  IntegrationAssertions.cs` Zeile 13–26: `IntegrationAssertions.FormatFailure`
  — gibt `Code: Message` zurück, wenn der `Result<T>` fehlgeschlagen ist, sonst
  `<success>`. Safe auf jedem `Result<T>` (vermeidet die `Result.Error`-Throw-
  Falle bei Erfolgs-Results). Wird in jeder Test-Klasse via
  `Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result))`
  verwendet.

- **Doku bereits synchron:** `step-002` hat den Doku-Sync vollständig
  umgesetzt: `docs/architecture-spec.md` §4 Nr. 16 (Zeile 310–312), §H mit
  `GRANT VIEW SERVER STATE` (Zeile 168–169, 175), `README.md` Zeile 14
  (Feature-Bullet), Zeile 28 (Tool-Count „16 Progressive Disclosure Schema
  Tools"), Zeile 105+111 (`VIEW SERVER STATE` im Permissions-Block). Der
  `step-002/fix-01`-Kritiker hat in „Logische Korrektheit" explizit
  bestätigt, dass die Doku nach `fix-01` konsistent bleibt (CTE-Korrektur
  ändert kein beobachtbares Verhalten). Damit ist die in der Roadmap
  ursprünglich als „step-003-Doku-Sync" markierte Lücke hinfällig — die
  Roadmap wurde entsprechend angepasst (EPIC-02-Restbedarf reduziert auf
  Integrationstest allein).

- **Linter-Baseline-Stand:** `tests/SqlToAi.Tests/AiNetLinter/rules/
  SqlToAi-baseline.json` enthält bereits Einträge für
  `src/SqlToAi/Database/IndexSuggestionService.cs` (Zeile 29),
  `src/SqlToAi/Database/IIndexSuggestionService.cs` (Zeile 21) und
  `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs` (Zeile 93)
  sowie `tests/SqlToAi.Tests/Integration/SqlServerFixture.cs` (Zeile 127).
  Wenn `step-003` die `SqlServerFixture.cs` ändert (neue Property), ändert
  sich deren Hash — die Datei ist aber bereits in der Baseline getrackt
  (Coder-Beobachtung aus `fix-01` Zeile 88: „Dateien, deren Hash in der
  Baseline steht, sind grandfathered; ihre vorhandenen Violations sind
  akzeptiert; Dateien, deren Hash nicht in der Baseline steht, müssen
  100% clean sein"). Die neue Datei
  `IndexSuggestionServiceIntegrationTests.cs` ist noch nicht in der Baseline
  → sie muss von Anfang an das `MaxLineCount`-Limit (500) einhalten, weil
  sie beim ersten `dotnet test`-Lauf als „untracked" gewertet wird und
  einen sauberen Lint-Stand liefern muss.

- **CTE-Korrektur nochmal verifizieren:** Der Planer liest im aktuellen
  `IndexSuggestionService.cs` Zeile 123–158 die finale SQL-Struktur nach:
  innere CTE `TopIndexes` mit `SELECT TOP (@Top)` auf `mig.index_handle`/
  `mid.statement`-Ebene, outer SELECT mit `ORDER BY ti.ImprovementScore DESC,
  ti.Statement, mic.column_id`. Das ist genau die `fix-01`-Form, die der
  Kritiker in `step-002/fix-01/step-review.md` als korrekt bestätigt hat.
  Der Integrationstest in `step-003` muss zeigen, dass diese CTE gegen
  reale DMV-Daten funktioniert.

## Intention

Nach diesem Step existiert in `tests/SqlToAi.Tests/Integration/` ein
Integrationstest, der den `IndexSuggestionService` gegen die reale
SQL-Server-Test-Instanz aus `src/SqlToAi/appsettings.json` (Server
`%COMPUTERNAME%\MSSQLSERVER2022`, Login `Agent`, Datenbank `DemoDB`)
ausführt. Der Test verifiziert (a) den Happy-Path mit echtem DMV-Zugriff
(Markdown mit `# Missing Index Recommendations`-Header + Restart-Hinweis +
Tabelle oder „No missing-index recommendations"-Notiz), (b) die Akzeptanz
der Parameter `table_name` / `top` / `min_score` ohne Crash, (c) die
Permission-Graceful-Degradation tolerant — falls der `Agent`-Login in der
konfigurierten Test-Umgebung wider Erwarten kein `VIEW SERVER STATE` hat,
muss der Service die strukturierte Permission-Notiz zurückgeben
(`IsSuccess = true`, `Value` enthält den `VIEW SERVER STATE`-Hinweis). Der
`SqlServerFixture` wird um genau eine Property + Konstruktor-Zeile für den
`IndexSuggestionService` erweitert, damit der Test im DI-Graphen an die
echte Test-DB andocken kann. Damit ist Konzept §DoD letzter Punkt für Idee 2
vollständig erfüllt, und EPIC-02 kann abgeschlossen werden.

## Konkrete Änderungen

### Datei 1: `tests/SqlToAi.Tests/Integration/SqlServerFixture.cs` (Änderung)

- **Was:** Zwei kleine Erweiterungen analog zum bestehenden `SchemaService`-
  Block (Zeile 56):
  1. Neues Property nach `public QueryValidationService QueryValidationService { get; }`:
     `public IndexSuggestionService IndexSuggestionService { get; }`
  2. Neue Konstruktor-Zeile nach `QueryValidationService = new QueryValidationService(...)`:
     `IndexSuggestionService = new IndexSuggestionService(ConnectionFactory, SecurityGuard, AccessLevelProvider, optionsWrapper, NullLogger<IndexSuggestionService>.Instance);`
  3. Neuer `using`-Import für `Microsoft.Extensions.Logging.Abstractions`
     (möglicherweise schon vorhanden via bestehendem `NullLogger<>`-
     Konstruktor — das hängt vom finalen Stand ab, der Coder prüft das
     vor dem Edit).
- **Warum:** Der `IndexSuggestionService` braucht eine reale
  `IDatabaseConnectionFactory`, `ISecurityGuard`, `IAccessLevelProvider`,
  `IOptions<SqlToAiOptions>` und `ILogger<IndexSuggestionService>`. Alle
  vier Dependencies sind im Fixture bereits instanziert (Zeile 48–61). Die
  Erweiterung ist mechanisch (5 Zeilen + 1 Property-Deklaration), folgt
  exakt dem `SchemaService`-Pattern (Zeile 56) und ändert keine bestehende
  Logik. Andere Integration-Tests sind nicht betroffen (sie holen sich
  jeweils nur die Property, die sie brauchen — `QueryExecutionService`,
  `QueryValidationService`, `SchemaService` bleiben unverändert).
- **Linter-Hinweis:** Die Datei ist in der Linter-Baseline getrackt
  (Hash vorhanden, grandfathered). Eine Änderung führt zu neuem Datei-Hash,
  bleibt aber grandfathered. Coder-Validierung: Datei bleibt unter
  `MaxLineCount` 500 (aktuell 89 Zeilen, +6 Zeilen → ~95 Zeilen, weit
  unter Limit).

### Datei 2: `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs` (NEU)

- **Was:** Neue `public sealed class IndexSuggestionServiceIntegrationTests`,
  Pattern 1:1 von `AccessLevelProviderIntegrationTests.cs` (kompaktes
  Format, nur die nötigsten 3–4 Tests). Konkrete Struktur:
  ```csharp
  #nullable enable

  namespace SqlToAi.Tests.Integration;

  [Trait("Category", "Integration")]
  [Collection(SqlServerCollectionFixture.Name)]
  public sealed class IndexSuggestionServiceIntegrationTests
  {
      private readonly SqlServerFixture _fx;
      private readonly string _db;

      public IndexSuggestionServiceIntegrationTests(SqlServerFixture fx)
      {
          _fx = fx;
          _db = TestConstants.DatabaseName;
      }

      // … 3-4 [Fact]-Methoden, siehe „Tests"-Abschnitt
  }
  ```
- **Warum:** Konzept §DoD letzter Punkt für Idee 2 verlangt explizit
  einen Integrationstest gegen eine echte Test-DB. Die existierenden
  Integration-Tests (`QueryExecutionServiceIntegrationTests`,
  `SchemaServiceIntegrationTests`, `AccessLevelProviderIntegrationTests`,
  `QueryValidationServiceIntegrationTests`) zeigen das exakte Pattern:
  `[Trait("Category", "Integration")]` + `[Collection(SqlServerCollectionFixture.Name)]`
  + Konstruktor-Injektion von `SqlServerFixture` + Verwendung von
  `TestConstants.DatabaseName` + `IntegrationAssertions.FormatFailure` für
  Result-Fehler-Output.
- **Linter-Hinweis:** Die Datei ist neu (kein Baseline-Eintrag) → muss
  beim ersten `dotnet test`-Lauf als „untracked" einen sauberen
  Lint-Stand liefern. Konkret: `MaxLineCount` 500 einhalten (komfortabel
  erreichbar bei 3–4 Tests mit jeweils ~15–25 Zeilen, geschätzt
  ~100–150 Zeilen Gesamtdatei). Kein manueller Eingriff in die Baseline
  nötig — `AiNetLinterTests.RecreateBaseline` läuft automatisch und nimmt
  die neue Datei mit auf.

### Datei 3: `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` (automatisch)

- **Was:** `AiNetLinterTests.RecreateBaseline` aktualisiert den Hash für
  `tests/SqlToAi.Tests/Integration/SqlServerFixture.cs` (Zeile 127) und
  fügt einen neuen Eintrag für `tests/SqlToAi.Tests/Integration/
  IndexSuggestionServiceIntegrationTests.cs` hinzu. **Kein manueller
  Eingriff** — die Datei wird vom Linter-Test im selben `dotnet test`-Lauf
  automatisch geschrieben. Konvention aus `step-002` Beobachtungen: nur
  geänderte/neue Dateien tauchen in der Doku auf, der Coder dokumentiert
  die Hashes in `step-003/step-result.md` für den Kritiker.
- **Warum:** Linter-Baseline-Mechanismus (siehe
  `step-002/fix-01/step-result.md` Beobachtung „Linter-Verhalten mit
  SqlToAi-baseline.json"). Coder-Workflow: `dotnet test` laufen lassen,
  Baseline aktualisiert sich automatisch, Commit.

## Tests

- [ ] `SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase` —
      Happy Path: ruft `_fx.IndexSuggestionService.SuggestIndexesAsync(_db, ct:
      TestContext.Current.CancellationToken)` auf, prüft `IsSuccess = true`,
      `Value` enthält den Header `# Missing Index Recommendations — DemoDB`
      und den Restart-Hinweis-Block (`"cumulative since the last SQL Server
      restart"`). Tolerant gegen beide Erfolgs-Varianten: entweder
      `No missing-index recommendations found in database 'DemoDB'.`
      (DMV leer, kein Workload auf der Test-DB) ODER Markdown-Tabelle
      mit `| Score |`-Spaltenheader (DMV hat Daten). Beide sind
      valide Tool-Outputs.
- [ ] `SuggestIndexesAsync_ShouldRespectTopParameter_AgainstRealDatabase` —
      Smoke-Test mit `top: 3`: ruft `SuggestIndexesAsync(_db, tableName: null,
      minScore: null, top: 3, ct: …)` auf, prüft `IsSuccess = true` und dass
      der Output die erwartete Markdown-Struktur hat (Header + Restart-Hinweis).
      Verifiziert, dass der `top`-Parameter gegen eine reale SQL-Server-
      Instanz ohne Crash akzeptiert wird (semantisch: das CTE-`TOP (@Top)`
      wird von SQL Server korrekt geparst und ausgeführt).
- [ ] `SuggestIndexesAsync_ShouldRespectTableNameFilter_AgainstRealDatabase` —
      Smoke-Test mit `tableName: "FakeProjects"`: ruft
      `SuggestIndexesAsync(_db, tableName: "FakeProjects", ct: …)` auf, prüft
      `IsSuccess = true` und dass der Output die erwartete Markdown-Struktur
      hat. Verifiziert, dass der `@TableName`-LIKE-Filterparameter
      (`mid.statement LIKE '%' + @TableName + '%'`) gegen die reale DB ohne
      Crash akzeptiert wird. Output kann leer sein (keine Missing-Index-
      Recommendations auf `FakeProjects` zum Test-Zeitpunkt) — das ist OK.
- [ ] `SuggestIndexesAsync_ShouldReturnPermissionNote_IfViewServerStateMissing_OtherwiseMarkdown`
      — Opportune Graceful-Degradation-Probe: ruft den Service mit der
      Test-DB-Default-Connection auf. Wenn der `Agent`-Login kein
      `VIEW SERVER STATE` hat, MUSS das Result `IsSuccess = true` sein
      und der Value den `VIEW SERVER STATE`-Hinweis enthalten (Graceful
      Degradation). Wenn der Login `VIEW SERVER STATE` hat, kommt
      entweder die Tabelle oder `No missing-index recommendations` zurück.
      Beide Varianten sind valide, der Test darf in beiden Fällen grün
      sein. Begründung: Test-DB-Setup kann variieren (manchmal hat der
      Login die Permission, manchmal nicht — abhängig vom initialen
      Setup der Test-Instanz), und der Service ist so gebaut, dass beide
      Fälle `IsSuccess = true` liefern. Test-Assert: `IsSuccess = true` +
      Output beginnt mit `# Missing Index Recommendations — DemoDB` +
      enthält entweder `VIEW SERVER STATE` ODER `cumulative since the
      last SQL Server restart` (Restart-Hinweis-Block ist in beiden
      Code-Pfaden vorhanden, siehe `IndexSuggestionService.RenderMarkdown`
      Zeile 214 und `RenderPermissionNote` Zeile 248).

**Hinweis zu Skip-Verhalten:** Die bestehenden Integration-Tests haben
**keine** explizite `Assert.Skip`-Logik — wenn die Test-DB nicht erreichbar
ist, schlägt die `SqlServerFixture`-Konstruktion fehl und alle Tests der
Collection brechen gleichzeitig ab. Das ist der etablierte Pattern (siehe
`QueryExecutionServiceIntegrationTests`, `SchemaServiceIntegrationTests`).
`step-003` folgt diesem Pattern — keine zusätzliche Skip-Logik. Der Vorteil
ist, dass eine fehlende Test-DB einen klaren Collection-Setup-Fehler
produziert, der vom Build-Tooling als „Infrastructure Issue" erkennbar ist,
nicht als „Test Failure". Der Nachteil (alle Tests in der Collection
schlagen fehl, nicht nur die von `step-003`) ist akzeptabel, weil es
bereits der etablierte Pattern ist und die Sammlung aller
Integration-Tests ohnehin nur Sinn ergibt, wenn die Test-DB verfügbar ist.

**Hinweis zu Workload-Generierung:** Eine kontrollierte Workload-
Generierung (z. B. ein `SELECT * FROM FakeProjects WHERE NonIndexedColumn = X`-
Pattern, um die DMV mit einem echten Missing-Index-Eintrag zu füllen) ist
**explizit NICHT** in diesem Step-Scope. Begründung: DMVs akkumulieren
seit dem letzten SQL-Server-Restart, eine Workload-Generierung wäre
nicht-deterministisch (anderer paralleler Workload, vorherige Test-Läufe,
Restart-Status) und anfällig für Race Conditions. Der hier gewählte
Smoke-Test-Ansatz beweist, dass die DMV-Query syntaktisch korrekt ist
und gegen einen realen SQL Server läuft — nicht, dass sie ein bestimmtes
Empfehlungs-Set liefert. Die Empfehlungs-Logik selbst (CTE-Top-N,
Score-Formel, Spalten-Gruppierung) ist bereits durch die 12 Unit-Tests in
`IndexSuggestionServiceTests.cs` + den `fix-01`-Zusatztest gegen synthetische
Daten abgesichert.

## Definition of Done

- [ ] Beide „Konkrete Änderungen" umgesetzt (Fixture-Erweiterung + neue
      Integrationstest-Datei)
- [ ] `dotnet build` aus `roadmap.md` Tech-Stack-Notiz grün (keine neuen
      Compiler-Warnungen, `TreatWarningsAsErrors=true`)
- [ ] `dotnet test` grün — bestehende Tests bleiben grün, neue
      Integration-Tests 1–4 grün **oder** (bei nicht verfügbarer
      Test-DB) die `SqlServerCollectionFixture`-Konstruktion schlägt
      mit einem klaren Infrastructure-Fehler fehl, der vom Build-Tooling
      erkennbar ist. Kein „still rot" durch unspezifizierte Test-Failures.
- [ ] `AiNetLinterTests.RecreateBaseline` läuft mit (automatisch, siehe
      `SqlToAiRichtlinien.mdc` §5) — `SqlToAi-baseline.json` automatisch
      aktualisiert mit neuem `SqlServerFixture.cs`-Hash und neuem Eintrag
      für `IndexSuggestionServiceIntegrationTests.cs`. Kein manuelles
      Hash-Rechnen.
- [ ] Neue Datei `IndexSuggestionServiceIntegrationTests.cs` ist unter
      `MaxLineCount` 500 (geschätzt ~100–150 Zeilen, weit unter Limit).
- [ ] Commit auf Branch `main` (lokal, kein Push), Conventional-Commit-
      Format, deutsch, imperativ, Subject ≤ 72 Zeichen, Suffix
      `[sql-index-suggestions]`
- [ ] `step-003/step-result.md` geschrieben mit Geänderte-Dateien-Liste,
      Commit-Hash, Build/Test-Output (inkl. Anzahl Tests vor/nach
      Integration-Tests), etwaigen Abweichungen vom Plan
- [ ] `status` in `step-plan.md` von `open` auf `done (pending audit)`
      gesetzt nach Abschluss der Coder-/Kritiker-Schleife

## Rules-Refs

- **`.agents/rules/SqlToAiRichtlinien.mdc` §4 (Doku-Sync-Pflicht):**
  **explizit entkräftet für diesen Step.** Begründung: `step-003` umfasst
  ausschließlich Test-Code (Fixture-Erweiterung + neue Integrationstest-
  Datei) ohne API-Wirkung. Die Service-Signatur
  (`SuggestIndexesAsync(IndexSuggestionArgs, CancellationToken)`) bleibt
  unverändert, der Tool-Output (Markdown-Format, Header, Restart-Hinweis,
  Graceful-Degradation-Notiz) bleibt unverändert, die Parameter
  (`database`/`table_name`/`min_score`/`top`) bleiben unverändert. Die
  bestehende Doku (`architecture-spec.md` §4 Nr. 16, §H mit
  `GRANT VIEW SERVER STATE`-Block, `README.md` Zeile 14, 28, 105, 111)
  ist konsistent zum Code und bedarf keiner Aktualisierung. Der
  `step-002/fix-01`-Kritiker hat in „Logische Korrektheit" explizit
  bestätigt, dass die CTE-Korrektur aus `fix-01` keine Doku-Wirkung
  hat. Eine Doku-Aktualisierung wäre redundant.
- **`.agents/rules/SqlToAiRichtlinien.mdc` §5 (Zero-Warning-Direktive,
  `TreatWarningsAsErrors`, AiNetLinter-Hinweis „kein manuelles
  Hash-Rechnen"):** eingehalten — Build grün mit 0 Warnungen erwartet;
  `RecreateBaseline` läuft automatisch.
- **`.agents/rules/AiNetLinter.mdc` Zeile 11 (sealed für konkrete
  Klassen):** eingehalten — neue Test-Klasse `sealed`, neuer
  `IndexSuggestionService`-Property im Fixture (kein neuer `sealed`-
  Konflikt).
- **`.agents/rules/AiNetLinter.mdc` Zeile 12 (`#nullable enable` am
  Dateianfang):** eingehalten — beide geänderten/erstellten Dateien
  mit `#nullable enable` (analog aller bestehenden Dateien in
  `tests/SqlToAi.Tests/Integration/`).
- **`.agents/rules/AiNetLinter.mdc` Zeile 13–14 (kein leeres `catch`;
  `Log + sichtbarer Fehler`):** nicht betroffen — Test-Code hat keine
  eigene `try/catch`-Logik; Service-internes `try/catch` bleibt
  unverändert.
- **`.agents/rules/AiNetLinter.mdc` Zeile 22 (`MaxMethodParameterCount`=4)
  und Zeile 27 (`MaxConstructorDependencies`=5):** nicht betroffen —
  keine Methoden- oder Konstruktor-Signatur-Änderung im
  Produktions-Code. Test-Methoden haben maximal 2 Parameter
  (`_db` + `top` oder `_db` + `tableName`), Konstruktor hat 1
  Parameter (`SqlServerFixture fx`).
- **`.agents/rules/AiNetLinter.mdc` Zeile 58
  (`EnforceNamespaceDirectoryMapping`):** eingehalten — neue Datei
  in `tests/SqlToAi.Tests/Integration/` → `namespace
  SqlToAi.Tests.Integration;` (analog `AccessLevelProviderIntegrationTests.cs`
  Zeile 6).
- **`.agents/rules/AiNetLinter.mdc` Zeile 67
  (`EnforceAsciiIdentifiers`):** eingehalten — keine Umlaute in
  Bezeichnern (`IndexSuggestionService`, `SuggestIndexesAsync` etc.).
- **`AiNetLinter.mdc` `MaxLineCount` = 500:** Die neue Datei
  `IndexSuggestionServiceIntegrationTests.cs` muss von Anfang an
  unter diesem Limit sein, weil sie noch nicht in der Baseline
  getrackt ist und beim ersten `dotnet test`-Lauf als „untracked"
  einen sauberen Lint-Stand liefern muss. Geschätzter Umfang:
  3–4 Tests × ~15–25 Zeilen + Klassen-Setup ~20 Zeilen = ~80–120
  Zeilen, weit unter dem Limit.

## Bekannte Ausnahmen

- **Test-DB-Verfügbarkeit als impliziter Skip-Guard.** Die Integration-Tests
  haben keine explizite `Assert.Skip`-Logik; wenn die Test-DB nicht
  erreichbar ist, schlägt die `SqlServerFixture`-Konstruktion fehl und
  alle Tests der Collection brechen ab. Begründung: das ist der
  etablierte Pattern aller bestehenden Integration-Tests
  (`QueryExecutionServiceIntegrationTests`, `SchemaServiceIntegrationTests`,
  `AccessLevelProviderIntegrationTests`, `QueryValidationServiceIntegrationTests`).
  Vorteil: ein klares Infrastructure-Signal bei DB-Problemen, kein
  „still rot". Nachteil: alle Collection-Tests schlagen gleichzeitig
  fehl, nicht nur die `IndexSuggestionServiceIntegrationTests`. Akzeptabel,
  weil ohnehin nur sinnvoll, wenn die Test-DB verfügbar ist.
- **Test 4 (Permission-Probe) ist opportune, nicht strikt.** Der Test
  akzeptiert beide Erfolgs-Varianten (echte Recommendations vs.
  Permission-Notiz), weil das `Agent`-Login-Setup in der Test-Umgebung
  variieren kann. Der Service-Code-Pfad `RenderPermissionNote` (Zeile
  243–252) und `RenderMarkdown` (Zeile 209–241) liefern beide
  `IsSuccess = true` mit dem `# Missing Index Recommendations — {db}`-
  Header und dem Restart-Hinweis-Block — der Test prüft auf diese
  beiden gemeinsamen Marker. Die Permission-Graceful-Degradation selbst
  ist bereits durch Unit-Test 9 in `IndexSuggestionServiceTests.cs`
  (`SuggestIndexesAsync_PermissionDeniedSqlException_ReturnsGracefulDegradationNote`)
  abgesichert — der Integrationstest ist eine opportunistische
  End-to-End-Bestätigung, kein Ersatz für den Unit-Test.
- **Keine Workload-Generierung im Test.** Bewusste Out-of-Scope-
  Entscheidung: keine kontrollierte Erzeugung von Missing-Index-
  Recommendations (z. B. durch `SELECT * FROM FakeProjects WHERE
  NonIndexedColumn = X`-Pattern-Executes), weil DMVs seit Server-
  Restart akkumulieren und eine Workload-Generierung nicht-
  deterministisch und anfällig für Race Conditions ist. Die
  Empfehlungs-Logik ist durch die 12 Unit-Tests +
  `fix-01`-Zusatztest in `IndexSuggestionServiceTests.cs` bereits
  vollständig abgesichert; der Integrationstest beweist die
  syntaktische Korrektheit der DMV-Query gegen einen realen SQL Server,
  nicht die inhaltliche Korrektheit des Empfehlungs-Sets.
- **Doku-Sync-Pflicht (`SqlToAiRichtlinien.mdc` §4) entfällt für
  `step-003`.** Begründung siehe `Rules-Refs` oben. Die bestehende
  Doku (von `step-002` umgesetzt, in `fix-01` als konsistent
  bestätigt) bleibt korrekt; eine Aktualisierung wäre redundant.
- **`SqlServerFixture`-Datei-Hash ändert sich.** Begründung: neue
  `IndexSuggestionService`-Property + Konstruktor-Zeile. Konsequenz:
  Linter-Baseline-Hash für `SqlServerFixture.cs` muss beim nächsten
  `RecreateBaseline`-Lauf aktualisiert werden. Das passiert automatisch
  durch `AiNetLinterTests.RecreateBaseline` (siehe `step-002/fix-01`-
  Coder-Beobachtung Zeile 88). Kein manueller Eingriff.

## Code-Skizze (optional)

```csharp
#nullable enable

namespace SqlToAi.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="SqlToAi.Database.IndexSuggestionService"/>
/// against a real SQL Server. Proves the DMV query (CTE-based, top-N per
/// index_handle) parses and executes against a live instance, and that
/// graceful degradation on missing VIEW SERVER STATE permission works
/// end-to-end (or that real recommendations are returned when the login
/// has the permission).
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class IndexSuggestionServiceIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _db;

    public IndexSuggestionServiceIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _db = TestConstants.DatabaseName;
    }

    [Fact]
    public async Task SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase()
    {
        var result = await _fx.IndexSuggestionService.SuggestIndexesAsync(
            _db, ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
        Assert.Contains("# Missing Index Recommendations — " + _db, result.Value);
        Assert.Contains("cumulative since the last SQL Server restart", result.Value);
        // Either "No missing-index recommendations found" OR Markdown table
        // with "| Score |" header — both are valid tool outputs.
        Assert.True(
            result.Value.Contains("No missing-index recommendations found", StringComparison.Ordinal)
            || result.Value.Contains("| Score |", StringComparison.Ordinal),
            "Expected either 'No recommendations' message or Markdown table with Score header.");
    }

    [Fact]
    public async Task SuggestIndexesAsync_ShouldRespectTopParameter_AgainstRealDatabase()
    {
        var result = await _fx.IndexSuggestionService.SuggestIndexesAsync(
            _db, tableName: null, minScore: null, top: 3,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.Contains("cumulative since the last SQL Server restart", result.Value);
    }

    [Fact]
    public async Task SuggestIndexesAsync_ShouldRespectTableNameFilter_AgainstRealDatabase()
    {
        var result = await _fx.IndexSuggestionService.SuggestIndexesAsync(
            _db, tableName: "FakeProjects", ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task SuggestIndexesAsync_ShouldReturnPermissionNote_IfViewServerStateMissing_OtherwiseMarkdown()
    {
        // Opportune probe: the configured 'Agent' login typically has VIEW SERVER STATE
        // (per architecture-spec.md §H). If it doesn't, the service must still return
        // a structured permission note (graceful degradation). Both outcomes are IsSuccess=true.
        var result = await _fx.IndexSuggestionService.SuggestIndexesAsync(
            _db, ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, IntegrationAssertions.FormatFailure(result));
        Assert.Contains("# Missing Index Recommendations — " + _db, result.Value);
        Assert.Contains("cumulative since the last SQL Server restart", result.Value);
        // Either real recommendations / no-recommendations message, or the permission note.
        Assert.True(
            result.Value.Contains("VIEW SERVER STATE", StringComparison.Ordinal)
            || result.Value.Contains("No missing-index recommendations", StringComparison.Ordinal)
            || result.Value.Contains("| Score |", StringComparison.Ordinal),
            "Expected permission note, 'no recommendations' message, or Markdown table.");
    }
}
```

```csharp
// tests/SqlToAi.Tests/Integration/SqlServerFixture.cs — Ergänzungen
// (nach Zeile 35, vor dem Konstruktor):

public IndexSuggestionService IndexSuggestionService { get; }

// Im Konstruktor nach Zeile 61 (QueryValidationService = new ...):
IndexSuggestionService = new IndexSuggestionService(
    ConnectionFactory,
    SecurityGuard,
    AccessLevelProvider,
    optionsWrapper,
    NullLogger<IndexSuggestionService>.Instance);
```

## Notes

- **Warum kein eigenes Epic für den Integrationstest:** Konzept §DoD
  letzter Punkt für Idee 2 ist explizit Bestandteil von EPIC-02 („Integrationstest
  gegen eine echte Test-DB in `tests/SqlToAi.Tests/Integration/`" als
  abschließender Konzept-DoD-Punkt für Idee 2). Der Test schließt EPIC-02
  ab; ein eigenes Epic wäre künstliche Aufspaltung.
- **Warum step-003 (Integrationstest) als separater Step, nicht im
  selben Schritt wie step-002:** Die in `step-002/step-plan.md` Zeile
  321 dokumentierte Planer-Notiz erklärt das im Detail: der Integrationstest
  braucht eine reale SQL-Server-Test-Instanz, die in `SqlServerFixture.cs`
  bereits vorgesehen ist; eine Test-DB-Suite in `dotnet test` würde
  eine optional konfigurierbare Connection voraussetzen, deren
  Verfügbarkeit nicht in jeder Build-Pipeline garantiert ist. Die
  Unit-Tests in `step-002` decken die Service-Logik vollständig ab;
  der Integrationstest validiert primär die SQL-Syntax und das reale
  DMV-Verhalten gegen einen laufenden Server. Der Schritt ist klein
  (eine Fixture-Erweiterung + eine neue Test-Datei mit 4 Tests),
  aber separat von `step-002`, um Build-Pipeline-Optionen (z. B.
  separate Test-Jobs mit/ohne DB-Verfügbarkeit) zu erleichtern.
- **Coder-Selbstprüfung vor Commit:** Nach Test-Implementation, vor
  Commit, mindestens manuell `dotnet build && dotnet test` durchführen.
  AiNetLinter-Baseline passt sich automatisch an; keine zusätzliche
  Aktion nötig. Wenn die Test-DB nicht verfügbar ist, ist die
  `SqlServerCollectionFixture`-Konstruktion der erwartete
  Failure-Punkt — das ist KEIN Test-Bug.
- **CTE-Beweis gegen Echtdatenbank:** Der hier geplante Integrationstest
  ist der finale Beweis, dass die CTE-Korrektur aus `fix-01` gegen
  einen realen SQL Server funktioniert. Der Unit-Test
  `SuggestIndexesAsync_MultipleHandlesWithDifferentColumnCounts_AllColumnsPerHandlePreserved`
  (Commit `bc488ec`, Datei `IndexSuggestionServiceTests.cs`) deckt die
  Top-N-Semantik pro `index_handle` synthetisch ab; der Integrationstest
  zeigt, dass die CTE von SQL Server syntaktisch akzeptiert und
  ausgeführt wird. Beide zusammen ergeben die vollständige
  Verifikations-Kette für die DMV-Query.
- **Workload-Generierung als bewusste Out-of-Scope-Entscheidung:** Falls
  ein penibler Leser fragt, warum der Integrationstest nicht
  kontrolliert eine fehlende Index-Empfehlung erzeugt (z. B. durch
  gezielte `SELECT *`-Patterns auf unindizierten Spalten): DMVs
  akkumulieren seit Server-Restart, ein kontrollierter Setup wäre
  race-condition-anfällig (anderer Workload auf der gleichen
  Test-Instanz) und nicht-deterministisch. Der hier gewählte
  Smoke-Test-Ansatz ist der pragmatisch korrekte Weg: beweist
  syntaktische Korrektheit und End-to-End-Ausführbarkeit gegen eine
  reale Instanz, ohne die Test-Pipeline durch Workload-Setup-
  Komplexität zu belasten. Die inhaltliche Korrektheit der
  Empfehlungs-Logik ist durch die 12 Unit-Tests +
  `fix-01`-Zusatztest vollständig abgesichert.
- **Verbleibender EPIC-02-Restbedarf nach step-003:** Nichts. Code,
  Doku, Unit-Tests und Integrationstest sind dann vollständig. Der
  Planer markiert EPIC-02 beim nächsten Aufruf (nach erfolgreichem
  `step-003`) als vollständig abgeschlossen (`[x]`) mit Hinweis auf
  den Integrationstest-Commit.
