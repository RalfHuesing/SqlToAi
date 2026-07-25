---
status: executing  # executing | done | aborted
task: <TASK-NAME>
started_at: <ISO-8601>
last_updated: <ISO-8601>
iteration_count: 0  # zählt Folge-Iterations (Steps die ein issues-Verdict nach sich ziehen)
current_step: step-001
---

# Task State: <TASK-NAME>

## Übersicht

- **Task-Status:** `executing`
- **Iterationen:** 0 / 3 (Loop-Guard)
- **Aktueller Schritt:** `step-001`
- **Gestartet:** <ISO-8601>
- **Zuletzt aktualisiert:** <ISO-8601>

## Steps

| Step | Status | Title | Coded | Reviewed | Commit |
|------|--------|-------|-------|----------|--------|
| step-001 | open | <Titel> | - | - | - |
| step-002 | open | <Titel> | - | - | - |
| ... | ... | ... | ... | ... | ... |

<Wird vom Orchestrator gepflegt. Status pro Step: open / in_progress / done / blocked.>

## History

<Append-only Log. Ein Eintrag pro Status-Wechsel oder signifikantem Event.
Format: `- <ISO-8601> — <Was passiert ist>`.>

- <ISO-8601> — Task angelegt
- <ISO-8601> — Planer hat N Steps generiert (Pfade: …)
- <ISO-8601> — step-001: open → in_progress (coder-Aufruf gestartet)
- <ISO-8601> — step-001: in_progress → done (pending audit), commit `<SHA>`
- <ISO-8601> — step-001: auditer-Verdict `approved`
- <ISO-8601> — step-002: open → in_progress
- ...

## Config (optional)

Falls `tasks/<name>/config.md` existiert, hier die Overrides dokumentieren.
Andernfalls gelten die Defaults aus `.agents/workflows/task-loop.md`.

```
max_iterations: 3
build_command: <aus Tech-Stack-Notiz>
test_command: <aus Tech-Stack-Notiz>
target_branch: <aktueller Branch, nicht hartcodiert>
```

## Abbruch-Bedingungen

- **Loop-Limit erreicht** (3 Folge-Iterations): Task → `aborted`,
  siehe `task-summary.md`
- **Blocker aufgetreten** (Step mit Status `blocked`): Loop pausiert,
  Nutzer klärt
