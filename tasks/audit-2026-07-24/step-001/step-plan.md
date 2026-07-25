---
status: done
type: step-plan
task: audit-2026-07-24
step: 001
title: "Punkt 12 — Wildcard-Tests für GlobMatcher und SecurityGuard ergänzen"
created_by: planer
created_at: 2026-07-25T18:30:00+02:00
related_to:
  - tasks/audit-2026-07-24/04-tests-doku-konsistenz.md (Teil A, Severity Mittel)
  - tasks/audit-2026-07-24/00-summary.md (Punkt 12)
---

# Step 001: Punkt 12 — Wildcard-Tests für GlobMatcher und SecurityGuard ergänzen

## Bezug

- **Task:** `audit-2026-07-24`
- **Quelle:** `04-tests-doku-konsistenz.md` Teil A, „GlobPatternMatcher hat keine eigene Testklasse" (Severity Mittel) und „SecurityGuard — keine Tests für `?`-Wildcard und Regex-Sonderzeichen in Datenbanknamen" (Severity Mittel)
- **Phase / Priorität:** Phase 3 — Doku & Konfigurationshygiene, Punkt 12

## Bewertung der Aufgaben-Doku

Bevor ich Steps plane, habe ich die in `00-summary.md` als ✅ markierten Items gegen den aktuellen `main`-Stand verifiziert (Commit-Hashes aus `git --no-pager log --oneline -20`). Die folgenden 11 Audit-Punkte sind bereits committet und werden im Planer-Step **nicht** erneut geplant:

| Punkt | Commit(s) | Verifikation |
|---|---|---|
| 1. Alias-Leak bei Anonymisierung/Tokenisierung | `102efbb` | bestätigt — `QueryExecutionService.GetColumnOrigins`/`AnonymizationColumnContext` mit `BaseTableName`/`BaseColumnName`/`BaseSchemaName` ist aktiv, Tests in `QueryExecutionServiceSchemaScopeTests.cs` decken es ab |
| 2. `sp_executesql`+`COMMIT` Guard-Bypass | `a41c413`, `03e6eac` | bestätigt — `ReadOnlyGuard.MutatingKeywordsRegex` enthält jetzt explizit `sp_executesql`, `QueryValidationService` ruft denselben Guard defensiv auf |
| 3. Rohe Fehlermeldung an KI filtern | `24e43f5` | bestätigt — `QueryExecutionService` generalisiert Fehlermeldung bei aktiver Anonymisierung; Klartext-Query im Log bleibt bewusst (`35d090b`) |
| 4. `QueryValidationService` Unit-Tests | `f86a1a1` | bestätigt — `tests/SqlToAi.Tests/Database/QueryValidationServiceTests.cs` vorhanden |
| 5. `sql_validate_query` Guard nachrüsten | `03e6eac` | bestätigt (gleiche Commit wie 2) |
| 6. Schema-blindes Ausschluss-/Regel-Matching | `918a919` | bestätigt — `AnonymizationRuleProvider` mit `SchemaPattern`-Spalte, Pareto-Vergleich; SQL-Skripte erweitert |
| 7. Regel-Präzedenz-Scoring | `314266e` | bestätigt — `FindMostSpecificMatch` mit Pareto-Dominanz-Vergleich statt gewichteter Summe; Dokumentation in `AnonymizationRuleProvider.cs:160-187` ausführlich begründet |
| 8. `AccessLevelProvider` numerische Tests | `319d0fe` | bestätigt — `AccessLevelProviderTests` deckt numerische Werte, Einzelspalten-Fallback und „keine Zeile"-Fall ab |
| 17. Gemeinsamer Test-Fake-Baustein | `381f022` | bestätigt — `tests/SqlToAi.Tests/TestSupport/{FakeDbConnection,FakeDbCommand,FakeDbDataReader,FakeDbParameter,FakeDbParameterCollection,FakeDbTransaction}.cs` vorhanden |
| 9. Doku `*Id`-Beispiel | `34ac806` | bestätigt — Warnhinweis in `README.md` und `docs/mcp-specification.md` (Abschnitt D) |
| 10. Totes Config-Paar entfernen | `320a17d` | bestätigt — `EnforceSafetyCheck`/`SafetyCheckSql` aus `SqlToAiOptions` entfernt, Migrationslogik angepasst |
| 11. Fehlercodes `0105`/`0106` implementieren | `e11876e` | bestätigt — `SqlToAiErrorMapper` mappt `SqlException`/`TimeoutException` gezielt auf `InfrastructureError`/`Timeout` |

