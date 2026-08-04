---
status: done (pending audit)
type: step-plan
task: audit-hardening
step: 003/fix-01
title: "Strukturschlüssel-Ausnahme positionsabhängig statt namensabhängig machen"
epic: EPIC-03
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-04T16:00:00+02:00
related_to: [step-003/step-review.md]
---

# Step 003/fix-01: Strukturschlüssel-Ausnahme positionsabhängig statt namensabhängig machen

## Bezug

- **Task:** `audit-hardening`
- **Epic:** `EPIC-03` aus `roadmap.md` — Fix für das einzige CRITICAL-Finding
  aus `step-003/step-review.md` (Verdict: `issues`).
- **Konzept-Referenz:** siehe `step-003/step-plan.md` (Muss-Haben 3,
  „MCP Trail Redaction"). Dieser Fix ändert nichts an der Intention von
  Step 003, sondern korrigiert einen Umsetzungsfehler darin.

## Aktueller Projektzustand (JIT-Kontext)

- `src/SqlToAi/Mcp/McpTrailWriter.cs` (Commit `c3952e4`, vollständig
  gelesen): `AnonymizeContainer(JsonNode?)` →
  `AnonymizeObjectProperties(JsonObject)` /
  `AnonymizeArrayElements(JsonArray)` laufen komplett kontextlos — keine
  der drei Methoden weiß, auf welcher Verschachtelungsebene oder in
  welchem strukturellen Kontext (Envelope-Wurzel vs. beliebiges
  verschachteltes Objekt) sie gerade operiert. `AnonymizeObjectProperties`
  (Zeile 331-346) prüft `StructuralKeys.Contains(key)` (Zeile 335)
  ausschließlich per Name, auf jeder Ebene identisch.
- **Drei verschiedene Aufrufer, drei verschiedene Dokument-Formen** (aus
  `Record`, Zeile 126-128):
  - `AnonymizeJsonStrings(record.ArgumentsJson)` — Wurzel **ist bereits**
    das freie, vom LLM benannte `arguments`-Objekt (z. B.
    `{"CustomerId": 42}`), **kein** JSON-RPC-Envelope. Hier darf **keine**
    Strukturschlüssel-Ausnahme greifen — auch nicht auf der Wurzelebene.
  - `AnonymizeJsonStrings(record.RawRequestJson)` — Wurzel **ist** der
    JSON-RPC-Envelope: `{"jsonrpc","id","method","params":{"name",
    "arguments":{...}}}`. Nur `jsonrpc`/`id`/`method` auf **dieser**
    Wurzelebene sind strukturell; `params.arguments` (beliebig
    verschachtelt) ist wieder freier LLM-Content, exakt wie oben.
  - `AnonymizeJsonStrings(record.ResponseJson)` — Wurzel ist ebenfalls der
    JSON-RPC-Envelope (`jsonrpc`/`id` + `result`/`error`). Zusätzlich gibt
    es `result.content[]` — ein Array von Content-Blöcken
    (`{"type":"text","text":"..."}`); nur das `type`-Discriminator-Feld
    **direkt in einem Element dieses `content`-Arrays** ist strukturell,
    nicht `type` irgendwo sonst im Baum.
  - Konsequenz: `jsonrpc`/`id`/`method` sind nur auf der Envelope-Wurzel
    strukturell und auch nur für die beiden Envelope-Dokumente
    (`RawRequestJson`, `ResponseJson`) — bei `ArgumentsJson` gibt es gar
    keine Envelope-Wurzel, dort ist die Ausnahme grundsätzlich fehl am
    Platz. `type` ist nur innerhalb eines direkten `content[]`-Elements
    strukturell.
- Die 6 neuen Tests aus Step 003 (`tests/SqlToAi.Tests/Mcp/
  McpTrailWriterTests.cs`) decken diesen Fall nicht ab — kein Test mit
  einem `arguments`-Property gleichen Namens wie ein Strukturschlüssel.
  Die bestehende Test-Infrastruktur (`CreateWriter`-Helper mit
  `anonymizerEnabled`-Parameter, `CreateAnonymizer`-Helper) kann
  unverändert wiederverwendet werden — nur neue Testfälle nötig, keine
  neue Test-Infrastruktur.

## Intention

Die Strukturschlüssel-Ausnahme darf nur noch dort greifen, wo sie
tatsächlich einen JSON-RPC-Envelope-Schlüssel bzw. einen
Content-Block-Discriminator meint — nicht bei jedem gleichnamigen
Property irgendwo im Baum. Dazu wird der Rekursion ein Positions-/
Kontext-Signal mitgegeben (welche Ebene/welcher strukturelle Ort gerade
verarbeitet wird), statt wie bisher rein über den String-Namen des Keys
zu entscheiden. Nach dem Fix muss ein `arguments`-Property namens `id`,
`type` oder `method` mit sensiblem String-Inhalt redigiert werden, während
die echten Envelope-Felder und der `content[]`-Discriminator weiterhin
lesbar bleiben.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Mcp/McpTrailWriter.cs`

- **Was:**
  - Rekursions-Kontext einführen, der mitträgt, ob der aktuell verarbeitete
    Knoten (a) die Envelope-Wurzel eines Envelope-Dokuments ist, und
    (b) ein direktes Element eines `content`-Arrays ist (Content-Block).
    Konkret z. B. ein kleiner `private readonly record struct
    RedactionContext(bool IsEnvelopeRoot, bool IsContentBlock)` (oder
    zwei einfache `bool`-Parameter, falls das dem Cognitive-Complexity-
    Limit besser bekommt — siehe Rules-Refs) statt des bisherigen
    kontextlosen Aufrufs.
  - `AnonymizeJsonStrings(string? json, bool isEnvelope)`: neuer
    `isEnvelope`-Parameter. Startet die Rekursion mit
    `IsEnvelopeRoot = isEnvelope`, `IsContentBlock = false`.
  - `Record(...)`: die drei Aufrufe entsprechend parametrisieren —
    `AnonymizeJsonStrings(record.ArgumentsJson, isEnvelope: false)`,
    `AnonymizeJsonStrings(record.RawRequestJson, isEnvelope: true)`,
    `AnonymizeJsonStrings(record.ResponseJson, isEnvelope: true)`.
  - `AnonymizeContainer`/`AnonymizeObjectProperties`/
    `AnonymizeArrayElements`: Kontext als zusätzlichen Parameter
    durchreichen.
  - `AnonymizeObjectProperties`: Ausnahme nur noch, wenn
    `(context.IsEnvelopeRoot && key is "jsonrpc" or "id" or "method")`
    **oder** `(context.IsContentBlock && key == "type")` — nicht mehr
    das globale `StructuralKeys.Contains(key)`. `StructuralKeys` als
    Feld entweder auf die drei Envelope-Keys reduzieren und umbenennen
    (z. B. `EnvelopeKeys`) oder ganz entfernen zugunsten der beiden
    getrennten Konstanten/Checks — `type` bekommt eine eigene,
    Content-Block-spezifische Prüfung.
  - Bei der Rekursion in verschachtelte Kinder muss der Kontext für alle
    Kinder **zurückgesetzt** werden (`IsEnvelopeRoot = false`,
    `IsContentBlock = false`), **außer** für den einen Sonderfall: wird
    gerade das Property `content` verarbeitet und ist dessen Wert ein
    `JsonArray`, müssen dessen **direkte** Objekt-Elemente mit
    `IsContentBlock = true` verarbeitet werden (nicht tiefer
    verschachtelte Objekte darunter). Achtung: dieser Sonderfall darf
    nicht rein über den Property-Namen `content` an beliebiger Stelle im
    Baum greifen, sondern soll nur dort sinnvoll aktiviert werden, wo er
    tatsächlich ein Content-Block-Array adressiert (praktisch: irgendein
    `content`-Array direkt unterhalb `result` im Response-Envelope — im
    Zweifel darf der Check aber grobzügiger sein, solange er nicht dazu
    führt, dass beliebige verschachtelte `type`-Properties in
    Nicht-Content-Kontexten ausgenommen werden; sicherste Variante: die
    `IsContentBlock`-Markierung gilt nur für genau eine Rekursionsebene
    tief und wird danach garantiert wieder zurückgesetzt).
  - Cognitive-Complexity im Blick behalten (`AiNetLinter.mdc`,
    `MaxCognitiveComplexity`, Limit 15 — in Step 003 bereits einmal
    überschritten und per Extract-Method behoben). Falls die
    Kontext-Weitergabe eine Methode wieder über das Limit treibt: weiter
    in kleine, fokussierte Hilfsmethoden extrahieren (z. B. eine
    `IsExemptStructuralKey(string key, RedactionContext context)`-Methode
    für die Exemptions-Prüfung selbst), keine tief verschachtelten
    `if`-Ketten.
- **Warum:** behebt exakt das CRITICAL-Finding — verhindert, dass ein vom
  LLM frei gewähltes `arguments`-Property namens `id`/`type`/`method` mit
  sensiblem String-Inhalt versehentlich unredigiert im Trail landet, weil
  die bisherige Prüfung nur den Namen, nicht die Position/den Kontext des
  Keys ansieht.

### Datei 2: `tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs`

- **Was:** Neuen Test ergänzen (mindestens einen, gerne pro betroffenem
  Envelope-Feld):
  - `ArgumentsJson`/`RawRequestJson` enthält ein `arguments`-Property mit
    einem der Namen `id`, `type` oder `method` und einem sensiblen
    String-Wert (z. B. einer Sozialversicherungsnummer-artigen
    Zeichenkette) → muss in `*-request.json` und `*-call.jsonl`
    **redigiert** erscheinen (nicht im Klartext).
  - Regressions-Sicherung der bestehenden Strukturschlüssel-Tests aus
    Step 003: `jsonrpc`/`id`/`method` auf der tatsächlichen
    Envelope-Wurzel bleiben weiterhin unverändert lesbar; `type` als
    Content-Block-Discriminator in `result.content[0].type` bleibt
    weiterhin unverändert lesbar.
  - Optional (falls ohne großen Zusatzaufwand realisierbar): ein
    verschachtelter Response-Payload, dessen Datenstruktur ein
    Objekt-Property namens `type` enthält, das **kein**
    Content-Block-Discriminator ist (z. B. irgendwo unterhalb der
    eigentlichen Tool-Ergebnisdaten) → muss redigiert werden, falls es
    ein String-Wert ist. Nur ergänzen, wenn ohne Verrenkungen
    darstellbar — kein Muss, da bereits durch den `arguments`-Testfall
    das Kernrisiko abgedeckt ist.
- **Warum:** Ohne diesen Test bleibt exakt die Lücke aus dem Finding
  unentdeckt reproduzierbar — der Kritiker hatte explizit bemängelt,
  dass keiner der 6 bestehenden Tests ein Argument-Property gleichen
  Namens wie ein Strukturschlüssel verwendet.

## Tests

- [ ] Neuer Test: `arguments`-Property namens `id` (oder `type`/`method`)
      mit sensiblem String-Inhalt wird redigiert (nicht im Klartext) in
      `*-request.json` **und** `*-call.jsonl` geschrieben.
- [ ] Bestehende Strukturschlüssel-Tests aus Step 003 bleiben grün:
      `jsonrpc`/`id`/`method` auf der Envelope-Wurzel und `type` als
      Content-Block-Discriminator bleiben unverändert lesbar.
- [ ] Alle bestehenden `McpTrailWriterTests`-Fälle weiterhin grün.

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`roadmap.md`) grün
- [ ] Test-Command aus Tech-Stack-Notiz grün
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-003/fix-01/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/AiNetLinter.mdc` — `MaxCognitiveComplexity`/
  `MaxCyclomaticComplexity`: die zusätzliche Kontext-Weitergabe darf
  keine der drei Redaction-Methoden wieder über das Limit treiben (in
  Step 003 bereits einmal per Extract-Method behoben) — im Zweifel
  weiter in kleine Hilfsmethoden aufteilen.
- `.agents/rules/SqlToAiRichtlinien.mdc` — keine hartkodierten Werte:
  die (ggf. umbenannten/aufgeteilten) Envelope-Key- und
  Content-Discriminator-Konstanten bleiben kurze, feste Literale ohne
  Konfigurationsbezug, wie schon in Step 003 bewertet.

## Bekannte Ausnahmen

- Keine.

## Code-Skizze (optional)

```csharp
private readonly record struct RedactionContext(bool IsEnvelopeRoot, bool IsContentBlock);

private string? AnonymizeJsonStrings(string? json, bool isEnvelope)
{
    if (string.IsNullOrEmpty(json)) return json;
    try
    {
        JsonNode? node = JsonNode.Parse(json);
        AnonymizeContainer(node, new RedactionContext(isEnvelope, IsContentBlock: false));
        return node?.ToJsonString(CompactJsonOptions) ?? json;
    }
    catch (JsonException)
    {
        return json;
    }
}

private void AnonymizeObjectProperties(JsonObject obj, RedactionContext context)
{
    foreach (string key in obj.Select(static kvp => kvp.Key).ToList())
    {
        if (IsExemptStructuralKey(key, context)) continue;

        if (obj[key] is JsonValue value && value.TryGetValue(out string? s))
        {
            obj[key] = _anonymizer.Anonymize(key, s);
        }
        else if (key == "content" && obj[key] is JsonArray contentArray)
        {
            AnonymizeArrayElements(contentArray, context with { IsEnvelopeRoot = false, IsContentBlock = true });
        }
        else
        {
            AnonymizeContainer(obj[key], context with { IsEnvelopeRoot = false, IsContentBlock = false });
        }
    }
}

private static bool IsExemptStructuralKey(string key, RedactionContext context) =>
    (context.IsEnvelopeRoot && key is "jsonrpc" or "id" or "method")
    || (context.IsContentBlock && key == "type");
```

Illustrativ — der Coder entscheidet die konkrete Struktur (record struct
vs. zwei bool-Parameter, Aufteilung in Hilfsmethoden), solange das
Verhalten (Ausnahme nur auf Envelope-Wurzel bzw. direktem
Content-Block-Element) erhalten bleibt.

## Notes

- Der Fix betrifft **ausschließlich** die Positions-/Kontext-Logik der
  Strukturschlüssel-Ausnahme. Keine Änderung an: der grundsätzlichen
  Redaction-Pipeline (`_anonymizer.Anonymize`), dem
  `ArrayElementPlaceholderName`, der Fail-Safe-Behandlung ungültigen
  JSONs, oder den bestehenden, bereits `approved`-artigen Teilen von
  Step 003.
- `TD-002` (`Anonymizer.IsColumnExcluded` wertet `context` nie aus) ist
  laut Kritiker explizit **nicht** Scope dieses Fixes — siehe
  `tech-debt.md`, nicht anfassen.
- „Sonstige Beobachtungen" aus `step-review.md` (Über-Redaktion von
  bereits serialisierten `content[0].text`-Blobs) sind ebenfalls
  **nicht** Scope — der Kritiker hat das explizit als bewusste,
  dokumentierte Scope-Grenze eingestuft, kein Finding.
- Beim Testen darauf achten, dass `ArgumentsJson` in der Praxis **kein**
  Envelope ist (siehe „Aktueller Projektzustand") — ein Test, der ein
  `id`-Property direkt im `ArgumentsJson`-Root prüft, deckt daher den
  eigentlichen Kernfall des Findings ab (LLM wählt `id` als
  Bind-Parameter-Namen im `parameters`-Objekt).
