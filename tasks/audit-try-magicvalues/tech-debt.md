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
