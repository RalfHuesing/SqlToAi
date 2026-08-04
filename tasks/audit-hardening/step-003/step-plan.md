---
status: done (pending audit)
type: step-plan
task: audit-hardening
step: "003"
title: "MCP-Trail-Redaction via IAnonymizer-Reuse"
epic: EPIC-03
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-04T11:00:00+02:00
related_to: []
---

# Step 003: MCP-Trail-Redaction via IAnonymizer-Reuse

## Bezug

- **Task:** `audit-hardening`
- **Epic:** `EPIC-03` aus `roadmap.md` — letztes noch offenes Epic:
  `McpTrailWriter` schreibt Request-Argumente und Response-Inhalte
  aktuell unredigiert auf die Festplatte.
- **Konzept-Referenz:** `konzept.md` Muss-Haben Punkt 3 ("MCP Trail
  Redaction (Anonymizer-Reuse)"), „Wie" Schritt 3, sowie der
  Kontext-Absatz in „Warum / Kontext" (Risikofall: lokaler Agent liest
  Trail-Dateien direkt vom Dateisystem und umgeht damit die
  MCP-eigene, `AccessLevel`-basierte Zugriffssteuerung).

## Aktueller Projektzustand (JIT-Kontext)

- **`McpTrailWriter.cs`** (vollständig gelesen): `Record(McpCallRecord)`
  schreibt pro Aufruf bis zu vier Dateien — `*-call.jsonl` (kompaktes
  `McpCallRecordShape`, enthält `record.ArgumentsJson`/`record.ResponseJson`
  1:1 als Strings), `*-request.json` (Pretty-Print von
  `record.RawRequestJson` — das ist die **volle** JSON-RPC-Anfrage
  inkl. verschachteltem `params.arguments`, nicht nur die separat
  vorliegende `ArgumentsJson`), `*-response.json` (Pretty-Print von
  `record.ResponseJson`) und optional `*-response.md` (Markdown-Text
  aus `result.content[0].text`, sofern erkennbar Markdown). Keine
  Stelle redigiert aktuell irgendetwas — alle vier Dateien enthalten
  exakt das, was über den MCP-Kanal floss, unverändert.
- **`IAnonymizer`** (`src/SqlToAi/Anonymization/IAnonymizer.cs` +
  `Anonymizer.cs`, vollständig gelesen): bietet zwei Overload-Paare —
  `Anonymize(columnName, value)` / `Anonymize(value, AnonymizationColumnContext)`
  und die tokenisierenden Pendants `Tokenize(...)`. Die
  Context-Variante braucht aufgelöste `TableName`/`OriginColumnName`/
  `SchemaName` (nur sinnvoll ableitbar aus einem echten Query-Result
  mit Spalten, siehe `QueryExecutionService.ResolveAnonymizationContextAsync`).
  Für `McpTrailWriter` gibt es **keinen** solchen Tabellen-/Spalten-Kontext
  — die geschriebenen Argumente/Response-Inhalte sind beliebige JSON-Werte
  aus bis zu ~15 verschiedenen MCP-Tools (Schema-Infos, Routine-Parameter,
  Query-Text, Ergebniszeilen, Fehlermeldungen, ...). Die einzig
  anwendbare Overload ist daher die **alias-only**-Variante
  `Anonymize(columnName, value)` (bzw. `Tokenize`) — der jeweilige
  JSON-Property-Name dient als bestmögliches `columnName`-Substitut,
  exakt wie es die XML-Doku dieser Overloads selbst als Rückwärts-
  kompatibilitätsfall vorsieht ("no schema context is available").
  `Anonymizer.Anonymize` maskiert nur Strings (scrambelt/hasht
  Zeichen für Zeichen, nicht-alphanumerische Zeichen bleiben erhalten)
  — Zahlen/Bools bleiben unverändert, das ist bereits das etablierte
  Verhalten für Query-Ergebnisse und wird 1:1 übernommen.
  `IsColumnExcluded` prüft aktuell ausschließlich den globalen Schalter
  `_options.Anonymizer.Enabled` (Kontext-Parameter wird nicht
  ausgewertet) — d. h. wenn der Nutzer Anonymisierung global
  deaktiviert (`Anonymizer.Enabled: false` in `appsettings.json`),
  bleibt auch die Trail-Redaction inaktiv. Das ist eine bewusste,
  vom Nutzer gesetzte globale Einstellung, keine `AccessLevel`-Lücke —
  entspricht der Anforderung "unabhängig vom `AccessLevel`" (die
  Trail-Redaction hat ohnehin nie Zugriff auf `AccessLevel`, sie läuft
  unbedingt für jeden Call, unabhängig davon, ob die jeweilige
  Datenbank `ReadWrite`/`ReadOnly`/`ReadOnlyAnonymized` ist).
