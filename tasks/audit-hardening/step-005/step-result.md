---
status: done
type: step-result
task: audit-hardening
step: "005"
epic: EPIC-05
step_type: single
coded_by: coder
coded_by_model: Claude Sonnet 5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T22:45:00+02:00
code_commit_hash: 6c83cc6
status_after: done (pending audit)
blocker_category: n/a
---

# Result Step 005: Anonymizer.IsColumnExcluded: TD-002 auflösen — stale Doku korrigieren statt ExcludedColumns wiederbeleben

## Zusammenfassung

Alle stale XML-Doc-Referenzen auf die am 2026-07-25 (Commit `9324ed1`) entfernte lokale
`ExcludedColumns`-Glob-Liste korrigiert, auf die tatsächliche Architektur (einzige
Ausschluss-Quelle: zentrale `AnonymizationRules`-Tabelle via `IAnonymizationRuleProvider`).
`IsColumnExcluded` bekam einen erklärenden Kommentar, warum `context` bewusst ungenutzt
bleibt. Keine Verhaltensänderung, reine Doku-/Kommentarkorrektur.

## Geänderte Dateien

- `src/SqlToAi/Configuration/SqlToAiOptions.cs` — `AnonymizerOptions`-Klassendoc umgeschrieben
  (kein `ExcludedColumns` mehr, beschreibt jetzt die zentrale Regel-Tabelle).
- `src/SqlToAi/Anonymization/IAnonymizationPolicyResolver.cs` — Interface-Doc umgeschrieben,
  nennt nur noch die zwei tatsächlich existierenden Ausschluss-Quellen (Master-Switch +
  `IAnonymizationRuleProvider`).
- `src/SqlToAi/Anonymization/IAnonymizer.cs` — `<param name="DbExclusions">`-Tag entfernt (Record
  hat nur drei Parameter), `<remarks>` ergänzt (Ausschluss-Entscheidung fällt beim Caller, nicht
  in `Anonymizer`); zwei weitere beim Lesen gefundene stale `ExcludedColumns`-Referenzen (Doku zu
  `OriginColumnName` und zu `Tokenize`) im selben Zug korrigiert — Plan sanktioniert das explizit
  unter „Notes" (weitere Funde im selben Step mitkorrigieren).
- `src/SqlToAi/Anonymization/Anonymizer.cs` — erklärender Kommentar über `IsColumnExcluded`
  ergänzt (Methodenkörper unverändert); zusätzlich zwei weitere stale `ExcludedColumns`-Stellen
  korrigiert, die beim Lesen auffielen: XML-Doc von `Anonymize(string, string)` und
  Konstruktor-Parameterdoc von `options`.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — Hash-Update für die vier
  geänderten Quelldateien (automatisch via `dotnet test`, mechanische Folge der
  Kommentaränderungen).

**Nicht geändert (Abweichung vom Plan, siehe unten):** `tasks/audit-hardening/tech-debt.md`
(„Datei 5" im Plan).

## Commit

- **Code-Commit-Hash:** `6c83cc6`
- **Message:**
  ```
  docs(anonymization): stale ExcludedColumns-Doku korrigieren (TD-002) [audit-hardening]

  XML-Doc-Kommentare in AnonymizerOptions, IAnonymizationPolicyResolver
  und IAnonymizer beschrieben noch die am 2026-07-25 (Commit 9324ed1)
  entfernte lokale ExcludedColumns-Glob-Liste. Korrigiert auf die
  tatsaechliche Architektur (einzige Ausschluss-Quelle: zentrale
  AnonymizationRules-Tabelle via IAnonymizationRuleProvider) und
  IsColumnExcluded um erklaerenden Kommentar ergaenzt, warum context
  bewusst ungenutzt bleibt. Keine Verhaltensaenderung.

  Refs: tasks/audit-hardening/step-005
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — Selbstbezug, siehe
  `git log`).

## Build-/Test-Output

```
dotnet build → grün (0 Warnungen, 0 Fehler)
dotnet test  → grün (500 Tests, 0 Fehler)
```

## Abweichungen vom Plan

- **„Datei 5" (`tasks/audit-hardening/tech-debt.md`, TD-002 auf „aufgelöst durch Klärung"
  setzen) wurde bewusst NICHT von mir umgesetzt.** Grund: Meine eigene Coder-Skill-Vorgabe
  verbietet das explizit („Keine eigenen Tech-Debt-Einträge anlegen — `tech-debt.md` gehört
  ausschließlich dem Kritiker"), und `spec.md`
  (`.agents/Agent-Scaffolding/dev-loop/drift-loop/spec.md:171`) bestätigt: „Einziger Schreiber:
  der Kritiker". Das bestehende Präzedenzbeispiel TD-001 belegt dasselbe Muster: die
  Statusänderung auf „erledigt" für TD-001 erfolgte im Kritiker-Review-Commit `f14f093`
  (`chore(task): step-004 Review dokumentieren ...`), **nicht** im Coder-Commit `7becaf3`. Der
  Plan hat hier vermutlich übersehen, dass die Tech-Debt-Log-Pflege außerhalb der
  Coder-Zuständigkeit liegt. Alle vier Code-/Doku-Änderungen aus dem Plan (Dateien 1–4) sind
  vollständig umgesetzt; der Kritiker kann TD-002 im eigenen Review-Commit anhand dieses
  Step-Results und Commit `6c83cc6` als aufgelöst markieren.
- Über die drei im Plan explizit genannten Fundstellen hinaus habe ich beim Lesen drei weitere
  stale `ExcludedColumns`-Erwähnungen in denselben Dateien gefunden (`Anonymizer.cs`:
  `Anonymize(string, string)`-Doc + Konstruktor-Parameterdoc; `IAnonymizer.cs`:
  `OriginColumnName`-Param-Doc + `Tokenize`-Doc) und im selben Schritt mitkorrigiert — der Plan
  sanktioniert das ausdrücklich unter „Notes" („Falls beim Umsetzen auffällt, dass weitere
  Doc-Kommentare … ebenfalls ExcludedColumns/DbExclusions erwähnen: im selben Step
  mitkorrigieren").

## Beobachtungen

Keine über die oben genannten Abweichungen hinaus.

## Bekannte Unschärfen

Keine.
