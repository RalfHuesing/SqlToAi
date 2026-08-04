---
status: done (pending audit)
type: step-plan
task: audit-hardening
step: "006"
title: "Content-Block-Kontext an result-Objekt der Envelope-Ebene koppeln (TD-003)"
epic: EPIC-06
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-04T23:45:00+02:00
related_to: [step-003/fix-01/step-plan.md, step-003/fix-01/step-review.md]
---

# Step 006: Content-Block-Kontext an result-Objekt der Envelope-Ebene koppeln (TD-003)

## Bezug

- **Task:** `audit-hardening`
- **Epic:** `EPIC-06` aus `roadmap.md` (aus TD-003) — letztes noch offenes
  Epic der Roadmap. Nach diesem Step sind (vorbehaltlich Review) alle
  Epics abgehakt/obsolet.
- **Konzept-Referenz:** `konzept.md` Muss-Haben 3 („MCP Trail Redaction").
  Dieser Step verschärft eine von `step-003/fix-01` selbst sanktionierte
  Restungenauigkeit, führt aber keine neue Anforderung aus `konzept.md`
  ein — reine Präzisierung eines bereits umgesetzten Muss-Habens.

## Aktueller Projektzustand (JIT-Kontext)

- `src/SqlToAi/Mcp/McpTrailWriter.cs` (aktueller Stand, Commit `d64241d`,
  vollständig gelesen): `RedactionContext` ist aktuell ein
  `record struct(bool IsEnvelopeRoot, bool IsContentBlock)` (Zeile 104).
  `AnonymizeObjectProperties` (Zeile 353-374) aktiviert den
  Content-Block-Kontext im Zweig
  `else if (key == "content" && obj[key] is JsonArray contentArray)`
  (Zeile 365-368) — **rein namensbasiert**, unabhängig davon, in welchem
  Objekt diese `content`-Property gefunden wird. `IsExemptStructuralKey`
  (Zeile 382-384) nutzt `context.IsContentBlock` dann, um das `type`-Feld
  direkter Content-Block-Elemente von der Redaktion auszunehmen.
- **Der Bug (TD-003):** Ein vom LLM frei gewähltes Objekt-Property namens
  `content` mit Array-Wert — egal ob in `ArgumentsJson`, irgendwo tief
  verschachtelt in `ResponseJson`, oder sonstwo im Baum — bekommt
  ebenfalls `IsContentBlock = true` für seine direkten Objekt-Elemente,
  wodurch ein `type`-Property darin fälschlich von der Redaktion
  ausgenommen würde. Tatsächlich ist `content[]` nur an **einer** Stelle
  strukturell bedeutsam: als `result.content[]` im JSON-RPC-Response-
  Envelope (siehe `ExtractMarkdownText`, Zeile 218-241, das exakt diesen
  Pfad — `root.result.content` — für die Markdown-Erkennung ansteuert;
  das ist die einzige tatsächlich vom MCP-Protokoll vorgesehene
  Content-Block-Struktur im gesamten Trail).
- **Wie das Fix-01-Muster (Vorbild) funktioniert:** `IsEnvelopeRoot` wird
  nur beim allerersten Aufruf von `AnonymizeJsonStrings` gesetzt
  (`isEnvelope`-Parameter, Zeile 141-143 in `Record`) und dann bei jedem
  Abstieg in ein Kind-Objekt/-Array explizit zurückgesetzt
  (`childContext = default`, Zeile 355/390). Derselbe Mechanismus lässt
  sich für „ist dieses Objekt das `result`-Objekt der Envelope-Wurzel"
  wiederverwenden — kein neues Muster nötig, nur ein drittes Flag nach
  demselben Rezept.
- **Bereits bestehende Tests, die als Regression dienen:**
  `Record_ShouldKeepContentBlockTypeDiscriminator_Readable_ButRedactNestedTypeElsewhere`
  (`tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs`, ca. Zeile 448+)
  deckt bereits `result.content[0].type` (muss lesbar bleiben) und ein
  verschachteltes `type` **innerhalb** eines bereits serialisierten
  Text-Blobs (bleibt redigiert, da dort gar kein `JsonArray` mehr
  vorliegt) ab. Diese Tests decken aber **nicht** den in TD-003
  benannten Fall (`content`-Array an anderer Stelle im *JSON-Baum*, z. B.
  `arguments.content` oder `result.someWrapper.content`) — dafür fehlt
  noch ein Test, das ist die eigentliche in TD-003 dokumentierte Lücke.
- **Wiederzuverwendende Struktur:** `RedactionContext` selbst (Record
  Struct + `with`-Ausdrücke), die `childContext = default`-Reset-Technik
  und `IsExemptStructuralKey` als zentrale Entscheidungsstelle — alles
  aus `step-003/fix-01` unverändert übernehmbar, nur um ein drittes Feld
  erweitert. Keine neue Infrastruktur nötig.

## Intention

Der Content-Block-Kontext (`IsContentBlock`) darf künftig nur noch dann
für ein `content`-Array aktiviert werden, wenn dieses Array als direkte
Property eines Objekts gefunden wird, das selbst das `result`-Objekt der
Envelope-Wurzel ist (`root.result.content`) — nicht mehr für jede
beliebige Objekt-Property namens `content` irgendwo im Baum, egal ob in
`arguments`, tiefer verschachtelt im Response, oder sonstwo. Das schließt
die in TD-003 dokumentierte Restlücke, ohne das bereits gehärtete
CRITICAL-Finding aus `step-003/fix-01` erneut aufzurollen.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Mcp/McpTrailWriter.cs`

- **Was:**
  - `RedactionContext` um ein drittes Flag `IsResultObject` erweitern:
    `private readonly record struct RedactionContext(bool IsEnvelopeRoot, bool IsContentBlock, bool IsResultObject)`.
    XML-Doc der Record-Struct und der einzelnen Parameter entsprechend
    ergänzen (Vorbild: die bestehenden Doc-Kommentare zu
    `IsEnvelopeRoot`/`IsContentBlock`, Zeile 95-103).
  - In `AnonymizeObjectProperties`: wenn der aktuell verarbeitete Key
    `"result"` heißt **und** `context.IsEnvelopeRoot` wahr ist (d. h. wir
    stehen tatsächlich auf der Envelope-Wurzel und steigen in deren
    `result`-Property ab), wird für den Abstieg in dieses eine Kind-Objekt
    `IsResultObject = true` gesetzt (alle anderen Flags zurückgesetzt wie
    gehabt). Für jeden anderen Abstieg bleibt `IsResultObject = false`
    (Standard-Reset, analog zu `IsEnvelopeRoot`/`IsContentBlock`).
  - Der bestehende `content`-Array-Sonderfall
    (`else if (key == "content" && obj[key] is JsonArray contentArray)`)
    bekommt eine zusätzliche Bedingung: nur wenn zusätzlich
    `context.IsResultObject` wahr ist, wird `IsContentBlock = true` für
    die direkten Elemente gesetzt. Fehlt `IsResultObject`, durchläuft das
    `content`-Array den normalen `AnonymizeContainer`-Pfad ohne
    Content-Block-Ausnahme (jede String-Leaf wird wie jede andere
    redigiert, kein `type`-Property wird ausgenommen).
  - `IsExemptStructuralKey` bleibt unverändert (`IsContentBlock` ist
    weiterhin die einzige Bedingung für die `type`-Ausnahme — nur die
    Aktivierung von `IsContentBlock` selbst wird jetzt strenger).
  - Reihenfolge/Struktur der `if`/`else if`-Kette in
    `AnonymizeObjectProperties` so wählen, dass die neue
    `result`-Sonderbehandlung klar von der `content`-Sonderbehandlung
    getrennt bleibt (z. B. eigener `else if`-Zweig für
    `context.IsEnvelopeRoot && key == "result"`, der `AnonymizeContainer`
    mit dem `IsResultObject`-Kontext aufruft, **vor** dem generischen
    `else`-Fallback). Cognitive-/Cyclomatic-Complexity im Blick behalten
    (siehe Rules-Refs) — bei Bedarf die Entscheidung „welcher Kontext für
    den Abstieg in `obj[key]`" in eine eigene kleine Hilfsmethode
    extrahieren (z. B. `RedactionContext ChildContextFor(string key,
    RedactionContext context)`), analog zum bereits bestehenden Muster
    `IsExemptStructuralKey`.
  - Kein Overengineering: `IsResultObject` gilt — wie `IsContentBlock`
    bereits heute — nur für genau eine Rekursionsebene tief und wird für
    alle tieferen Nachkommen garantiert zurückgesetzt (identisches Muster
    wie der bestehende `content`-Sonderfall).
- **Warum:** behebt TD-003 exakt — der Content-Block-Kontext wird nicht
  mehr durch den bloßen Property-Namen `content` ausgelöst, sondern nur
  noch dort, wo er tatsächlich das MCP-Protokoll-Konstrukt
  `result.content[]` adressiert.

### Datei 2: `tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs`

- **Was:** Mindestens zwei neue Tests ergänzen (Muster: bestehende Tests
  rund um `Record_ShouldRedactArgumentsProperties_NamedLikeStructuralKeys`
  bzw. `Record_ShouldKeepContentBlockTypeDiscriminator...` als Vorbild für
  Test-Aufbau, `CreateWriter(enabled: true, anonymizerEnabled: true)`):
  1. Ein `arguments`-Property namens `content` mit einem `JsonArray`-Wert,
     dessen Elemente ein `type`-Property mit sensiblem String-Inhalt
     tragen (z. B. `{"content":[{"type":"SENSITIVE-VALUE"}]}` als
     `ArgumentsJson`) → das `type`-Property **muss jetzt redigiert**
     erscheinen (nicht mehr exempt) — das ist der in TD-003 konkret
     benannte Angriffsfall.
  2. Regressions-Sicherung: das bestehende, echte `result.content[0].type`
     bleibt weiterhin unverändert lesbar (bereits vorhandener Test
     `Record_ShouldKeepContentBlockTypeDiscriminator...` deckt das ab,
     muss nach der Änderung weiterhin grün sein — keine neue Test-Logik
     nötig, nur Bestätigung beim Testlauf).
  3. Optional, falls ohne Verrenkungen darstellbar: ein `content`-Array,
     das zwar unterhalb von `result` liegt, aber nicht direkt (z. B.
     `result.someWrapper.content`), sollte **ebenfalls nicht** als
     Content-Block behandelt werden (nur `result.content` direkt zählt) —
     nur ergänzen, wenn es das Kernrisiko nicht verwässert; kein Muss, da
     Fall 1 das eigentliche TD-003-Risiko bereits abdeckt.
- **Warum:** Ohne Test 1 bleibt die in TD-003 dokumentierte Lücke
  unentdeckt reproduzierbar — exakt der vom Kritiker in `tech-debt.md`
  genannte Fall („ein LLM-gewähltes `arguments.content`-Array mit
  `type`-Properties würde ebenfalls (fälschlich) exempt behandelt").

## Tests

- [ ] Neuer Test: `arguments.content`-Array mit `type`-Property und
      sensiblem String-Inhalt wird nach dem Fix redigiert (vorher exempt).
- [ ] Bestehender Test `Record_ShouldKeepContentBlockTypeDiscriminator_Readable_ButRedactNestedTypeElsewhere`
      bleibt grün (`result.content[0].type` weiterhin lesbar).
- [ ] Bestehender Test `Record_ShouldRedactArgumentsProperties_NamedLikeStructuralKeys`
      (aus `step-003/fix-01`) bleibt grün — keine Regression am
      CRITICAL-Fix.
- [ ] Alle bestehenden `McpTrailWriterTests`-Fälle weiterhin grün.

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`roadmap.md`) grün
- [ ] Test-Command aus Tech-Stack-Notiz grün
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-006/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/AiNetLinter.mdc` — `MaxCognitiveComplexity` (15) /
  `MaxCyclomaticComplexity` (12): das dritte Kontext-Flag darf
  `AnonymizeObjectProperties` nicht wieder über das Limit treiben (wurde
  in `step-003`/`step-003/fix-01` bereits je einmal per Extract-Method
  behoben) — im Zweifel weiter in kleine Hilfsmethoden aufteilen (z. B.
  eigene `ChildContextFor`-Methode statt tieferer `if`-Verschachtelung).
  `MaxLineCount` (500): Datei liegt aktuell bei ~408 Zeilen, Spielraum
  vorhanden, aber im Blick behalten.
- `.agents/rules/SqlToAiRichtlinien.mdc` — keine hartkodierten Werte: das
  Literal `"result"` bleibt ein kurzes, festes Strukturschlüssel-Literal
  ohne Konfigurationsbezug, wie bereits `"content"`/`"type"`/`EnvelopeKeys`
  im bestehenden Code — kein `IOptions<T>`-Feld nötig.

## Bekannte Ausnahmen

- Keine.

## Code-Skizze (optional)

```csharp
private readonly record struct RedactionContext(bool IsEnvelopeRoot, bool IsContentBlock, bool IsResultObject);

private void AnonymizeObjectProperties(JsonObject obj, RedactionContext context)
{
    RedactionContext childContext = default;

    foreach (string key in obj.Select(static kvp => kvp.Key).ToList())
    {
        if (IsExemptStructuralKey(key, context)) continue;

        if (obj[key] is JsonValue value && value.TryGetValue(out string? stringValue))
        {
            obj[key] = _anonymizer.Anonymize(key, stringValue);
        }
        else if (context.IsResultObject && key == "content" && obj[key] is JsonArray contentArray)
        {
            AnonymizeArrayElements(contentArray, childContext with { IsContentBlock = true });
        }
        else if (context.IsEnvelopeRoot && key == "result")
        {
            AnonymizeContainer(obj[key], childContext with { IsResultObject = true });
        }
        else
        {
            AnonymizeContainer(obj[key], childContext);
        }
    }
}
```

Illustrativ — der Coder entscheidet die konkrete Struktur (weitere
Extraktion in Hilfsmethoden, falls Complexity-Limits das verlangen),
solange das Verhalten (Content-Block-Ausnahme nur für `result.content[]`
direkt an der Envelope-Wurzel) erhalten bleibt.

## Notes

- Der Fix betrifft **ausschließlich** die Aktivierungsbedingung von
  `IsContentBlock`. Keine Änderung an: `IsExemptStructuralKey` selbst, der
  grundsätzlichen Redaction-Pipeline (`_anonymizer.Anonymize`), der
  Ein-Ebene-Tiefe-Garantie für `IsContentBlock` (bleibt wie in
  `step-003/fix-01` etabliert), oder dem `EnvelopeKeys`-Mechanismus.
- `TD-002` ist bereits durch `step-005` abgeschlossen (siehe
  `roadmap.md` EPIC-05) — nicht erneut anfassen.
- Nach diesem Step sind alle drei Tech-Debt-Epics (EPIC-04/05/06) sowie
  EPIC-01/02/03 abgedeckt — der nächste Planer-Aufruf im Step-Modus
  sollte (vorbehaltlich Review-Ergebnis dieses Steps) „keine offenen
  Epics mehr" melden.
- `ExtractMarkdownText` (Zeile 218-241) verwendet bereits denselben
  `root.result.content`-Pfad rein lesend (ohne Rekursions-Kontext, da es
  über `JsonDocument`/`JsonElement` statt `JsonNode` arbeitet) — das
  bestätigt, dass `result.content[]` tatsächlich der einzige im Code
  bereits als strukturell erkannte Content-Block-Pfad ist, keine
  Neuerfindung eines Konzepts.
