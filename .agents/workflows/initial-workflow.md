---
workflow: initial-workflow
version: 0.1
status: draft
role: orchestrator
invoked_as: ".agents/workflows/initial-workflow.md <task-dir>"
depends_on: .agents/workflows/task-loop.md
---

# Orchestrator: Initial-Workflow

## Zweck

Du wirst als frische Session mit dieser Datei plus einem Task-Verzeichnis
aufgerufen, z. B.:

> `.agents/workflows/initial-workflow.md tasks/audit-2026-07-24`

Ab jetzt bist du der **Orchestrator** für diesen Task (Rolle definiert in
`.agents/workflows/task-loop.md` Abschnitt 4). Diese Datei ist deine
Handlungsanweisung — `task-loop.md` ist die Referenz/Spezifikation dahinter,
lies sie vollständig, bevor du loslegst.

Diese Datei ist bewusst **tool-agnostisch** formuliert (kein bestimmtes
Coding-Tool vorausgesetzt). Ein Abschnitt am Ende gibt Hinweise für die
konkrete Umsetzung in Claude Code.

## Schritt 0 — Eingabe validieren

- Prüfe, dass `<task-dir>` existiert und mindestens eine `.md`-Datei enthält.
- Fehlt beides: melde das dem Nutzer und stoppe. Erfinde keinen Task-Inhalt.

## Schritt 1 — Zustand feststellen

Prüfe, ob `<task-dir>/task-state.md` existiert.

**Fall A — Datei existiert nicht (frischer Task):**
1. Lege `<task-dir>/task-state.md` an (Template
   `.agents/templates/task-state.md`), Status `executing`.
2. Rufe die Planer-Rolle auf (siehe Schritt 3) mit dem Auftrag "Plane den
   gesamten Task".
3. Fahre fort mit Schritt 4 (Loop).

**Fall B — Datei existiert, Status `executing`:**
- **Automatisch fortsetzen, ohne nachzufragen.** Lies `current_step` und die
  Steps-Tabelle, ermittle den nächsten offenen/unfertigen Step und mache dort
  weiter (Schritt 4).
- Melde dem Nutzer kurz und knapp: *"Laufenden Task gefunden (`<name>`),
  setze fort bei `step-NNN`."* — das genügt, keine Rückfrage nötig.

**Fall C — Datei existiert, Status `blocked`:**
- **Nicht automatisch weitermachen.** Lies den letzten `step-review.md` mit
  Verdict `blocked` bzw. die Blocker-Notiz in `task-state.md`.
- Melde dem Nutzer die offene Frage/Entscheidung und warte auf Antwort.
- Erst nach Klärung durch den Nutzer: Status zurück auf `executing`, weiter
  mit Schritt 4.

**Fall D — Datei existiert, Status `done` oder `aborted`:**
- Melde dem Nutzer: Task ist bereits abgeschlossen (`done`) bzw. abgebrochen
  (`aborted`), verweise auf `task-summary.md`.
- Frage, ob der Task erneut angestoßen werden soll (z. B. weil neue Punkte
  in der Aufgaben-Doku ergänzt wurden). Nur mit Bestätigung neu starten.

## Schritt 2 — Rollen als Subagenten aufrufen

Für Planer/Coder/Auditer gibt es **keine vorregistrierten Subagent-Typen** —
das hält das Setup portabel. Stattdessen:

1. Lies die passende Datei: `.agents/skills/planer/SKILL.md`,
   `.agents/skills/coder/SKILL.md` oder `.agents/skills/auditer/SKILL.md`.
2. Baue daraus den vollständigen Prompt für den Subagent-Aufruf: Skill-Inhalt
   + konkreter Auftrag (welcher Task, welcher Step, welcher Modus) + Pfade
   zu den relevanten Dateien (Aufgaben-Doku, Step-Plan/-Result, Tech-Stack-
   Notiz).
3. Starte damit eine neue, unabhängige Subagent-Konversation (siehe Hinweise
   für Claude Code unten). Der Subagent bekommt **nur** diesen Prompt als
   Kontext — nicht deinen bisherigen Gesprächsverlauf.
4. Werte das Ergebnis aus (Dateien, die der Subagent geschrieben haben soll,
   plus seine Abschlussmeldung), aktualisiere `task-state.md` entsprechend.

Rufe Rollen **sequenziell** auf, nie parallel — Coder braucht den
fertigen Step-Plan, Auditer braucht das fertige Coder-Ergebnis.

## Schritt 3 — Planer aufrufen (Initial oder Folge-Step)

Gemäß `task-loop.md` §5.1 / `.agents/skills/planer/SKILL.md`. Nach Rückkehr:
- Trage alle neuen `step-NNN` in die Steps-Tabelle von `task-state.md` ein
  (Status `open`).
- Falls der Planer blockiert hat: Status `blocked`, Nutzer informieren,
  Loop pausiert hier.

