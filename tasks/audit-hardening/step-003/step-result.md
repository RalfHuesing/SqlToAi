---
status: done
type: step-result
task: audit-hardening
step: "003"
epic: EPIC-03
step_type: single
coded_by: coder
coded_by_model: Claude Sonnet 5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T13:30:00+02:00
code_commit_hash: c3952e4
status_after: done
blocker_category: n/a
---

# Result Step 003: MCP-Trail-Redaction via IAnonymizer-Reuse

## Zusammenfassung

`McpTrailWriter` bekommt `IAnonymizer` per Konstruktor-Injection und redigiert
`ArgumentsJson`/`RawRequestJson`/`ResponseJson` vor allen vier Schreibvorgängen
über eine neue rekursive JSON-Walk-Methode (`AnonymizeJsonStrings` →
`AnonymizeContainer` → `AnonymizeObjectProperties`/`AnonymizeArrayElements`),
die jeden String-Leaf via `_anonymizer.Anonymize(propertyName, value)`
ersetzt. `jsonrpc`/`id`/`method`/`type` bleiben unredigiert. `Program.cs`
brauchte keine Änderung (DI löst den zusätzlichen Singleton-Parameter
automatisch auf).

## Geänderte Dateien

- `src/SqlToAi/Mcp/McpTrailWriter.cs` — `IAnonymizer`-Konstruktor-Parameter,
  neue Redaction-Pipeline (`AnonymizeJsonStrings`/`AnonymizeContainer`/
  `AnonymizeObjectProperties`/`AnonymizeArrayElements`), `Record`/`ToJsonShape`
  nutzen jetzt die anonymisierten Strings für alle vier Ausgabedateien.
- `tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs` — `CreateWriter`-Helper um
  optionalen `anonymizerEnabled`-Parameter erweitert (Default `false`, echte
  `Anonymizer`-Instanz), neuer `CreateAnonymizer`-Helper, 6 neue Tests für
  Redaction/Strukturschlüssel/Zahlen-Bools/Disabled-Fall/Fail-Safe.

## Commit

