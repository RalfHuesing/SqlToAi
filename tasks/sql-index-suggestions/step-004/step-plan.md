---
status: open
type: step-plan
task: sql-index-suggestions
step: 004
title: "Post-Completion Tech-Debt Cleanup — TD-001 fixen, Rest als out-of-scope markieren"
epic: EPIC-03
estimated_risk: low
step_type: single
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-05T10:40:00+02:00
related_to:
  - tasks/sql-index-suggestions/tech-debt.md
  - tasks/sql-index-suggestions/task-summary.md
  - tasks/sql-index-suggestions/konzept.md#172
  - tasks/sql-index-suggestions/roadmap.md
---

# Step 004: Post-Completion Tech-Debt Cleanup — TD-001 fixen, Rest als out-of-scope markieren

## Bezug

- **Task:** `sql-index-suggestions`
- **Epic:** `EPIC-03` (Post-Completion Tech-Debt Cleanup) — neues Mini-Epic, im selben Planer-Aufruf in `roadmap.md` angelegt
- **Konzept-Referenz:** `tasks/sql-index-suggestions/konzept.md` Zeile 172 (Index-Name-Beispiel im Backtick-Block) — Konzept-Plan-Implementierung-Inkonsistenz, die in `tech-debt.md` als TD-001 dokumentiert ist
- **Reopen-Auftrag:** Task war im ersten Durchlauf `done` (`task-summary.md` final_status `done`, alle 4 Steps approved, 522/522 Tests grün). Nutzer hat anschließend angeordnet, dass die in `tech-debt.md` gesammelten Tech-Debts nachgegangen wird, mit der Policy: in-scope (aus `konzept.md` ableitbar) → fixen, out-of-scope → explizit als „out of scope, won't fix in diesem Task" markiert.

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen vorgefunden (relevant für diesen Schritt):

- **`konzept.md` Zeile 172** (im Backtick-Block direkt nach der XML-Plan-Skizze): das Code-Beispiel zeigt
  `CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_OrderDate ON [dbo].[Orders] (CustomerId, OrderDate) INCLUDE (Amount, Status);`
  — alle einfachen Unterstriche im Index-Namen. Das ist die Konzept-Form.
- **`src/SqlToAi/Database/PerformanceMeasurementService.cs` Zeile 399-405** (`BuildCreateIndexStatement`): die implementierte Form ist `IX_Orders_CustomerId__OrderDate` — einfacher `_` zwischen Tabellenname und erster Spalte, `__` als Trenner zwischen mehreren Schlüsselspalten. Diese Form ist in `step-001` deliberate gewählt worden (Planer-Begründung im `step-001/step-plan.md`: „bessere Lesbarkeit bei mehreren Spalten").
- **Kritiker in `step-001/step-review.md`:** bestätigt, dass beide Formen gültige SQL-Identifier sind. Die Konzept-Pfeil-Form ist illustrativ, nicht normativ.
- **522/522 Tests grün** (Build + Test, inkl. `AiNetLinterTests.RecreateBaseline`, inkl. 4/4 Integration-Tests gegen die reale SQL-Server-2025-Test-DB) — verifiziert in `step-003/step-result.md` Reopen-Phase, Zeile 287-304. Der 522er-Stand ist die Konstante, an der dieser Step nichts ändert.
- **Keine Code-Änderung geplant** in diesem Step: die Konzept-Form ist die einzige sichtbare Doku-Inkonsistenz, und die Auflösung erfolgt durch Anpassung der **Konzept-Beispiel-Zeile** an den implementierten Code — null Code-Changes, null Test-Changes, null Risiko für die Test-Pipeline.
- **`tech-debt.md` Index-Tabelle** führt 6 offene + 1 erledigte Einträge (`TD-003` ist bereits in `step-002` erledigt). Die 6 offenen werden in diesem Step klassifiziert: TD-001 in-scope → erledigen, TD-002/004/005/006/007 out-of-scope → explizit markieren.
- **Nutzer-Klassifizierung** wurde vorab in der Orchestrator-Befragung 2026-08-05 abgestimmt und im Planer-Auftrag dokumentiert; keine eigene Entscheidung nötig.
- **TD-001-Konzept-vs-Code-Entscheidung** (vom Nutzer an den Orchestrator delegiert): **Konzept an Code anpassen** (Option 2). Begründung: 1) die `__`-Wahl des Planers in `step-001` war deliberate und begründet („bessere Lesbarkeit bei mehreren Spalten"); 2) Kritiker hat bestätigt: „Beide Formen sind gültige SQL-Identifier"; 3) Konzept-Beispiel ist illustrativ, nicht normativ; 4) Code läuft produktiv mit 522/522 Tests — minimal-invasive Lösung: 1 Zeile in `konzept.md`, null Code-Changes, null Test-Changes, null Risiko.

