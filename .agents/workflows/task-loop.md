---
workflow: task-loop
version: 0.1
status: draft
applies_to: tasks/*
---

# Workflow: Task-Loop (Plan → Code → Audit)

## 1. Intention

Dieser Workflow zerlegt eine vom Nutzer definierte Aufgabe in kleine, präzise
umsetzbare Schritte, setzt diese autonom um und prüft sie gegen den
ursprünglichen Auftrag. Das Ergebnis ist ein konsistenter, getesteter,
dokumentierter Stand des Projekts — ohne dass der Nutzer jeden Schritt
manuell anstoßen muss.

Der Workflow ist gedacht für Arbeiten, die **mehrere aufeinander aufbauende
Änderungen** erfordern (Audits, Refactorings, Feature-Implementierungen).
Für triviale Einzeländerungen ist er Overkill — die direkt im Editor machen.

## 2. Anwendung (aus Nutzersicht)

1. **Verzeichnis anlegen:** `tasks/<kurzname>/` (z. B. `tasks/audit-2026-07-24/`)
2. **Aufgabe dokumentieren:** Eine oder mehrere Markdown-Dateien, die die
   Aufgabe so konkret wie möglich beschreiben (siehe Abschnitt 6 für
   Mindestanforderungen).
3. **Workflow starten:** Im Mavis-Root sagen:
   > „Starte Workflow `task-loop` für `tasks/<kurzname>/`."
4. **Warten / Beobachten:** Der Orchestrator meldet sich regelmäßig
   (optional per Cron, falls der Nutzer nicht live dabei ist).
5. **Ergebnis reviewen:** `tasks/<kurzname>/task-summary.md` lesen.
   - Alle Punkte grün → fertig
   - Punkte `blocked` → Nutzer entscheidet, Loop kann nach Klärung
     fortgesetzt werden
   - `aborted` → Loop-Limit erreicht, offene Punkte im Summary dokumentiert

## 3. Voraussetzungen

Damit der Workflow sinnvoll laufen kann, müssen folgende Anker im Projekt
vorhanden sein. Der Planer **muss** diese lesen, bevor er Steps generiert.

- **Projektkonventionen:** `.agents/rules/**` (oder vergleichbar) — Coding-
  Style, Architektur, Sicherheitsleitplanken, Test-Konventionen
- **Projektdokumentation:** `README.md`, `docs/**` (oder vergleichbar) —
  was macht die Anwendung, wie wird sie gebaut/getestet
- **Projekt selbst:** Build-/Test-Konfigurationen, CI-Pipelines — der
  Planer leitet daraus ab, welche Build-/Test-Commands gelten
- **Git-Repository:** Commits pro Step sind Standard (siehe 7.3)

Fehlen Anker, fragt der Planer nach oder blockiert mit `blocked`.

## 4. Rollen

| Rolle | Wer | Aufgabe |
|---|---|---|
| **Nutzer** | Ralf | Definiert Aufgabe, startet Workflow, reviewt Summary, klärt Blocker |
| **Orchestrator** | Mavis (root-session) | Führt Loop aus, ruft Subagents, pflegt Task-State |
| **Planer** | Subagent | Liest Aufgabe + Anker, generiert/aktualisiert Steps |
| **Coder** | Subagent | Setzt genau einen Step um, schreibt `result.md`, committet |
| **Auditer** | Subagent | Prüft letzten Coder-Output, legt ggf. Folge-Step an |

Die drei Subagent-Rollen (Planer/Coder/Auditer) werden vom Orchestrator per
`task`-Tool mit jeweils eigenem System-Prompt gestartet (siehe
`.agents/skills/<rolle>/SKILL.md`).

## 5. Phasen

### 5.1 Initialisierung (einmalig pro Task)
- Orchestrator erstellt `tasks/<name>/task-state.md` mit Status `executing`
- Orchestrator ruft Planer auf mit Verweis auf das Task-Verzeichnis
- Planer liest Aufgabe + Anker, generiert Steps
- Pro Step: `tasks/<name>/step-NNN/step-plan.md` mit Status `open`

### 5.2 Loop (pro Step)
Reihenfolge: **Coder → Auditer → (ggf. neuer Step) → nächster Step**

Für jeden `open` Step in der Reihenfolge:
1. Orchestrator setzt Step auf `in_progress`
2. Orchestrator ruft Coder mit Step-Plan als Input
3. Coder implementiert, schreibt `step-NNN/result.md`, committet
4. Orchestrator setzt Step auf `done (pending audit)`
5. Orchestrator ruft Auditer mit Step-Plan + Result als Input
6. Auditer prüft, schreibt `step-NNN/review.md`:
   - **OK** → Orchestrator setzt Step auf `done`
   - **Issues** → Auditer legt neuen `step-(N+1)` an mit Status `open`,
     alter Step wird auf `done (superseded by step-(N+1))` gesetzt
   - **Blocker** → Auditer markiert Step als `blocked`, Loop pausiert

### 5.3 Globaler 360°-Audit (am Ende)
Nachdem alle Steps `done` sind:
- Orchestrator ruft Auditer mit **gesamter Task-Definition + allen
  Result/Review-MDs + Projekt-Code** als Input auf
- Auditer prüft: passt das Ergebnis zur ursprünglichen Intention des Tasks?
  Sind keine Seiteneffekte übersehen worden? Läuft Build/Test?
- Ergebnis in `tasks/<name>/task-summary.md`
- Task-State auf `done` (oder `aborted` bei gravierenden Findings)

## 6. Mindestanforderungen an die Aufgaben-Doku

Der Planer akzeptiert freie Markdown-Struktur, aber die Aufgabe **muss**
folgendes enthalten (sonst wird der Planer blocken):

- **Was** soll erreicht werden (Ziel in 2-5 Sätzen)
- **Warum** (Kontext, Hintergrund, Constraints)
- **Wo** im Projekt (Dateien, Module, Features)
- **Wie** (konkretes Vorgehen, Code-Skizzen, Referenzen) — so detailliert
  wie möglich
- **Definition of Done** (welche Tests müssen grün sein, welche Doku
  aktualisiert werden, welche Commits/Branches)

Optional aber hilfreich: Severity/Priorisierung der einzelnen Punkte.

## 7. Konventionen

### 7.1 Status-Header (YAML-Frontmatter)
Jede Datei in `tasks/<name>/` und `tasks/<name>/step-NNN/` beginnt mit
YAML-Frontmatter. Das `status`-Feld ist die Quelle der Wahrheit für
„wer ist dran".

### 7.2 Schritt-Größe
Die Schritt-Größe wird vom Planer entschieden (siehe
`.agents/skills/planer/SKILL.md`). Es gibt keine fixe Obergrenze — der
Planer balanciert zwischen „in einem Commit commitbar", „in einer
Review-Runde prüfbar" und „kleiner als die Gesamtaufgabe".

### 7.3 Git-Strategie
- Pro Step ein eigener Commit auf dem **aktuellen Branch** (kein
  hartcodierter Branch — der Nutzer arbeitet, wo er arbeitet)
- Conventional Commits, deutsche Imperativ-Form
- Pro Step: Code + Tests + Doku in **einem** Commit
- **Kein Push durch den Workflow** — der Nutzer pusht selbst, wenn er
  bereit ist. Der Workflow macht nur lokale Commits.

### 7.4 Build/Test-Erkennung
Der Planer leitet Build-/Test-Commands **aus dem Projekt** ab:
- `.csproj`/`.sln` → `dotnet build` / `dotnet test`
- `pyproject.toml`/`pytest.ini` → `pytest`
- `package.json` mit `test`-Script → `npm test` / `pnpm test`
- `Cargo.toml` → `cargo build` / `cargo test`
- etc.

Coder und Auditer nutzen diese Commands. Sie sind **nicht** im Workflow
hartcodiert.

### 7.5 Loop-Guard
- **Max 3 Iterationen** pro Task (= max 3 Coder-Aufrufe, die einen
  Folge-Step nach sich ziehen). Ein direkter `approved` zählt nicht
  gegen das Limit.
- Bei Erreichen: Task-State auf `aborted`, alle offenen Steps im Summary
  gelistet, Nutzer entscheidet.
- Grund: Endlos-Loops verhindern. Bei 3 Folge-Iterations ohne Lösung
  stimmt meist die Aufgaben-Definition oder der Ansatz nicht.
- Konfigurierbar pro Task via `tasks/<name>/config.md` (Feld
  `max_iterations`).

## 8. Edge-Cases & Failure-Modes

| Situation | Verhalten |
|---|---|
| Coder schreibt kein `result.md` | Step bleibt auf `in_progress`, nach Timeout → `blocked` |
| Coder committet nicht | result.md fehlt Commit-Hash → `blocked` |
| Auditer findet Code-Verstoß gegen `.agents/rules` | Folge-Step mit konkretem Fix-Plan |
| Auditer will größeren Umbau vorschlagen | **Nicht erlaubt** — Auditer blockt mit `blocked`, Nutzer entscheidet |
| Build/Test schlägt fehl | Coder fixt im selben Step; falls nicht möglich → `blocked` |
| Planer erkennt: Aufgabe zu vage | Blockt sofort mit Begründung |
| Nutzer ergänzt während Loop neue Findings | Manueller Eingriff: Loop pausieren, neue Files rein, Loop fortsetzen |
| Diskspace/Git-Konflikt/was auch immer | `blocked`, Nutzer klärt |

## 9. Deliverables

Am Ende eines erfolgreichen Loops existieren:
- `tasks/<name>/task-summary.md` — was gemacht wurde, Status, offene Punkte
- `tasks/<name>/task-state.md` — finale History
- `tasks/<name>/step-NNN/step-plan.md` — was geplant war
- `tasks/<name>/step-NNN/result.md` — was gemacht wurde
- `tasks/<name>/step-NNN/review.md` — was auditiert wurde
- N Commits in Git, einer pro Step (lokal, nicht gepusht)

## 10. Wartung & Versionierung

- Änderungen am Workflow → Version bumpen, hier dokumentieren
- Skill-Änderungen (Planer/Coder/Auditer) → Changelog im jeweiligen Skill
- Breaking Changes am Status-Modell oder an Konventionen → Workflow-Version
  major bump
