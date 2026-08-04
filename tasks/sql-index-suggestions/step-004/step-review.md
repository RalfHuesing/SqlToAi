---
status: done
type: step-review
task: sql-index-suggestions
step: 004
epic: EPIC-03
step_type: single
reviewed_by: kritiker
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-05T11:00:00+02:00
verdict: approved
tech_debt_ids: [TD-001, TD-002, TD-003, TD-004, TD-005, TD-006, TD-007]  # step-004-Spezialfall: alle 7 betroffen (Status-Updates an 6, TD-003 unverändert) — keine NEU erzeugten TDs in diesem Review
---

# Review Step 004: Post-Completion Tech-Debt Cleanup

## Verdict

- [x] **approved** — alle vier Prüfebenen ok
- [ ] **issues** — Fix-Step `step-<NNN>/fix-<XX>` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `<rules_dir>/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Konzept-Treue: passt die Umsetzung zu `konzept.md` (Scope, Non-Goals, Muss-Haben)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (oder Baselines beachtet)

## Befund

### Plan-Erfüllung

Alle neun DoD-Punkte erfüllt: `konzept.md:172` zeigt jetzt `IX_Orders_CustomerId__OrderDate` (eine Wort-Ersetzung, exakt der im Plan vorgegebenen Diff-Linie entsprechend), `tech-debt.md` trägt für TD-001 den „erledigt in step-004"-Status mit Begründung und Verweis auf den Konzept-Edit, für TD-002/004/005/006/007 den „out of scope, won't fix in diesem Task"-Status mit expliziter pro-Eintrag-Begründung, TD-003 unverändert (bereits in `step-002` erledigt), Frontmatter `last_updated` von `2026-08-05T10:00:00+02:00` auf `2026-08-05T10:40:00+02:00` aktualisiert, `task-summary.md` enthält am Ende den neuen Abschnitt „Post-Completion-Tech-Debt-Cleanup (step-004)" mit den drei verlangten Unterabschnitten (Ergebnis pro TD / endgültige Statistik / Epic-/Commit-Verweise), `step-004/step-plan.md`-Frontmatter-Status von `open` auf `done (pending audit)`, Commits `651c526` (Code/Markdown) und `7c92a3a` (Result-Doku) auf `main` mit korrektem Conventional-Commit-Format, deutsch, imperativ, Subject-Längen 60 Zeichen (Code) und 51 Zeichen (Doku), beide mit Suffix `[sql-index-suggestions]`. `roadmap.md` EPIC-03 war bereits durch den Planer im selben Aufruf angelegt; das ist konsistent zur Plan-Notiz, dass `roadmap.md` nicht in den Coder-Scope fällt. Commit-Strategie-Aufteilung (Code + Doku) ist die vom Plan/Orchestrator explizit erlaubte Variante (Plan-Notes Zeile 216; Orchestrator-Hinweis: „wenn du aus Konsistenz-Gründen dennoch einen zweiten Doku-Commit willst, ist das OK, aber nicht erforderlich"), und der Doku-Commit referenziert den Code-Commit-Hash konsistent im `step-result.md`-Frontmatter.

### Rules-Konformität

Plan §Rules-Refs bestätigt: keine direkt anwendbaren Regeln (`SqlToAiRichtlinien.mdc` §4 Doku-Sync-Pflicht entkräftet, weil dieser Step selbst Doku-Edit ist; §5 Zero-Warning-Direktive entkräftet, weil kein C#-Code angefasst wird; `AiNetLinter.mdc` entkräftet, weil keine C#-Datei geändert wird). Die eingehaltenen Doku-/Commit-Konventionen (Conventional Commit, deutsch, imperativ, Suffix `[sql-index-suggestions]`, Subject ≤ 72 Zeichen, repo-relative Markdown-Links) sind in beiden Commits sichtbar korrekt. `step-004/step-result.md` dokumentiert die `git status`-CRLF-Warnungen transparent als normales Windows-Verhalten ohne Auswirkung auf den Commit-Inhalt.

### Logische Korrektheit

Konzept-Edit `IX_Orders_CustomerId_OrderDate` → `IX_Orders_CustomerId__OrderDate` ist die korrekte Anwendung der in `step-001` deliberate gewählten Konvention (einfacher `_` zwischen Tabellenname und erster Spalte, `__` als Trenner zwischen mehreren Schlüsselspalten — `step-001/step-result.md` Zeile 80-94 dokumentiert die Entscheidung gegen die Plan-Code-Skizze und für die Plan-Prose-Lesart; `step-001/step-review.md` Zeile 41 segnet das explizit ab). Repo-weite Suche bestätigt: nur ein einziges Vorkommen in `konzept.md` (Zeile 172); alle anderen Erwähnungen in `tech-debt.md`, `task-summary.md` (inkl. „Konzept erfüllt?"-Block Zeile 78 und „Offene Punkte"-Liste Zeile 205-243) und `roadmap.md` (Zeile 82-86) referenzieren die Diskrepanz dokumentarisch und müssen beide Formen zeigen. Die fünf out-of-scope-Begründungen sind alle substantiiert und halten einer Konzept-Gegenprüfung stand: TD-002 (Konzept §Muss-Haben, §Wie-Idee-1 Zeilen 157-172, §DoD erwähnen `DESC` nicht; Konzept-Beispiel Zeile 161-172 hat keine absteigend indizierte Spalte — bestätigt), TD-004 (Konzept §Zielplattformen nennt „kein neuer Stack" ohne SQL-Server-Version; §Muss-Haben Idee 2 und §DoD ebenfalls ohne Versionsangabe — bestätigt), TD-005 (Konzept §DoD für Idee 2 verlangt nur „Integrationstest gegen eine echte Test-DB", keine Aussage zur Setup-Reproduzierbarkeit — bestätigt), TD-006 (Konzept §DoD Zeile 210-211 verlangt nur „Graceful Degradation verifiziert (Unit- oder Integrationstest)", keine Aussage zur Toleranz einzelner Tests — bestätigt), TD-007 (Konzept schweigt vollständig über Test-Strategie, Mock vs. Integration, statische Schema-Validierung, CI/CD-Pflicht-Integrationstests — bestätigt). Die Status-Update-Sequenz in `tech-debt.md` ist konsistent zum TD-003-Pattern (Index ohne Status-Spalte, Status ausschließlich im Volltext — bewusste Designentscheidung des Planers, korrekt umgesetzt). Die Begriffswahl „won't fix in diesem Task" (nicht „won't fix ever") ist sauber: sie reserviert die Option, in einem Folge-Task als eigenes Epic neu zu bewerten.

### Konzept-Treue (Ebene 4)

Kein Non-Goal aus `konzept.md` verletzt (kein DDL-Render in `sql_suggest_indexes`, keine DTA-Anbindung, kein `DBCC AUTOPILOT`, keine Schreiboperation, keine Harmonisierung der anderen dokumentierten Konzept-Plan-Inkonsistenzen wie `avg_user_cost` vs. `avg_total_user_cost`). Kein Muss-Haben-Punkt fehlt. Die Entscheidung „Konzept an Code anpassen" (Option 2 in der Orchestrator-Befragung) ist konsistent zur `step-001`-Deliberation (Planer hat `__` als Spalten-Trenner bewusst gewählt, Kritiker hat in `step-001` bestätigt: „Beide Formen sind gültige SQL-Identifier; die Prose-Lesart (`__` als Spalten-Trenner) ist explizit Planer-/Coder-Spec") und zur Konzept-Charakterisierung der Pfeil-Form als illustratives Beispiel (kein normativer Spec-Punkt in §Muss-Haben/§DoD). Die TD-001-Statusbegründung in `tech-debt.md` referenziert die Option-2-Klassifizierung explizit („Klassifizierung 2026-08-05 per Nutzer-Vorgabe (Option 2 in der Orchestrator-Befragung: 'Konzept an Code anpassen')"), Entscheidungsgrundlage ist transparent dokumentiert. Die `roadmap.md` EPIC-03-Beschreibung entspricht 1:1 dem Plan (Post-Completion-Reopen-Auftrag, Klassifizierungs-Policy, TD-001-in-scope, TD-002/004/005/006/007 out-of-scope, kein Code-/Test-Change, Risiko `low`, step_type `single`).

### Build-/Test-Status

```
dotnet build SqlToAi.slnx  → grün (0 Warnungen, 0 Fehler, TreatWarningsAsErrors=true)
dotnet test  SqlToAi.slnx  → grün (522 Tests, 0 Fehler, 0 übersprungen, ~6 s, inkl. AiNetLinterTests.RecreateBaseline)
```

Eigene Reproduktion am 2026-08-05: 522/522 grün. Die Markdown-Edits haben die Test-Pipeline erwartungsgemäß nicht berührt — die 522-Konstante aus dem `step-003`-Reopen-Lauf ist unverändert.

## Sonstige Beobachtungen / MINOR / NITPICK

- **Konsistenz im `task-summary.md`:** Der neue Post-Completion-Abschnitt (Zeile 310-371) sagt „0 offen-unmarkiert + 2 erledigt (TD-003, TD-001) + 5 out-of-scope-markiert (TD-002, TD-004, TD-005, TD-006, TD-007)"; die ältere „Offene Punkte"-Liste (Zeile 205-243) führt TD-001 weiterhin mit `[ ]` als „offen" und TD-002/004/005/006/007 ebenfalls als „offen". Die Tech-Debt-Zusammenfassung (Zeile 169-193) zählt TD-001 noch als „mittel", nicht „erledigt". Der Plan-DoD verlangt nur den neuen Abschnitt am Ende (kein Anfassen der alten Sichten), und der Coder hat den Plan 1:1 umgesetzt — die Inkonsistenz ist also eine bewusste Designentscheidung, kein Coder-Fehler. Sie wird hier nur festgehalten, damit sie im Folge-Loop (z. B. durch den globalen Kritiker oder in einem späteren Aufräum-Pass) bewusst bereinigt werden kann. **Kein Finding** — innerhalb des Step-Scopes kein Handlungsbedarf.

## Tech-Debt-Einträge aus diesem Review

Keine neuen Einträge — in diesem Step werden die bestehenden Tech-Debts final klassifiziert (6 Status-Updates + 1 unverändert), keine neuen Architektur-/Anti-Pattern-Beobachtungen. Die `tech_debt_ids`-Liste im Frontmatter referenziert alle 7 betroffenen Einträge als Review-Spezifikum (Schritt-004-Spezialfall, weil der Schritt *alle* offenen TDs adressiert, nicht nur einen Teil), nicht als neu erzeugte TDs. Vollständige Status-Updates und Begründungen stehen in `tech-debt.md` (Pointer-Prinzip).
