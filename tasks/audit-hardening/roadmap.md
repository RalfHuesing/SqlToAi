---
status: active  # active | done
task: audit-hardening
derived_from: konzept.md
created_at: 2026-08-04T08:00:00+02:00
last_updated: 2026-08-04T09:00:00+02:00
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
---

# Roadmap: audit-hardening

Grober Anker, kein Detailplan — Detail-Steps entstehen erst JIT im
Step-Modus des Planers, siehe `../../.agents/Agent-Scaffolding/dev-loop/drift-loop/spec.md` §7.2. Diese Datei wird
laufend angepasst (Epics abgehakt, ergänzt, umformuliert oder als
obsolet markiert) — kein starres Vorab-Dokument.

## Tech-Stack-Notiz

- **Build-Command:** `dotnet build` (Solution: `SqlToAi.slnx`, VS-2026-Format; Projekte: `src/SqlToAi/SqlToAi.csproj` (Exe, .NET 10/C# 14, `TreatWarningsAsErrors=true`), `tests/SqlToAi.Tests/SqlToAi.Tests.csproj`)
- **Test-Command:** `dotnet test` (xUnit v3; enthält u. a. `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` und `AiNetLinterTests.RecreateBaseline` — Letzterer aktualisiert `SqlToAi-baseline.json` automatisch bei jedem Lauf, niemals manuell Hashes eintragen; Integrationstests laufen ggf. unter eigener Kategorie, siehe `--filter "Category!=Integration"` in `SqlToAiRichtlinien.mdc`)
- **Lint-Command:** Kein separates CLI-Kommando — Linting läuft als Teil von `dotnet test` (`AiNetLinter.exe`, sofern auf der Maschine vorhanden; fehlt es, werden die Linter-Tests via `Assert.Skip` übersprungen statt zu failen)
- **Code-Style-Kurzfassung:** `#nullable enable` in jeder `.cs`-Datei; `sealed` für konkrete Klassen; Methoden ≤60 Zeilen (bei CC≤3 ∧ CogC≤5 bis 150 als `warning`); ≤4 Methodenparameter (sonst Parameter-`record`); max. 1 `bool`-Parameter; keine hartkodierten Zahlen/Timeouts/Strings in `.cs` — alles über `IOptions<T>`-Klassen mit Property-Initializer-Defaults, gespiegelt in `appsettings.json` (Pflicht wegen `AppSettingsMigrator`); kein leeres `catch`; kein `dynamic`; `out` nur in `Try*`; PascalCase für öffentliche Member; ASCII-only Bezeichner; `Result<T>`-Pattern an MCP-Tool-Schnittstellen bevorzugt; Fehler nur über Error-Catalog-Codes `SQL-AI-0001`..`SQL-AI-0110`.
- **Commit-Konventionen:** Conventional Commits, **auf Deutsch**, imperativ (z. B. „fix(query): CommandTimeout konfigurierbar machen“); autonome Commits nach jedem abgeschlossenen Feature/Bugfix/grünen Testlauf, kein Warten auf Aufforderung (siehe `SqlToAiRichtlinien.mdc` §4). Kommunikation mit dem Nutzer: Deutsch. Code/Doku/Bezeichner: Englisch.

## Regel-Index

- `.agents/rules/AiNetLinter.mdc` — automatisch generierte C#-Codequalitäts-Grenzwerte (Methoden-/Dateilänge, Komplexität, Parameterzahl, `sealed`, Namensregeln, verbotene Patterns wie `dynamic`/leeres `catch`) und Projekt-Overrides.
- `.agents/rules/SqlToAiRichtlinien.mdc` — Architektur- und Workflow-Leitplanken für SqlToAi: Sicherheits-/Access-Control-Konzepte, Windows/PowerShell-Tooling, Build/Test-Kommandos, Doku-Sync-Pflicht, Sprachregeln, Commit-Pflicht, keine hartkodierten Werte (AppSettings-Pflicht), Zero-Warning-Direktive & Baseline-Handling.

## Epics

- [x] EPIC-01: CommandTimeout-Konfigurierbarkeit & Umbenennung — Entfernen von `command.CommandTimeout = 0` in [QueryExecutionService.cs:251](src/SqlToAi/Database/QueryExecutionService.cs#L251); neue `QueryExecutionOptions.CommandTimeoutSeconds` (appsettings-gebunden) statt Hardcode; Umbenennung der bestehenden, irreführenden `SqlServerOptions.CommandTimeoutSeconds` → `ConnectTimeoutSeconds` (Options-Klasse [SqlToAiOptions.cs](src/SqlToAi/Configuration/SqlToAiOptions.cs), `appsettings.json`, Referenz in [SqlConnectionFactory.cs:44](src/SqlToAi/Database/SqlConnectionFactory.cs#L44)) — konzept.md Muss-Haben 1 / „Wie" Schritt 1. **Erledigt in step-001** (approved, siehe `step-001/step-review.md`); Nebenbefund TD-001 (`QueryValidationService` verwendet `ConnectTimeoutSeconds` als Command-Timeout) dokumentiert, bewusst kein eigenes Epic (Nutzer-Entscheidung vorbehalten).
- [x] EPIC-02: Serverseitiges Row-Limit via SET ROWCOUNT — `SET ROWCOUNT @limit` als Session-Setting vor `ExecuteReaderAsync` in [QueryExecutionService.cs](src/SqlToAi/Database/QueryExecutionService.cs) setzen, analog zum bestehenden `ExecuteSetOptionAsync`-Helper (vgl. `QueryValidationService.cs` NOEXEC-Pattern als Referenz für Session-Settings innerhalb der Transaction); bestehende clientseitige `while (rowCount < args.RowLimit ...)`-Schleife bleibt unverändert als Fallback bestehen — konzept.md Muss-Haben 2 / „Wie" Schritt 2. **Erledigt in step-002** (approved, siehe `step-002/step-review.md`): `SET ROWCOUNT {limit}` über `ExecuteSetOptionAsync`, Reader-Block in `try/finally` mit unbedingtem `SET ROWCOUNT 0`-Reset, clientseitige Schleife unverändert als zweites Sicherheitsnetz erhalten.
- [x] EPIC-03: MCP-Trail-Redaction via Anonymizer-Reuse — [McpTrailWriter.cs](src/SqlToAi/Mcp/McpTrailWriter.cs) wendet vor `File.AppendAllText`/`File.WriteAllText` dieselbe bestehende `IAnonymizer`-Anonymisierung (PII-Glob-Patterns, ScramblePattern/Hash) auf die geschriebenen Request-Argumente und Response-Inhalte an, unabhängig vom `AccessLevel` der jeweiligen Datenbank — konzept.md Muss-Haben 3 / „Wie" Schritt 3. **Erledigt in step-003, Fix in step-003/fix-01** (approved, siehe `step-003/fix-01/step-review.md`): ursprüngliches CRITICAL-Finding (Strukturschlüssel-Ausnahme namensbasiert statt positionsabhängig) im Fix behoben; verbleibende Nebenbefunde TD-002 und TD-003 dokumentiert, bewusst kein eigenes Epic (Nutzer-Entscheidung vorbehalten — s.u. jedoch explizite Nutzer-Entscheidung für TD-001..003 gemeinsam).

Reihenfolge folgt der Nummerierung in `konzept.md` „Wie" (1→2→3); EPIC-01 ist inhaltlich unabhängig von EPIC-02/03 und könnte auch parallel geplant werden, wird aber in dieser Reihenfolge abgearbeitet, da `konzept.md` sie so priorisiert (Options-Umbenennung zuerst, um Namenskollisionen zu vermeiden, bevor weitere Options-Felder ergänzt werden).

Nicht als eigenes Epic (bewusst, siehe `konzept.md` Non-Goals): keine neue `ci.yml`; keine Verschlüsselung des MCP Trail at-rest; kein Streaming/`IAsyncEnumerable`-Ersatz für `ExecuteAndSerializeAsync` (Nice-to-Have, spätere Iteration); keine weitere `QueryTokenResolver`-Typsicherheits-Härtung (Nice-to-Have, spätere Iteration).

### Tech-Debt-Epics (Nutzer-Entscheidung 2026-08-04)

Abweichend vom Standard-Workflow (Tech-Debt-Einträge werden sonst nie
automatisch in Epics überführt, siehe `tech-debt.md` Kopf / `spec.md`
§8.3/§9) hat der Nutzer im Chat explizit entschieden: „Es sammelt sich
techdebt an, sofern du das ohne Nutzer-Entscheidungen selbst lösen kannst
→ löse es mit. Ziel: wir haben kein Tech-Debt und alles ist grün lt.
Konzept." Das ist die geforderte manuelle Nutzer-Entscheidung, um TD-001,
TD-002 und TD-003 (siehe `tech-debt.md`, alle Status weiterhin `offen` bis
zum jeweiligen Step-Abschluss) in Epics zu überführen:

- [x] EPIC-04 (aus TD-001): `QueryValidationService` verwendet `SqlServerOptions.ConnectTimeoutSeconds` (Connection-Timeout) als `DbCommand.CommandTimeout` für die `SET NOEXEC ON`/Parse-Only/`SET NOEXEC OFF`-Befehle in [QueryValidationService.cs:143,151,160](src/SqlToAi/Database/QueryValidationService.cs#L143) — semantisch falsche Options-Quelle seit der Umbenennung in Step 001. **Erledigt in step-004** (approved, siehe `step-004/step-review.md`): liest `CommandTimeout` jetzt aus `QueryExecutionOptions.CommandTimeoutSeconds`; TD-001 in `tech-debt.md` auf erledigt gesetzt.
- [x] EPIC-05 (aus TD-002): `Anonymizer.IsColumnExcluded` in [Anonymizer.cs:74](src/SqlToAi/Anonymization/Anonymizer.cs#L74) wertet den `context`-Parameter (Tabellen-/Spaltenname) nie aus — `AnonymizerOptions.ExcludedColumns`-Glob-Patterns greifen projektweit nirgends, weder für Query-Ergebnisse noch für den Trail-Redaction-Pfad aus EPIC-03. Tatsächliche Glob-Pattern-Prüfung gegen den Kontext ergänzen. **Erledigt in step-005** (approved, siehe `step-005/step-review.md`): TD-002 durch Klärung/Architektur-Review aufgelöst — `ExcludedColumns` existiert seit Commit `9324ed1` (vor Task-Start) nicht mehr, keine Code-Änderung nötig, stattdessen stale XML-Doc-Kommentare korrigiert.
- [x] EPIC-06 (aus TD-003): Der Content-Block-Kontext (`IsContentBlock`) in [McpTrailWriter.cs](src/SqlToAi/Mcp/McpTrailWriter.cs) (`AnonymizeObjectProperties`) wird für jede Objekt-Property namens `content` mit Array-Wert aktiviert, nicht nur für das tatsächliche `result.content[]` im Response-Envelope. Präziser an die Envelope-Herkunft koppeln. **Erledigt in step-006** (approved, siehe `step-006/step-review.md`): `RedactionContext` um `IsResultObject` erweitert; `content`-Sonderfall aktiviert `IsContentBlock` nur noch unter `context.IsResultObject && key == "content"`; TD-003 in `tech-debt.md` auf erledigt gesetzt.
