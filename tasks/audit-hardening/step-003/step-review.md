---
status: done
type: step-review
task: audit-hardening
step: "003"
epic: EPIC-03
step_type: single
reviewed_by: kritiker
reviewed_by_model: Claude Sonnet 5
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-04T15:00:00+02:00
verdict: issues
tech_debt_ids: [TD-002]
---

# Review Step 003: MCP-Trail-Redaction via IAnonymizer-Reuse

## Verdict

- [ ] **approved** — alle vier Prüfebenen ok
- [x] **issues** — Fix-Step `step-003/fix-01` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `<rules_dir>/**` eingehalten
- [ ] Logische Korrektheit: Code macht was er soll, nicht nur „grün" — siehe Finding 1
- [ ] Konzept-Treue: passt die Umsetzung zu `konzept.md` — siehe Finding 1
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (497/497)

## Befund

### Plan-Erfüllung

Alle drei „Konkrete Änderungen"-Punkte aus `step-plan.md` wurden umgesetzt:
Konstruktor-Erweiterung um `IAnonymizer` (`McpTrailWriter.cs`), keine
Änderung an `Program.cs` nötig (DI löst auf, wie im Plan vorhergesehen),
`CreateWriter`-Testhelper erweitert plus 6 neue Tests. Alle im Plan
geforderten Testfälle (Redaction über alle vier Dateien, Strukturschlüssel
bleiben lesbar, Zahlen/Bools unverändert, `Enabled: false` lässt Trail
unverändert, Fail-Safe bei ungültigem JSON) sind vorhanden und grün. Die im
Plan dokumentierte Abweichung (Extract-Method wegen `MaxCognitiveComplexity`,
`CreateWriter`-Default `false` statt `true`) ist sauber begründet, funktional
äquivalent zum Plan-Vorschlag und ändert an der Plan-Erfüllung nichts.

### Rules-Konformität

- `AiNetLinter.mdc` `MaxMethodParameterCount`: Konstruktor hat 3 Parameter,
  unter dem Limit. Eingehalten.
- `AiNetLinter.mdc` `MaxCognitiveComplexity`: ursprüngliche
  `AnonymizeContainer`-Methode verletzte das Limit (18 > 15) und wurde in
  `AnonymizeObjectProperties`/`AnonymizeArrayElements` extrahiert — nach der
  Extraktion grün, Baseline-Update im selben Commit enthalten. Eingehalten.
- `SqlToAiRichtlinien.mdc` (keine hartkodierten Werte): `StructuralKeys`-Set
  und `ArrayElementPlaceholderName = "value"` sind feste, kurze Literale ohne
  Konfigurationsbezug (Plan sah das explizit als unkritisch vor). Eingehalten.
- Commit-Konventionen: Conventional-Commit-Format, deutsche Message,
  `Refs:`-Zeile vorhanden. Eingehalten.

### Logische Korrektheit

Die Extract-Method-Refaktorierung (`AnonymizeJsonStrings` →
`AnonymizeContainer` → `AnonymizeObjectProperties`/`AnonymizeArrayElements`)
ist strukturell treu zur Code-Skizze aus dem Plan — die rekursive Logik
selbst ist durch die Aufteilung nicht verändert oder gebrochen worden, alle
Zweige (Objekt/Array/Wert) sind weiterhin vorhanden und funktional identisch.

Allerdings enthält bereits die **Code-Skizze des Plans selbst** (und
unverändert die finale Implementierung) einen Korrektheitsfehler, den der
Plan an anderer Stelle explizit als zu vermeidendes Risiko benennt, aber
nicht konsequent umsetzt — siehe Finding 1 unten.

### Konzept-Treue (Ebene 4)

Die Kernanforderung aus `konzept.md` Muss-Haben 3 — Anonymisierung
**unabhängig vom `AccessLevel`** der jeweiligen Datenbank, unbedingt für
jeden Call — ist korrekt umgesetzt: `AnonymizeJsonStrings` wird in `Record`
für jeden Aufruf angewendet, ohne jede `AccessLevel`-Abfrage; der einzige
Schalter ist der globale `Anonymizer.Enabled`, was laut Plan und Konzept so
gewollt ist (Trail-Redaction kennt `AccessLevel` gar nicht — das ist
Absicht, nicht Lücke). Keine Verschlüsselung, kein neues Krypto-System
eingeführt — reine Wiederverwendung von `IAnonymizer`, wie im Non-Goal
„Verschlüsselung des MCP Trail at-rest" gefordert.

Der Rest der Ebene-4-Prüfung ist sauber — bis auf denselben Punkt wie oben
(Finding 1): die konkrete Umsetzung der Strukturschlüssel-Ausnahme
untergräbt in einem realistischen Szenario genau das Muss-Haben-Ziel
„MCP-Trail-Dateien enthalten dieselbe Anonymisierung ... unabhängig vom
`AccessLevel`" — nicht wegen `AccessLevel`, sondern wegen einer zu groben
Namens-Übereinstimmung, die der Plan selbst als Risiko benannt hatte.

### Build-/Test-Status

