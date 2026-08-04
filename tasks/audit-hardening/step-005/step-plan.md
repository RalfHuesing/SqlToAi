---
status: done (pending audit)
type: step-plan
task: audit-hardening
step: "005"
title: "Anonymizer.IsColumnExcluded: TD-002 auflösen — stale Doku korrigieren statt ExcludedColumns wiederbeleben"
epic: EPIC-05
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-04T22:15:00+02:00
related_to: []
---

# Step 005: Anonymizer.IsColumnExcluded: TD-002 auflösen — stale Doku korrigieren statt ExcludedColumns wiederbeleben

## Bezug

- **Task:** `audit-hardening`
- **Epic:** `EPIC-05` aus `roadmap.md` — TD-002: `Anonymizer.IsColumnExcluded` wertet
  `context` nie aus; `AnonymizerOptions.ExcludedColumns`-Glob-Patterns sollen laut
  Tech-Debt-Eintrag „projektweit nirgends" greifen.
- **Konzept-Referenz:** Kein direkter `konzept.md`-Bezug (Step stammt aus TD-002, wie
  EPIC-04/TD-001 aus step-004). `konzept.md` Muss-Haben 3 (Trail-Redaction) verlangt nur
  den globalen Schalter, keine spaltenspezifische Ausnahmeliste.

## Aktueller Projektzustand (JIT-Kontext)

**Zentraler Befund dieses Steps — weicht vom ursprünglichen TD-002-Vorschlag ab:**

