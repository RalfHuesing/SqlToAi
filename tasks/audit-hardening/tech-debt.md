---
task: audit-hardening
type: tech-debt-log
maintained_by: kritiker
last_updated: 2026-08-04T19:30:00+02:00
---

# Tech-Debt-Log: audit-hardening

Append-only. Jeder Eintrag ist eine vom Kritiker während eines
Step-Reviews beobachtete, aber bewusst **nicht** gefixte Auffälligkeit
außerhalb des Scopes des jeweiligen Steps (Architektur, Anti-Pattern,
Duplikation, Konsistenz) — siehe `../spec.md` §8.3/§9.

**Priorität ist reine Sortierhilfe für den Menschen, kein Auslöser.**
Bewusst `hoch`/`mittel`/`niedrig` (deutsch) statt `CRITICAL`/`MAJOR`/
`MINOR`, um jede Verwechslung mit den blockierenden Findings in
`step-review.md` auszuschließen — kein Eintrag hier führt automatisch zu
einem Fix-Step oder einem neuen Epic. Das entscheidet ausschließlich der
Nutzer (manuell, z. B. durch Ergänzen eines Epics in `roadmap.md` mit
Verweis auf die Tech-Debt-ID).

## Index

| ID | Bereich / Datei | Priorität | Kurzfassung |
|---|---|---|---|
| TD-001 | `src/SqlToAi/Database/QueryValidationService.cs` (Zeilen 143, 151, 160) | niedrig | Nutzt `SqlServerOptions.ConnectTimeoutSeconds` (Connection-Timeout-Option) als Command-Timeout für `SET NOEXEC`/Parse-Only-Validierungsbefehle — Name passt seit der Umbenennung in Step 001 nicht mehr zum Verwendungszweck. |
| TD-002 | `src/SqlToAi/Anonymization/Anonymizer.cs:74` (`IsColumnExcluded`) | hoch | Wertet den `context`-Parameter nie aus — `AnonymizerOptions.ExcludedColumns`-Glob-Patterns greifen projektweit nirgends (weder für Query-Ergebnisse noch für den neuen Trail-Redaction-Pfad aus Step 003), obwohl die XML-Doku und `IAnonymizationPolicyResolver` (genutzt für die "Anonymized: Yes/No"-Schema-Hinweise) das Gegenteil suggerieren. |
| TD-003 | `src/SqlToAi/Mcp/McpTrailWriter.cs` (`AnonymizeObjectProperties`, `content`-Array-Sonderfall) | niedrig | Der Content-Block-Kontext (`IsContentBlock`) wird für jede Objekt-Property namens `content` mit Array-Wert aktiviert, nicht nur für `result.content[]` im Response-Envelope — vom Plan als akzeptabel sanktioniert, aber ein LLM-gewähltes `arguments.content`-Array mit `type`-Properties würde ebenfalls (fälschlich) exempt behandelt. |

## Einträge

### TD-001 — `QueryValidationService` verwendet `ConnectTimeoutSeconds` als Command-Timeout [Priorität: niedrig]

- **Gefunden in:** step-001 (Kritiker-Review vom 2026-08-04)
- **Ort:** `src/SqlToAi/Database/QueryValidationService.cs:143,151,160` (`setNoexecCmd.CommandTimeout`,
  `queryCmd.CommandTimeout`, `resetCmd.CommandTimeout` — jeweils `_dbOptions.ConnectTimeoutSeconds`)
