---
workflow: task-loop
version: 0.1
status: draft
applies_to: "<task-dir>/* (frei wählbarer Ort, siehe Pfad-Hinweis)"
---

# Workflow: Task-Loop (Plan → Code → Audit)

## Pfad-Hinweis

Verweise in diesem Dokument auf andere Dateien **innerhalb von
`dev-loop/`** (`orchestrator.md`, `skills/**`, `templates/**`,
`../planning/…`) sind relativ zu diesem Ordner (`task-loop/`) zu
verstehen — funktionieren unabhängig davon, wo `dev-loop/` in einem
Projekt liegt. Verweise auf **projekteigene** Konventionen
(`.agents/rules/**`, `README.md`, `docs/**`) meinen den Ort relativ zum
**Projekt-Root**, unabhängig von `dev-loop`s eigenem Standort.
`tasks/<name>/` ist als Konvention/Beispiel zu lesen, nicht als feste
Vorgabe — das Task-Verzeichnis kann irgendwo liegen, es wird bei jedem
Aufruf explizit übergeben.

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

1. **Verzeichnis anlegen:** z. B. `tasks/<kurzname>/` (oder wo auch immer
   du deine Task-Verzeichnisse in deinem Projekt anlegst)
2. **Aufgabe dokumentieren:** Eine oder mehrere Markdown-Dateien, die die
   Aufgabe so konkret wie möglich beschreiben (siehe Abschnitt 6 für
   Mindestanforderungen). Existiert noch keine ausgereifte Aufgaben-Doku,
   nur eine grobe Idee: erst `../planning/orchestrator.md` nutzen, um sie
   im Dialog zu `konzept.md` zu schärfen — dessen Exit-Kriterium ist
   genau Abschnitt 6 hier.
3. **Workflow starten:** `orchestrator.md <task-dir>` aufrufen (siehe
   [`README.md`](README.md)).
4. **Warten / Beobachten:** Der Orchestrator meldet sich regelmäßig
   (optional per Cron, falls der Nutzer nicht live dabei ist).
5. **Ergebnis reviewen:** `<task-dir>/task-summary.md` lesen.
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
| **Nutzer** | — | Definiert Aufgabe, startet Workflow, reviewt Summary, klärt Blocker |
| **Orchestrator** | Root-Session | Führt Loop aus, ruft Subagents, pflegt Task-State |
| **Planer** | Subagent | Liest Aufgabe + Anker, generiert/aktualisiert Steps |
| **Coder** | Subagent | Setzt genau einen Step um, schreibt `result.md`, committet |
| **Auditer** | Subagent | Prüft letzten Coder-Output, legt ggf. Fix-Step an |

Die drei Subagent-Rollen (Planer/Coder/Auditer) werden vom Orchestrator per
Agent-Aufruf mit jeweils eigenem Prompt gestartet (siehe
`skills/<rolle>/SKILL.md`).

## 5. Phasen

**Nebenläufigkeit — strikt verboten:** Der gesamte Loop ist **rein
seriell**, ohne Ausnahme. Genau ein Subagent (Planer/Coder/Auditer) läuft
zu jedem Zeitpunkt, egal ob innerhalb eines Steps (Rollen-Reihenfolge)
oder über Steps/Fix-Runden hinweg — auch dann, wenn zwei Steps inhaltlich
unabhängig aussehen (verschiedene Dateien, kein offensichtlicher
Konflikt). Grund: alle Subagenten arbeiten auf demselben Git-Working-Tree
und demselben Branch; Dateiüberlappung ist irrelevant, parallele
Commits/Working-Tree-Änderungen auf demselben Checkout sind ein
Integritätsrisiko, keine Effizienzsteigerung. Der Orchestrator wartet
jeden Subagenten vollständig ab, bevor der nächste startet.

### 5.1 Initialisierung (einmalig pro Task)
- Orchestrator erstellt `<task-dir>/task-state.md` mit Status `executing`
- Orchestrator ruft Planer auf mit Verweis auf das Task-Verzeichnis
- Planer liest Aufgabe + Anker, generiert Steps
- Pro Step: `<task-dir>/step-NNN/step-plan.md` mit Status `open`

