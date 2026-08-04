---
status: done
type: step-review
task: sql-index-suggestions
step: 005
epic: EPIC-04
step_type: single
reviewed_by: kritiker
reviewed_by_model: claude-sonnet-5
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-04T00:00:00+02:00
verdict: approved
tech_debt_ids: []
---

# Review Step 005: TD-002 — `DESC`-Sortierung in `BuildCreateIndexStatement` korrekt rendern

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

Alle DoD-Punkte erfüllt: `ExtractMissingIndexWarnings` ruft den neuen Helper `WithDescendingSuffix` für `EQUALITY`/`INEQUALITY` auf, `INCLUDE` bleibt unverändert; `BuildCreateIndexStatement` unangetastet (wie gefordert); vier neue Tests (die zwei Pflicht-Tests plus beide optionalen) decken `Descending="True"`, `Descending="False"` (Regressionsschutz), Mehrfach-DESC und INCLUDE-Ignoranz ab; alle vier bestehenden Missing-Index-Tests bleiben unverändert grün; Commit `a1492c6` trägt Conventional-Commit-Format, Deutsch, Suffix `[sql-index-suggestions]`, Subject 72 Zeichen (Plan-Abweichung dokumentiert und nachvollziehbar begründet); `docs/architecture-spec.md`/`README.md`/`ToolRegistry.cs` bewusst unverändert (Doku-Sync-Entkräftung aus dem Plan greift, da rein interner Render-Pfad).

### Rules-Konformität

Alle im Plan referenzierten Regeln eingehalten: `TreatWarningsAsErrors=true` — Build 0 Warnungen; `AiNetLinterTests.RecreateBaseline` lief automatisch (Baseline aktualisiert); `MaxMethodLineCount 60` eingehalten (`ExtractMissingIndexWarnings` 46 Zeilen, `WithDescendingSuffix` 15 Zeilen), Datei 477 LOC (Limit 500), `MaxMethodParameterCount 4` eingehalten (3 Parameter); `sealed`/`#nullable enable` unverändert vorhanden; Commit-Konventionen eingehalten.

### Logische Korrektheit

Kernverhalten korrekt: `WithDescendingSuffix` hängt case-insensitive exakt bei `Descending="True"` ein ` DESC` an, alle anderen Werte (fehlend, `"False"`, andere Strings) lassen die Spalte unverändert; `INCLUDE`-Zweig bleibt unangetastet wie spezifiziert; `BuildCreateIndexStatement` propagiert die vorformatierten Strings unverändert per `string.Join`. Ein Detail, das kein Finding rechtfertigt, aber notiert gehört: siehe „Sonstige Beobachtungen" unten (Positions-Kopplung zwischen gefilterter Namensliste und ungefilterter Column-Liste).

### Konzept-Treue (Ebene 4)

`konzept.md` schweigt bewusst über `DESC` (Plan-Analyse dazu korrekt); dieser Fix ist eine explizit vom Nutzer angeordnete Konzept-Erweiterung (EPIC-04/TD-002), keine Konzept-Ableitung. Kein Non-Goal verletzt, kein Muss-Haben-Punkt fehlt, Scope entspricht exakt der Plan-Intention (eine private Methode erweitert, kein neues API-Feld, keine Architekturänderung).

### Build-/Test-Status

```
dotnet build SqlToAi.slnx → grün (0 Warnungen, 0 Fehler)
dotnet test  SqlToAi.slnx → 525/526 grün, 1 Fehler (siehe unten)
dotnet test --filter "FullyQualifiedName~PerformanceMeasurementServiceTests" → 14/14 grün
```

Der einzige Fehlschlag (`IndexSuggestionServiceIntegrationTests.SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase`) betrifft eine andere Datei/einen anderen Service (`IndexSuggestionService`, nicht `PerformanceMeasurementService`) und ist nicht Teil des Commit-Diffs von `a1492c6`. Die Fehlermeldung passt exakt zum bereits dokumentierten, offenen `TD-006` (Test 1 akzeptiert die Graceful-Degradation-Notiz bei fehlender `VIEW SERVER STATE` nicht, geplant für `step-007`) — ein Infrastruktur-/Setup-Zustand außerhalb des Step-005-Scopes, kein durch diesen Step verursachter Regressions-Fehler. Alle Missing-Index-/`PerformanceMeasurementService`-Tests sind 100% grün. `git status` nach dem Testlauf zeigte nur eine durch den eigenen `RecreateBaseline`-Lauf neu geschriebene `SqlToAi-baseline.json` (nicht-deterministische Hash-Regeneration über unveränderte Dateien) — zurückgesetzt, keine bleibende Abweichung vom Commit-Stand.

## Sonstige Beobachtungen / MINOR / NITPICK

- **Positions-Kopplung in `WithDescendingSuffix` (`PerformanceMeasurementService.cs:391-405`):** Der Helper matcht `names[i]` (aus der vorab per `Where(n => !string.IsNullOrEmpty(n))` gefilterten Liste) gegen `columns[i]` aus einer zweiten, ungefilterten `columnGroup.Elements(ns + "Column")`-Traversierung. Bei identischer Reihenfolge/Anzahl (Normalfall, jedes SQL-Server-`MissingIndex`-`Column`-Element trägt laut Showplan-Schema immer ein `Name`-Attribut) ist das korrekt. Enthielte eine `ColumnGroup` aber ein `Column`-Element ohne/mit leerem `Name` neben validen Spalten, würde sich die Indexzuordnung verschieben und ein `Descending`-Attribut könnte der falschen Spalte zugeordnet werden. Diese Positions-Kopplung stammt aus der Plan-Code-Skizze selbst (Datei 1, Variante mit `FormatKeyColumns`) und ist mit echtem SQL-Server-Output praktisch nicht auslösbar — daher kein Finding (kein `MaxMethodLineCount`/Rules-Verstoß, keine Abnahme-Kriterien verfehlt, kein Konzept-Bezug). Robusteres Muster für eine künftige Berührung dieser Methode: `Descending` direkt im selben LINQ-Durchlauf neben `Name` auswerten (Tupel `(name, desc)`) statt einer zweiten, separat gefilterten Traversierung — vermeidet die implizite Index-Annahme vollständig.

- **Nicht mehr in `tech-debt.md`:** Nach `approved`-Verdict entfernt der Planer/Nutzer laut Plan-Notes den `TD-002`-Eintrag aus `tech-debt.md` (Status-Policy) — kein Kritiker-Zuständigkeitsbereich, nur als Hinweis für den nächsten Schritt.