**Sonderfall Punkt 22 (Commits `ee2e1e2` und `bcef6a9`):** Commit `bcef6a9` hat `src/SqlToAi/Anonymization/GlobPatternMatcher.cs` entfernt (die Klasse war durch den vorangegangenen Refactor in `ee2e1e2` unbenutzt geworden) und `README.md`/`docs/mcp-specification.md` synchronisiert. **Aber** der konsolidierende Teil von Punkt 22 — `SecurityGuard.MatchesPattern` (`src/SqlToAi/Security/SecurityGuard.cs:60-80`) dupliziert weiterhin denselben Glob→Regex-Algorithmus. Es bleibt also ein kleiner Rest-Step, der in `step-008` geplant ist.

Daraus folgt die finale Step-Reihenfolge: **001, 002, 003, 004, 005, 006, 007, 008** (= Punkte 12, 13, 14+15+16 geclustert, 18, 19, 20, 21, 22-Rest).

## Tech-Stack-Notiz (für alle Steps dieses Tasks)

- **Sprache:** C# 14, .NET 10
- **Test-Framework:** xUnit v3 (Test-Kategorien `Unit`, `Integration` — Integration manuell/separat)
- **DB-Zugriff:** `Microsoft.Data.SqlClient`, Dapper
- **JSON:** `System.Text.Json`
- **IDE:** Visual Studio 2026 mit `.slnx`-Format
- **Build-Command:** `dotnet build` (Solution-Root `C:\Daten\Entwicklung\Ralf\SqlToAi\SqlToAi.slnx`)
- **Test-Command:** `dotnet test --filter "Category!=Integration"`
- **Linter:** AiNetLinter (custom, strikt — `sealed`, Methodenlänge, keine Hardcodes etc.) — Konventionen in `.agents/rules/`
- **Commit-Konvention:** Conventional Commits, deutsche Imperativ-Form
- **Bekannte Baseline-Ausnahmen** (vorbestehend, **nicht** Teil dieses Tasks — Coder und Auditer dürfen NICHT meckern):
  - `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` (AiNetLinter-Baseline)
  - `QueryExecutionServiceIntegrationTests.ExecuteQueryAsync_ShouldRespectDatabaseExclusions_AgainstRealTable` (Integrationssuite, fehlende `CREATE TABLE`-Rechte des Demo-Logins in `DemoDB`)

## Intention

Der `LikePatternMatcher` (für SQL-`LIKE`-Muster in den Anonymisierungsregeln) hat bereits eine dedizierte Testklasse (`LikePatternMatcherTests.cs`, 10 Theory-Fälle inkl. `?`-Wildcard, leere Pattern, Spezifitäts-Score). Die äquivalenten `?`-/Regex-Sonderzeichen-/Timeout-Pfade für die **Glob-`-*`/`?`-Wildcard**-Matcher sind ungetestet: `SecurityGuard.MatchesPattern` (`src/SqlToAi/Security/SecurityGuard.cs:60-80`) — die Wildcard-Logik hinter `Databases.Allowed`/`Blocked`/`SqlServer.ExcludedDatabases` und damit direkt für die Datenbank-Whitelist zuständig — hat keine dedizierte Testklasse; `SecurityGuardTests.cs` testet nur exakte Treffer und `*`-Wildcards.

Ziel: Eine neue `SecurityGuardTests`-Theorie-Reihe analog zu `LikePatternMatcherTests`, die `?`, Regex-Sonderzeichen (`.`, `+`, `(`) im Datenbanknamen, leere Pattern/Text und den Timeout-Fallback abdeckt. Da `GlobPatternMatcher` seit Commit `bcef6a9` gelöscht ist, konzentriert sich dieser Step vollständig auf `SecurityGuard`; eine dedizierte `GlobMatcherTests`-Klasse wird im `step-008`-Refactor entstehen, sobald der Matcher als gemeinsames Utility extrahiert ist (dort kommen dann GlobMatcher-spezifische Tests dazu — siehe `step-008`).

## Konkrete Änderungen

### Datei 1: `tests/SqlToAi.Tests/Security/SecurityGuardTests.cs`

