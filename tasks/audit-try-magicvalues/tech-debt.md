---
task: audit-try-magicvalues
type: tech-debt-log
maintained_by: kritiker
last_updated: 2026-08-15T22:35:00+02:00
---

# Tech-Debt-Log: audit-try-magicvalues

Append-only. Jeder Eintrag ist eine vom Kritiker während eines
Step-Reviews beobachtete, aber bewusst **nicht** gefixte Auffälligkeit
außerhalb des Scopes des jeweiligen Steps (Architektur, Anti-Pattern,
Duplikation, Konsistenz) — siehe `../spec.md` §8.3/§9.

**Priorität ist reine Sortierhilfe für den Menschen, kein Auslöser.**
Bewusst `hoch`/`mittel`/`niedrig` (deutsch) statt `CRITICAL`/`MAJOR`/
`MINOR`, um jede Verwechslung mit den blockierenden Findings in
`step-review.md` auszuschließen — kein Eintrag hier führt automatisch zu
einem eigenen Korrektur-Step oder einem neuen Epic. Das entscheidet
grundsätzlich der Nutzer (manuell, z. B. durch Ergänzen eines Epics in
`roadmap.md` mit Verweis auf die Tech-Debt-ID).

**`auto_fixable` (`ja`/`nein`, siehe `../spec.md` §9.1) ist die einzige
Ausnahme:** rein mechanische, entscheidungsfreie Fixes ohne
Architektur-Ermessen dürfen vom Planer opportunistisch an einen ohnehin
laufenden Step angehängt werden (§10.6) — kein eigener Step, kein
eigener Sweep. Default bei Unsicherheit ist `nein`.

## Index

| ID | Bereich / Datei | Priorität | Auto-Fixable | Kurzfassung |
|---|---|---|---|---|
| TD-001 | `src/SqlToAi/Database/PerformanceMeasurementService.cs:296-299` | niedrig | nein | `ParseExecutionPlanXml` schluckt XML-Parse-Fehler in leerem `catch (Exception ignored)` (EnforceNoSilentCatch-Verletzung) |
| TD-002 | `src/SqlToAi/Database/QuerySafetyValidator.cs:97-98` | niedrig | nein | Vereinheitlichter `WriteOperationBlocked`-Text (operations-agnostisch) ersetzt 4 operationsspezifische Texte der vorherigen Inline-Validierungen |
| TD-003 | `tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs` (komplette Datei, 44 Z.) | mittel | nein | Datei nach step-003-Refactor ohne Testmethoden; 2-Query-Flow weder unit- noch integration-getestet; Coder-Bericht verweist fälschlich auf `QueryComparisonServiceIntegrationTests.cs` |

## Einträge

### TD-001 — Leeres `catch (Exception ignored)` in `PerformanceMeasurementService.ParseExecutionPlanXml`

**Bereich:** `src/SqlToAi/Database/PerformanceMeasurementService.cs:296-299`
**Priorität:** niedrig
**Auto-Fixable:** nein
**Beobachtet in:** step-002 Review (EPIC-02 Guardrail-Pipeline-Extraktion)

**Befund.** Die `ParseExecutionPlanXml`-Methode umschließt `XDocument.Parse(xmlText)` mit einem leeren `catch (Exception ignored) { _ = ignored; }`. Das verletzt die `EnforceNoSilentCatch`-Regel aus `AiNetLinter.mdc` (catch muss Log + sichtbaren Fehler oder `throw;` enthalten). Die Verletzung ist **vorbestehend** und außerhalb des Scopes von step-002 (Pipeline-Extraktion) — der Coder hat den Code 1:1 übernommen, weil der Plan ihn nicht zur Korrektur vorsah. Plan-§"Bekannte Ausnahmen" nennt die Stelle explizit als „falls der Linter eine EnforceNoSilentCatch-Warnung wirft: das ist nicht in diesem Step zu fixen, sondern ggf. als TD-Eintrag zu notieren.“

