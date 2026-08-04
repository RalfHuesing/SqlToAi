---
status: done
type: step-result
task: audit-hardening
step: "006"
epic: EPIC-06
step_type: single
coded_by: coder
coded_by_model: Claude Sonnet 5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T23:59:00+02:00
code_commit_hash: e21a934
status_after: done (pending audit)
blocker_category: n/a
---

# Result Step 006: Content-Block-Kontext an result-Objekt der Envelope-Ebene koppeln (TD-003)

## Zusammenfassung

`RedactionContext` (`McpTrailWriter.cs`) um ein drittes Flag `IsResultObject` erweitert.
`IsContentBlock` wird jetzt nur noch aktiviert, wenn das `content`-Array direkte Property des
`result`-Objekts der Envelope-Wurzel ist (`context.IsResultObject && key == "content"`) — nicht
mehr für jede beliebige, gleichnamige `content`-Property irgendwo im JSON-Baum. Die
Kontext-Weitergabe für den Abstieg wurde in eine eigene Hilfsmethode `ChildContextFor`
extrahiert (analog zum bestehenden `IsExemptStructuralKey`-Muster), um die Cyclomatic-/
Cognitive-Complexity der Methode nicht zu erhöhen. Drei neue Tests decken den TD-003-Angriffsfall
(`arguments.content`), den Tiefenfall (`result.someWrapper.content`, optional laut Plan) sowie die
Regression (`result.content[0].type` bleibt lesbar) ab.

## Geänderte Dateien

- `src/SqlToAi/Mcp/McpTrailWriter.cs` — `RedactionContext` um `IsResultObject` erweitert;
  `AnonymizeObjectProperties` unterscheidet jetzt drei Zweige (Struktur-Redaction, `result`-Abstieg,
  generischer Abstieg über neue `ChildContextFor`-Hilfsmethode); `content`-Sonderfall nur noch
  unter `context.IsResultObject` aktiv. `IsExemptStructuralKey` unverändert.
- `tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs` — auf die allgemeinen (nicht
  redaktionsspezifischen) Tests reduziert; Redaction-Tests in eigene Datei ausgelagert (siehe
  „Abweichungen vom Plan").
- `tests/SqlToAi.Tests/Mcp/McpTrailWriterRedactionTests.cs` (neu) — alle bisherigen
  Redaction-Tests aus `McpTrailWriterTests.cs` unverändert übernommen (eigene Fixture-Instanz,
  kein gemeinsamer State), plus drei neue Fälle:
  `Record_ShouldRedactTypeProperty_InContentArrayNotOnResult` (TD-003-Kernfall:
  `arguments.content[].type` mit sensiblem Wert wird jetzt redigiert),
  `Record_ShouldRedactTypeProperty_InNestedContentArray_NotDirectlyOnResult` (optionaler
  Tiefenfall aus dem Plan: `result.someWrapper.content[].type` ebenfalls redigiert),
  `Record_ShouldKeepContentBlockTypeDiscriminator_Readable_ButRedactNestedTypeElsewhere`
  (bereits vorhanden, unverändert, dient als Regressionstest für `result.content[0].type`).
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — Hash-Update für die vier
  geänderten/neuen Dateien (automatische Folge von `dotnet test`).

## Commit

- **Code-Commit-Hash:** `e21a934`
- **Message:**
  ```
  fix(mcp): Content-Block-Kontext an result.content[] der Envelope-Wurzel binden (TD-003) [audit-hardening]

  RedactionContext bekommt ein drittes Flag IsResultObject: der
  type-Discriminator wird nur noch fuer result.content[] direkt an der
  Envelope-Wurzel ausgenommen, nicht mehr fuer jede beliebige
  content-Array-Property irgendwo im JSON-Baum (z. B. arguments.content).
  Testdatei wegen MaxLineCount in McpTrailWriterTests.cs (allgemein) und
  McpTrailWriterRedactionTests.cs (Redaction-spezifisch) aufgeteilt.

  Refs: tasks/audit-hardening/step-006
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier drin — Selbstbezug, siehe
  `git log`).

## Build-/Test-Output

```
dotnet build → grün (0 Warnungen, 0 Fehler)
dotnet test  → grün (502 Tests, 0 Fehler)
```

## Abweichungen vom Plan

- **Test-Datei aufgeteilt, nicht im Plan vorgesehen.** Der Plan sah nur „mindestens zwei neue
  Tests" in `McpTrailWriterTests.cs` vor. Nach dem Hinzufügen der neuen Tests überschritt die Datei
  mit 534 Zeilen die `MaxLineCount`-Grenze (500, `.agents/rules/AiNetLinter.mdc`), was
  `RunLinterShouldBeCleanOrBaselineMatch` als neue (nicht im Baseline enthaltene) Violation rot
  werden ließ. Statt die Grenze zu unterlaufen (z. B. durch Wegkürzen von Kommentaren) habe ich die
  Datei entlang der bereits im Code vorhandenen Trennlinie „// Redaction (IAnonymizer reuse) …"
  aufgeteilt: `McpTrailWriterTests.cs` (301 Zeilen, allgemeine Tests) und die neue
  `McpTrailWriterRedactionTests.cs` (290 Zeilen, alle Redaction-Tests inkl. der drei neuen). Beide
  Klassen haben eine eigene, unabhängige Fixture (kein gemeinsamer State, xUnit-Standardmuster) —
  bewusst keine Basisklasse/Partial-Class eingeführt, da die Linter-Doku „Partial" explizit als
  letztes Mittel nur für bereits-partiale Dateien nennt und eine neue Vererbungshierarchie eine
  größere strukturelle Änderung gewesen wäre, als der Step rechtfertigt. Verhalten unverändert,
  reine Datei-Organisation.
- Optionaler Test 3 aus dem Plan (`result.someWrapper.content` nicht als Content-Block behandeln)
  wurde ergänzt, da er ohne Verrenkungen darstellbar war (der Plan stellte das als „nice to have",
  kein Muss).

## Beobachtungen

Keine neuen über die im Plan bereits dokumentierten JIT-Beobachtungen hinaus.

## Bekannte Unschärfen

- Die Aufteilung der Testdatei war für mich als Coder eine reine MaxLineCount-Notwendigkeit, keine
  im Plan antizipierte Struktur. Der Kritiker sollte kurz bestätigen, dass die Trennung
  (allgemein vs. redaktionsspezifisch, mit dupliziertem Fixture-Setup statt gemeinsamer Basisklasse)
  als angemessen gilt und nicht doch eine gemeinsame Basisklasse bevorzugt wird — ich habe mich für
  das einfachere, weniger invasive Muster entschieden.
