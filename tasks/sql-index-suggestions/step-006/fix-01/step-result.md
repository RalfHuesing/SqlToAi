---
status: blocked
type: step-result
task: sql-index-suggestions
step: 006/fix-01
epic: EPIC-04
step_type: single
coded_by: coder
coded_by_model: claude-sonnet-5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-05T20:15:00+02:00
code_commit_hash: 75fb296
status_after: blocked
blocker_category: content
---

# Result Step 006/fix-01: Versionsabhängige DMV-Query-Konstruktion in `IndexSuggestionService.LoadSuggestionsAsync`

## Zusammenfassung

Die versionsabhängige Query-Konstruktion wurde exakt wie im Plan
spezifiziert umgesetzt: `Sql2019CompatibleQuery`/`Sql2025Query` als
vollständige Konstanten, `GetServerMajorVersion`/`SelectSql` als
Helper, Schwelle Hauptversion ≥ 17, `DmvMockConnectionFactory` nach
`TestSupport/` ausgelagert (mit neuem `serverVersion`-Parameter),
drei neue/angepasste Unit-Tests grün. **Der Step ist trotzdem
`blocked`**: Die vier Integrationstests gegen die reale Test-DB
schlagen weiterhin mit demselben `SqlException: Ungültiger
Spaltenname "index_group_handle"` fehl wie in `step-006`. Ursache
ist nicht ein Implementierungsfehler, sondern eine durch gezielte
Diagnose-Probes empirisch widerlegte Kernannahme des Plans:
`connection.ServerVersion` meldet auf der Test-Instanz `16.00.1190`
(bestätigt auch über `SERVERPROPERTY('ProductVersion')` =
`16.0.1190.2`, `@@VERSION` = "Microsoft SQL Server 2022 (RTM-GDR)
(KB5102334) - 16.0.1190.2"), **nicht** Hauptversion ≥ 17 — obwohl das
DMV-Schema dieser Instanz bereits die 2025-Spaltennamen
(`group_handle` statt `index_group_handle`) verwendet. Die
Hauptversion allein ist auf dieser konkreten Instanz kein
zuverlässiger Indikator für das tatsächliche DMV-Schema (vermutlich
wurde die Spalten-Umbenennung per GDR/CU in den 2022-Zweig
zurückportiert, ohne die Hauptversion anzuheben). Die im Plan als
"Restrisiko" benannte, aber als unwahrscheinlich eingestufte
Situation ("falls `ServerVersion` ... ein unerwartetes Format
liefert") ist damit eingetreten — nur nicht als Formatproblem,
sondern als Entkopplung von Versionsnummer und tatsächlichem Schema.

## Geänderte Dateien

- `src/SqlToAi/Database/IndexSuggestionService.cs` — `Sql2025MinMajorVersion`,
  `Sql2019CompatibleQuery`, `Sql2025Query`, `GetServerMajorVersion`,
  `SelectSql` neu; `LoadSuggestionsAsync` wählt `sql` jetzt über
  `SelectSql(GetServerMajorVersion(connection))`; Kommentarblock
  ersetzt. 1:1 wie im Plan spezifiziert.
- `tests/SqlToAi.Tests/TestSupport/DmvMockConnectionFactory.cs` (neu) —
  `DmvColumn`/`DmvRow`/`DmvMockConnectionFactory` aus
  `IndexSuggestionServiceTests.cs` ausgelagert, mit neuem optionalen
  `serverVersion`-Parameter (Default `"16.0"`, rückwärtskompatibel).
- `tests/SqlToAi.Tests/Database/IndexSuggestionServiceTests.cs` —
  „Fake DB plumbing"-Abschnitt entfernt (474 statt 506 Zeilen, unter
  `MaxLineCount 500` verifiziert); zwei neue Tests
  (`...UsesSqlServer2025SyntaxWhenServerReportsMajorVersion17`,
  `...FallsBackToSqlServer2019SyntaxWhenServerVersionUnparseable`
  als `[Theory]` mit `""`/`"not-a-version"`); Test 8
  (`...UsesSqlServer2019CompatibleSyntax`) unverändert grün.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` —
  automatisch durch `AiNetLinterTests.RecreateBaseline` aktualisiert.

## Commit

- **Code-Commit-Hash:** `75fb296`
- **Message:** `fix(dmv): versionsabhaengige Syntax in
  IndexSuggestionService [sql-index-suggestions]` (Body dokumentiert
  den Blocker, siehe `git log -1 75fb296`).
- **Branch:** `main`
- **Push:** nein (lokal)
- **Begründung, warum trotz `blocked` committet wurde:** Die Umsetzung
  selbst ist kein Defekt — sie setzt den Plan exakt um, Build ist
  grün, alle 526 Unit-Tests (inkl. der 3 neuen/angepassten) sind grün.
  Der Fehlschlag liegt ausschließlich in der empirisch widerlegten
  Kernannahme, dass `ServerVersion`-Hauptversion und tatsächliches
  DMV-Schema auf der konkreten Test-Instanz korrelieren — ein
  sinnvoller, dokumentierter Zwischenstand (Coder-Skill
  „Commit-Verhalten bei blocked").

## Build-/Test-Output

```
dotnet build SqlToAi.slnx → grün (0 Warnungen, 0 Fehler)
dotnet test SqlToAi.slnx  → 526/530 grün, 4 Fehler
```

Fehler-Aufschlüsselung (gekürzt, alle vier identisch):

```
SqlToAi.Tests.Integration.IndexSuggestionServiceIntegrationTests
  .SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase
  .SuggestIndexesAsync_ShouldRespectTopParameter_AgainstRealDatabase
  .SuggestIndexesAsync_ShouldRespectTableNameFilter_AgainstRealDatabase
  .SuggestIndexesAsync_ShouldReturnPermissionNote_IfViewServerStateMissing_OtherwiseMarkdown
  → SQL-AI-0102: Query error: Ungültiger Spaltenname "index_group_handle".
```

Alle 3 neuen/angepassten Unit-Tests aus diesem Fix sowie die 12
unveränderten Bestandstests in `IndexSuggestionServiceTests.cs`:
grün. `AiNetLinterTests.RecreateBaseline` lief automatisch mit.

## Diagnose (Vorab-Klassifikation, Schritt 4a)

Vor jedem Fix-Versuch wurde die Fehlersignatur geprüft: kein
"connection refused"/"command not found"/fehlendes SDK-Muster,
sondern ein SQL-Syntaxfehler identisch zu `step-006` → kein
offensichtlicher Infrastruktur-/Tooling-Blocker außerhalb des Scopes,
also normales Vorgehen mit Versuchsbudget 3 (`content`-Kategorie bei
Erschöpfung).

**Versuch 1 (Plan 1:1 umgesetzt):** `LoadSuggestionsAsync` wählt SQL
über `connection.ServerVersion`, Schwelle Hauptversion ≥ 17. Ergebnis:
alle 4 Integrationstests weiterhin rot, identischer Fehler wie
`step-006`.

Um die Ursache zu verifizieren, wurde eine temporäre Diagnose (nicht
committet, nur lokal ausgeführt und wieder gelöscht — kein Bestandteil
des Fixes) gegen die reale Test-Instanz ausgeführt:

- `connection.ServerVersion` → `"16.00.1190"`
- `SERVERPROPERTY('ProductVersion')` → `"16.0.1190.2"`,
  `ProductMajorVersion` → `16`, `Edition` → `"Developer Edition
  (64-bit)"`
- `@@VERSION` → `"Microsoft SQL Server 2022 (RTM-GDR) (KB5102334) -
  16.0.1190.2 (X64) ... Developer Edition (64-bit) on Windows 10 Pro
  10.0 ..."`
- `SELECT TOP (0) * FROM sys.dm_db_missing_index_group_stats` schlägt
  mit `VIEW SERVER PERFORMANCE STATE`-Berechtigungsfehler fehl (ein
  eigenständiges, von diesem Fix nicht adressiertes Berechtigungsthema
  der Metadaten-Abfrage selbst) — konnte die DMV-Spalten daher nicht
  direkt per Schema-Introspektion auflisten, aber der ursprüngliche
  `SqlException`-Fehlertext ("Ungültiger Spaltenname
  'index_group_handle'") aus dem eigentlichen `LoadSuggestionsAsync`-
  Aufruf (der über die `IndexSuggestionService`-eigene Berechtigung
  läuft, nicht über die Diagnose-Query) bestätigt bereits eindeutig:
  die Spalte `index_group_handle` existiert auf dieser Instanz nicht
  (mehr).

**Schlussfolgerung:** Die Instanz meldet sich sowohl über
`ServerVersion` als auch `SERVERPROPERTY`/`@@VERSION` konsistent als
Hauptversion 16 (SQL Server 2022, RTM-GDR-Build), hat aber bereits das
2025-DMV-Spaltenschema. Damit ist **jede** versionsnummernbasierte
Schwelle (unabhängig davon, welche konkrete ADO.NET-/T-SQL-Property
sie abfragt) auf dieser Instanz strukturell unfähig, die richtige
Query-Variante zu wählen — es handelt sich nicht um einen behebbaren
Implementierungsfehler innerhalb des im Plan vorgegebenen Mechanismus,
sondern um eine Invalidierung des Mechanismus selbst durch die reale
Umgebung. Ein "Versuch 2" mit einer anderen Versions-Property (z. B.
`SERVERPROPERTY('ProductMajorVersion')` statt `ServerVersion`) wurde
deshalb nicht mehr unternommen — es wurde bereits diagnostisch
bestätigt, dass beide dieselbe (falsche) Hauptversion liefern, ein
weiterer Versuch mit derselben Kategorie von Signal hätte keine neue
Information ergeben. Ein tragfähiger Fix würde eine **grundsätzlich
andere** Erkennungsmechanik erfordern (z. B. Schema-Introspektion auf
`sys.columns`/`sys.dm_db_missing_index_columns`, oder ein
Try/Catch-Fallback zwischen beiden Query-Varianten) — das ist ein
architektonischer Ansatzwechsel, den der Plan explizit ausgeschlossen
hat ("kein zusätzlicher DB-Roundtrip nötig", "Versionsauswahl ...
ohne DB-Roundtrip") und den der Coder nicht eigenmächtig anstelle des
vorgegebenen Mechanismus einführen darf (keine Scope-Erweiterung,
keine grundsätzlichen Umbauten). Deshalb: Eskalation an den Nutzer
statt Versuch 2/3 mit demselben, bereits als unzureichend erwiesenen
Mechanismus.

## Abweichungen vom Plan

- **Kernmechanismus des Plans funktioniert auf der realen
  Test-Instanz nicht:** Siehe „Diagnose" oben — `connection.ServerVersion`
  korreliert auf dieser Instanz nicht mit dem tatsächlichen
  DMV-Schema. Das ist keine Abweichung in der Umsetzung (die
  Umsetzung folgt dem Plan exakt), sondern eine Widerlegung der
  Plan-Prämisse durch die reale Umgebung — analog zu `step-006`, wo
  bereits die vorherige Prämisse (Rückwärtskompatibilitäts-Alias)
  widerlegt wurde.
- **Kein Versuch 2/3 mit alternativer Versions-Property:** Begründet
  in „Diagnose" — hätte dieselbe, bereits bestätigt falsche
  Information geliefert, kein sinnvoller weiterer Versuch innerhalb
  des vorgegebenen Mechanismus.
- **Temporäre Diagnose-Datei nicht committet:** Eine
  `TempServerVersionProbeTests.cs` wurde lokal angelegt, ausgeführt
  und wieder gelöscht — reine Verifikation, kein Bestandteil des
  Fixes, folgt „keine Scope-Erweiterung".

## Beobachtungen

- **AiNetLinter-Nichtdeterminismus-Anomalie erneut aufgetreten, wie
  im Plan vorab abgegrenzt:** Der `dotnet test`-Lauf hat
  `SqlToAi-baseline.json` für zahlreiche step-006/fix-01-fremde
  Dateien mit neuen Hashes überschrieben (u. a.
  `IIndexSuggestionService.cs`, `IOptimizationBenchmarkService.cs`,
  `QueryComparisonService.cs`, `AccessLevelProvider.cs`,
  `SecurityGuard.cs`, mehrere Test-Dateien) — dieselbe bereits in
  `step-006/step-result.md` gemeldete Anomalie. Wie im Fix-Plan
  („Bezug", Scope-Disziplin) explizit festgelegt: nicht Teil dieses
  Fix-Scopes, nicht selbst behoben, dem Kritiker/Nutzer zur Bewertung
  vorgelegt. Die Baseline-Datei wurde trotzdem mitcommittet (anders
  als in `step-006`), weil in diesem Lauf `RunLinterShouldBeCleanOrBaselineMatch`
  nicht separat als rot gemeldet wurde (nur die 4 Integrationstests
  schlugen fehl) — der Gesamtzustand war insofern konsistent genug für
  einen Commit.
- **`VIEW SERVER PERFORMANCE STATE`-Berechtigungsfehler bei direkter
  DMV-Introspektion (Diagnose, nicht Bestandteil des Fixes):** Bei dem
  Versuch, die tatsächlichen Spaltennamen von
  `sys.dm_db_missing_index_group_stats` per
  `SELECT TOP (0) * FROM ...` zu ermitteln, wurde nicht der erwartete
  `VIEW SERVER STATE`-Fehler (den `IsViewServerStatePermissionError`
  bereits behandelt), sondern `VIEW SERVER PERFORMANCE STATE`
  gemeldet — eine in SQL Server 2022+ eingeführte, feingranularere
  Berechtigung. Dies betraf nur die Diagnose-Query direkt gegen die
  DMV (das eigentliche `IndexSuggestionService` selbst schlägt vorher
  bereits am Spaltennamen fehl und erreicht diesen Berechtigungspfad
  nicht). Nicht im Scope dieses Fixes, aber möglicherweise relevant
  für eine künftige, tiefere Diagnose oder für TD-006 — dem
  Kritiker zur Bewertung vorgelegt, kein eigener Tech-Debt-Eintrag.

## Bekannte Unschärfen

- **Warum hat diese konkrete Instanz Hauptversion 16 aber
  2025-Schema?** Nicht abschließend geklärt — plausibelste Erklärung
  (GDR/CU-Backport der DMV-Spaltenumbenennung ohne
  Hauptversions-Anhebung) ist eine Vermutung, keine verifizierte
  Microsoft-Dokumentation. Für die Entscheidung über den nächsten
  Schritt relevant: falls dies ein Artefakt der lokalen Testumgebung
  ist (z. B. eine inkonsistent gepatchte Entwickler-Instanz) und nicht
  repräsentativ für reale SQL-Server-2022-Installationen, könnte eine
  robustere Lösung (Schema-Introspektion statt Versionsnummer) trotz
  des zusätzlichen Roundtrips die einzige praktikable Option sein.
- **Test 1 (TD-006) weiterhin verdeckt:** Wie in `step-006` bleibt
  Test 1 durch denselben `SqlException` wie Tests 2–4 verdeckt, fällt
  also nicht in seinen bekannten TD-006-Assertion-Zustand zurück —
  erwartungsgemäß, da der Kernblocker weiterhin besteht.

## Falls Status `blocked`

**Blocker-Art:** `content`

**Blockiert weil:** Die vom Plan vorgegebene Erkennungsmechanik
(`connection.ServerVersion`-Hauptversion als Signal für das
DMV-Schema) ist auf der realen Test-Instanz nachweislich unzuverlässig
— die Instanz meldet Hauptversion 16 (SQL Server 2022, RTM-GDR
16.0.1190.2, bestätigt über drei unabhängige Wege:
`ServerVersion`, `SERVERPROPERTY('ProductVersion'/'ProductMajorVersion')`,
`@@VERSION`), verwendet aber bereits das 2025-Spaltenschema
(`group_handle` statt `index_group_handle`). Jede reine
Versionsnummern-Schwelle scheitert daran strukturell, unabhängig von
der konkreten Property oder dem Schwellenwert.

**Brauche von Nutzer:** Eine Entscheidung über den tatsächlichen
Erkennungsmechanismus, da die im Plan ausgeschlossene Option (ein
zusätzlicher DB-Roundtrip zur Schema-Introspektion, oder ein
Try/Catch-Fallback zwischen beiden Query-Varianten bei
`SqlException`) jetzt die einzige erkennbare Alternative ist, die
gegen diese konkrete Instanz tatsächlich funktionieren würde. Fragen
dafür: (a) Ist die Test-Instanz repräsentativ (Backport-Verhalten auf
realen SQL-Server-2022-Installationen zu erwarten) oder ein Artefakt
der lokalen Umgebung? (b) Bevorzugte Mechanik, falls ein Umbau nötig
ist — Schema-Introspektion vor der Hauptquery, oder
Try/Catch-Fallback (2025-Syntax zuerst versuchen, bei
"Ungültiger Spaltenname" auf 2019/2022 zurückfallen)? (c) Bleibt die
Versionsnummer-Schwelle als zusätzliches Signal erhalten (z. B. als
schnellerer Pfad für eindeutige Fälle), oder wird sie komplett
ersetzt?

**Bisher erreicht:** Der komplette, im Plan spezifizierte Code
(versionsbasierte Query-Auswahl, Test-Auslagerung,
MaxLineCount-Fix, 3 neue/angepasste Unit-Tests) ist implementiert,
committet und alle Unit-Tests sind grün — nur die vier
Integrationstests bleiben rot, weil die Plan-Prämisse zur
Versions-Schema-Korrelation auf der konkreten Instanz nicht zutrifft.
Offen: die eigentliche Schema-Erkennung, die auch auf dieser Instanz
funktioniert.