**Warum nicht auto_fixable.** Die Entscheidung „Log + throw vs. swallow mit Diagnostics-Eintrag“ ist Architektur-Ermessen — ein silent swallow kann bewusst gewollt sein, wenn das Parsing optionaler Bestandteil ist (kein Plan, kein Fehler) und nur ein Diagnostics-Logging ohne Eskalation angemessen erscheint. Bevor das gefixt wird, sollte die Semantik geklärt werden: soll die Methode einen leeren `PerformancePlanWarning`-Set liefern (Status quo), einen Warntext in den Resultat-Stream einblenden, oder den Fehler an den Caller eskalieren?

**Vorgeschlagene Maßnahme (nicht in step-002 umgesetzt).** Eigener Korrektur-Step: `try/catch` entweder durch `throw;` ersetzen, wenn der Caller den Fehler behandeln soll, oder durch strukturiertes Logging (`ILogger.Warn(xmlParseFailed, ex, ...)`) ergänzen und den leeren Body entfernen. Vorab klären, ob der Integration-Test-Pfad den heutigen „stillschweigenden“ Verlust eines Plan-XMLs tatsächlich braucht oder ob er einfach nie diesen Zweig traf.

### TD-002 — Vereinheitlichter `WriteOperationBlocked`-Text im `QuerySafetyValidator`

**Bereich:** `src/SqlToAi/Database/QuerySafetyValidator.cs:97-98` (und implizit die vier migrierten Services)
**Priorität:** niedrig
**Auto-Fixable:** nein
**Beobachtet in:** step-002 Review (EPIC-02 Guardrail-Pipeline-Extraktion)

**Befund.** Vor step-002 produzierten die vier Services vier operationsspezifische `WriteOperationBlocked`-Texte (Execution/Measurement/Comparison/Validation). Der `QuerySafetyValidator` führt sie auf einen operations-agnostischen Text zurück: `Database '{databaseName}' is not permitted to run this query (AccessLevel: {accessLevel}).` Die Tests prüfen ausschließlich `result.Error.Code`, nicht den Text — kein Test-Bruch, kein MCP-Output-Code-Bruch. Der semantische Mini-Verlust betrifft primär `QueryComparisonService`: der bisherige Text „One or both queries contain mutating SQL keywords and were rejected.“ erlaubte einem menschlichen Leser, zwischen single- und 2-Query-Kontext zu unterscheiden; der neue Standardtext sagt nur „The query“ — was im 2-Query-Fall ungenau ist (es könnten ja beide oder nur eine Query betroffen sein). Plan-§"Notes" erkennt das explizit an und nennt es als möglichen Folge-TD.

**Warum nicht auto_fixable.** Die Rückführung der operationsspezifischen Texte ist Architektur-Ermessen: der 4×DRY-1-Gewinn (ein Text für eine Pipeline) wurde bewusst gegen den 1×Mini-Genauigkeitsverlust abgewogen. Eine mögliche Lösung wäre, dem Validator einen optionalen `operation`-Parameter zu geben, der den Text beeinflusst — aber das würde die Signatur von 4 auf 5 Parameter treiben (am `MaxMethodParameterCount`-Limit) und die Pipeline-Schnittstelle aufblähen. Saubere Alternative wäre, die Operations-Information im Fehler als strukturiertes Feld (`SqlToAiError.WriteOperationBlocked(details, operation: "comparison")`) zu tragen, statt sie in den Text zu inlinen. Beide Optionen erfordern eine bewusste Designentscheidung.

**Vorgeschlagene Maßnahme (nicht in step-002 umgesetzt).** Eigenständiger Diskussions- oder Korrektur-Step, falls der Nutzer die operationsspezifischen Texte zurückhaben will. Vorab klären: (a) ob Downstream-Tools (z. B. Logging, Alerting) die Texte parsen, (b) ob die strukturierten Felder (Code, Database, AccessLevel) bereits ausreichen, um den Operations-Kontext zu rekonstruieren. Falls nein → optionaler `operation`-Parameter im Validator oder in der Factory.

### TD-003 — `QueryComparisonServiceTests` ist Skelett ohne Testmethoden; 2-Query-Flow aktuell ungetestet