- **Was:** Eine neue `[Theory]`-Methode `MatchesPattern_ShouldEvaluateGlobWildcardsCaseInsensitively(string text, string pattern, bool expected)` und `MatchesPattern_ShouldReturnFalse_OnTimeoutOrEmptyInput(string text, string pattern)` ergänzen, die intern die (per `InternalsVisibleTo` zugängliche) `SecurityGuard` Wildcard-Logik gegen die dokumentierten Glob-Wildcards testen.
- **Warum:** Aktuell deckt `SecurityGuardTests` nur die Hoch-Ebene `IsDatabaseAllowed`-Entscheidung ab; die zugrunde liegende Wildcard-Mechanik (`MatchesPattern`) ist Black-Box und ein Regex-Escaping-Bug (z. B. ein DB-Name mit `.` wie `MyServer.1` matcht mit `Demo_*` falsch, weil `.` nicht escaped ist) würde unentdeckt bleiben.
- **Konkrete Test-Cases (mindestens):**
  - `("Demo_App", "Demo_?", true)` — Single-Char-Wildcard matcht genau ein Zeichen
  - `("Demo_App", "Demo_??", false)` — `?` matched **ein** Zeichen, nicht zwei
  - `("MyServer.1", "MyServer.1", true)` — exakter Treffer inkl. Regex-Sonderzeichen
  - `("MyServer.1", "MyServer?", true)` — `?` ersetzt das `.`
  - `("MyServer.1", "MyServer.1*", true)` — `*` nach Sonderzeichen
  - `("MyServerX1", "MyServer.1", false)` — `.` als Literal, nicht als Regex-Metazeichen (Escaping-Edge-Case)
  - `("MyServer.1", "", false)` — leeres Pattern
  - `("", "Demo_*", false)` — leerer Text
  - Case-Insensitivity: `("demo_app", "DEMO_?", true)`
  - Negativ-Vergleich zu `*`: `("Demo_App", "Demo_App?", false)` vs. `("Demo_App", "Demo_App*", true)`

### Datei 2: `src/SqlToAi/Security/SecurityGuard.cs`

- **Was:** `private static bool MatchesPattern(string text, string pattern)` per `[InternalsVisibleTo("SqlToAi.Tests")]`-Mechanismus testbar machen, falls noch nicht geschehen.
- **Warum:** Direkter Reflection-Zugriff auf `private static` ist möglich, aber das Assembly-`InternalsVisibleTo`-Attribut auf der Hauptassembly ist die saubere, im Projekt bereits etablierte Variante. Falls das Attribut fehlt: ergänzen in `src/SqlToAi/Properties/AssemblyInfo.cs` (oder in `SqlToAi.csproj` via `<ItemGroup><InternalsVisibleTo Include="SqlToAi.Tests" /></ItemGroup>`).
- **Prüfen:** Existiert `InternalsVisibleTo` für `SqlToAi.Tests` schon? Falls ja, kann der Test direkt auf `MatchesPattern` zugreifen. Falls nein, vor dem Test-Commit Attribut hinzufügen.

## Tests

- [ ] `MatchesPattern_ShouldEvaluateGlobWildcardsCaseInsensitively` — Theory über 8-10 Inline-Daten (siehe oben)
- [ ] `MatchesPattern_ShouldReturnFalse_OnTimeoutOrEmptyInput` — Theory mit leerem Text, leerem Pattern, beidem leer
- [ ] Bestehende `SecurityGuardTests` (3 Facts) bleiben grün und unverändert
- [ ] `dotnet build SqlToAi.slnx` 0 Warnungen, 0 Fehler
- [ ] `dotnet test --filter "Category!=Integration"` grün (Bekannte Baseline-Ausnahme `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` bleibt; in `SqlToAi-baseline.json` muss **kein** neuer Hash für `SecurityGuard.cs` oder `SecurityGuardTests.cs` ergänzt werden, sofern sich die Zeilenzahl nicht ändert — bei Hinzufügen einer `[Theory]`-Methode in der Test-Datei **muss** der Hash in `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` aktualisiert werden)

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command grün (0 Warnings, 0 Errors)
- [ ] Test-Command grün (Ausnahmen siehe oben)
- [ ] Commit auf aktuellem Branch (`test(security): ergänze Glob-Wildcard-Tests für SecurityGuard.MatchesPattern`)
- [ ] `step-001/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „xUnit v3 Tests: Pflicht für alle funktionalen Änderungen, Sicherheitsüberprüfungen" (Anonymisierungs-/Sicherheitsrelevanz)
- `.agents/rules/AiNetLinter.mdc#general/EnforceSealedClasses` — Test-Klassen `sealed` (bereits eingehalten in `SecurityGuardTests`)
- `.agents/rules/AiNetLinter.mdc#test-coverage/EnableTestSentinel` — `// @covers SqlToAi.Security.SecurityGuard` (bereits in Zeile 9 vorhanden)