`AnonymizerOptions.ExcludedColumns` **existiert nicht mehr** im aktuellen Code. Per
`git log -- src/SqlToAi/Anonymization/Anonymizer.cs` und `git show 9324ed1`: Commit
`9324ed1` („refactor(anonymization): veraltete lokale Ausschlüsse entfernen und rein auf
AnonymizationRules konsolidieren", 2026-07-25, **vor** Task-Start 2026-08-04) hat die
komplette lokale Glob-Exclusion-Infrastruktur bewusst entfernt: `AnonymizerExclusionProvider`,
`IAnonymizerExclusionProvider`, `sql-scripts/02_anonymizer_exclusions.sql`, die zugehörigen
Tests und die `ExcludedColumns`-Property selbst. Seitdem ist die **einzige** Ausschluss-Quelle
im Projekt die zentrale DB-Regel-Tabelle über `IAnonymizationRuleProvider`/
`AnonymizationRuleProvider` (`src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs`,
LIKE-Pattern über `LikePatternMatcher`, Pareto-Spezifitäts-Auflösung).

TD-002 wurde während `step-003`s Review (2026-08-04, **nach** Commit `9324ed1`) formuliert
und referenziert `AnonymizerOptions.ExcludedColumns` — vermutlich aus dem älteren
Audit-Dokument (`tasks/audit-2026-07-24/...`), das vor der Konsolidierung entstand, oder aus
noch nicht bereinigten XML-Doc-Kommentaren im Code selbst (s. u.). Der im Tech-Debt-Vorschlag
skizzierte Fix („`IsColumnExcluded` um Glob-Pattern-Prüfung gegen `context` erweitern, damit
`ExcludedColumns` wie dokumentiert wirkt") würde faktisch die am 2026-07-25 bewusst entfernte
lokale Exclusion-Liste **wieder einführen** — das widerspricht der expliziten
Architektur-Entscheidung dieses Commits (Konsolidierung auf eine einzige Ausschluss-Quelle).
Dieser Step plant daher **nicht** die Wiedereinführung von `ExcludedColumns`.

**Was tatsächlich noch stimmt an TD-002 (verifiziert):**

- `IsColumnExcluded` lautet weiterhin exakt `return !_options.Anonymizer.Enabled;`
  ([Anonymizer.cs:74-77](src/SqlToAi/Anonymization/Anonymizer.cs#L74)) — `context` bleibt
  ungenutzt.
- Für den Query-Ergebnis-Pfad ist das **unschädlich**: `QueryExecutionService` löst die
  Ausschluss-Entscheidung bereits *vor* jedem `_anonymizer.Anonymize(...)`-Aufruf selbst über
  `_anonymizationRuleProvider.IsExcludedAsync(...)` auf und überspringt `Anonymize` für
  ausgeschlossene Zellen komplett (`IsFlagSet(anonCtx.CentralExclusions, columnIndex)` in
  [QueryExecutionService.Anonymization.cs:126](src/SqlToAi/Database/QueryExecutionService.Anonymization.cs#L126)).
  `IsColumnExcluded`s Untätigkeit bezüglich `context` verursacht hier **keine** übersehene
  Anonymisierung.
- Für `McpTrailWriter` (Trail-Redaction, Step 003) gibt es dagegen **keine** vergleichbare
  Vorfilterung: `AnonymizeObjectProperties`/`AnonymizeArrayElements`
  ([McpTrailWriter.cs:363,396](src/SqlToAi/Mcp/McpTrailWriter.cs#L363)) rufen direkt
  `_anonymizer.Anonymize(key, stringValue)` auf (alias-only-Overload, kein DB/Schema/Tabellen-
  Kontext verfügbar — JSON-Argumente/Response-Werte haben keine Tabellenherkunft). Ein
  spaltennamen-basierter Ausschluss ist hier architektonisch nicht über den zentralen,
  DB-gebundenen `IAnonymizationRuleProvider` lösbar (der braucht `databaseName`/`schemaName`/
  `tableName`, die für freie JSON-Properties nicht existieren). Das ist aber laut
  `konzept.md` Muss-Haben 3 auch nicht gefordert — nur der globale Schalter.
- **Wirklich irreführend** sind ausschließlich drei XML-Doc-Kommentare, die noch die
  entfernte Architektur beschreiben (geprüft, keine anderen Fundstellen — `README.md` und
  `docs/architecture-spec.md` sind bereits korrekt, beschreiben nur `AnonymizationRules`):
  - `AnonymizerOptions`-Klassendoc
    ([SqlToAiOptions.cs:49-51](src/SqlToAi/Configuration/SqlToAiOptions.cs#L49)): „unless its
    name matches one of the `ExcludedColumns` glob patterns" — Property existiert nicht mehr.
  - `IAnonymizationPolicyResolver`-Interface-Doc
    ([IAnonymizationPolicyResolver.cs:6-9](src/SqlToAi/Anonymization/IAnonymizationPolicyResolver.cs#L6)):
    „combining the global master switch, the glob `ExcludedColumns` patterns, the legacy
    per-database exclusion table, and the central `IAnonymizationRuleProvider` rules" — die
    tatsächliche Implementierung (`AnonymizationPolicyResolver.WillAnonymizeAsync`) prüft nur
    `Enabled` + `_ruleProvider.IsExcludedAsync(...)`, keine der beiden anderen genannten Quellen.
  - `AnonymizationColumnContext`-Record-Doc
    ([IAnonymizer.cs:27](src/SqlToAi/Anonymization/IAnonymizer.cs#L27)): `<param
    name="DbExclusions">` beschreibt eine Property, die der Record gar nicht (mehr) hat (nur
    `TableName`/`OriginColumnName`/`SchemaName`, drei Parameter, kein `DbExclusions`) — reiner
    Doc-Leichen-Kommentar, vermutlich aus derselben vor-9324ed1-Ära.
- **Bestehende Tests:** `tests/SqlToAi.Tests/Anonymization/AnonymizerTests.cs` enthält keine
  Erwartung zu `ExcludedColumns` oder spaltenspezifischem Ausschluss (durchgesehen,
  vollständig) — es gibt also **kein** Test-Risiko einer Verhaltensänderung, weil dieser Step
  bewusst **keine** Verhaltensänderung vornimmt (reine Doku-Korrektur + erklärender Kommentar).
  Ebenso kein Treffer für `ExcludedColumns`/`DbExclusions` in `tests/**`.

**Grund für `estimated_risk: low` (Abweichung von der ursprünglichen Einschätzung im
Auftrag):** Der Orchestrator-Auftrag ging von „ändert bestehendes Anonymisierungsverhalten in
Query-Ausgabe UND Trail gleichzeitig" aus (daher Vermutung `medium`). Nach JIT-Codeprüfung
ist das nicht der Fall — dieser Step ändert **kein** Laufzeitverhalten (keine neue
Config-Property, keine geänderte Anonymisierungs-Entscheidung), sondern korrigiert
ausschließlich stale XML-Doc-Kommentare und ergänzt einen erklärenden Kommentar in
`IsColumnExcluded` selbst. Das Risiko einer Regression ist entsprechend gering.

## Intention

Nach diesem Step beschreiben alle drei betroffenen XML-Doc-Kommentare
(`AnonymizerOptions`, `IAnonymizationPolicyResolver`, `AnonymizationColumnContext`) exakt die
aktuelle, tatsächliche Architektur (einzige Ausschluss-Quelle: zentrale
`AnonymizationRules`-Tabelle via `IAnonymizationRuleProvider`; Trail-Redaction hat keinen
spaltenspezifischen Ausschluss, nur den globalen Schalter). `IsColumnExcluded` selbst bekommt
einen kurzen erklärenden Kommentar, warum `context` bewusst nicht in die Entscheidung
einfließt — damit ein künftiger Kritiker/Planer denselben (mittlerweile stale) Befund nicht
erneut als TD-Eintrag aufmacht. TD-002 wird in `tech-debt.md` als „aufgelöst durch Klärung"
(nicht durch neue Funktionalität) markiert, mit Verweis auf diesen Step und Commit
`9324ed1` als Beleg, dass die im ursprünglichen Vorschlag genannte Config-Property bereits vor
Task-Start entfernt wurde.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Configuration/SqlToAiOptions.cs` (Zeile ~46-54, `AnonymizerOptions`-Klassendoc)

- **Was:** `<para>`-Absatz umschreiben: „Default behavior: every string column is anonymized
  with `DefaultMode` unless the central `AnonymizationRules` table (see
  `AnonymizationRuleProvider`) marks it as excluded (`Anonymize == false`) for the resolved
  database/schema/table/column. There is no local, options-based exclusion list anymore (see
  `AnonymizerExclusionProvider` removal, 2026-07-25) — the central rule table is the single
  source of truth." Verweis auf Per-Datenbank-`AccessCheckSql`-Absatz bleibt unverändert
  (weiterhin korrekt).
- **Warum:** Aktuell suggeriert der Kommentar eine `ExcludedColumns`-Glob-Property, die es
  nicht mehr gibt — genau die von TD-002 beanstandete Irreführung.

### Datei 2: `src/SqlToAi/Anonymization/IAnonymizationPolicyResolver.cs` (Zeile 5-11, Interface-Doc)

- **Was:** Satz „combining the global master switch, the glob `ExcludedColumns` patterns, the
  legacy per-database exclusion table, and the central `IAnonymizationRuleProvider` rules"
  ersetzen durch: „combining the global master switch (`AnonymizerOptions.Enabled`) and the
  central `IAnonymizationRuleProvider` rules — the only two exclusion sources that currently
  exist (see `AnonymizationPolicyResolver.WillAnonymizeAsync`)."
- **Warum:** Beschreibt zwei zusätzliche, nicht mehr existierende Ausschluss-Quellen; die
  tatsächliche Implementierung prüft nur die zwei genannten.

### Datei 3: `src/SqlToAi/Anonymization/IAnonymizer.cs` (Zeile ~11-27, `AnonymizationColumnContext`-Record-Doc)

- **Was:** `<param name="DbExclusions">`-Tag entfernen (beschreibt eine nicht existierende
  Property — der Record hat nur drei Parameter: `TableName`, `OriginColumnName`,
  `SchemaName`). Stattdessen kurzer Zusatz zu `OriginColumnName`- oder Klassen-Summary-Doc:
  Hinweis, dass die eigentliche Ausschluss-Entscheidung **nicht** in `Anonymizer` selbst
  fällt, sondern vom jeweiligen Aufrufer vorab getroffen wird (`QueryExecutionService` via
  `IAnonymizationRuleProvider`, bevor `Anonymize`/`Tokenize` überhaupt aufgerufen wird) — der
  Context-Parameter dient in `Anonymizer` selbst nur der Nachvollziehbarkeit/Tests, nicht der
  Entscheidung.
- **Warum:** Referenziert eine Property, die es im Record nicht (mehr) gibt — reiner
  Doku-Leichnam, verwirrt jeden, der den Record liest und nach `DbExclusions` sucht.

### Datei 4: `src/SqlToAi/Anonymization/Anonymizer.cs` (Zeile 74-77, `IsColumnExcluded`)

- **Was:** Kurzer erklärender Kommentar direkt über der Methode ergänzen, z. B.: „`context`
  is intentionally unused here — column/table-specific exclusion decisions are made upstream
  by callers via the central `IAnonymizationRuleProvider` (see
  `QueryExecutionService.Anonymization.cs`), which needs async DB access and full
  database/schema/table context that this synchronous method does not have. `Anonymizer`
  itself only ever applies the global master switch (`AnonymizerOptions.Enabled`); there is
  no local, per-column exclusion mechanism anymore (removed 2026-07-25, commit 9324ed1, see
  TD-002)." Methodenkörper (`return !_options.Anonymizer.Enabled;`) bleibt unverändert —
  reiner Kommentar, keine Logikänderung.
- **Warum:** Macht die (korrekte) Absicht explizit, statt dass ein künftiger Leser erneut
  „context wird nie ausgewertet" als Bug meldet.

### Datei 5: `tasks/audit-hardening/tech-debt.md` (TD-002-Eintrag)

- **Was:** Index-Zeile und Volltext-Eintrag TD-002 auf „aufgelöst durch Klärung" setzen
  (durchstreichen wie bei TD-001, aber mit anderer Begründung): dokumentieren, dass
  `AnonymizerOptions.ExcludedColumns` bereits vor Task-Start (Commit `9324ed1`,
  2026-07-25) entfernt wurde, der ursprüngliche Fix-Vorschlag daher obsolet ist, und stattdessen
  die drei stale XML-Doc-Kommentare korrigiert + ein erklärender Kommentar in
  `IsColumnExcluded` ergänzt wurden. Verweis auf `step-005`/Commit-Hash (nach Umsetzung).
- **Warum:** Hält den Tech-Debt-Log konsistent mit dem tatsächlichen Ausgang, analog zum
  TD-001-Muster in step-004.

## Tests

Keine neuen Unit-Tests nötig — dieser Step ändert keine Laufzeitlogik (nur XML-Doc-Kommentare
und einen Code-Kommentar). Bestehende Test-Suite (`dotnet test`) muss unverändert grün
bleiben, insbesondere:
- [ ] `AnonymizerTests` (alle Fälle unverändert grün, keine Assertion betroffen)
- [ ] `AnonymizationPolicyResolverTests` (unverändert grün)
- [ ] `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` (Baseline-Hash-Update nur nötig,
  falls sich Datei-Hashes durch die Kommentaränderungen ändern — automatisch über
  `dotnet test`, niemals manuell)

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt (5 Dateien: 3× XML-Doc-Korrektur, 1× erklärender
  Kommentar, 1× Tech-Debt-Log-Update)
- [ ] Build-Command (`dotnet build`) grün, 0 Warnungen
- [ ] Test-Command (`dotnet test`) grün, keine Regression
- [ ] Commit auf `main` (Conventional Commit, Deutsch, imperativ)
- [ ] `step-005/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `open`/`in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` (Dokumentations-Synchronisation) — auch wenn
  `README.md`/`docs/architecture-spec.md` hier bereits korrekt sind (verifiziert, keine
  Änderung nötig dort), gilt dieselbe Sorgfaltspflicht sinngemäß für die projektinternen
  XML-Doc-Kommentare, die dieser Step korrigiert.
- `.agents/rules/AiNetLinter.mdc` — reine Kommentaränderungen sind unkritisch bzgl.
  Methoden-/Dateilänge, aber Baseline-Hash-Update beachten (automatisch via `dotnet test`,
  siehe Tech-Stack-Notiz in `roadmap.md`).

## Bekannte Ausnahmen

Keine.

## Notes

- **Bewusst nicht getan:** keine Wiedereinführung einer lokalen `ExcludedColumns`-Glob-Liste
  oder eines `AnonymizerExclusionProvider`-Äquivalents — das widerspräche der expliziten
  Architektur-Entscheidung aus Commit `9324ed1` (Konsolidierung auf eine einzige,
  zentrale Ausschluss-Quelle). Sollte der Nutzer nach diesem Step trotzdem eine
  spaltennamen-basierte Ausnahme speziell für den Trail-Redaction-Pfad (`McpTrailWriter`, ohne
  DB/Tabellen-Kontext) wollen, wäre das ein neues, eigenständiges Epic mit eigener
  Nutzer-Entscheidung — nicht Teil dieses Steps.
- **Bewusst nicht angefasst:** `GlobMatcher.cs` — dessen Klassendoc erwähnt bereits korrekt
  „previously by `Anonymizer` (column exclusion)" (Vergangenheitsform, stimmt), keine Änderung
  nötig.
- Falls beim Umsetzen auffällt, dass weitere Doc-Kommentare (z. B. in Tests oder anderen
  Anonymization-Dateien) ebenfalls `ExcludedColumns`/`DbExclusions` erwähnen: im selben Step
  mitkorrigieren (Scope bleibt „stale Doku-Referenzen auf entfernte Exclusion-Mechanismen"),
  aber keine neuen Config-Properties oder Verhaltensänderungen einführen.
