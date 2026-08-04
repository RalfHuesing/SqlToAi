---
task: sql-index-suggestions
type: tech-debt-log
maintained_by: kritiker
last_updated: 2026-08-05T14:25:00+02:00
---

# Tech-Debt-Log: sql-index-suggestions

**Status-Policy (gültig ab 2026-08-05, per Nutzer-Vorgabe):** Diese Datei
enthält ausschließlich **offene** Tech-Debt-Einträge. Sobald ein Eintrag
erledigt ist (durch einen Step in diesem Task) oder explizit abgelehnt
wird (Nutzer-Vorgabe), wird er aus dieser Datei entfernt. Git-Historie
bewahrt den Volltext für zukünftige Referenz.

**Priorität** ist reine Sortierhilfe für den Menschen, kein Auslöser.
Bewusst `hoch`/`mittel`/`niedrig` (deutsch) statt `CRITICAL`/`MAJOR`/
`MINOR`, um jede Verwechslung mit den blockierenden Findings in
`step-review.md` auszuschließen — kein Eintrag hier führt automatisch zu
einem Fix-Step oder einem neuen Epic. Das entscheidet ausschließlich der
Nutzer (manuell, z. B. durch Ergänzen eines Epics in `roadmap.md` mit
Verweis auf die Tech-Debt-ID).

## Index

| ID | Bereich / Datei | Priorität | Kurzfassung |
|---|---|---|---|
| TD-006 | `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs` (Test 1) | niedrig | Test 1 (`SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase`) akzeptiert nur „No recommendations" oder Markdown-Tabelle, NICHT die Graceful-Degradation-Notiz; Test 4 akzeptiert beide. Asymmetrie erzwingt implizit `VIEW SERVER STATE`-Setup-Voraussetzung — Erweiterung analog Test 4 würde Test 1 setup-tolerant machen. |

## Einträge

### TD-006 — Test 1 in `IndexSuggestionServiceIntegrationTests` ist nicht tolerant gegen Graceful-Degradation [Priorität: niedrig]

- **Gefunden in:** step-003 (Kritiker-Review vom 2026-08-05, Reopen)
- **Ort:** `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs:26-42` (Test 1 `SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase`)
- **Befund:** Test 1 akzeptiert nur zwei Output-Pfade: „No missing-index recommendations found" ODER Markdown-Tabelle mit `| Score |`-Header. Die Graceful-Degradation-Notiz (`RenderPermissionNote`, enthält „VIEW SERVER STATE" und „**Note:**") wird von Test 1 nicht akzeptiert. Test 4 (Zeile 65-83) akzeptiert hingegen alle drei Pfade (Permission-Notiz, No-Recommendations, Markdown-Tabelle). Diese Asymmetrie zwischen Test 1 und Test 4 erzwingt implizit, dass der `Agent`-Login `VIEW SERVER STATE` hat — sonst schlägt Test 1 fehl, obwohl die Architektur (Spec §4 Nr. 16) genau diese Notiz als dritten gültigen Output-Pfad vorsieht. Die Asymmetrie war im ursprünglichen Plan (Zeile 300-316) bereits angelegt; sie wird erst durch das fehlende `GRANT VIEW SERVER STATE` (TD-005) zum praktischen Problem.
- **Warum nicht sofort gefixt:** Außerhalb des Scopes des CTE-Fix-Reopen (per User-Anweisung: keine Änderung am Test-Code in `IndexSuggestionServiceIntegrationTests.cs`). Die Test-Erweiterung ist eine reine Copy-Paste-Übernahme der Test-4-Logik in Test 1 und semantisch trivial.
- **Vorschlag:** Test 1 analog zu Test 4 um den dritten Pfad erweitern: Assertion um `result.Value.Contains("VIEW SERVER STATE", StringComparison.Ordinal)` ergänzen. Dadurch wäre Test 1 setup-tolerant und bräuchte kein lokales `GRANT VIEW SERVER STATE` als Voraussetzung. Entscheidungsträger ist der Planer/Nutzer; sauberer Folge-Step (klein, 1-2 Zeilen Änderung).
- **Status:** **in Bearbeitung (step-007)** — Nutzer hat 2026-08-05 angeordnet, TD-006 umzusetzen. Coder erweitert Test 1 um den Graceful-Degradation-Pfad (analog Test 4). Wird in `step-007` (EPIC-04) gefixt. Nach step-007-`approved` wird der Eintrag aus `tech-debt.md` entfernt.
