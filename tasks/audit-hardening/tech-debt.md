---
task: audit-hardening
type: tech-debt-log
maintained_by: kritiker
last_updated: 2026-08-04T15:00:00+02:00
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