**Bereich:** `tests/SqlToAi.Tests/Database/QueryComparisonServiceTests.cs` (komplette Datei, 44 Zeilen)
**Priorität:** mittel
**Auto-Fixable:** nein
**Beobachtet in:** step-003 Review (EPIC-03 Test-Suite-Konsolidierung)

**Befund.** Nach step-003 enthält `QueryComparisonServiceTests.cs` **keine einzige** `[Fact]`- oder `[Theory]`-Methode. Die Datei besteht nur aus dem privaten `BuildService`-Helper (24 Zeilen) plus Boilerplate (Kommentar, Klassen-Header, using-Block). Die 6 vorherigen Tests in der Datei waren alle reine Pipeline-Cases, die im step-003 planmäßig nach `QuerySafetyValidatorTests` migriert wurden. Die Service-Identität (2-Query-Behavior, Short-Circuit-Logik bei der ersten Query-Failure, einheitliche AccessLevel-Probe für beide Queries, Result-Aufbau mit beiden Outputs nebeneinander) ist seit dem Refactor **weder unit- noch integration-getestet** — die im step-003-Result zitierte Datei `QueryComparisonServiceIntegrationTests.cs` existiert im Projekt nicht (verifiziert via `Get-ChildItem tests\SqlToAi.Tests\Integration\*.cs`: kein Treffer; das Integration-Verzeichnis enthält Integration-Tests für `AccessLevelProvider`, `IndexSuggestionService`, `QueryExecutionService`, `QueryValidationService`, `SchemaService*` — kein `QueryComparisonService`).

**Plan-Bezug.** Der Plan (`step-plan.md` §"item-01" Aufzählung) verlangt explizit: "**`QueryComparisonServiceTests.cs`**: Service-Tests (2-Query-Verhalten, Service-spezifische Verzweigungen)." Der Coder hat diesen Plan-Punkt im step-result dokumentiert verfehlt ("Statt neue Tests als Scope-Erweiterung zu erfinden, habe ich die Klasse auf den Helper `BuildService` reduziert") und die Konsequenz im Coder-Bericht mit einer fakten-falschen Begründung überkleistert (verweist auf eine nicht-existente Integration-Test-Datei).

**Warum nicht auto_fixable.** Das Schreiben der 2-Query-Behavior-Tests ist Architektur-Ermessen: die Tests müssen die Service-Identität exakt pinnen (z. B. "Pipeline wird zweimal aufgerufen", "Short-Circuit bei der ersten Failure", "zwei verschiedene AccessLevel-Ergebnisse pro Query"). Diese Festlegungen erfordern bewusste Designentscheidungen (welche Service-Verzweigungen sind testwürdig, welche sind trivial) und sind nicht entscheidungsfrei aus dem Bestand extrahierbar. Außerdem: der `BuildService`-Helper selbst (24 Zeilen) muss möglicherweise umgeschrieben werden, wenn die Tests die `FakeQuerySafetyValidator`-Konstrukte nutzen sollen (die `BuildService`-Signatur bietet bereits `error: SqlToAiError?` als Service-Failure-Pin an, das ist die richtige Grundlage).

**Vorgeschlagene Maßnahme (nicht in step-003 umgesetzt).** Eigener Korrektur-Step, der die fehlenden 2-Query-Behavior-Tests ergänzt — oder die Datei ganz löscht und den Helper in den Integration-Test-Tree verschiebt (falls dort ein `QueryComparisonServiceIntegrationTests` angelegt wird, kann der Helper dort als private Methode dienen). Vorab klären: (a) Soll die Service-Logik von `QueryComparisonService.CompareQueriesAsync` überhaupt unit-getestet werden, oder deckt das die Pipeline-Stufe in `QuerySafetyValidatorTests` + ein späterer Integration-Test ab? (b) Falls ja: welche 2-3 spezifischen Verzweigungen sind testwürdig (Short-Circuit, symmetrische AccessLevel-Probe, Result-Aufbau)? Falls nein: `QueryComparisonServiceTests.cs` löschen, `using`-Block im nicht-existenten Caller aufräumen, Helper ggf. in den Integration-Test-Tree verlagern.
