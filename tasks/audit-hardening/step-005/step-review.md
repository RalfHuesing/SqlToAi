---
status: done
type: step-review
task: audit-hardening
step: "005"
epic: EPIC-05
step_type: single
reviewed_by: kritiker
reviewed_by_model: Claude Sonnet 5
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-04T23:30:00+02:00
verdict: approved
tech_debt_ids: []
---

# Review Step 005: Anonymizer.IsColumnExcluded: TD-002 auflösen — stale Doku korrigieren statt ExcludedColumns wiederbeleben

## Verdict

- [x] **approved** — alle vier Prüfebenen ok
- [ ] **issues**
- [ ] **blocked**

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `<rules_dir>/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Konzept-Treue: passt die Umsetzung zu `konzept.md` (Scope, Non-Goals, Muss-Haben)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (oder Baselines beachtet)

## Befund

### Plan-Erfüllung

Alle vier Doku-/Kommentar-Änderungen aus dem Plan umgesetzt (verifiziert per `git show 6c83cc6`), plus drei vom Plan sanktionierte zusätzliche Fundstellen (Notes-Abschnitt erlaubt das ausdrücklich). „Datei 5" (Tech-Debt-Log-Update) bewusst nicht vom Coder umgesetzt, korrekt gemäß Coder-Skill/`spec.md` — übernehme ich als Kritiker.

### Rules-Konformität

`SqlToAiRichtlinien.mdc#4` (Doku-Synchronisation) eingehalten — genau das ist der Zweck dieses Steps. `AiNetLinter.mdc`: Baseline-Hash-Update in `SqlToAi-baseline.json` korrekt automatisch mitgeliefert (verifiziert im Diff, vier Datei-Hashes aktualisiert).

### Logische Korrektheit

Verifiziert per eigener Codeprüfung, nicht nur übernommen aus dem Plan: `IsColumnExcluded` (Anonymizer.cs:74-77) ist unverändert `return !_options.Anonymizer.Enabled;` — reiner Kommentar-Zusatz, keine Logik geändert. `git grep ExcludedColumns` über das gesamte Repo (außerhalb der Task-eigenen Historien-Dokumente von `audit-hardening`, die die Historie korrekt beschreiben) liefert **keinen** Treffer mehr in Produktionscode — die Property existiert tatsächlich nicht mehr (Commit `9324ed1`, 2026-07-25, vor Task-Start, per `git show 9324ed1 --stat` verifiziert: `AnonymizerExclusionProvider`, `IAnonymizerExclusionProvider`, `02_anonymizer_exclusions.sql` und `ExcludedColumns`-Property vollständig entfernt). Der Diff selbst (`git show 6c83cc6`) entspricht exakt den im Plan beschriebenen Textänderungen.

### Konzept-Treue (Ebene 4)

Kein Non-Goal berührt, kein Muss-Haben verfehlt. `konzept.md` Muss-Haben 3 (Trail-Redaction) verlangt ausdrücklich nur „dieselbe bestehende Anonymisierung (PII-Glob-Patterns, Hash/ScramblePattern)" — dieser Ausdruck bezieht sich, verifiziert gegen `AnonymizerOptions.DefaultMode` (Werte: `ScramblePattern`/`Hash`), auf den Maskierungs-**Algorithmus**, nicht auf eine spaltenspezifische Ausschlussliste. Es gibt also keinen impliziten Muss-Haben-Punkt zu einer Spalten-Exclusion für den Trail, den dieser Step verfehlt hätte.

**Eigene Verifikation der zentralen Planer-Einschätzung (nicht nur übernommen):**

- `AnonymizerOptions.ExcludedColumns` existiert nachweislich nicht mehr im Code (s.o.).
- Query-Ergebnis-Pfad: `QueryExecutionService.Anonymization.cs:126` ruft vor jedem Anonymisierungsversuch `_anonymizationRuleProvider.IsExcludedAsync(...)` auf und speichert das Ergebnis in `anonCtx.CentralExclusions`; `AnonymizeCell` (Zeile ~114-131) prüft `IsFlagSet(anonCtx.CentralExclusions, columnIndex)` **vor** dem Aufruf von `_anonymizer.Anonymize`/`Tokenize` und überspringt ausgeschlossene Zellen komplett — selbst verifiziert, nicht nur aus dem Plan übernommen. `IsColumnExcluded`s Untätigkeit ist hier also tatsächlich folgenlos.
- Trail-Pfad: `McpTrailWriter.cs:363,396` ruft `_anonymizer.Anonymize(key, stringValue)` (Alias-only-Overload) **direkt** auf, ohne vorherige `IsExcludedAsync`-Prüfung — selbst verifiziert. Es gibt hier **keine** Möglichkeit einer spaltenspezifischen Ausnahme, weder über die zentrale Regeltabelle (die braucht `databaseName`/`schemaName`/`tableName`, die für freie JSON-Properties nicht existieren) noch über eine lokale Liste (entfernt). Das ist aber eine **Über**-Anonymisierung (bei Enabled=true wird grundsätzlich alles maskiert, nichts wird versehentlich durchgelassen), keine PII-Exposure-Lücke — die sicherheitsrelevante Richtung von TD-002 („Ausschlüsse greifen nicht, PII könnte durchrutschen") ist damit für den Trail-Pfad nicht gegeben; im Gegenteil, der Trail-Pfad anonymisiert eher zu viel statt zu wenig. Das deckt sich mit `konzept.md` Muss-Haben 3, das explizit nur den globalen Schalter fordert, keine granulare Ausnahmeliste.

**Ergebnis: Ich bestätige die Planer-Einschätzung nach eigener, unabhängiger Codeprüfung.** TD-002 beruhte auf einer zum Formulierungszeitpunkt bereits veralteten Prämisse (`ExcludedColumns` als real existierende, aber kaputte Funktion). Tatsächlich existiert die Funktion nicht mehr, der einzige verbleibende Exclusion-Mechanismus (zentrale Regeltabelle) funktioniert für den Query-Pfad korrekt, und die verbleibende architektonische Lücke im Trail-Pfad (keine granulare Ausnahme, nur globaler Schalter) ist von `konzept.md` explizit nicht gefordert und stellt keine PII-Exposure dar, sondern höchstens eine Komfort-Einschränkung (potenziell zu aggressive Redaction bestimmter, eigentlich unkritischer Properties im Trail). Das rechtfertigt keinen neuen Tech-Debt-Eintrag oberhalb dessen, was TD-003 bereits zum Trail-Redaction-Verhalten dokumentiert — ich lege daher **keinen** neuen TD-Eintrag an.

### Build-/Test-Status

```
dotnet build → grün (0 Warnungen, 0 Fehler)
dotnet test  → grün (500 Tests, 0 Fehler)
```

## Tech-Debt-Einträge aus diesem Review

Keine neuen. TD-002 wird als aufgelöst markiert (siehe `tech-debt.md`-Update in diesem Review-Commit) — durch Klärung, nicht durch Code-Änderung.