- **DI/Registrierung** (`Program.cs:192-206`): `IAnonymizer` ist
  `AddSingleton<IAnonymizer, Anonymizer>()` registriert (Zeile 194,
  vor der `IMcpTrailWriter`-Registrierung in Zeile 206) — `McpTrailWriter`
  kann `IAnonymizer` einfach zusätzlich per Konstruktor-Injection
  bekommen, keine Registrierungs-Änderung nötig, keine Zyklen (Anonymizer
  hängt nur von `IOptions<SqlToAiOptions>` und `ITokenVault` ab).
- **Tests** (`tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs`,
  gelesen): `CreateWriter(enabled: bool)`-Helper konstruiert
  `McpTrailWriter` aktuell nur mit `IOptions<SqlToAiOptions>` +
  `NullLogger`; muss um einen `IAnonymizer`-Parameter erweitert werden
  (echte `Anonymizer`-Instanz oder Test-Double, je nachdem was die
  bestehenden Tests am wenigsten stört — bestehende Tests erwarten
  unveränderten Text in Kontrollfällen, daher am einfachsten ein
  Test-`IAnonymizer`, der Werte erkennbar transformiert, plus optional
  die echte `Anonymizer`-Klasse für einen End-to-End-Redaction-Test).

## Intention

`McpTrailWriter.Record` wendet vor jedem Schreibvorgang dieselbe
`IAnonymizer`-Maskierung an, die auch für `ReadOnlyAnonymized`-Datenbanken
in `QueryExecutionService` genutzt wird — unbedingt, für jeden Call,
unabhängig vom `AccessLevel` der jeweiligen Datenbank (die Trail-Redaction
kennt `AccessLevel` gar nicht, das ist der Punkt). Damit sieht ein lokaler
Agent, der die Trail-Dateien direkt vom Dateisystem liest, nie mehr
Rohdaten als das, was ohnehin über eine `ReadOnlyAnonymized`-Datenbank
via MCP-Kanal sichtbar wäre. Da es für beliebige Tool-Argumente/Responses
keinen auflösbaren Tabellen-/Spaltenkontext gibt, wird die vorhandene
alias-only-Overload (`Anonymize(columnName, value)`) rekursiv auf jeden
String-Wert im geparsten JSON-Baum angewendet — mit dem jeweiligen
JSON-Property-Namen als `columnName`. Kein neuer Krypto-Mechanismus, keine
neue Redaction-Engine — reine Wiederverwendung des bestehenden
`IAnonymizer`.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Mcp/McpTrailWriter.cs`

- **Was:**
  - Konstruktor um `IAnonymizer anonymizer`-Parameter erweitern (4.
    Parameter — `AiNetLinter.mdc`-Grenze `MaxMethodParameterCount`
    beachten, aktuell 2 Parameter, nach Erweiterung 3: `IOptions<SqlToAiOptions>`,
    `ILogger<McpTrailWriter>`, `IAnonymizer` — unter dem Limit).
  - Neue private, rekursive Hilfsmethode (Vorschlag:
    `AnonymizeJsonStrings(string? json)`), die einen JSON-String via
    `System.Text.Json.Nodes.JsonNode.Parse` parst, den Baum rekursiv
    durchläuft (`JsonObject`/`JsonArray`/`JsonValue`) und jeden
    String-Leaf-Wert durch `_anonymizer.Anonymize(propertyName, value)`
    ersetzt (Array-Elemente ohne eigenen Property-Namen: fester
    Platzhalter, z. B. `"value"`, als `columnName`). Bekannte
    JSON-RPC-Strukturschlüssel auf jeder Ebene — `jsonrpc`, `id`,
    `method`, `type` (Content-Block-Discriminator wie `"text"`) —
    von der Anonymisierung ausnehmen, damit Korrelation/Lesbarkeit der
    Trail-Metadaten erhalten bleibt (nur deren *Werte* ausschließen,
    nicht andere gleichnamige Properties tiefer im Baum versehentlich
    mit-ausschließen — pro Ebene prüfen, nicht global per String-Suche).
    Fällt `JsonNode.Parse` auf ungültiges JSON (kann bei den
    Companion-Dateien vorkommen, siehe bestehendes Fallback-Verhalten
    in `WritePrettyJson`), Original-String unverändert zurückgeben
    (fail-safe wie das bestehende Muster, kein Absturz der Trail-Aufzeichnung).
  - `Record(...)`: `record.ArgumentsJson`, `record.RawRequestJson` und
    `record.ResponseJson` **vor** allen weiteren Verwendungen (jsonl-Zeile,
    `WritePrettyJson`-Aufrufe, `WriteMarkdownCompanion`) durch die
    anonymisierten Varianten ersetzen (z. B. lokale Variablen
    `anonymizedArgs`/`anonymizedRequest`/`anonymizedResponse`), damit
    alle vier Ausgabedateien konsistent aus derselben anonymisierten
    Quelle gespeist werden — keine doppelte Parsing-Logik an
    verschiedenen Stellen.
  - `ToJsonShape` verwendet dann `anonymizedArgs`/`anonymizedResponse`
    statt der Rohwerte aus `record`.
- **Warum:** einzige Stelle, an der Trail-Inhalte vor
  `File.AppendAllText`/`File.WriteAllText` landen (konzept.md
  Zeilen 112-129) — hier greift die Redaction für alle vier Dateitypen
  gleichzeitig.

### Datei 2: `src/SqlToAi/Program.cs`

- **Was:** Prüfen, ob `IMcpTrailWriter`-Registrierung (Zeile 206) durch
  die geänderte Konstruktor-Signatur weiterhin ohne Anpassung auflöst
  (reines `AddSingleton<IMcpTrailWriter, McpTrailWriter>()`, DI löst
  den zusätzlichen `IAnonymizer`-Parameter automatisch auf, da
  bereits als Singleton registriert) — falls ja, keine Code-Änderung
  nötig, nur zur Kenntnis nehmen; falls die Reihenfolge der
  Registrierungen doch relevant wäre (ist sie bei `AddSingleton` mit
  Konstruktor-Injection nicht), entsprechend anpassen.
- **Warum:** sicherstellen, dass die neue Abhängigkeit zur Laufzeit
  auflösbar ist, ohne den DI-Container-Aufbau zu brechen.

### Datei 3: `tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs`

- **Was:**
  - `CreateWriter`-Helper um `IAnonymizer`-Parameter erweitern (Default:
    echte `Anonymizer`-Instanz mit `Anonymizer.Enabled: true`,
    `DefaultMode: ScramblePattern`, analog zu `appsettings.json`-Default,
    plus Test-Overload/Parameter, um Anonymisierung für die bestehenden,
    auf unveränderten Text prüfenden Assertions gezielt abzuschalten,
    falls nötig — bestehende Tests zuerst laufen lassen und prüfen, ob
    sie mit aktivierter Anonymisierung noch sinnvoll sind oder angepasst
    werden müssen).
  - Neue Tests:
    - Ein Response mit erkennbarem PII-artigem String im
      `content[0].text` (z. B. ein Name) wird in `*-response.json`,
      `*-call.jsonl` und `*-response.md` **nicht** im Klartext
      geschrieben (scrambled/gehasht je nach `DefaultMode`).
    - Ein `ArgumentsJson`/`RawRequestJson` mit einem sensiblen
      String-Argument (z. B. ein Suchbegriff) wird in `*-request.json`
      und `*-call.jsonl` ebenfalls redigiert.
    - Strukturschlüssel (`jsonrpc`, `id`, `method`) bleiben in allen
      Companion-Dateien unverändert lesbar (Korrelation nicht
      gebrochen).
    - Zahlen-/Bool-Werte in Argumenten/Response bleiben unverändert
      (kein versehentliches String-Casting).
    - Deaktivierte Anonymisierung (`Anonymizer.Enabled: false`) lässt
      den Trail unverändert (Rückwärtskompatibilität/bestehendes
      Verhalten, entspricht der globalen Nutzer-Einstellung).
    - Ungültiges JSON in einem der Felder führt nicht zum Absturz der
      Aufzeichnung (fail-safe, Original wird schreiben).
- **Warum:** Definition-of-Done-Punkt aus `konzept.md` ("MCP-Trail-Dateien
  enthalten dieselbe Anonymisierung wie die an das LLM gesendeten
  Query-Ergebnisse — unabhängig vom `AccessLevel`") ist ohne Test auf
  Dateiebene nicht verifizierbar.

## Tests

- [ ] Response-Text mit PII-artigem String wird in allen vier
      Ausgabedateien redigiert geschrieben, nicht im Klartext.
- [ ] Request-Argumente mit sensiblem String werden redigiert
      geschrieben (sowohl `*-request.json` als auch `*-call.jsonl`).
- [ ] `jsonrpc`/`id`/`method` bleiben in allen Companion-Dateien
      unverändert lesbar.
- [ ] Numerische/Bool-Werte bleiben unverändert (kein Anonymisieren
      von Nicht-Strings).
- [ ] `Anonymizer.Enabled: false` lässt den Trail unverändert.
- [ ] Ungültiges JSON in `ArgumentsJson`/`ResponseJson` führt nicht zum
      Absturz von `Record(...)` (bestehendes Fail-Safe-Verhalten bleibt
      erhalten).
- [ ] Bestehende `McpTrailWriterTests`-Fälle weiterhin grün (ggf.
      angepasst an aktivierte Default-Anonymisierung).

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`roadmap.md`) grün
- [ ] Test-Command aus Tech-Stack-Notiz grün
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-003/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/AiNetLinter.mdc` — `MaxMethodParameterCount`
  (Konstruktor-Erweiterung bleibt unter dem Limit),
  `MaxCyclomaticComplexity`/`MaxCognitiveComplexity` (die neue
  rekursive JSON-Walk-Methode sollte als kleine, fokussierte Methode
  mit `switch` über `JsonNode`-Typen bleiben, keine tief verschachtelten
  `if`-Ketten), `sealed`-Pflicht für neue Klassen (falls ein separater
  Helper-Typ statt einer privaten Methode gewählt wird).
