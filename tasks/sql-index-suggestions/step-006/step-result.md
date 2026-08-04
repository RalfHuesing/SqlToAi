---
status: blocked
type: step-result
task: sql-index-suggestions
step: 006
epic: EPIC-04
step_type: single
coded_by: coder
coded_by_model: claude-sonnet-5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T15:30:00+02:00
code_commit_hash: 2011331
status_after: blocked
blocker_category: content
---

# Result Step 006: TD-004 — SQL-Server-2019/2022-kompatible Syntax in `IndexSuggestionService.LoadSuggestionsAsync`

## Zusammenfassung

Die Code-Änderung wurde exakt wie im Plan spezifiziert umgesetzt:
`migs.group_handle` → `migs.index_group_handle`, `CROSS APPLY
sys.dm_db_missing_index_columns(...)` → `INNER JOIN
sys.dm_db_missing_index_columns AS mic ON mic.index_handle =
ti.IndexHandle`, Kommentarblock auf SQL-Server-2019-Mindestversion
umgestellt, neuer Unit-Test
`SuggestIndexesAsync_GeneratedSql_UsesSqlServer2019CompatibleSyntax`
grün. **Der Step ist trotzdem `blocked`**: Die vom Plan als
Kernrisiko benannte, vom Nutzer akzeptierte Annahme — SQL Server 2025
führe die alten DMV-Spalten-/Objektnamen als
Rückwärtskompatibilitäts-Alias weiter — ist empirisch widerlegt. Alle
vier Integrationstests gegen die reale Test-DB schlagen mit einem
**neuen** `SqlException` fehl (`Ungültiger Spaltenname
"index_group_handle"`), nicht mit dem bekannten TD-006-Assertion-
Fehler. Das ist exakt das im Plan unter „Risiken" beschriebene
Blocker-Szenario; der Plan verlangt explizit, den Step in diesem Fall
nicht als erfolgreich zu melden, sondern die Entscheidung über das
weitere Vorgehen an den Nutzer zu eskalieren.

## Geänderte Dateien

- `src/SqlToAi/Database/IndexSuggestionService.cs` — SQL-Text der
  `Scored`-CTE und des finalen `SELECT` in `LoadSuggestionsAsync` auf
  2019/2022-Syntax umgestellt (siehe „Konkrete Änderungen" im Plan,
  1:1 umgesetzt); Kommentarblock über der SQL-Konstante ersetzt.
- `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs` — ein
  neuer Test (Code-Skizze aus dem Plan 1:1 übernommen), eingefügt
  zwischen Test 8 und Test 9. 12 bestehende Tests unverändert.

## Commit

- **Code-Commit-Hash:** `2011331`
- **Message:** `fix(dmv): 2019/2022-Syntax in DMV-Query
  [sql-index-suggestions]` (Body dokumentiert den Blocker, siehe
  `git log -1 2011331`).
