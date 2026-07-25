---
status: open
type: step-plan
task: <TASK-NAME>
step: <NNN>              # im Fix-Modus: <NNN>/fix-<XX>
title: "<Titel des Steps>"
estimated_risk: <low|medium|high>  # Einschätzung des Planers, siehe SKILL.md. Aktuell rein informativ, keine automatische Konsequenz.
created_by: planer
created_by_model: <Modell-ID, z. B. claude-sonnet-5>
created_by_model_knowledge_cutoff: <z. B. 2026-01>
created_at: <ISO-8601>
related_to: []
---

# Step <NNN>: <Titel>

## Bezug

- **Task:** `<TASK-NAME>`
- **Quelle:** <Verweis auf den Teil der Aufgaben-Doku, z. B. "audit-2026-07-24#12" oder "Feature X — Sektion 3">
- **Phase / Priorität:** <falls in der Aufgaben-Doku definiert>

## Intention

<2-3 Sätze: Was soll nach diesem Step erreicht sein? Warum genau so?>

## Konkrete Änderungen

### Datei 1: `pfad/zu/datei.cs` (Zeile X-Y)

- **Was:** <konkrete Änderung, nicht "implementiere X" sondern "in Foo.cs Zeile 13 ergänze: ...">
- **Warum:** <kurz>

### Datei 2: ...

<Wiederholen für jede betroffene Datei. Wenn keine Datei, sondern z. B. nur Doku: hier explizit "Doku: README.md, Sektion X — Absatz Y erweitern um Z.">

## Tests

- [ ] <Test 1 — exakter Name oder Beschreibung>
- [ ] <Test 2>
- [ ] <ggf. manueller Smoke-Test>

<Wenn keine Tests nötig sind (z. B. reine Doku-Änderung): "Keine — Begründung warum.">

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz grün (0 Warnings, 0 Errors)
- [ ] Test-Command aus Tech-Stack-Notiz grün (Ausnahmen siehe „Bekannte Ausnahmen")
- [ ] Commit auf aktuellem Branch (Conventional Commit, aus Tech-Stack-Notiz/Projekt-Rules abgeleitete Sprache/Form)
- [ ] `step-NNN/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/<datei>#<Abschnitt>` — <was daran relevant ist> (projekt-root-relativ)
- <weitere>

## Bekannte Ausnahmen

- <Test der flaky ist, mit Begründung warum ignorierbar>

## Code-Skizze (optional)

```
// Wenn hilfreich: ein Code-Snippet das zeigt, wie die Änderung aussehen soll
```

## Notes

<Alles was sonst noch relevant ist: Edge-Cases, Stolperfallen, Hinweise auf bestehende Patterns die zu nutzen sind, …>