- `.agents/rules/SqlToAiRichtlinien.mdc` — keine hartkodierten
  Werte/Strings (Platzhalter-`columnName` für Array-Elemente und die
  Liste der ausgenommenen Strukturschlüssel sind kurze, feste
  Literale ohne Konfigurationsbezug, kein Options-Zwang zu erwarten,
  aber im Zweifel beim Coden gegen die Datei prüfen); DI-Registrierung
  über `IOptions<T>`; Commit-/Sprachregeln.

## Bekannte Ausnahmen

- Keine.

## Code-Skizze (optional)

```csharp
private string? AnonymizeJsonStrings(string? json)
{
    if (string.IsNullOrEmpty(json)) return json;

    try
    {
        var node = JsonNode.Parse(json);
        AnonymizeNode(node, propertyName: null);
        return node?.ToJsonString(CompactJsonOptions) ?? json;
    }
    catch (JsonException)
    {
        return json; // fail-safe: trail must never break on malformed JSON
    }
}

private void AnonymizeNode(JsonNode? node, string? propertyName)
{
    switch (node)
    {
        case JsonObject obj:
            foreach (var (key, child) in obj.ToList())
            {
                if (IsStructuralKey(key)) continue;
                if (child is JsonValue val && val.TryGetValue<string>(out var s))
                {
                    obj[key] = _anonymizer.Anonymize(key, s);
                }
                else
                {
                    AnonymizeNode(child, key);
                }
            }
            break;
        case JsonArray arr:
            foreach (var item in arr)
            {
                AnonymizeNode(item, propertyName);
            }
            break;
    }
}
```