- **Branch:** `main`
- **Push:** nein (lokal)
- **Begründung, warum trotz `blocked` committet wurde:** Die
  Code-Änderung selbst ist kein Defekt — sie setzt den Plan exakt um,
  Build ist grün, alle 13 Unit-Tests (12 bestehende + 1 neuer) sind
  grün. Der Fehlschlag liegt ausschließlich in der empirisch
  widerlegten, vom Nutzer akzeptierten Annahme zur
  SQL-Server-2025-Rückwärtskompatibilität — das ist ein sinnvoller,
  dokumentierter Zwischenstand (Coder-Skill „Commit-Verhalten bei
  blocked").

## Build-/Test-Output

```
dotnet build SqlToAi.slnx → grün (0 Warnungen, 0 Fehler)
dotnet test SqlToAi.slnx  → 522/527 grün, 5 Fehler
```

Fehler-Aufschlüsselung (gekürzt):

```
SqlToAi.Tests.Integration.IndexSuggestionServiceIntegrationTests
  .SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase
  .SuggestIndexesAsync_ShouldRespectTopParameter_AgainstRealDatabase
  .SuggestIndexesAsync_ShouldRespectTableNameFilter_AgainstRealDatabase
  .SuggestIndexesAsync_ShouldReturnPermissionNote_IfViewServerStateMissing_OtherwiseMarkdown
  → SQL-AI-0102: Query error: Ungültiger Spaltenname "index_group_handle".
  (SqlException, nicht die bekannte TD-006-Assertion-Verletzung — siehe „Abweichungen vom Plan")

SqlToAi.Tests.AiNetLinter.AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch
  → Exit-Code 1, 6 Violations (siehe „Beobachtungen")
```

Gezielter Vorab-Lauf (nur `IndexSuggestionServiceTests.cs`, Unit
only): 13/13 grün, inkl. des neuen SQL-Syntax-Tests.

## Abweichungen vom Plan

- **Kernrisiko des Plans ist eingetreten:** Der Plan (Abschnitt
  „Risiken") hat exakt dieses Szenario vorausgesagt — „Falls SQL
  Server 2025 `migs.index_group_handle` NICHT als Alias akzeptiert
  (harter SQL-Fehler 'Invalid column name') … schlagen die
  Integrationstests 2–4 mit einem neuen SQL-Fehler fehl … Step nicht
  als erfolgreich melden, sondern als `blocked` an den Nutzer
  eskalieren." Genau das ist eingetreten, und zwar bei allen vier
  Integrationstests (nicht nur 2–4), inkl. Test 1 — der zuvor
  (step-005) an einer TD-006-Assertion scheiterte, scheitert jetzt
  stattdessen an derselben `SqlException` wie die anderen drei. Test 1
  ist damit für diesen Lauf nicht mehr vom bekannten TD-006-Zustand
  unterscheidbar — sein Fehlschlag ist vollständig durch den neuen
  SQL-Fehler verdeckt/ersetzt.
- **Keine Fix-Versuche unternommen:** Gemäß Plan-Vorgabe „die
  Entscheidung, wie weiter zu verfahren ist (z. B. versionsabhängige
  Query-Konstruktion), liegt beim Nutzer, nicht beim Coder" wurde
  kein Reparaturversuch (z. B. Rückkehr zur 2025-Syntax, bedingte
  Query je nach Server-Version, Alias-Suche) unternommen. Das
  Versuchsbudget aus Schritt 4a wurde nicht verbraucht — das ist
  bewusst kein „normaler" Code-Defekt, sondern eine vorab im Plan als
  Eskalationsfall klassifizierte Situation.
- **`SqlToAi-baseline.json` nicht committet:** `dotnet test` hat
  `AiNetLinterTests.RecreateBaseline` automatisch mitlaufen lassen und
  die Baseline-Datei lokal verändert. Anders als in `step-005` wurde
  diese Änderung **nicht** in den Code-Commit übernommen, weil (a) die
  Validierungs-Testmethode (`RunLinterShouldBeCleanOrBaselineMatch`)
  in diesem Lauf ohnehin fehlschlägt (siehe „Beobachtungen") und (b)
  die Baseline-Hashes bei wiederholten Läufen auch für unveränderte
  Dateien variierten (siehe „Beobachtungen") — ein Commit dieser Datei
  hätte in diesem Zustand keinen belastbaren Wert. `git checkout --
  tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` wurde
  ausgeführt, um den Arbeitsbaum sauber zu halten.

## Beobachtungen

- **AiNetLinter-Anomalie, nicht auf diesen Step zurückführbar:** Mit
  den step-006-Änderungen im Arbeitsbaum meldet
  `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` 6
  Violations, davon 5 in Dateien, die dieser Step **nicht** anfasst
  (`PerformanceMeasurementService.cs` ×2 `MaxMethodParameterCount`,
  `ToolDispatcher.cs` `MaxConstructorDependencies`,
  `GlobMatcherTests.cs` `AvoidExcessiveMiddleMen`,
  `ToolDispatcherTests.cs` `MaxMethodParameterCount`). Auf dem
  unveränderten `main`-Stand (mehrfach mit sauberem `obj`/`bin`-Rebuild
  verifiziert) meldet derselbe Testlauf 0 Violations. Nur die
  `MaxLineCount`-Violation auf `IndexSuggestionServiceTests.cs` (506
  statt max. 500 Zeilen) ist inhaltlich diesem Step zuzurechnen (der
  neue Test hat die Datei über das Limit geschoben). Die übrigen 5
  Violations sind reproduzierbar an- und abwesend je nachdem, ob die
  step-006-Änderungen im Arbeitsbaum liegen oder nicht — obwohl sie
  Dateien betreffen, die dieser Step nicht verändert. Zusätzlich
  variierten bei mehreren `dotnet test`-Läufen mit identischem
  Quellcode die in `SqlToAi-baseline.json` geschriebenen SHA-256-
  Hashes für unveränderte Dateien. Das riecht nach einem
  Tooling-/Nichtdeterminismus-Problem in `AiNetLinter.exe` oder seiner
  Testintegration, nicht nach echten neuen Verstößen durch
  step-006-Code. Nicht selbst behoben (außerhalb Scope, keine
  Tech-Debt-Eintragung durch den Coder) — dem Kritiker zur Bewertung
  vorgelegt.
- **`MaxLineCount`-Verstoß auf dem Testfile ist real und step-006-
  verursacht:** 506 Zeilen (Limit 500) durch den neuen Test. Nicht
  behoben, da außerhalb des im Plan definierten Scopes (Plan nennt
  nur die `MaxMethodLineCount`-Vorbestehung von
  `LoadSuggestionsAsync` als bekannt, nicht diese neue
  `MaxLineCount`-Grenze) und da der Step ohnehin blockiert — eine
  Datei-Aufteilung wäre eine Scope-Erweiterung, die der nächste
  Planer-Schritt (oder der Nutzer) entscheiden sollte, sobald der
  SQL-Kernblocker aufgelöst ist.
- **Test 1 (TD-006) aktuell nicht mehr unterscheidbar:** Wie oben
  unter „Abweichungen" beschrieben, schlägt Test 1 jetzt am selben
  `SqlException` fehl wie Tests 2–4, nicht mehr an der TD-006-
  Assertion. Sobald der Kernblocker gelöst ist (z. B. durch Rückkehr
  zur bisherigen Syntax oder eine versionsabhängige Query), muss neu
  geprüft werden, ob Test 1 wieder in den bekannten TD-006-Zustand
  zurückfällt oder ob sich sein Verhalten geändert hat.

## Bekannte Unschärfen

- **Warum genau schlägt `index_group_handle` fehl?** Der SQL-Fehler
  („Ungültiger Spaltenname 'index_group_handle'") deutet darauf hin,
  dass die Test-Instanz (SQL Server 2025 RTM 17.0.1000.7 laut
  step-003) `sys.dm_db_missing_index_group_stats.index_group_handle`
  tatsächlich **nicht** mehr als Alias unterstützt — die
  Rückwärtskompatibilitäts-Annahme aus der Nutzer-Vorgabe war falsch.
  Nicht verifiziert wurde, ob z. B. `sys.dm_db_missing_index_columns`
  als klassischer View-Join ebenfalls fehlschlagen würde (der Fehler
  tritt schon bei der ersten CTE auf, bevor die zweite Änderung
  überhaupt zum Zuge kommt) — diese zweite Teilfrage bleibt für die
  Eskalation offen.
- **Kein Rollback durchgeführt:** Der Commit enthält die
  2019/2022-Syntax, obwohl sie gegen die aktuelle Test-DB
  nachweislich nicht funktioniert. Das ist eine bewusste Entscheidung
  (siehe „Abweichungen vom Plan"), keine Owner-Entscheidung — falls
  der Nutzer stattdessen einen Rollback auf die step-003-Syntax
  bevorzugt, ist das ein einfacher `git revert 2011331` oder ein neuer
  Step.
