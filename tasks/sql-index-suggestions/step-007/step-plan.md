---
status: done (approved)
type: step-plan
task: sql-index-suggestions
step: 007
title: "TD-006 — Test 1 Graceful-Degradation-Toleranz"
epic: EPIC-04
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: claude-sonnet-5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-05T18:30:00+02:00
related_to: []
---

# Step 007: TD-006 — Test 1 Graceful-Degradation-Toleranz

## Bezug

- **Task:** `sql-index-suggestions`
- **Epic:** `EPIC-04` aus `roadmap.md` — letzter offener Punkt des Epics
  (TD-002 bereits in step-005 erledigt, TD-004 als "won't fix"
  geschlossen und per Revert `09fa038` zurückgesetzt). step-007 ist der
  einzige noch offene Teil von EPIC-04.
- **Konzept-Referenz:** `konzept.md` §Permission-Handling / §Wie-Idee-2
  (Idee 2, `sql_suggest_indexes`) — die Graceful-Degradation-Notiz bei
  fehlender `VIEW SERVER STATE`-Berechtigung ist dort als dritter
  gültiger Output-Pfad des Tools spezifiziert (siehe auch
  `docs/architecture-spec.md` §4 Nr. 16).

## Aktueller Projektzustand (JIT-Kontext)

- `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs`
  wurde gelesen (aktueller Stand nach Revert-Commit `09fa038`). Datei
  enthält exakt 4 Tests, Struktur und Zeilennummern stimmen mit der
  TD-006-Beschreibung überein:
  - **Test 1** (Zeile 26-42) `SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase`:
    Assertion (Zeile 38-41) akzeptiert aktuell nur zwei Pfade —
    `"No missing-index recommendations found"` ODER `"| Score |"`
    (Markdown-Tabellen-Header). Der Graceful-Degradation-Pfad
    (Permission-Notiz mit `"VIEW SERVER STATE"`) wird NICHT akzeptiert.
  - **Test 4** (Zeile 65-83) `SuggestIndexesAsync_ShouldReturnPermissionNote_IfViewServerStateMissing_OtherwiseMarkdown`:
    Assertion (Zeile 78-82) akzeptiert bereits alle drei Pfade
    (`"VIEW SERVER STATE"` ODER `"No missing-index recommendations"`
    ODER `"| Score |"`) — das ist exakt das Ziel-Pattern für Test 1.
  - Beide Tests rufen `SuggestIndexesAsync` mit identischen/sehr
    ähnlichen Parametern auf (`_db`, keine Filter) und prüfen vorher
    dieselben zwei Basis-Assertions (`IsSuccess`, Header, Restart-Hinweis).
- Keine anderen Strukturen im Spiel — reine 1:1-Übernahme der bereits
  bestehenden Assertion-Logik aus Test 4 in Test 1. Kein neuer Helper
  nötig, keine Duplikation über das in Test 4 bereits etablierte Pattern
  hinaus.
- `tech-debt.md` TD-006 ist bereits als "in Bearbeitung (step-007)"
  markiert (Nutzer-Direktive), `roadmap.md` EPIC-04 beschreibt step-007
  bereits mit identischem Scope — keine Abweichung zwischen Vorgabe und
  vorgefundenem Code, keine Anpassung an `roadmap.md` in diesem
  Planungsschritt nötig (EPIC-04 bleibt bis `approved` offen, siehe
  Schritt 6).

## Intention

Test 1 soll — wie Test 4 bereits — alle drei laut Architektur gültigen
Output-Pfade von `SuggestIndexesAsync` akzeptieren (Permission-Notiz,
"No recommendations", Markdown-Tabelle), statt implizit ein lokales
`GRANT VIEW SERVER STATE` als Testvoraussetzung zu erzwingen. Nach diesem
Step ist Test 1 setup-tolerant und TD-006 sowie EPIC-04 vollständig
abgeschlossen.

## Konkrete Änderungen

### Datei 1: `tests/SqlToAi.Tests/Integration/IndexSuggestionServiceIntegrationTests.cs` (Zeile 36-41)

- **Was:** Die Assertion in Test 1
  (`SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase`)
  um den dritten Pfad erweitern: zusätzliche Bedingung
  `result.Value.Contains("VIEW SERVER STATE", StringComparison.Ordinal)`
  im `Assert.True(...)`-Aufruf ergänzen (analog Test 4, Zeile 78-82).
  Kommentar (Zeile 36-37) und Failure-Message (Zeile 41) entsprechend auf
  drei Pfade anpassen.
- **Warum:** TD-006 — Asymmetrie zu Test 4 beseitigen, Test 1
  setup-tolerant gegen fehlende `VIEW SERVER STATE`-Berechtigung machen.

## Tests

- [ ] `SuggestIndexesAsync_ShouldReturnMarkdownWithRestartHint_AgainstRealDatabase`
      (erweiterter Test 1) — muss weiterhin grün sein, unabhängig davon,
      ob die lokale Test-Instanz `VIEW SERVER STATE` hat oder nicht.
- [ ] Alle übrigen Tests (`dotnet test`) bleiben grün — reine
      Assertion-Erweiterung, kein Produktionscode betroffen.

## Definition of Done

- [ ] Alle "Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`roadmap.md`) grün (`dotnet build`)
- [ ] Test-Command aus Tech-Stack-Notiz grün (`dotnet test`)
- [ ] Commit auf aktuellem Branch (Conventional Commit, Deutsch,
      Suffix `[sql-index-suggestions]`)
- [ ] `step-007/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf
      `done (pending audit)` gesetzt

## Rules-Refs

- Keine spezifisch passende Regel im Regel-Index gefunden — reine
  Test-Assertion-Erweiterung ohne neue Struktur, keine
  Architektur-/Naming-/AppSettings-Implikation. `.agents/rules/AiNetLinter.mdc`
  (Test-LOC-Grenzwerte) bleibt relevant im Hintergrund, aber die Änderung
  ist zu klein, um die Testmethode über die Grenzwerte zu heben (aktuell
  ~17 LOC, bleibt nach Erweiterung deutlich unter dem Limit von 100 LOC).

## Bekannte Ausnahmen

- Keine.

## Code-Skizze (optional)

```csharp
// Analog Test 4 (Zeile 78-82):
Assert.True(
    result.Value.Contains("VIEW SERVER STATE", StringComparison.Ordinal)
    || result.Value.Contains("No missing-index recommendations found", StringComparison.Ordinal)
    || result.Value.Contains("| Score |", StringComparison.Ordinal),
    "Expected permission note, 'No recommendations' message, or Markdown table with Score header.");
```

## Notes

- Reine Copy-Paste-Übernahme der bereits etablierten Assertion-Logik aus
  Test 4 — bewusst keine Extraktion in einen gemeinsamen Helper, da beide
  Tests unterschiedliche Vor-Assertions haben und die Duplikation minimal
  (eine Bedingung) und lokal begrenzt bleibt; eine Helper-Extraktion wäre
  Scope-Creep für diesen trivialen Step.
- Nach `approved`-Verdikt dieses Steps: TD-006-Eintrag aus `tech-debt.md`
  entfernen (per Policy aus `tech-debt.md`-Frontmatter), `roadmap.md`
  EPIC-04 als vollständig abgehakt markieren (das ist der letzte offene
  Punkt des Epics), `task-state.md` Status ggf. auf `done`
  zurücksetzen — das ist Aufgabe des nächsten Step-Modus-Aufrufs
  (Schritt 1, Roadmap-Abgleich), nicht dieses Step-Plans.