### 5.2 Loop (pro Step)
Reihenfolge: **Coder → Auditer → (ggf. Fix-Step) → nächster Step**

Für jeden `open` Step in der Reihenfolge:
1. Orchestrator setzt Step auf `in_progress`
2. Orchestrator ruft Coder mit Step-Plan als Input
3. Coder implementiert, schreibt `step-NNN/step-result.md`, committet
4. Orchestrator setzt Step auf `done (pending audit)`
5. Orchestrator ruft Auditer mit Step-Plan + Result als Input
6. Auditer prüft, schreibt `step-NNN/step-review.md`:
   - **approved** → Orchestrator setzt Step auf `done`
   - **issues** → Orchestrator legt einen **Fix-Step** an: `step-NNN/fix-XX/`
     (`XX` = nächste freie Nummer *innerhalb* dieses Steps, Start `01`)
     mit Status `open`. Äußerer Step wird auf `done (fix-XX pending)`
     gesetzt. Der Fix-Step durchläuft denselben Zyklus (Planer im
     Fix-Modus → Coder → Auditer) — siehe §5.2.1.
   - **blocked** → Auditer markiert Step als `blocked`, Loop pausiert

### 5.2.1 Fix-Steps (Nachbesserung innerhalb eines Steps)

Ein `issues`-Verdict erzeugt **keinen neuen Top-Level-Step**, sondern einen
Fix-Step *innerhalb* des betroffenen Steps: `step-NNN/fix-XX/`, mit
denselben drei Dateien (`step-plan.md`, `step-result.md`, `step-review.md`)
wie ein normaler Step.

**Warum ein eigener Namensraum statt `step-(N+1)`:** Der Planer legt zu
Beginn i. d. R. **alle** Steps des Tasks auf einmal an — ein Step pro
unabhängigem Arbeitspunkt. `step-(N+1)` ist zu diesem Zeitpunkt fast immer
bereits durch ein anderes, unabhängiges Thema belegt — eine Kollision im
Namensraum ist bei Batch-Planung der Normalfall, keine Ausnahme.
Fix-Steps in einem eigenen Unterordner sind strukturell kollisionsfrei.

Ablauf:
1. Orchestrator ermittelt die nächste freie `fix-XX` unter `step-NNN/`
   (höchste vorhandene + 1, Start bei `01`).