## Intention

Nach diesem Step ist der `sql-index-suggestions`-Task vollständig abgeschlossen: die einzige in-scope-Tech-Debt (TD-001, Konzept-Divergenz) ist behoben, alle 5 out-of-scope-Tech-Debts sind explizit als „out of scope, won't fix in diesem Task" markiert (statt nur implizit „offen"), und `task-summary.md` enthält den Post-Completion-Abschnitt, der den finalen Stand des Tech-Debt-Cleanups dokumentiert. Kein Build/Test-Lauf nötig (kein Code-Change); Smoke-Verifikation `dotnet test` als optionale Bestätigung, dass die Markdown-Änderungen die Test-Pipeline nicht berührt haben.

## Konkrete Änderungen

### Datei 1: `tasks/sql-index-suggestions/konzept.md` (Zeile 172)

- **Was:** Im Backtick-Block, der das vollständige `CREATE NONCLUSTERED INDEX`-Beispiel-Statement zeigt, den Index-Namen `IX_Orders_CustomerId_OrderDate` → `IX_Orders_CustomerId__OrderDate` ändern (doppelter Unterstrich zwischen den beiden Schlüsselspalten). Eine Zeile, eine Wort-Ersetzung.
- **Warum:** Konzept-Beispiel an die implementierte Form angleichen (Planer-/Nutzer-Entscheidung, Option 2 in der Orchestrator-Befragung). Die implementierte Form ist deliberate, code-läuft, Tests grün — eine Anpassung an Code richtet das kleinere Übel (Konzept-Form ist ohnehin nur illustrativ), während eine Anpassung an Konzept entweder den Code unnötig refactorn würde (semantisch wertlos, beide Formen valide) oder eine Inkonsistenz zwischen Planer-Lesart und Code erzeugt.
- **Vorher:**
  ```
  → `CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_OrderDate ON [dbo].[Orders] (CustomerId, OrderDate) INCLUDE (Amount, Status);`
  ```
- **Nachher:**
  ```
  → `CREATE NONCLUSTERED INDEX IX_Orders_CustomerId__OrderDate ON [dbo].[Orders] (CustomerId, OrderDate) INCLUDE (Amount, Status);`
  ```
- **Reichweite:** exakt diese eine Zeile, keine weiteren Konzept-Änderungen (alle anderen Doku-Inkonsistenzen aus `task-summary.md` „Globale Audit-Befunde" Abschnitt sind als bewusst-nicht-harmonisiert markiert, z. B. Konzept-Formel `avg_user_cost` vs. DMV-Spalte `avg_total_user_cost` — die hat der globale Kritiker explizit als „nicht zu harmonisieren" eingestuft, weil der Code die korrekte Interpretation liefert).

### Datei 2: `tasks/sql-index-suggestions/tech-debt.md` (Volltext-Status-Updates + `last_updated`)

