---
task: sql-performance
type: tech-debt-log
maintained_by: kritiker
last_updated: 2026-08-03T10:06:00Z
---

# Tech-Debt-Log: sql-performance

Append-only. Jeder Eintrag ist eine vom Kritiker während eines
Step-Reviews beobachtete, aber bewusst **nicht** gefixte Auffälligkeit
außerhalb des Scopes des jeweiligen Steps.

**Priorität ist reine Sortierhilfe für den Menschen, kein Auslöser.**

## Index

| ID | Bereich / Datei | Priorität | Kurzfassung |
|---|---|---|---|
| TD-001 | `docs/mcp-specification.md` | niedrig | Datei komplett auf Deutsch verfasst, obwohl `SqlToAiRichtlinien.mdc` Englisch für `docs/**` vorschreibt; step-004 hat neue Sätze stilkonsistent ebenfalls auf Deutsch ergänzt. |

## Einträge

### TD-001 — `mcp-specification.md` nicht englischsprachig [Priorität: niedrig]

- **Gefunden in:** step-004 (Kritiker-Review vom 2026-08-03)
- **Ort:** `docs/mcp-specification.md` (gesamte Datei)
- **Befund:** `SqlToAiRichtlinien.mdc` Abschnitt 4 schreibt für `docs/**` englische Sprache vor.
  `mcp-specification.md` ist komplett auf Deutsch verfasst — ein bereits vor `sql-performance`
  bestehender, projektweiter Zustand. `step-004` hat die neuen Punkte 12/14/15 bewusst
  stilkonsistent ebenfalls auf Deutsch ergänzt (siehe Step-Plan „Bekannte Ausnahmen"), statt die
  Sprachvorgabe an dieser Stelle isoliert durchzusetzen.
- **Warum nicht sofort gefixt:** Volltext-Übersetzung der gesamten Datei ist deutlich größer als
  der Scope von EPIC-04 (der nur die *inhaltliche* Aktualität der Punkte 12/14/15 zum Ziel hatte)
  und beträfe auch alle vorherigen Abschnitte, nicht nur die in `sql-performance` geänderten.
- **Vorschlag:** Eigenes Epic/Task „mcp-specification.md ins Englische übersetzen" anlegen, falls
  gewünscht.
- **Status:** offen
