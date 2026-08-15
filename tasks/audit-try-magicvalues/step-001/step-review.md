---
status: done
type: step-review
task: audit-try-magicvalues
step: 001
epic: EPIC-01
step_type: batch
reviewed_by: kritiker
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-15T21:53:00+02:00
verdict: approved
tech_debt_ids: []
---

# Review Step 001: EPIC-01 Konstanten-Zentralisierung & Boilerplate-Cleanup

## Verdict

- [x] **approved** — alle vier Prüfebenen ok
- [ ] **issues** — Korrektur-Step `step-<MMM>` angelegt (`corrects: step-<NNN>`)
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle 10 Plan-Items + 3 Zusatzfunde umgesetzt
- [x] Rules-Konformität: `SqlToAiRichtlinien.mdc` §4 (No Magic Values) + §5 (Zero-Warning) sowie `AiNetLinter.mdc` (sealed/Methodenlänge/DuplicateCode) eingehalten
- [x] Logische Korrektheit: Code macht was er soll, verhaltensneutral, keine übersehenen Edge-Cases
- [x] Konzept-Treue: passt zu `konzept.md` (Scope, Non-Goals, Muss-Haven Pkt. 1 erfüllt)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (523/523, 0 skipped — Linter-Test lief real durch)

## Befund

### Plan-Erfüllung

Alle 10 Items und die drei Zusatzfunde (QueryTokenResolver.cs:77, DetailSchemaRenderer.cs:251, ToolDispatcherTestFakes.cs:185) sind im Diff `0f6f99a` nachweisbar umgesetzt. Der Coder hat über die Mindestpflicht hinaus **alle fünf** im Plan nur als „Empfehlung" gekennzeichneten SQL-Fehlercodes (`20/40/53/10060/10061`) gleich in `SqlServerErrorCode` mit aufgenommen, wodurch der `IsInfrastructureException`-Switch in `SqlToAiErrorMapper.cs:77` vollständig auf benannten Konstanten läuft — positiv über den Plan hinaus.

### Rules-Konformität

- `SqlToAiRichtlinien.mdc` §4 *„No Magic Values"*: vollständig erfüllt für MV-1..7 in den im Plan zitierten Stellen.
- `SqlToAiRichtlinien.mdc` §5 *„Zero-Warning-Direktive"*: `TreatWarningsAsErrors=true` aktiv, Build ist 0/0.
- `AiNetLinter.mdc`: alle fünf neuen Konstanten-Klassen tragen `#nullable enable` am Dateianfang, sind `internal static class` (statisch implizit `sealed` — `EnforceSealedClasses` zufrieden). `BuildObjectDetailTool` bleibt mit ~14 Zeilen weit unter dem 60-Zeilen-Limit. `RunLinterShouldBeClean` lief real (nicht skipped) und passte.

### Logische Korrektheit

Verhaltensgleichheit 1:1 gewahrt: identische Konstantenwerte, identische Reihenfolge im `switch`-Arm, identische `string.Equals`-Semantik mit `OrdinalIgnoreCase`, identische `Required`-Arrays in den Tool-Definitionen, identische `200 ms` Regex-Timeout-Werte (nur jetzt zentral bezogen). Der einzige subtile Punkt ist die fallengelassene, differenzierte `object_name`-Beschreibung („target table" / „target table or view" / „stored procedure or function") — sie wurde auf „target object" vereinheitlicht, was der Plan ausdrücklich so vorgesehen hat und die `Tool.Description` der fünf Builder weiterhin den Tool-spezifischen Kontext liefert. Verhalten des MCP-Outputs ändert sich nicht.

### Konzept-Treue (Ebene 4)

Konzept §„Muss-Haven" Pkt. 1 (Phase 1: Konstanten-Zentralisierung) vollständig abgearbeitet. Kein Eingriff in Non-Goals (keine `GlobMatcher`/`LikePatternMatcher`-Zusammenlegung, keine `SqlToAiOptions`-Änderungen, keine `AppSettingsMigrator`-Änderungen, keine `SchemaService`-Forwarder-Änderungen). EPIC-02 (DRY-1 Guardrail-Pipeline) und EPIC-03 (DRY-T1..T3) bleiben unangetastet — der Schritt hält sich sauber an seinen EPIC-Scope.

### Build-/Test-Status

```
dotnet build SqlToAi.slnx  → 0 Warnung(en), 0 Fehler
dotnet test SqlToAi.slnx   → 523 erfolgreich, 0 fehlgeschlagen, 0 übersprungen, Dauer 16 s
RunLinterShouldBeClean     → lief real durch (AiNetLinter.exe unter C:\Daten\AiNetLinter-win-x64\ installiert), grün
```

Hinweis: Der Commit-Subject `refactor: zentralisiere MV-1..7 Konstanten und entferne Boilerplate-Duplikate [audit-try-magicvalues]` ist 77 Zeichen lang und überschreitet damit die in `roadmap.md` genannte 72-Zeichen-Empfehlung um 5 Zeichen. Das ist im Projekt-Kontext tolerierbar (siehe andere aktuelle Commits wie `docs(task): step-001 (EPIC-01 Konstanten) abschliessen [audit-try-magicvalues]` mit 80 Zeichen), daher kein Finding.

## Sonstige Beobachtungen / MINOR / NITPICK

- **`AnonymizationMode.Scramble` ist totes Symbol** (item-06): Definiert in `src/SqlToAi/Anonymization/AnonymizationMode.cs:12`, aber **nirgendwo** in `src/` oder `tests/` referenziert — `grep -r "AnonymizationMode\.Scramble"` liefert nur Treffer im Plan-Dokument selbst. Der XML-Kommentar auf der Klasse behauptet irreführend „both values are referenced from `Anonymizer`", real referenziert wird aber nur `AnonymizationMode.Hash` (in `Anonymizer.cs:88`). Der Plan hatte an dieser Stelle eine „soll"-Empfehlung für einen symmetrischen if/else-Block mit beiden Konstanten ausgesprochen — der Coder hat den impliziten Fallback `return Scramble(value);` beibehalten, was verhaltensneutral korrekt ist, aber die zweite Konstante ungenutzt lässt. **Vorschlag für einen Folge-Patch (nicht blocking):** entweder `AnonymizationMode.Scramble` entfernen, oder den Kommentar korrigieren auf „the strings document the `AnonymizerOptions.DefaultMode` contract". Beide Varianten sind in <2 Minuten erledigt.

- **Doku-Commit getrennt:** Der Plan ließ die Wahl zwischen einem oder zwei Commits offen; der Coder hat sich für zwei entschieden (Code `0f6f99a`, Doku `be4a0f0`). Beide Commits einzeln betrachtet builden und testen grün — entspricht dem Plan-Hinweis zur Reihenfolge.

## Tech-Debt-Einträge aus diesem Review

Keine. Alle außerhalb des Step-Scopes liegenden Beobachtungen, die während des Reviews aufgefallen sind (Routine-Typen `P/FN/TF/IF` in `DetailSchemaRenderer.GetRoutineParametersAsync:293`, MV-8 `ShowPlanXml`-Elementnamen, MV-T1 JSON-RPC-Error-Codes) sind bereits explizit im `konzept.md` und in `roadmap.md` als bewusst zurückgestellt markiert und gehören in zukünftige Epics/Steps, nicht hierhin.