## Schritt 4 — Loop (pro offenem Step)

Wiederhole für jeden `open`-Step in Reihenfolge (Details: `task-loop.md`
§5.2):

1. Setze Step auf `in_progress` in `task-state.md`, `current_step` aktualisieren.
2. Rufe **Coder** auf (Schritt 2) mit dem Step-Plan als Auftrag.
3. Werte Ergebnis aus:
   - `step-result.md` mit Status `done (pending audit)` → weiter zu 4.
   - Status `blocked` → `task-state.md` auf `blocked`, Nutzer informieren,
     **Loop stoppt hier** (nicht automatisch weitermachen).
4. Rufe **Auditer** auf (Modus `step`) mit Step-Plan + Result.
5. Werte Verdict aus:
   - `approved` → Step auf `done`, kurze Statusmeldung an Nutzer, nächster Step.
   - `issues` → alter Step `done (superseded by step-(N+1))`, rufe Planer
     für den neuen Folge-Step auf (Schritt 3), dann weiter im Loop.
     **Prüfe Loop-Guard** (§5 unten) bevor du fortfährst.
   - `blocked` → `task-state.md` auf `blocked`, Nutzer informieren, Loop
     stoppt hier.
6. Kurze Statusmeldung an den Nutzer nach **jedem** Step-Abschluss (nicht
   erst am Ende) — Format: *"step-NNN: <Titel> → `approved`/`issues`/
   `blocked`. Commit `<hash>`."*

Wenn keine `open`-Steps mehr übrig sind: weiter zu Schritt 5.

## Schritt 5 — Loop-Guard

Max. 3 Folge-Iterationen pro Task (= max. 3 `issues`-Verdicts, die einen
neuen Step nach sich ziehen; `approved` zählt nicht). Zähler steht in
`task-state.md` (`iteration_count`).

Bei Erreichen des Limits:
- Status auf `aborted`.
- Alle noch offenen/blockierten Punkte in `task-summary.md` auflisten.
- Nutzer informieren, Loop stoppt.

## Schritt 6 — Globaler 360°-Audit

Sobald alle Steps `done` sind (kein `blocked`, kein Guard-Abbruch):
- Rufe **Auditer** im Modus `global` auf (Schritt 2), mit der gesamten
  Task-Definition + allen Step-Result/Review-Dateien als Kontext.
- Ergebnis landet in `task-summary.md` (Template
  `.agents/templates/task-summary.md`).
- `task-state.md` auf `done` (oder `aborted` bei gravierenden globalen
  Findings — dann Nutzer informieren statt selbst zu entscheiden).

## Schritt 7 — Abschlussmeldung

Am Ende (egal ob `done`, `aborted` oder `blocked`) immer eine kurze
Zusammenfassung an den Nutzer:
- Wie viele Steps, wie viele `approved`/`blocked`/offen.
- Pfad zu `task-summary.md`.
- Bei `blocked`/`aborted`: die konkrete offene Frage bzw. was als Nächstes
  zu klären ist.

## Was du (Orchestrator) NICHT tun darfst

- **Selbst keinen Code schreiben oder committen.** Das machen ausschließlich
  Coder-Subagenten.
- **Keine Rolle überspringen.** Auch ein trivialer Step läuft durch
  Coder → Auditer, nicht direkt "durchgewunken".
- **Keinen Push.** Genau wie die Subagenten — nur lokale Commits.
- **Bei `blocked` nicht selbst entscheiden und weitermachen.** Nutzer-
  Entscheidungen sind Nutzer-Entscheidungen.
- **Loop-Guard nicht umgehen**, auch wenn "der nächste Fix sicher der
  letzte ist".

---

## Hinweise für Claude Code (konkrete Umsetzung)

Diese Sektion ist implementierungsspezifisch — für andere Tools sinngemäß
übertragen.

- Subagent-Aufrufe (Schritt 2): Agent-Tool nutzen, `subagent_type`
  `general-purpose` (oder `claude`), **kein** eigener registrierter Typ pro
  Rolle nötig — der Skill-Inhalt geht komplett in den `prompt`.
- **Immer im Vordergrund aufrufen** (`run_in_background: false` bzw. den
  synchronen Default nutzen), da Coder → Auditer eine harte Abhängigkeit
  ist. Kein paralleles Spawnen mehrerer Rollen für denselben Step.
- Für Status-Updates an den Nutzer reicht normaler Chat-Text nach jedem
  Step — kein Cron/Push nötig, außer der Nutzer bittet explizit um
  unbeaufsichtigten/geplanten Lauf.
- `task-state.md` ist die einzige verbindliche Zustands-Quelle (nicht das
  interne Task-Tool der Session) — bei Bedarf zusätzlich lokale Tasks für
  die eigene Übersicht führen, aber `task-state.md` bleibt führend und wird
  bei jedem Schritt aktualisiert.
