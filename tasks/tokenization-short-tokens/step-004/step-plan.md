---
status: done
type: step-plan
task: tokenization-short-tokens
step: "004"
title: "Dokumentations-Synchronisation (README.md & mcp-specification.md)"
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-07-28T15:21:00Z
related_to:
  - step-001
  - step-002
  - step-003
---

# Step 004: Dokumentations-Synchronisation (README.md & mcp-specification.md)

## Bezug

- **Task:** `tokenization-short-tokens`
- **Quelle:** [Konzept.md](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tasks/tokenization-short-tokens/Konzept.md) & [SqlToAiRichtlinien.mdc](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/.agents/rules/SqlToAiRichtlinien.mdc#4)

## Intention

Aktualisierung der Projektdokumentation in `docs/mcp-specification.md` und `README.md` zur Entfernung der `Secret`-Konfiguration und Dokumentation der kompakten Kurz-Tokens.

## Konkrete Änderungen

### Datei 1: `docs/mcp-specification.md` (Zeile 120-145)

- **Was:**
  - Ersetzen der HMAC-SHA256 Erklärung durch die Beschreibung des In-Memory Kurz-Token-Schemas (`§§§T1§§§`).
  - Entfernen des `"Secret": ""` Eintrags im JSON-Beispiel.
  - Entfernen des Hinweises auf `Secret` und `%SQLTOAI_TOKEN_SECRET%`.

### Datei 2: `README.md`

- **Was:** Prüfen und Aktualisieren von Erwähnungen der Tokenisierung (falls vorhanden).

## Tests

- [ ] Manuelle Prüfung der Markdown-Dateien auf Konsistenz und Korrektheit in englischer Sprache.

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Dokumentation vollständig in englischer Sprache verfasst
- [ ] Commit auf aktuellem Branch (`docs(anonymization): update mcp-specification and documentation for short tokenization`)
- [ ] `step-004/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — Dokumentations-Synchronisation (Pflicht) & Englisch als Dokumentationssprache