- **Code-Commit-Hash:** `c3952e4`
- **Message:**
  ```
  feat(mcp): redigiere MCP-Trail via IAnonymizer-Reuse [audit-hardening]

  McpTrailWriter.Record wendet vor jedem Schreiben aller vier Trail-Dateien
  (*-call.jsonl, *-request.json, *-response.json, *-response.md) dieselbe
  IAnonymizer-Maskierung an, die auch fuer ReadOnlyAnonymized-Datenbanken
  genutzt wird. Da fuer generisches MCP-Tool-JSON kein Tabellen-/Spalten-
  kontext existiert, wird die alias-only-Overload Anonymize(columnName,
  value) rekursiv auf jeden String-Leaf im JSON-Baum angewendet, mit dem
  jeweiligen Property-Namen als columnName-Substitut. Strukturschluessel
  (jsonrpc, id, method, type) bleiben unredigiert, damit die Korrelation
  zwischen Trail-Dateien erhalten bleibt. Ungueltiges JSON faellt fail-safe
  auf den Original-String zurueck.

  Refs: tasks/audit-hardening/step-003
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin).

## Build-/Test-Output

```
dotnet build → grün
dotnet test  → grün (497 Tests, 0 Fehler)
```

Zwischenzeitlich schlug der Pflicht-Testlauf einmal fehl:
`AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` meldete
`MaxCognitiveComplexity` (18 > 15) für die ursprüngliche
`AnonymizeContainer`-Methode. Behoben durch Extraktion in
`AnonymizeObjectProperties`/`AnonymizeArrayElements` (Extract-Method, kein
Scope-Wechsel); der finale Testlauf (497/497 grün) ist der oben zitierte.
`tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` musste dadurch
mit den neuen Datei-Hashes für `McpTrailWriter.cs`/`McpTrailWriterTests.cs`
mitgeführt werden (automatisch vom Linter-Test aktualisiert) — im
Code-Commit enthalten, da sonst der Linter-Test beim nächsten Lauf wieder
rot wäre.

## Abweichungen vom Plan

- **`CreateWriter`-Default bleibt `anonymizerEnabled: false` statt `true`.**
  Der Plan schlug vor, den Default auf eine aktivierte, echte
  `Anonymizer`-Instanz zu setzen und stattdessen die *bestehenden* Tests
  gezielt auf deaktiviert umzustellen. Beim Durchlesen der 9 bestehenden
  Tests zeigte sich: sechs von ihnen prüfen explizit unveränderten
  Klartext-Inhalt (`Record_ShouldWriteCompanionRequestJsonAndResponseJson`,
  `Record_ShouldWriteResponseMd_WhenResponseIsMarkdown`,
  `Record_ShouldIncludeRawArgsAndResponse_Verbatim` u. a.) und sind
  inhaltlich nicht Redaction-Tests, sondern Tests für Companion-Datei-Form,
  Markdown-Erkennung, Verbatim-Schreiben, Sanitizing, Thread-Safety. Um
  keinen dieser bestehenden Testkörper anzufassen (Scope-Minimierung), habe
  ich stattdessen den Default auf `false` belassen (bewahrt exakt das
  bisherige Verhalten alle bestehenden Aufrufer) und nur die sechs neuen,
  redaction-spezifischen Tests explizit `anonymizerEnabled: true` übergeben
  lassen. Funktional identisch zum Plan-Vorschlag (jeder bestehende Test
  bleibt grün, jeder neue Test deckt Redaction ab), nur mit umgekehrtem
  Default und ohne Änderungen an den bestehenden Testkörpern.
- Kleinere Abweichung von der Code-Skizze im Plan: statt einer einzigen
  rekursiven `AnonymizeNode(node, propertyName)`-Methode mit `foreach`-Body
  direkt im `switch` wurden die Objekt-/Array-Zweige in eigene Methoden
  (`AnonymizeObjectProperties`, `AnonymizeArrayElements`) extrahiert, weil
  die Skizzen-Variante beim ersten Testlauf gegen `MaxCognitiveComplexity`
  (Limit 15, siehe `AiNetLinter.mdc`) verstieß (18 gemessen). Verhalten
  identisch, nur strukturell aufgeteilt.

## Beobachtungen

- `Anonymizer.IsColumnExcluded` prüft aktuell ausschließlich den globalen
  Schalter `_options.Anonymizer.Enabled` — der `context`-Parameter wird nie
  ausgewertet (auch nicht für `ExcludedColumns`-Glob-Patterns, die laut
  XML-Doku der Klasse eigentlich greifen sollten). Das war schon vor diesem
  Step so und wirkt sich auf die Trail-Redaction nur insofern aus, als eine
  künftige spaltenspezifische Ausnahmeliste für Trail-Inhalte damit aktuell
  nicht möglich wäre — kein Blocker für diesen Step (Plan verlangt nur den
  globalen Schalter), aber ein möglicher Tech-Debt-Kandidat, falls das
  Projekt später spaltenspezifische Trail-Ausnahmen braucht.
- `PerformanceMeasurementService.cs` (`ExecuteWarmupRunsAsync`/
  `ExecuteMeasuredRunsAsync`, je 8 Parameter) und `ToolDispatcher.cs`
  (Konstruktor mit 6 Abhängigkeiten) sowie `GlobMatcherTests.cs`
  (`AvoidExcessiveMiddleMen`) erzeugen ebenfalls Linter-Violations laut dem
  Report — diese waren bereits vor meiner Änderung vorhanden (nicht von mir
  verursacht, keine Datei davon wurde in diesem Step berührt) und sind
  offenbar über eine Baseline abgedeckt, die der Linter-Test bereits vor
  meinem Lauf kannte. Nur zur Kenntnis, kein eigener Fix versucht (außerhalb
  des Scopes).

## Bekannte Unschärfen

- Der Plan sah für Array-Elemente einen "festen Platzhalter" als
  `columnName` vor; ich habe dafür die Konstante
  `ArrayElementPlaceholderName = "value"` gewählt (exakt der im Plan
  genannte Vorschlagswert). Sollte der Kritiker einen anderen Namen
  bevorzugen, ist das eine reine String-Konstante ohne weitere Tragweite.
- Die neuen Tests prüfen Redaction anhand von `Assert.DoesNotContain` auf
  den ursprünglichen Klartext-String — das verifiziert, dass der Wert
  *irgendwie* verändert wurde, nicht das exakte Scramble-Ergebnis
  (deterministisch, aber vom internen Hash/Random-Seed abhängig). Das
  entspricht dem bestehenden Test-Stil für `Anonymizer` selbst (keine
  Tests, die exakte Scramble-Ausgaben hart codieren), sollte aber bei der
  Prüfung im Hinterkopf bleiben.
- Ich habe nicht geprüft, ob es MCP-Tools gibt, deren Response-JSON
  verschachtelte Arrays von Objekten mit erneut verschachtelten Arrays
  enthält (z. B. `sql_get_schema`-Tabellenzeilen als Array von Objekten mit
  Array-Properties) — die Rekursion deckt das strukturell ab, aber ich habe
  keinen echten Tool-Response-Payload dieser Tiefe als Testfall nachgebaut,
  nur synthetische Beispiele.
