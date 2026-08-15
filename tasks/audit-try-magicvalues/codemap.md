---
task: audit-try-magicvalues
type: codemap
maintained_by: planer, coder, kritiker
last_updated: 2026-08-15T21:55:00+02:00
---

# CodeMap: audit-try-magicvalues

Task-scoped Landkarte — existiert nur für diesen Task, wird mit
`<task-dir>` gelöscht, kein projektweites Artefakt. Enthält **nur**, was
für diesen Task relevant ist (Module/Dateien/Bereiche, die ein Step
tatsächlich berührt hat oder für die Planung des nächsten Steps
gebraucht wird) — kein Anspruch auf vollständige Projektabdeckung.

**Pointer-Prinzip — wie Regel-Index (`roadmap.md`) und Tech-Debt-Index
(`tech-debt.md`):** Jeder Eintrag ist Ort + **ein Satz**, was dort ist
und wozu es für diesen Task relevant ist — keine Verhaltensbeschreibung,
kein „wie funktioniert das im Detail". Verhaltensbehauptungen veralten,
Ortsangaben kaum. Wer mehr wissen muss, liest die Datei selbst nach —
das ersetzt die Map nie, sie beschleunigt nur das Finden.

**Warum das trotzdem verlässlich bleibt (anders als generische Doku):**
Der gesamte Loop läuft strikt seriell — genau ein Subagent gleichzeitig
(`../spec.md` §6). Zwischen einem Coder-Update und dem nächsten Lesezugriff
kann sich am Code strukturell nichts geändert haben, was hier nicht auch
eingetragen wurde. Die Map ist also, solange sie gepflegt wird, tatsächlich
aktuell — kein Snapshot mit Drift-Risiko. **Schritt 2 im Step-Modus des
Planers („tatsächlichen Projektzustand lesen", `../spec.md` §7.2) bleibt
trotzdem Pflicht** — die Map sagt *wo* nachschauen, ersetzt nie das
Nachschauen selbst.

## Pflege — wer trägt wann ein

- **Planer, Roadmap-Modus (einmalig):** befüllt die Map initial aus dem
  Grobüberblick, den er beim Ableiten der Epics ohnehin über den
  Bestandscode gewinnt (`../skills/planer/SKILL.md` Roadmap-Modus
  Schritt 1).
- **Coder (jeder Step):** ergänzt/aktualisiert Einträge für tatsächlich
  angelegte oder geänderte Module, **vor** dem Doku-Commit
  (`../skills/coder/SKILL.md` Schritt 6a).
- **Planer, Step-Modus (jeder Step):** liest die Map vor dem Planen,
  ergänzt neue Bereiche, die er beim Lesen des Ist-Zustands entdeckt.
  Zusätzlich Grundlage für den Anti-Loop-Check (siehe unten).
- **Kritiker:** prüft stichprobenartig, ob die Map dem tatsächlichen Diff
  entspricht (Teil von Ebene 1, Plan-Erfüllung) — schreibt selbst nur bei
  offensichtlicher Lücke/Fehler nach, ist aber nicht Haupt-Pfleger.

## Anti-Loop-Nutzen

Bevor der Planer im Step-Modus einen neuen Step plant, gleicht er sein
Vorhaben gegen die hier verzeichneten, bereits getroffenen Entscheidungen
ab. Widerspricht der neue Plan erkennbar einem hier festgehaltenen,
bereits umgesetzten Stand (z. B. Step-234 würde zurückdrehen, was Step-123
laut Map bewusst so gebaut hat): entweder im neuen Step-Plan explizit als
Erweiterung begründen, oder den alten Eintrag hier als „obsolet —
<Grund>" markieren (nicht löschen) — nie stillschweigend widersprechen.
Das verhindert kein Kreisen zu 100 %, macht ein Hin-und-Her aber
wenigstens sichtbar und begründungspflichtig statt stillschweigend.

## Karte

- **`src/SqlToAi/Database/`** — `QueryExecutionService`, `QueryValidationService`, `PerformanceMeasurementService`, `QueryComparisonService` (alle 4 Guardrail-Services für EPIC-02, migriert in step-002: konsumieren jetzt `IQuerySafetyValidator`, Inline-Pipeline geloescht, Constructor-Deps um 3 Security-Interfaces reduziert), `IndexSuggestionService` (MV-1 Error-Codes 297/300, unveraendert), `OptimizationBenchmarkService` (MV-2 Verdicts), `SqlToAiErrorMapper` (MV-1 Codes −2/121/258/233/18456), `SchemaService` + `TableSchemaRenderer` + `DetailSchemaRenderer` (DRY-2 `DdlUnavailableNote`).
  - **`src/SqlToAi/Database/QuerySafetyValidator.cs`** (neu, step-002) — `internal sealed class QuerySafetyValidator` plus `public interface IQuerySafetyValidator` plus `public sealed record QuerySafetyCheckResult(AccessLevel, bool IsWriteAllowed)` — Single Source of Truth der 6-stufigen Guardrail-Pipeline (Parameter, Whitelist, AccessLevel, ReadOnlyGuard, Multi-Statement). `allowSchemaOnly`-Parameter bewahrt die `QueryValidationService`-Sonderbehandlung von `AccessLevel.SchemaOnly`. Drei Dependencies (`ISecurityGuard`/`IAccessLevelProvider`/`IReadOnlyGuard`), 30-Zeilen-Body, alle Linter-Limits eingehalten.
  - **`src/SqlToAi/Database/SqlServerErrorCode.cs`** (neu, step-001) — `internal static class` mit benannten `const int` für SQL-Server-Fehlernummern (Permissions, Timeouts, Connection-Resets, Auth, Instance-/Server-Lookup).
  - **`src/SqlToAi/Database/BenchmarkVerdict.cs`** (neu, step-001) — `internal static class` mit `const string`-Verdicts (`Recommended`, `NotRecommended`, `Neutral`, `UnsafeDueToDataMismatch`) für den MCP-Output-Vertrag.
  - **`src/SqlToAi/Database/SqlServerObjectType.cs`** (neu, step-001) — `internal static class` mit `const string` für `sys.objects.type` (`UserTable = "U"`, `View = "V"`).
- **`tests/SqlToAi.Tests/Database/QueryExecutionServiceMockDb.cs`** (step-002) — enthaelt jetzt zusaetzlich `internal sealed class FakeQuerySafetyValidator : IQuerySafetyValidator` mit drei Konstruktor-Varianten (Ergebnis, Fehler, delegierend an realen `QuerySafetyValidator` mit Legacy-Fakes). Legacy-Fakes `FakeSecurityGuard`/`FakeAccessLevelProvider`/`FakeReadOnlyGuard` bleiben unveraendert fuer `IndexSuggestionServiceTests`.
- **`src/SqlToAi/Anonymization/`** — `Anonymizer` (MV-4 FNV-1a Konstanten, MV-6 `Hash`/`Scramble`-Modi), `AnonymizationRuleProvider` (MV-3 Gewichtungsfaktoren 1000/100/10), `LikePatternMatcher` (DRY-5/MV-5 Regex-Timeout).
  - **`src/SqlToAi/Anonymization/AnonymizationMode.cs`** (neu, step-001) — `internal static class` mit den Anonymisierungs-Modus-Konstanten (`Hash`, `Scramble`).
- **`src/SqlToAi/Security/`** — `ReadOnlyGuard` (MV-5 `TimeSpan.FromMilliseconds(200)`), `ISecurityGuard`, `IAccessLevelProvider` — alle drei ab step-002 nur noch ueber `QuerySafetyValidator` referenziert (von `IndexSuggestionService` direkt).
  - **`src/SqlToAi/Security/SecurityConstants.cs`** (neu, step-001) — `public static class` mit `DefaultRegexTimeout` als zentrale ReDoS-Schutzgrenze (200 ms), genutzt von `ReadOnlyGuard`, `GlobMatcher`, `LikePatternMatcher`, `QueryTokenResolver`.
- **`src/SqlToAi/Domain/`** — `GlobMatcher` (DRY-5/MV-5 Regex-Timeout, kein Merge mit `LikePatternMatcher`), `SqlToAiError` (Error-Katalog `SQL-AI-*` bleibt unangetastet, MV-P3), `Result` (Result-Pattern, Pflicht).
- **`src/SqlToAi/Mcp/`** — `ToolRegistry` (DRY-3 `OptionalStringParam`-Cleanup, DRY-4 `BuildDetailTool`-Helper, MV-2 Verdict-Strings, MV-7 Objekttyp-Strings), `McpHost` (DRY-P3 Positivbefund, AOT-Source-Generator — nicht anfassen), `ToolDispatcher`.
- **`src/SqlToAi/Configuration/`** — `SqlToAiOptions` (MV-P1 Positivbefund, Property-Initializer bleiben einzig autorisierter Ort für Defaults), `AppSettingsMigrator` (MV-P2 `"Password"`-Schlüsselname, kein Klartext).
- **`tests/SqlToAi.Tests/Database/`** — `*ServiceTests` (DRY-T3 33 redundante Guardrail-Negativ-Tests, step-002: auf neuen `FakeQuerySafetyValidator` umgestellt, Inhalt unveraendert, Konsolidierung folgt in EPIC-03), `QueryExecutionServiceMockDb` (enthaelt seit step-002 `FakeQuerySafetyValidator`), `SchemaServiceMockDb`, `PerformanceMeasurementServiceTests` (DRY-T2 8 duplizierte ShowPlan-XML-Blöcke), `OptimizationBenchmarkServiceTests` (MV-2 Testseite).
- **`tests/SqlToAi.Tests/Mcp/`** — `ToolDispatcherTestFakes` (DRY-T1 zentrale Fakes), `McpTrailWriterTests`/`McpTrailWriterRedactionTests` (DRY-T1 `GetDayDir()`-Duplikat).
- **`tests/SqlToAi.Tests/TestSupport/`** — gemeinsame Fakes (`AnonymizationTestHelper`, `McpTrailTestHelper`, `FakeDb*`); Bündelungsziel für DRY-T1.
