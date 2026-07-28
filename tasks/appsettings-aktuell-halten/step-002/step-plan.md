---
status: done (pending audit)
type: step-plan
task: appsettings-aktuell-halten
step: step-002
title: "Entwicklungsrichtlinien (.agents/rules/SqlToAiRichtlinien.mdc) aktualisieren"
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Gemini 3.6 Flash
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-07-28T11:35:00+02:00
related_to: []
---

# Step 002: Entwicklungsrichtlinien (.agents/rules/SqlToAiRichtlinien.mdc) aktualisieren

## Bezug

- **Task:** `appsettings-aktuell-halten`
- **Quelle:** [Konzept.md](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tasks/appsettings-aktuell-halten/Konzept.md#L36) — Aktualisierung der Entwickler-Regeln
- **Phase / Priorität:** Dokumentation / Mittel

## Tech-Stack-Notiz

- **Build-Command:** `dotnet build`
- **Test-Command:** `dotnet test`
- **Lint-Command:** Entfällt
- **Code-Style:** Markdown / Frontmatter
- **Commit-Konventionen:** Conventional Commits (Deutsch, imperativ, z. B. `docs(rules): ...`)

## Intention

In den Entwicklungsrichtlinien des Projekts (`.agents/rules/SqlToAiRichtlinien.mdc`) soll explizit verankert werden, dass jede neu eingeführte Konfigurationsoption lückenlos in der Haupt-`appsettings.json` mit sinnvollen Defaults dokumentiert/definiert sein muss, damit die automatische Synchronisierung bei Anwendungsstart stets alle Optionen vorfindet.

## Konkrete Änderungen

### Datei 1: [SqlToAiRichtlinien.mdc](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/.agents/rules/SqlToAiRichtlinien.mdc) (Sektion 4 "Keine hartkodierten Werte")

- **Was:** Den Abschnitt "Keine hartkodierten Werte (No Magic Values)" um die Vorschrift ergänzen: Jede neue Konfigurationsoption muss zwingend in der eingebetteten Haupt-`appsettings.json` mit einem sinnvollen Default-Wert hinterlegt werden.
- **Warum:** Stellt sicher, dass zukünftige Features von der automatischen AppSettings-Synchronisierung abgedeckt werden.

## Tests

- Keine automatisierten Code-Tests erforderlich (reine Dokumentationsänderung).

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command `dotnet build` grün
- [ ] Test-Command `dotnet test` grün
- [ ] Commit auf `main` (Conventional Commit)
- [ ] `tasks/appsettings-aktuell-halten/step-002/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4-updates-dokumentation--sprachen-updates-documentation--languages`