- **Was:** Status-Updates für 6 Tech-Debt-Einträge. TD-001 auf „erledigt in step-004" mit Begründung und Verweis auf den Konzept-Edit. TD-002, TD-004, TD-005, TD-006, TD-007 jeweils auf „out of scope, won't fix in diesem Task" mit expliziter Begründung, warum `konzept.md` schweigt und der Eintrag nicht aus dem Konzept ableitbar ist. TD-003 bleibt unverändert (bereits in `step-002` erledigt). Frontmatter `last_updated` aktualisieren.
- **Warum:** Der Cleanup-Auftrag des Nutzers verlangt eine explizite Klassifizierung pro Eintrag (in-scope vs. out-of-scope). Die Volltexte sind append-only, daher wird der Status direkt in den bestehenden Einträgen aktualisiert (nicht neue Einträge oben angehängt — TD-001 wird zu TD-003's Schwester, beide haben ein-Status-`erledigt`).
- **Konkret pro Eintrag (Änderungen am Ende des jeweiligen Volltext-Eintrags):**

  **TD-001 (Konzept- vs. Plan-Prose-Inkonsistenz beim Index-Name-Format):**
  - Status von `offen` → `erledigt in step-004 — Konzept-Beispiel in `konzept.md` Zeile 172 an die implementierte Form `IX_Orders_CustomerId__OrderDate` angepasst (doppelter Unterstrich zwischen den beiden Schlüsselspalten). Kein Code-Change, kein Test-Change, 522/522 Tests grün bleiben. Konzept-Pfeil-Form war illustrativ (kein normativer Spec-Punkt in §Muss-Haben/§DoD); die `__`-Wahl in `step-001` war deliberate (Planer-Begründung: „bessere Lesbarkeit bei mehreren Spalten").`
  - Optional: am Anfang des Volltext-Eintrags einen kurzen Absatz ergänzen, der die Resolution dokumentiert (analog zu TD-003's „erledigt"-Absatz).

  **TD-002 (`DESC`-Sortierung in `ColumnGroup`-Spalten wird ignoriert):**
  - Status von `offen` → `out of scope, won't fix in diesem Task — Konzept schweigt über DESC-Sortierung. `konzept.md` §Muss-Haben, §Wie-Idee-1 (Zeilen 157-172) und §DoD erwähnen `DESC` nicht; das Konzept-Beispiel (Zeile 161-172) enthält keine absteigend indizierte Spalte. Eine Implementierung wäre eine konzeptuelle Erweiterung (neues Feature „absteigende Indizes unterstützen"), keine Konzept-Ableitung. Empfehlung: bei zukünftigem Bedarf als eigenes Epic in einem Folge-Task aufnehmen (siehe `task-summary.md` „Empfehlungen" TD-002). Klassifizierung 2026-08-05 per Nutzer-Vorgabe.`

  **TD-004 (SQL-Server-2025-Spezifik, fehlende Versionsnotiz):**
  - Status von `offen` → `out of scope, won't fix in diesem Task — Konzept schweigt über SQL-Server-Mindestversion. `konzept.md` §Zielplattformen sagt „Kein neuer Stack — reine Erweiterung des bestehenden .NET-10/C#-14-MCP-Servers" und nennt keine SQL-Server-Version. Die in `step-003`-Reopen entdeckte 2025-Spezifik (`group_handle` + TVF) ist eine emergente Eigenschaft der Test-Instanz (Microsoft SQL Server 2025 RTM 17.0.1000.7), keine Konzept-Vorgabe. Eine Versionsnotiz oder versionsabhängige CTE-Konstruktion wäre eine Architektur-/Deployment-Entscheidung jenseits des Konzepts. Empfehlung: bei Bedarf an Rückwärtskompatibilität (SQL Server 2019/2022) als eigenes Epic in einem Folge-Task. Klassifizierung 2026-08-05 per Nutzer-Vorgabe.`

  **TD-005 (Test-Environment-Setup `GRANT VIEW SERVER STATE TO [Agent]` nicht reproduzierbar):**
  - Status von `offen` → `out of scope, won't fix in diesem Task — Test-Environment-Setup ist CI/CD-Infrastruktur, kein Konzept-Gegenstand. `konzept.md` §DoD verlangt für Idee 2 nur „Integrationstest gegen eine echte Test-DB in `tests/SqlToAi.Tests/Integration/`" — keine Aussage zur Reproduzierbarkeit des Setups. Der `GRANT VIEW SERVER STATE TO [Agent]` ist eine lokale Test-Infrastruktur-Maßnahme, die im `step-003`-Reopen einmalig ausgeführt wurde und die Tests grün bekam; ein reproduzierbares Setup-Skript in `scripts/` oder als Initialisierung in `SqlServerFixture.cs` wäre eine Test-Strategie-/CI-Entscheidung. Empfehlung: in einem CI-Hardening-Folge-Task adressieren. Klassifizierung 2026-08-05 per Nutzer-Vorgabe.`

  **TD-006 (Test 1 akzeptiert Graceful-Degradation-Notiz nicht, Asymmetrie zu Test 4):**
  - Status von `offen` → `out of scope, won't fix in diesem Task — Test-Design-Detail, kein Konzept-Verstoß. `konzept.md` §DoD für Idee 2 verlangt nur „Graceful Degradation bei fehlender `VIEW SERVER STATE` verifiziert (Unit- oder Integrationstest, der den Permission-Fehler simuliert/auslöst)" — keine Aussage zur Toleranz einzelner Tests gegen Graceful-Degradation oder zur Test-1-vs-Test-4-Asymmetrie. Die Asymmetrie ist im Plan-Original bereits angelegt (Zeile 300-316 in `step-003/step-plan.md`); die Auflösung wäre eine Test-Refactoring-Entscheidung. Empfehlung: im TD-005-Folge-Task (CI-Hardening) mit-adressieren, dann werden beide TDs setup-tolerant. Klassifizierung 2026-08-05 per Nutzer-Vorgabe.`

  **TD-007 (`DmvMockConnectionFactory` deckt SQL-Syntaxfehler nicht ab, systemischer Test-Coverage-Gap):**
  - Status von `offen` → `out of scope, won't fix in diesem Task — Test-Strategie-/Architektur-Frage, Konzept schweigt. `konzept.md` macht keine Aussage zur Test-Strategie (Mock vs. Integration, statische Schema-Validierung, verpflichtende Integration-Tests in CI/CD). Die in `step-002/fix-01` und `step-003`-Reopen sichtbar gewordenen Lücken (CTE-Alias-Bug, SQL-Server-2025-Inkompatibilitäten) sind eine systemische Eigenschaft der Mock-Strategie, die alle künftigen DMV-basierten Tools betrifft. Eine Lösung (statische DMV-Spalten-Whitelist, Compile-Check gegen reale DMV-Schemata, oder CI-Container) wäre eine Architektur-/Test-Strategie-Entscheidung jenseits dieses Tasks. Empfehlung: 80% des Problems sind bereits durch TD-005+TD-006 adressierbar; die restlichen 20% (statische Validierung) lohnen sich nur bei mehreren geplanten DMV-Tools. Klassifizierung 2026-08-05 per Nutzer-Vorgabe.`

- **`last_updated` Frontmatter-Feld:** von `2026-08-05T10:00:00+02:00` → `2026-08-05T10:40:00+02:00` (gleicher Zeitstempel wie dieses Step-Plan-`created_at`).
- **Index-Tabelle:** unverändert (kein Status-Feld in der Tabelle, nur ID/Bereich/Priorität/Kurzfassung). Die Status-Updates stehen ausschließlich in den Volltext-Einträgen — konsistent mit dem bestehenden TD-003-Pattern (TD-003 ist im Index ohne Status, aber im Volltext als „erledigt" markiert).

### Datei 3: `tasks/sql-index-suggestions/task-summary.md` (Post-Completion-Abschnitt ergänzen)

- **Was:** Neuen Abschnitt am Ende der Datei einfügen, nach dem bestehenden „## Verdict" / `done`-Abschnitt. Titel: `## Post-Completion-Tech-Debt-Cleanup (step-004)`. Inhalt: kurze Zusammenfassung des Cleanup-Ergebnisses (was wurde gemacht, in-scope vs. out-of-scope, endgültige Tech-Debt-Statistik).
- **Warum:** Der globale Kritiker hat in `task-summary.md` den finalen Stand zum Task-Ende dokumentiert (Status `done`, alle 4 Steps approved, 6 offene + 1 erledigte Tech-Debt). Der Post-Completion-Cleanup ist eine bewusste Erweiterung dieses Stands und verdient eine eigene Sektion, damit der Lesefluss linear bleibt (zuerst Task-Abschluss, dann Post-Completion-Härtung).
- **Struktur des neuen Abschnitts (siehe Code-Skizze unten):**
  - 1 Absatz Kontext (warum es diesen Abschnitt gibt — Reopen-Auftrag des Nutzers)
  - 1 Tabelle oder Bullet-Liste mit dem Ergebnis pro Tech-Debt (TD-001 erledigt; TD-002/004/005/006/007 out-of-scope, won't fix; TD-003 schon vorher erledigt)
  - 1 Absatz endgültige Statistik (vorher 6 offen + 1 erledigt → nachher 5 out-of-scope-markiert + 2 erledigt; 0 offen-unmarkiert)
  - 1 Absatz Verweis auf `roadmap.md` EPIC-03 und Commit(s) dieses Steps

### Datei 4: `tasks/sql-index-suggestions/roadmap.md` (EPIC-03 angelegt + `last_updated`)

- **Was:** Neues Epic `EPIC-03` nach dem EPIC-02-Block einfügen. Frontmatter `last_updated` aktualisieren.
- **Status:** `[ ]` (offen), wird durch step-004 auf `[x]` abgehakt, sobald step-004 Status `approved` bekommt.
- **Inhalt:** Kurzbeschreibung des Reopen-Auftrags, Verweis auf die TD-Klassifizierung aus der Orchestrator-Befragung, Erwartung an step-004 (was der Step tut), Risiko `low`, kein Code-Change.
- **Begründung für eigenes Mini-Epic:** Der Planer-Auftrag nennt die Option „kleines Mini-Epic EPIC-03 anlegen, damit die Steps-Tabelle weiterhin wächst und die Konvention eingehalten wird" als Empfehlung. EPIC-03 ist ein reguläres Epic, kein impliziter Tech-Debt-Nachzug (der Nutzer hat explizit angeordnet, daher ist die Scope-Erweiterung legitim, siehe Skill §7.2 Schritt 1: „neues Epic ergänzen, wenn ein neuer Muss-Haben-Punkt … den weder ein bestehendes Epic noch ein Tech-Debt-Eintrag abdeckt" — analog: hier deckt kein Epic den Post-Completion-Reopen-Auftrag ab).

## Tests

Keine — Begründung: Es werden ausschließlich Markdown-Doc-Files modifiziert (`konzept.md`, `tech-debt.md`, `task-summary.md`, `roadmap.md`). Kein C#-Code wird angefasst, daher keine Test-Datei, keine Test-Logik, keine Test-Erwartung ändert sich. Die 522/522-Tests-Konstante bleibt formal unverändert.

Optional: als **Smoke-Verifikation** am Ende des Steps `dotnet test` laufen lassen — Erwartung: 522/522 grün (gleich wie nach `step-003`-Reopen). Falls Smoke-Test ausgeführt wird, in `step-result.md` mit dem einzeiligen Ergebnis festhalten (analog `step-003/step-result.md` Zeile 287-304). Falls Smoke-Test ausgelassen wird: in `step-result.md` explizit dokumentieren, warum (z. B. „Smoke-Test entfällt, da kein Code-Change — die 522/522-Konstante aus `step-003`-Reopen bleibt formal erhalten").

## Definition of Done

- [ ] `tasks/sql-index-suggestions/konzept.md` Zeile 172: Index-Name auf `IX_Orders_CustomerId__OrderDate` geändert (doppelter Unterstrich zwischen den beiden Schlüsselspalten)
- [ ] `tasks/sql-index-suggestions/tech-debt.md`: TD-001-Status auf „erledigt in step-004" mit Begründung und Verweis auf den Konzept-Edit
- [ ] `tasks/sql-index-suggestions/tech-debt.md`: TD-002, TD-004, TD-005, TD-006, TD-007 Status auf „out of scope, won't fix in diesem Task" mit expliziter Begründung pro Eintrag
- [ ] `tasks/sql-index-suggestions/tech-debt.md` Frontmatter `last_updated` aktualisiert
- [ ] `tasks/sql-index-suggestions/task-summary.md`: neuer Abschnitt „Post-Completion-Tech-Debt-Cleanup (step-004)" am Ende eingefügt
- [ ] `tasks/sql-index-suggestions/roadmap.md` (von Planer bereits in diesem Aufruf erledigt): EPIC-03 angelegt mit Verweis auf den Reopen-Auftrag und die TD-Klassifizierung
- [ ] Optional: `dotnet test` als Smoke-Verifikation, Ergebnis 522/522 grün
- [ ] Commit auf `main` (Conventional Commit, deutsch, imperativ, Subject ≤ 72 Zeichen, Suffix `[sql-index-suggestions]`)
- [ ] `step-004/step-result.md` geschrieben mit Selbst-Referenz auf den Code-Commit (kein Doku-Commit nötig, da die Markdown-Edits und der Step-Plan-Status alle im selben Commit zusammengefasst werden können — siehe Notes)
- [ ] `status` in `step-004/step-plan.md` Frontmatter von `open` auf `done (pending audit)` gesetzt

## Rules-Refs

Keine direkt anwendbaren Regeln für diesen Step:

- `SqlToAiRichtlinien.mdc` §4 (Doku-Sync-Pflicht: `architecture-spec.md` und `README.md` ohne Aufforderung mit-aktualisieren) — **nicht anwendbar**. Die Pflicht zielt darauf, Code-Änderungen in die Doku zu spiegeln; dieser Step ist selbst eine Doku-Änderung (Konzept, Tech-Debt, Task-Summary, Roadmap), es gibt keinen Code-Change, der gespiegelt werden müsste. `architecture-spec.md` und `README.md` sind durch die vorherigen Steps bereits synchron zum implementierten Code und bleiben unverändert.
- `SqlToAiRichtlinien.mdc` §5 (Zero-Warning-Direktive, `TreatWarningsAsErrors`) — **nicht anwendbar**. Es wird kein C#-Code geändert, der Build läuft formal unverändert (Smoke-Test optional).
- `AiNetLinter.mdc` — **nicht anwendbar**. Keine C#-Datei wird angefasst, der Linter läuft auf den geänderten Markdown-Dateien nicht.

## Bekannte Ausnahmen

Keine.

## Code-Skizze (optional)

Nicht relevant — keine C#-Code-Änderung in diesem Step. Einzige nicht-Doc-Änderung ist die Aktualisierung des `step-004/step-plan.md`-Statusfelds (`open` → `done (pending audit)`), die der Coder am Ende vornimmt.

Die Struktur des neuen `task-summary.md`-Abschnitts (zur Vorab-Skizze für den Coder):

```markdown
## Post-Completion-Tech-Debt-Cleanup (step-004)

Nach Abschluss des Tasks (`task-summary.md` Verdict `done`, alle 4
Steps approved, 522/522 Tests grün) hat der Nutzer am 2026-08-05
angeordnet, die in `tech-debt.md` gesammelten Tech-Debts nach Klassifikation
(in-scope → fixen / out-of-scope → explizit markieren) zu adressieren.
Die Klassifizierung wurde in der Orchestrator-Befragung 2026-08-05
abgestimmt; die Umsetzung erfolgt in `step-004` (Epic EPIC-03, Risiko
`low`, kein Code-Change, kein Test-Change).

### Ergebnis pro Tech-Debt

- **TD-001** (Konzept-Index-Name-Format `IX_Orders_CustomerId_OrderDate`
  vs. Code `IX_Orders_CustomerId__OrderDate`) — **erledigt in step-004**:
  Konzept-Beispiel in `konzept.md` Zeile 172 an die implementierte Form
  angepasst. Kein Code-Change, kein Test-Change, 522/522 Tests grün
  bleiben. Konzept-Pfeil-Form war illustrativ; die `__`-Wahl in
  `step-001` war deliberate (Planer-Begründung: „bessere Lesbarkeit bei
  mehreren Spalten").
- **TD-002** (`DESC`-Sortierung in `ColumnGroup` ignoriert) —
  **out of scope, won't fix in diesem Task**: Konzept schweigt über
  `DESC`, Konzept-Beispiel hat keine absteigend indizierte Spalte. Eine
  Implementierung wäre eine konzeptuelle Erweiterung (kein Bugfix).
- **TD-003** (`IsShowplanPermissionError` generalisiert) — bereits in
  `step-002` erledigt, unverändert.
- **TD-004** (SQL-Server-2025-Spezifik, fehlende Versionsnotiz) —
  **out of scope, won't fix in diesem Task**: Konzept schweigt über
  SQL-Server-Mindestversion, die 2025-Spezifik ist emergente Eigenschaft
  der Test-Instanz. Eine Versionsnotiz wäre Architektur-/Setup-Entscheidung.
- **TD-005** (Test-Environment-Setup `GRANT VIEW SERVER STATE TO
  [Agent]` nicht reproduzierbar) — **out of scope, won't fix in diesem
  Task**: Test-Infrastruktur (CI/CD), kein Konzept-Gegenstand. Konzept
  verlangt nur „Integrationstest gegen eine echte Test-DB".
- **TD-006** (Test 1 akzeptiert Graceful-Degradation-Notiz nicht,
  Asymmetrie zu Test 4) — **out of scope, won't fix in diesem Task**:
  Test-Design-Detail, kein Konzept-Verstoß. Konzept verlangt nur „Tests
  vorhanden" für Graceful Degradation, keine Aussage zur Test-Toleranz.
- **TD-007** (`DmvMockConnectionFactory` deckt SQL-Syntaxfehler nicht ab,
  systemischer Test-Coverage-Gap) — **out of scope, won't fix in diesem
  Task**: Test-Strategie-/Architektur-Frage, Konzept schweigt. 80% des
  Problems bereits durch TD-005+TD-006 adressierbar; restliche 20%
  (statische Validierung) lohnen nur bei mehreren DMV-Tools.

### Endgültige Tech-Debt-Statistik

- **Vor step-004:** 6 offen + 1 erledigt (TD-003)
- **Nach step-004:** 0 offen + 2 erledigt (TD-003, TD-001) +
  5 out-of-scope-markiert (TD-002, TD-004, TD-005, TD-006, TD-007)
- **Build-/Test-Stand:** 522/522 Tests grün, `dotnet build` 0 Warnungen
  (Smoke-Verifikation optional; bei Ausführung in `step-result.md`
  festgehalten).

### Epic- und Commit-Verweise

- **Epic:** EPIC-03 „Post-Completion Tech-Debt Cleanup" in `roadmap.md`
  (mit diesem Step abgehakt).
- **Step:** `step-004` (verbraucht keine Fix-Runde; keine `fix-XX/`
  Unterordner, keine `issues`-Verdikte erwartet — der Step ist 100%
  Doku-Edit mit deterministisch grünem Smoke-Test).
- **Commits:** ein gemeinsamer Commit für alle Markdown-Edits +
  `step-004/step-plan.md`-Status-Update (konzept.md, tech-debt.md,
  task-summary.md, roadmap.md, step-plan.md) — Conventional Commit,
  deutsch, imperativ, Suffix `[sql-index-suggestions]`. Subject-Vorschlag:
  `docs(task): Post-Completion Tech-Debt Cleanup [sql-index-suggestions]`
```

## Notes

- **Commit-Strategie für diesen Step:** Es gibt **keinen** Code-Commit (kein `.cs`-File wird geändert) — daher entfallen die sonst üblichen zwei Commits (Code + Doku/Result) und es reicht **ein einziger Commit** für alle vier Markdown-Edits + `step-plan.md`-Status-Update. Der Commit-Body listet die fünf Änderungen auf (vier Doc-Dateien + step-plan.md-Status). Subject-Länge inkl. Suffix `[sql-index-suggestions]` (29 Zeichen Suffix) + `docs(task): Post-Completion Tech-Debt Cleanup ` (43 Zeichen) = 72 Zeichen — exakt am Limit, Conventional-Commit-konform.
- **Kein `fix-XX/` Unterordner:** Der Step hat kein `issues`-Verdikt-Risiko (rein sequenzielle Doku-Edits, deterministisch). Sollte der Kritiker wider Erwarten einen `issues`-Befund melden (z. B. „Konzept-Form anders gewollt"), wäre `fix-01` der Standard-Pfad; aktuell nicht erwartet.
- **Kein `task-state.md`-Update durch den Coder:** Der `task-state.md` Frontmatter `current_step: step-004` ist bereits durch den Orchestrator beim Reopen gesetzt (siehe Datei Zeile 8). Der Coder muss nur die Steps-Tabelle-Zeile für `step-004` ergänzen, sobald der Step-Status final ist — das macht der Orchestrator zusammen mit dem Review-Commit (siehe Skill §6.2 Punkt 4).
- **`related_to`-Verweise** zeigen auf alle vier Dateien, die der Coder lesen muss (`tech-debt.md`, `task-summary.md`, `konzept.md` Zeile 172, `roadmap.md`) — Pointer-Prinzip, kein Cache (Skill §10.6): „Coder und Kritiker lesen bei nicht-leerem `related_to` deshalb den **aktuellen** Stand nach".
- **Reihenfolge der Edits** (Empfehlung, nicht vorgeschrieben): 1) `konzept.md` (eine Zeile, atomar), 2) `tech-debt.md` (sechs Volltext-Updates + Frontmatter), 3) `task-summary.md` (neuer Abschnitt am Ende), 4) `step-004/step-plan.md` Status `open` → `done (pending audit)`. `roadmap.md` ist bereits in diesem Planer-Aufruf aktualisiert.
- **Verifizierung der Konzept-Änderung** (für den Coder): Vor dem Commit einmal die geänderte Zeile in `konzept.md` per `git diff` oder direkt mit dem Read-Tool sichten — der Backtick-Block ist 1 Zeile, die Ersetzung betrifft genau 1 Wort (`IX_Orders_CustomerId_OrderDate` → `IX_Orders_CustomerId__OrderDate`).
- **Sprachstil im neuen `task-summary.md`-Abschnitt:** Konsistent mit dem bestehenden Stil des Dokuments (deutsch, sachlich, mit klarer Statistik- und Empfehlungs-Trennung). Siehe Code-Skizze oben.
- **Smoke-Test-Entscheidung:** Der Coder entscheidet selbst, ob er `dotnet test` als Smoke-Verifikation laufen lässt. Empfehlung: ja, weil ein einzeiliger `522/522 grün`-Eintrag in `step-result.md` dem Kritiker die Verifikation erleichtert und den formalen Beweis liefert, dass die Markdown-Edits die Test-Pipeline nicht berührt haben. Aufwand: ~7 Sekunden.
- **Klassifizierungs-Begründungen in `tech-debt.md`-Volltexten:** Im Stil der bestehenden `Warum nicht sofort gefixt`-Absätze. Jeder out-of-scope-Eintrag bekommt eine kurze Begründung (1-2 Sätze), die erklärt, warum `konzept.md` schweigt und der Eintrag nicht aus dem Konzept ableitbar ist. Verweis auf `task-summary.md` „Post-Completion-Tech-Debt-Cleanup" Abschnitt für die Sammel-Begründung.
- **Konsistenz mit TD-003 (bereits erledigt):** TD-003 wurde in `step-002` erledigt und der Volltext enthält bereits einen „Status: erledigt in step-002"-Block mit Verweis auf den Helper-Refactor. TD-001 in `step-004` folgt demselben Muster: Status-Update im Volltext, kurze Begründung der Resolution, Verweis auf den auslösenden Step. Format-Konsistenz ist gewollt.