## Bekannte Ausnahmen

- `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` — vorbestehende Baseline-Ausnahme, **nicht** Teil dieses Tasks. Falls dieser Test wegen neu hinzugefügter Zeilen in `SecurityGuardTests.cs` fehlschlägt: Hash in `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` (Key `tests/SqlToAi.Tests/Security/SecurityGuardTests.cs`) aktualisieren.

## Code-Skizze (optional)

```csharp
// In tests/SqlToAi.Tests/Security/SecurityGuardTests.cs (Ergänzung, kein Ersatz)

[Theory]
[InlineData("Demo_App", "Demo_?", true)]
[InlineData("Demo_App", "Demo_??", false)]
[InlineData("MyServer.1", "MyServer.1", true)]
[InlineData("MyServer.1", "MyServer?", true)]
[InlineData("MyServer.1", "MyServer.1*", true)]
[InlineData("MyServerX1", "MyServer.1", false)] // '.' must be escaped
[InlineData("demo_app", "DEMO_?", true)]
[InlineData("Demo_App", "Demo_App?", false)]
[InlineData("Demo_App", "Demo_App*", true)]
public void MatchesPattern_ShouldEvaluateGlobWildcardsCaseInsensitively(string text, string pattern, bool expected)
{
    // Reflection-Zugriff auf private static MatchesPattern, oder
    // InternalsVisibleTo("SqlToAi.Tests") + direkter Aufruf
    var actual = InvokeMatchesPattern(text, pattern);
    Assert.Equal(expected, actual);
}

[Theory]
[InlineData("", "Demo_*")]
[InlineData("Demo_App", "")]
[InlineData("", "")]
public void MatchesPattern_ShouldReturnFalse_OnTimeoutOrEmptyInput(string text, string pattern)
{
    Assert.False(InvokeMatchesPattern(text, pattern));
}
```

## Notes

- **Wichtig für den Auditer:** Der ursprüngliche `04-tests-doku-konsistenz.md`-Befund erwähnt auch eine dedizierte `GlobPatternMatcherTests`-Klasse. **Diese Klasse wird hier bewusst nicht angelegt**, weil `GlobPatternMatcher.cs` seit Commit `bcef6a9` nicht mehr existiert. GlobMatcher-Tests entstehen in `step-008`, sobald der Matcher als gemeinsames Utility (`SqlToAi.Domain.GlobMatcher`) extrahiert ist.
- **Reflection vs. InternalsVisibleTo:** Falls `InternalsVisibleTo` nicht existieren sollte, Reflection (`typeof(SecurityGuard).GetMethod("MatchesPattern", BindingFlags.NonPublic | BindingFlags.Static)`) ist die pragmatische Variante und vermeidet Hauptassembly-Änderungen. Empfehlung: Variante mit `InternalsVisibleTo` wählen, da das Projekt bereits ähnliche Patterns nutzt (z. B. `internal` Klassen wie `SqlMultiStatementDetector`, `SqlLiteralScanner`).
- **Linter-Baseline:** Diese Test-Datei hat aktuell einen Eintrag in `SqlToAi-baseline.json` (siehe `SqlToAi-baseline.json` Stand Commit `bcef6a9`). Bei Hinzufügen der neuen `[Theory]`-Methoden MUSS der Hash-Wert neu berechnet und die Baseline aktualisiert werden. Das ist eine reine Hash-Aktualisierung, kein semantischer Eingriff.
- **Kein Code-Change am Produktionscode außer ggf. `InternalsVisibleTo`:** Die Glob→Regex-Logik in `SecurityGuard.MatchesPattern` ist nicht fehlerhaft — sie funktioniert. Dieser Step fügt nur Tests hinzu.
