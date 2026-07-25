---
status: done
type: step-review
task: <TASK-NAME>
step: <NNN>              # im Fix-Modus: <NNN>/fix-<XX>
reviewed_by: auditer
reviewed_by_model: <Modell-ID, z. B. claude-sonnet-5>
reviewed_by_model_knowledge_cutoff: <z. B. 2026-01>
reviewed_at: <ISO-8601>
verdict: approved  # approved | issues | blocked
---

# Review Step <NNN>: <Titel>

## Verdict

- [x] **approved** — alle drei Prüfebenen ok
- [ ] **issues** — Fix-Step `step-<NNN>/fix-<XX>` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [ ] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [ ] Rules-Konformität: `.agents/rules/**` eingehalten
- [ ] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [ ] Build: selbst nachgeprüft, grün
- [ ] Tests: selbst nachgeprüft, grün (oder Baselines beachtet)

## Befund

### Plan-Erfüllung

<Pro Punkt aus dem Plan: erfüllt / teilweise / nicht erfüllt.
Bei „teilweise" oder „nicht": konkret was fehlt.>

### Rules-Konformität

<Pro relevante Regel: eingehalten / verletzt. Bei Verletzung: Datei + Zeile
+ Regel-Name + Soll-Zustand.>

### Logische Korrektheit

<Eigene Beobachtungen zur Semantik. „Sieht richtig aus, aber…" ist das,
was hier rein soll. Auch: Test-Coverage-Lücken die nicht aus dem Plan
kommen, aber gefunden wurden.>

### Build-Status

```
<Build-Command + Ergebnis>
```

### Test-Status

```
<Test-Command + Ergebnis>
```

## Findings (bei `issues`)

<Nummerierte Liste, präzise. Jeder Punkt: Datei:Zeile + Was + Wie fixen.>

1. `pfad/zu/datei.cs:42` — <Befund>. **Fix:** <konkret>.
2. ...

## Frage an Nutzer (bei `blocked`)

<Was genau unklar ist. Z. B. „Plan schlägt vor, X in einen Helper zu
extrahieren — das berührt aber Y in einem anderen Modul. Soll das in
diesem Step mitgemacht werden, oder als eigener Refactoring-Step
angelegt werden?">

## Sonstige Beobachtungen (nicht als Issues zu werten)

<Dinge die ich gesehen habe, die aber außerhalb des Scopes dieses Steps
oder dieses Tasks liegen. Pro Punkt: kurze Beschreibung. Diese Liste
fließt ggf. in den globalen Audit am Ende.>