```
dotnet build → grün (0 Warnungen, 0 Fehler)
dotnet test  → grün (497 Tests, 0 Fehler)
```

## Findings (nur bei `issues`)

1. `src/SqlToAi/Mcp/McpTrailWriter.cs:335` (`AnonymizeObjectProperties`) —
   **[CRITICAL] [Logik / Konzept-Treue]** Die Strukturschlüssel-Ausnahme
   (`if (StructuralKeys.Contains(key)) continue;`) prüft **nur den
   Property-Namen**, unabhängig davon, auf welcher Ebene/in welchem Kontext
   er auftritt — genau das, was `step-plan.md` (Abschnitt „Konkrete
   Änderungen", Datei 1) explizit ausschließen wollte: „nur deren *Werte*
   ausschließen, nicht andere gleichnamige Properties tiefer im Baum
   versehentlich mit-ausschließen — pro Ebene prüfen, nicht global per
   String-Suche". Die Implementierung tut exakt das Gegenteil: sie schließt
   *jedes* Vorkommen von `jsonrpc`/`id`/`method`/`type` auf *jeder*
   Verschachtelungsebene von der Anonymisierung aus, nicht nur die
   tatsächlichen JSON-RPC-Envelope-Schlüssel bzw. den Content-Block-
   Discriminator.
   Konkretes, reales Angriffsszenario: `sql_execute_query` (und
   `sql_get_routine_parameters`, `sql_benchmark_optimization`,
   `sql_compare_queries`) nehmen ein frei benanntes `parameters`-Objekt
   entgegen, dessen Schlüssel vom aufrufenden LLM frei gewählt werden
   (`ToolRegistry.cs` Beschreibung: „z. B. `{\"CustomerId\": 42}`"). Wählt
   das LLM für einen Bind-Parameter zufällig den Namen `id`, `type` oder
   `method` (z. B. `{"id": "123-45-6789"}` als String-Bindparameter für
   eine Sozialversicherungsnummer-Suche), wird dieser Wert in
   `ArgumentsJson`/`RawRequestJson` **nicht** anonymisiert und landet im
   Klartext in `*-request.json` und `*-call.jsonl` — obwohl genau das
   (PII-Werte aus Tool-Argumenten in Trail-Dateien) der Kernzweck von
   Muss-Haben 3 ist zu verhindern, unabhängig vom `AccessLevel`. Die 6 neuen
   Tests decken diesen Fall nicht ab (kein Test mit einem Argument-Property
   gleichen Namens wie ein Strukturschlüssel).
   **Fix:** Strukturschlüssel-Ausnahme positionsabhängig statt
   namensabhängig machen — z. B. nur beim Wurzelobjekt der JSON-RPC-Hülle
   (`jsonrpc`, `id`, `method` dort) und nur innerhalb eines
   `content[]`-Elements (`type`) ausnehmen, nicht rekursiv überall. Am
   einfachsten über einen zusätzlichen „ist das die Envelope-Ebene bzw. ein
   Content-Block"-Kontext-Flag, das nur an der obersten Rekursionsstufe
   bzw. beim direkten Betreten eines `content[]`-Objekts gesetzt wird, statt
   der aktuellen globalen `StructuralKeys.Contains(key)`-Prüfung, die auf
   jeder Ebene gleich greift. Test ergänzen: `parameters`-Objekt mit einem
   Schlüssel `id`/`type`/`method`, das einen sensiblen String enthält, muss
   redigiert werden.

## Sonstige Beobachtungen / MINOR / NITPICK

- Der Coder hat in „Bekannte Unschärfen" korrekt notiert, keinen echten,
  tief verschachtelten Tool-Response-Payload nachgebaut zu haben — das war
  im konkreten Test-Set kein Problem (Rekursion selbst ist strukturell
  korrekt), aber es hat verhindert, dass die Namenskollision aus Finding 1
  auffiel, da alle Testfälle synthetisch und flach gehalten waren.
- Die im Coder-Kommentar erwähnte Grobheit der alias-only-Overload bei
  bereits als Ganzes serialisierten Query-Ergebnis-Strings (`content[0].text`
  enthält bei den meisten Tools ein komplettes, bereits serialisiertes
  Ergebnis als ein einziger String-Leaf) führt in der Praxis zu
  **Über-Redaktion** (der ganze Blob wird zeichenweise scrambled), nicht zu
  Unter-Redaktion — das verletzt Muss-Haben 3 nicht (es wird nie mehr
  gezeigt als über `ReadOnlyAnonymized`, eher weniger), ist aber eine
  bewusste, im Plan dokumentierte Scope-Grenze (keine Tabellen-/
  Spalten-Auflösung für Trail-Redaction) und daher kein Finding.

## Tech-Debt-Einträge aus diesem Review

- `TD-002` (siehe `tech-debt.md`) — `Anonymizer.IsColumnExcluded` wertet den
  `context`-Parameter nie aus, wodurch `AnonymizerOptions.ExcludedColumns`
  projektweit (nicht nur für den Trail) wirkungslos ist — vorbestehend,
  außerhalb des Scopes von Step 003.
