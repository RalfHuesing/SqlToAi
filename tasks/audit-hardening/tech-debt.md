---
task: audit-hardening
type: tech-debt-log
maintained_by: kritiker
last_updated: 2026-08-04T00:00:00+02:00
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