2. Orchestrator ruft den **Planer im Fix-Modus** auf (siehe
   `skills/planer/SKILL.md`): Input ist der `step-review.md`-Befund
   (Abschnitt „Findings"), nicht die gesamte Aufgaben-Doku. Output:
   `step-NNN/fix-XX/step-plan.md`.
3. Danach normaler Coder → Auditer-Zyklus, Ergebnisse landen in
   `step-NNN/fix-XX/step-result.md` / `step-review.md`.
4. `approved` → gesamter `step-NNN` (inkl. aller Fix-Runden) geht auf
   `done`. `issues` → nächste `fix-XX`, sofern Budget nicht erschöpft
   (§7.5). `blocked` → wie gehabt, Loop pausiert.

Ein Fix-Step betrifft ausschließlich den Scope des ursprünglichen
Findings — keine Ausweitung auf andere Teile des Steps oder des Tasks.

Findet der **globale 360°-Audit** (§5.3) am Ende einen komplett neuen,
keinem bestehenden Step zuordenbaren Punkt, ist das kein Fix-Step, sondern
ein echter neuer Top-Level-Step — nummeriert als höchste vorhandene
`step-NNN` + 1, nicht als Folge des zuletzt geprüften Steps.

### 5.3 Globaler 360°-Audit (am Ende)
Nachdem alle Steps `done` sind:
- Orchestrator ruft Auditer mit **gesamter Task-Definition + allen
  Result/Review-MDs + Projekt-Code** als Input auf
- Auditer prüft: passt das Ergebnis zur ursprünglichen Intention des Tasks?
  Sind keine Seiteneffekte übersehen worden? Läuft Build/Test?
- Ergebnis in `<task-dir>/task-summary.md`
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
Jede Datei in `<task-dir>/` und `<task-dir>/step-NNN/` beginnt mit
YAML-Frontmatter. Das `status`-Feld ist die Quelle der Wahrheit für
„wer ist dran".

### 7.2 Schritt-Größe
Die Schritt-Größe wird vom Planer entschieden (siehe
`skills/planer/SKILL.md`). Es gibt keine fixe Obergrenze — der Planer
balanciert zwischen „in einem Commit commitbar", „in einer Review-Runde
prüfbar" und „kleiner als die Gesamtaufgabe".

### 7.3 Git-Strategie
- Alles auf dem **aktuellen Branch** (kein hartcodierter Branch — der
  Nutzer arbeitet, wo er arbeitet)
- Conventional Commits, deutsche Imperativ-Form (sofern Projekt-Rules
  nichts anderes vorgeben — siehe `skills/coder/SKILL.md`)
- **Task-Doku wird mitcommittet, nicht nur auf der Platte belassen** —
  jeder Step hinterlässt eine nachvollziehbare Commit-Historie seiner
  Zustände. Pro Step entstehen dabei mehrere kleine Commits statt einem
  großen:
  1. **Code-Commit** (Coder): Code + Tests + ggf. Produkt-Doku
  2. **Doku-Commit** (Coder): `step-plan.md`-Status + `step-result.md`
  3. **Planungs-Commit** (Orchestrator): neue `step-plan.md`-Datei(en)
     nach jedem Planer-Aufruf
  4. **Review-Commit** (Orchestrator): `step-review.md` +
     Status-Update in `step-plan.md` nach jedem Auditer-Aufruf

  Grund für mehrere statt eines Commits: `step-result.md` referenziert
  den Hash des Code-Commits — der kann erst *nach* dem Code-Commit
  bekannt sein, ein einziger gemeinsamer Commit wäre also nur per
  nachträglichem Amend möglich, was hier bewusst vermieden wird (siehe
  `skills/coder/SKILL.md` Schritt 5-7).
- **Kein Push durch den Workflow** — der Nutzer pusht selbst, wenn er
  bereit ist. Der Workflow macht nur lokale Commits.
- **Git-Historie ist geschriebene Vergangenheit — nach einem Commit wird
  er nie wieder verändert.** Das gilt absolut, für jeden Commit,
  unabhängig davon, wie unwichtig er wirkt, wie unpassend platziert er
  aussieht, wie lang sein Subject ist, oder ob er schon von anderen
  Dateien per Hash referenziert wird. **Ausnahmslos verboten:**
  `git commit --amend`, `git rebase` (auch `-i`), `git reset --hard` auf
  bereits committete Commits, `git filter-branch`/`filter-repo`,
  Force-Push. Das gilt für **jede** Rolle, insbesondere den
  Orchestrator selbst — nicht nur für den Coder (dessen spezifisches
  „kein Amend fürs Code-Ergebnis" oben ist nur ein Sonderfall dieser
  allgemeinen Regel, nicht deren ganzer Umfang).
  - **Fällt auf, dass etwas vergessen wurde** (z. B. Task-Doku, die vor
    Einführung einer neuen Regel liegen geblieben ist): einfach **jetzt**
    committen, an der aktuellen `HEAD`-Position, mit einer ehrlichen
    Commit-Message, die das als Nachtrag kennzeichnet (z. B.
    `chore(task): Nachtrag — vor Doku-Commit-Regel liegen gebliebene
    Step-Dateien committen`).
  - **Nie versuchen, die Historie so aussehen zu lassen, als wäre der
    Commit schon immer an der „richtigen" chronologischen Stelle
    gewesen.** Ein Rebase zu diesem Zweck schreibt jeden nachfolgenden
    Commit-Hash um — auch wenn der Inhalt dabei unverändert bleibt,
    werden dadurch alle bereits geschriebenen Commit-Hash-Referenzen in
    `step-result.md`/`step-review.md`/`task-state.md` ungültig
    (verwaiste, nicht mehr erreichbare Hashes), und Zeitstempel/
    Reihenfolge werden für jeden, der die Historie später liest,
    unzuverlässig.

### 7.4 Build/Test-Erkennung
Der Planer leitet Build-/Test-Commands **aus dem Projekt** ab:
- `.csproj`/`.sln` → `dotnet build` / `dotnet test`
- `pyproject.toml`/`pytest.ini` → `pytest`
- `package.json` mit `test`-Script → `npm test` / `pnpm test`
- `Cargo.toml` → `cargo build` / `cargo test`
- `go.mod` → `go build ./...` / `go test ./...`
- etc.

Coder und Auditer nutzen diese Commands. Sie sind **nicht** im Workflow
hartcodiert.

### 7.5 Loop-Guard (Fix-Budget)
- **Max 3 Fix-Runden pro Step** (`step-NNN/fix-01` .. `fix-03`). Der
  Guard ist bewusst **pro Step**, nicht pro Task: Der Planer legt mehrere
  unabhängige Steps auf einmal an, und dass mehrere davon je einmal
  nachgebessert werden müssen, ist normal und kein Alarmsignal. Ein
  einzelner Step, der auch nach 3 Fix-Runden nicht grün wird, ist das
  eigentliche Alarmsignal.
- Bei Erreichen des Limits für einen Step: dieser Step → `blocked` (wie
  in §8 beschrieben — der Loop pausiert, Nutzer klärt).
- **Zusätzlicher Task-weiter Not-Anker:** Bei insgesamt mehr als
  `max_total_fix_rounds` (Default 12) Fix-Runden über alle Steps des
  Tasks hinweg → gesamter Task auf `aborted`, unabhängig vom Status der
  Einzel-Steps. Schutz gegen systemische Probleme (z. B. eine falsche
  Tech-Stack-Notiz), die sich durch viele Steps zieht.
- Grund für Budget generell: Endlos-Loops verhindern. Wenn ein Step auch
  nach 3 Fix-Runden nicht grün wird, stimmt meist der Step-Scope oder der
  Ansatz nicht.
- Konfigurierbar pro Task via `<task-dir>/config.md` (Felder
  `max_fix_rounds_per_step`, Default 3; `max_total_fix_rounds`, Default 12).

## 8. Edge-Cases & Failure-Modes

| Situation | Verhalten |
|---|---|
| Coder schreibt kein `result.md` | Step bleibt auf `in_progress`, nach Timeout → `blocked` |
| Coder committet nicht | result.md fehlt Commit-Hash → `blocked` |
| Auditer findet Code-Verstoß gegen `.agents/rules` | Fix-Step mit konkretem Fix-Plan |
| Auditer will größeren Umbau vorschlagen | **Nicht erlaubt** — Auditer blockt mit `blocked`, Nutzer entscheidet |
| Build/Test schlägt fehl | Coder fixt im selben Step; falls nicht möglich → `blocked` |
| Planer erkennt: Aufgabe zu vage | Blockt sofort mit Begründung |
| Nutzer ergänzt während Loop neue Findings | Manueller Eingriff: Loop pausieren, neue Files rein, Loop fortsetzen |
| Diskspace/Git-Konflikt/was auch immer | `blocked`, Nutzer klärt |

## 9. Deliverables

Am Ende eines erfolgreichen Loops existieren:
- `<task-dir>/task-summary.md` — was gemacht wurde, Status, offene Punkte
- `<task-dir>/task-state.md` — finale History
- `<task-dir>/step-NNN/step-plan.md` — was geplant war
- `<task-dir>/step-NNN/step-result.md` — was gemacht wurde
- `<task-dir>/step-NNN/step-review.md` — was auditiert wurde
- `<task-dir>/step-NNN/fix-XX/…` — sofern nachgebessert wurde, dieselben
  drei Dateien pro Fix-Runde
- Mehrere Commits in Git pro Step (Code, Doku, Planung, Review — siehe
  §7.3), alle lokal, nicht gepusht — zusammen eine vollständige,
  lesbare Historie aller Step-Zustände und Fix-Runden

## 10. Wartung & Versionierung

- Änderungen am Workflow → Version bumpen, hier dokumentieren
- Skill-Änderungen (Planer/Coder/Auditer) → Changelog im jeweiligen Skill
- Breaking Changes am Status-Modell oder an Konventionen → Workflow-Version
  major bump