- **Befund:** Diese drei Zeilen verwenden die Connection-Timeout-Option `SqlServerOptions.ConnectTimeoutSeconds`
  (ADO.NET `ConnectTimeout`, gedacht für den Verbindungsaufbau) als `DbCommand.CommandTimeout` für
  die `SET NOEXEC ON`/Parse-Only/`SET NOEXEC OFF`-Befehle der Query-Validierung. Das ist funktional
  unauffällig (identischer Wert, unverändertes Laufzeitverhalten — Step 001 hat diese drei Referenzen
  rein mechanisch von `CommandTimeoutSeconds` auf `ConnectTimeoutSeconds` umbenannt, um den Build nicht
  zu brechen), aber semantisch dasselbe Fehlbenennungs-Muster wie der ursprüngliche Audit-Fund: der
  Name der verwendeten Option passt nicht mehr zum tatsächlichen Verwendungszweck (Command- statt
  Connection-Timeout). Strukturell identisch zum bereits in `step-plan.md` (Abschnitt „Notes")
  benannten, ebenfalls nicht angefassten `SecondaryConnectionSettings.CommandTimeoutSeconds` in
  `SecondaryConnectionBuilder.cs:54` (dort umgekehrt: korrekter Name, aber als `ConnectTimeout`
  verwendet).
- **Warum nicht sofort gefixt:** `konzept.md` Muss-Haben 1 nennt ausschließlich die Umbenennung von
  `SqlServerOptions.CommandTimeoutSeconds` sowie die neue `QueryExecutionOptions.CommandTimeoutSeconds`
  für `QueryExecutionService`. `QueryValidationService` semantisch auf eine andere Options-Quelle
  umzustellen (z. B. `QueryExecutionOptions.CommandTimeoutSeconds` oder eine eigene
  Validation-Timeout-Option) wäre eine Scope-Erweiterung über Step 001 und über `konzept.md` hinaus.
- **Vorschlag:** Eigenes, kleines Epic/Step: `QueryValidationService` auf
  `QueryExecutionOptions.CommandTimeoutSeconds` (oder eine dedizierte
  `QueryExecutionOptions.ValidationTimeoutSeconds`) umstellen, damit der Command-Timeout der
  Validierungsabfragen nicht mehr an der Connection-Timeout-Option hängt. Ggf. im selben Zug
  `SecondaryConnectionSettings.CommandTimeoutSeconds` (`SecondaryConnectionBuilder.cs:54`) korrigieren,
  da strukturell dasselbe Muster.
- **Status:** offen

### TD-002 — `Anonymizer.IsColumnExcluded` wertet `context` nie aus [Priorität: hoch]

- **Gefunden in:** step-003 (Kritiker-Review vom 2026-08-04)
- **Ort:** `src/SqlToAi/Anonymization/Anonymizer.cs:74` (`IsColumnExcluded`)
- **Befund:** `IsColumnExcluded` lautet vollständig
  `return !_options.Anonymizer.Enabled;` — der übergebene
  `AnonymizationColumnContext` (Tabellen-/Spaltenname) wird nie
  ausgewertet. Damit greifen `AnonymizerOptions.ExcludedColumns`
  Glob-Patterns **an keiner Stelle im Projekt**, weder für die
  Context-Overload (`Anonymize(value, context)`, genutzt von
  `QueryExecutionService` für Query-Ergebnisse) noch für die neue
  alias-only-Nutzung in `McpTrailWriter` aus Step 003 — beide rufen
  letztlich dieselbe `IsColumnExcluded`-Methode auf. Die XML-Doku der
  Klasse sowie `IAnonymizationPolicyResolver` (genutzt für die
  „Anonymized: Yes/No"-Hinweise, die `sql_get_schema` proaktiv anzeigt)
  suggerieren dagegen, dass spaltenspezifische Ausnahmen wirken —
  potenziell irreführend für Nutzer, die sich auf diese Anzeige verlassen.
- **Warum nicht sofort gefixt:** vorbestehend (nicht durch Step 003
  verursacht oder verändert), betrifft die komplette Anonymizer-
  Infrastruktur projektweit, nicht nur den Trail-Redaction-Scope dieses
  Steps. `konzept.md` Muss-Haben 3 verlangt nur den globalen Schalter für
  die Trail-Redaction, keine spaltenspezifische Ausnahmeliste — ein Fix
  wäre eine Scope-Erweiterung weit über Step 003 hinaus.
- **Vorschlag:** Eigenes Epic/Step: `IsColumnExcluded` um tatsächliche
  Glob-Pattern-Prüfung gegen `context.TableName`/`context.OriginColumnName`
  (bzw. `context` an sich) erweitern, damit `ExcludedColumns` wie
  dokumentiert wirkt — sowohl für Query-Ergebnisse als auch für den Trail.
- **Status:** offen

### TD-003 — `content`-Array-Sonderfall in `McpTrailWriter` positionsunabhängig [Priorität: niedrig]

- **Gefunden in:** step-003/fix-01 (Kritiker-Review vom 2026-08-04)
- **Ort:** `src/SqlToAi/Mcp/McpTrailWriter.cs` (`AnonymizeObjectProperties`,
  Zweig `key == "content" && obj[key] is JsonArray contentArray`)
- **Befund:** Der Content-Block-Kontext (`IsContentBlock = true`) wird für
  **jede** Objekt-Property namens `content` mit `JsonArray`-Wert aktiviert,
  unabhängig davon, ob es sich tatsächlich um `result.content[]` im
  Response-Envelope handelt. Ein LLM-gewähltes `arguments.content`-Array
  (z. B. ein Bind-Parameter namens `content`, dessen Wert ein Array von
  Objekten mit `type`-Property ist) würde ebenfalls `IsContentBlock`-
  Behandlung für seine direkten Elemente bekommen — dessen `type`-Property
  bliebe dann fälschlich unredigiert, falls sensibel. Der Schaden ist durch
  die Ein-Ebene-Tiefe-Begrenzung (`IsContentBlock` wird nach einem
  Rekursionsschritt garantiert zurückgesetzt) eng begrenzt.
- **Warum nicht sofort gefixt:** `step-003/fix-01/step-plan.md` sanktioniert
  diese Grobheit explizit („im Zweifel darf der Check aber grobzügiger
  sein, solange er nicht dazu führt, dass beliebige verschachtelte
  `type`-Properties in Nicht-Content-Kontexten ausgenommen werden") — kein
  neu durch den Fix eingeführtes Problem, sondern eine bewusst in Kauf
  genommene Restungenauigkeit des Fixes selbst. Eine präzisere Bindung an
  „nur `result.content[]` im Response-Envelope" wäre eine über den
  aktuellen Fix-Scope hinausgehende Verschärfung.
- **Vorschlag:** Bei Bedarf den Content-Block-Kontext zusätzlich an
  `context.IsEnvelopeRoot`-Herkunft koppeln (z. B. nur aktivieren, wenn der
  `content`-Array-Fund selbst unterhalb eines `result`-Objekts auf der
  Envelope-Ebene liegt), statt an den Property-Namen allein.
- **Status:** offen