## Notes

- Bewusst **keine** Verwendung der Context-Overload
  (`Anonymize(value, AnonymizationColumnContext)`) — die dafür nötige
  Tabellen-/Spalten-Auflösung existiert für generische MCP-Tool-JSON
  nicht und wäre eine Scope-Erweiterung weit über `konzept.md` hinaus.
  Die alias-only-Overload ist explizit für genau diesen Fall gedacht
  (siehe deren XML-Doku: "No schema context is available here").
- `System.Text.Json.Nodes.JsonNode` (mutable) statt `JsonDocument`
  (immutable, bereits für `WritePrettyJson`/`ExtractMarkdownText`
  genutzt) verwenden, da hier Werte in-place ersetzt werden müssen,
  nicht nur gelesen. `JsonDocument` bleibt an seinen bestehenden
  Stellen unverändert.
- Der Konstruktor-Parameter `IAnonymizer` sollte **vor** den
  bestehenden Feldzuweisungen dieselbe Reihenfolge wie in
  `Program.cs` (`IOptions<SqlToAiOptions>`, `ILogger<...>`,
  `IAnonymizer`) übernehmen oder — falls das Team-Konvention ist —
  alphabetisch/nach Wichtigkeit; keine harte Vorgabe, nur Konsistenz
  mit dem restlichen Konstruktor-Stil in der Datei wahren.
- `Enabled`-Check: `McpTrailWriter.Record` prüft bereits ganz am
  Anfang `if (!_options.McpTrail.Enabled) return;` — die neue
  Anonymisierung greift nur, wenn der Trail selbst aktiv ist; ob
  Anonymisierung selbst aktiv ist, entscheidet weiterhin
  `SqlToAiOptions.Anonymizer.Enabled` (globaler Schalter, siehe
  `IsColumnExcluded` in `Anonymizer.cs`) — beide Schalter sind
  unabhängig voneinander und beide bereits vorhanden, keine neue
  Options-Klasse nötig.
- Auf Konsistenz zwischen der jsonl-Zeile (`ToJsonShape`) und den
  Companion-Dateien achten: beide müssen aus derselben anonymisierten
  Quelle stammen, sonst könnte die JSONL-Metadatenzeile redigiert sein,
  während `*-request.json` noch die Rohdaten von `RawRequestJson`
  zeigt (oder umgekehrt) — das wäre eine Lücke, die das eigentliche
  Ziel (Dateisystem-Zugriffsweg absichern) nur teilweise erfüllt.
