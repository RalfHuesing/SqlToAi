---
status: done (approved)
type: step-plan
task: sql-index-suggestions
step: 005
title: "TD-002 — DESC-Sortierung in BuildCreateIndexStatement korrekt rendern"
epic: EPIC-04
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-05T15:00:00+02:00
related_to:
  - tasks/sql-index-suggestions/tech-debt.md#TD-002
  - tasks/sql-index-suggestions/step-001/step-result.md
  - tasks/sql-index-suggestions/step-001/step-review.md
  - tasks/sql-index-suggestions/step-001/step-plan.md
  - tasks/sql-index-suggestions/roadmap.md#EPIC-04
---

# Step 005: TD-002 — `DESC`-Sortierung in `BuildCreateIndexStatement` korrekt rendern

## Bezug

- **Task:** `sql-index-suggestions`
- **Epic:** `EPIC-04` aus `roadmap.md` — Post-Completion Tech-Debt Cleanup
  Round 2 (Nutzer-Anordnung 2026-08-05). Konkret: TD-002 in
  `tech-debt.md` (Status: „in Bearbeitung (step-005)").
- **Tech-Debt-Referenz:** `tech-debt.md` Eintrag TD-002 — SQL-Server kann
  in `<MissingIndex>`-XML-Plans Spalten mit
  `<Column Name="X" Descending="True" />` markieren. Aktueller Code
  übernimmt nur `Name` 1:1, ignoriert `Descending`. Das gebaute DDL ist
  für absteigend indizierte Spalten semantisch unvollständig (es fehlt
  die `DESC`-Direktive in der Schlüsselspaltenliste). Funktional
  funktioniert der Index weiterhin aufsteigend, also nicht falsch,
  nur nicht deckungsgleich mit der SQL-Server-Empfehlung.
- **Vorgänger-Kontext:**
  - `step-001/step-plan.md` (Datei 2 Zeile 121–148) hat den Helper
    `BuildCreateIndexStatement` eingeführt, aber bewusst ohne
    `Descending`-Behandlung geplant (Scope-Entscheidung des Planers).
  - `step-001/step-result.md` Beobachtungen (Zeile 131–140) hat den
    Coder auf das `Descending="True"`-Defizit hingewiesen — explizit
    als „kein Scope-Item, keine Aktion" vermerkt.
  - `step-001/step-review.md` hat den Punkt als „Sonstige Beobachtungen"
    aufgenommen, nicht als Finding.
  - Nutzer hat 2026-08-05 angeordnet, TD-002 in EPIC-04 umzusetzen.
- **Konzept-Referenz:** `konzept.md` §Muss-Haven Idee 1, §Wie Idee 1
  (Zeilen 33–41, 157–174) — schweigt über `DESC`. Der Fix ist eine
  Konzept-Erweiterung, keine Konzept-Ableitung; daher keine
  Konzept-Änderung nötig.

## Aktueller Projektzustand (JIT-Kontext)

Beim Lesen des relevanten Codes vorgefunden:

- **`PerformanceMeasurementService.ExtractMissingIndexWarnings`
  (Zeile 336–378):** Iteriert pro `<MissingIndex>`-Element über die
  direkten `<ColumnGroup>`-Kinder (`mi.Elements(ns + "ColumnGroup")`,
  Z. 347) und ordnet sie anhand des `Usage`-Attributs drei
  `List<string>` zu (`equality`, `inequality`, `include`, Z. 344–346).
  Pro `ColumnGroup` werden die `Column`-Kinder in einer LINQ-Kette
  verarbeitet (Z. 350–354):

  ```csharp
  var cols = cg.Elements(ns + "Column")
               .Select(c => c.Attribute("Name")?.Value)
               .Where(n => !string.IsNullOrEmpty(n))
               .Select(n => n!)
               .ToList();
  ```

  Dabei wird **nur** das `Name`-Attribut gelesen. Das
  `Descending`-Attribut wird weder gelesen noch ausgewertet — kein
  XPath-`Attribute("Descending")`, keine Fallunterscheidung. Der
  Datenverlust passiert hier, noch bevor `BuildCreateIndexStatement`
  aufgerufen wird.

- **`PerformanceMeasurementService.BuildCreateIndexStatement`
  (Zeile 380–425):** Signatur
  `private static string? BuildCreateIndexStatement(string table,
  List<string> equality, List<string> inequality, List<string> include)`.
  Schlüsselspalten werden mit `equality.Concat(inequality).ToList()`
  (Z. 402) zu `keyCols` zusammengeführt und über
  `string.Join(", ", keyCols)` (Z. 414) in der `ON`-Klausel gerendert.
  Die Render-Logik propagiert den Spalten-String 1:1 — wenn die
  Spaltenliste bereits den `DESC`-Suffix trägt, kommt er automatisch
  im DDL an. **Keine Render-Änderung nötig**, wenn die
  Extraktion so umgestellt wird, dass jeder Listeneintrag die korrekte
  ASC/DESC-Notation bereits enthält.

- **INCLUDE-Spalten:** Eine `INCLUDE`-Klausel trägt keine
  Sortierrichtung (`INCLUDE (col)` — keine `ASC`/`DESC`-Modifier
  möglich). Das `Column`-Element in einer `<ColumnGroup Usage="INCLUDE">`
  kann laut SQL-Server-Schemata ebenfalls ein `Descending`-Attribut
  tragen (technisch möglich), aber SQL-Server setzt es dort nie auf
  `True`. Der Code soll `Descending` für `INCLUDE`-Spalten defensiv
  ignorieren (gleicher Spaltenname, kein `DESC`-Suffix, weil
  semantisch falsch) — siehe „Konkrete Änderungen" Datei 1 Punkt 2.

- **Bestehende Tests in
  `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs`:**
  - `ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly`
    (Z. 95–128) — `<MissingIndex Table="[dbo].[Orders]" />` **ohne**
    `ColumnGroup`-Kinder → `MissingIndexStatement == null`. Bleibt
    grün, weil die Änderung nur Pfade mit `ColumnGroup`-Kindern
    betrifft.
  - `…_MissingIndex_EqualityOnly_BuildsStatement` (Z. 130–166) — ein
    `<Column Name="CustomerId" />` **ohne** `Descending`-Attribut.
    Bleibt grün: kein `Descending` → kein `DESC`-Suffix → exakt das
    bisherige DDL.
  - `…_MissingIndex_EqualityPlusInequalityPlusInclude_BuildsFullStatement`
    (Z. 168–211) — alle Spalten ohne `Descending`. Bleibt grün aus
    demselben Grund.
  - `…_MissingIndex_EqualityOnlyWithInclude_BuildsStatementWithInclude`
    (Z. 213–251) — Spalten ohne `Descending`. Bleibt grün.
  - **Wichtig:** Keiner der bestehenden Tests hat eine Spalte mit
    `Descending="True"` — alle vier Tests sind `Descending`-frei und
    bleiben grün, **weil** die Render-Logik rückwärtskompatibel ist
    (fehlendes `Descending` → kein Suffix).

- **JSON-Output / API-Grenze:** Das `MissingIndexStatement`-Feld ist
  seit `step-001` ein `string?` (JSON-Property
  `missing_index_statement`, via `JsonIgnoreCondition.WhenWritingNull`
  unterdrückt wenn null). Der neue `DESC`-Suffix ändert den Feld-Typ
  nicht, das JSON-Schema nicht, die Tool-Description nicht. Kein
  API-Change.

- **Doku-Sync-Pflicht
  (`SqlToAiRichtlinien.mdc#4`):** Die Pflicht zielt auf
  Code-Änderungen, die API-, Tool- oder Markdown-Output betreffen
  (`docs/architecture-spec.md` §4 Nr. 14 + `README.md` Feature-Bullet).
  Diese Änderung betrifft **ausschließlich** den internen
  DDL-Render-Pfad — die öffentliche Aussage „liefert ein
  CREATE NONCLUSTERED INDEX-Statement, das die Spalten aus dem
  XML-Plan widerspiegelt" bleibt korrekt (und wird mit `DESC` sogar
  treuer). `architecture-spec.md` und `README.md` sind bereits
  seit `step-001` synchron zum implementierten Code und bleiben es.
  Doku-Sync-Pflicht entkräftet — Begründung im Plan dokumentiert
  (siehe „Rules-Refs").

- **`IsPermissionError`-Methode (Z. 272–273):** im Plan ignoriert
  (kein TD-003-Relevanz, schon in `step-002` generalisiert —
  siehe `tech-debt.md` TD-003 als erledigt markiert).

Der Plan erweitert damit eine bestehende private Methode um genau
eine `Attribute`-Auswertung pro `Column`-Kind und ein bis zwei
neue Tests — keine neue Klasse, keine neue Schnittstelle, keine
Architekturänderung, keine Render-Logik-Änderung in
`BuildCreateIndexStatement`, keine API-Änderung.

## Intention

Nach diesem Step liefert `BuildCreateIndexStatement` für jede
`<Column Name="X" Descending="True" />` aus einem
`<MissingIndex>`-XML-Plan das `DESC`-Suffix korrekt im
Schlüsselspalten-Teil des DDL mit aus — z. B.
`... (CustomerId ASC, OrderDate DESC)`. Aufsteigende Spalten
(Standard, `Descending` fehlt oder ist `False`) bleiben
unverändert rückwärtskompatibel. Das DDL ist damit semantisch
deckungsgleich mit der SQL-Server-Empfehlung. Bestehende Tests
bleiben grün, neue Tests verifizieren den `DESC`-Pfad in
mindestens einer sinnvollen Konfiguration.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/PerformanceMeasurementService.cs` (Zeile 336–378, `ExtractMissingIndexWarnings`)

- **Was:** Die LINQ-Kette in `ExtractMissingIndexWarnings` (Z. 350–354)
  so umstellen, dass pro `Column`-Kind zusätzlich zum `Name` das
  `Descending`-Attribut ausgewertet und in den Spalten-String
  eingebaut wird. Konkret: Statt
  `c.Attribute("Name")?.Value` einen formatierten String
  `name` oder `$"{name} DESC"` in die Liste schreiben, abhängig
  vom `Descending`-Attribut. Drei Variationen (alle äquivalent
  im Ergebnis, der Coder wählt eine):

  - **Variante A (kompakt, ein LINQ-Ausdruck):**
    ```csharp
    var cols = cg.Elements(ns + "Column")
        .Select(c =>
        {
            string? name = c.Attribute("Name")?.Value;
            bool desc = string.Equals(
                c.Attribute("Descending")?.Value, "True",
                StringComparison.OrdinalIgnoreCase);
            return (name, desc);
        })
        .Where(t => !string.IsNullOrEmpty(t.name))
        .Select(t => t.desc ? t.name + " DESC" : t.name!)
        .ToList();
    ```
  - **Variante B (readable, foreach):** `foreach` über
    `cg.Elements(ns + "Column")`, lokale Variablen, conditional
    `Add(name)` oder `Add(name + " DESC")`. Kompatibel zum
    bestehenden `switch (usage)`-Pattern.
  - **Variante C (Helper-Local-Function):** kleine
    `string FormatColumn(XElement c)`-Local-Function in
    `ExtractMissingIndexWarnings` definieren, im LINQ aufrufen.
    Etwas mehr Code, klarste Trennung.

  Der Coder wählt die Lesbarkeit-Variante; alle drei sind
  regelkonform.

- **`Descending`-Auswertung — `INCLUDE`-Spezialfall:** Für Spalten
  in einer `<ColumnGroup Usage="INCLUDE">` darf `DESC` **nicht**
  angehängt werden — die `INCLUDE`-Klausel kennt keine
  Sortierrichtung. Lösung: Die `Descending`-Auswertung
  **ausschließlich** für `Usage in { "EQUALITY", "INEQUALITY" }`
  anwenden; für `Usage == "INCLUDE"` Spaltenname 1:1 (wie bisher)
  übernehmen. Drei Optionen:

  - (i) pro `switch (usage)`-Zweig separat
    `ExtractKeyColumns(cg, withDescending: true)` bzw.
    `ExtractIncludeColumns(cg)` aufrufen,
  - (ii) `Descending` in der LINQ-Kette mitauswerten, aber im
    `switch (usage)`-Zweig für `"INCLUDE"` den Suffix wieder
    abschneiden (`cols = cols.Select(s => s.EndsWith(" DESC",
    StringComparison.Ordinal) ? s[..^5] : s).ToList();`),
  - (iii) `Descending` erst im `switch (usage)`-Zweig anhängen,
    nicht in der LINQ-Kette.

  **(iii) ist die sauberste Variante** (klare Trennung: LINQ
  sammelt `(name, isDescending)`-Paare, `switch` rendert je nach
  Usage). Wenn der Coder (i) oder (iii) wählt, ist die
  `Include`-Logik automatisch korrekt; bei (ii) muss der Cleanup
  explizit sein.

  **Empfehlung an den Coder: Variante A oder B kombiniert mit
  Include-Spezialfall nach Variante (iii) — die LINQ-Kette
  liefert `(name, isDescending)`-Tupel oder gleich den korrekt
  gerenderten String mit `DESC` nur für
  EQUALITY/INEQUALITY; für INCLUDE wird der Spaltenname
  unverändert übernommen.** Wenn der Coder unsicher ist: die
  einfachste robuste Lösung ist, die `Descending`-Auswertung
  *innerhalb* der `switch (usage)`-Zweige für EQUALITY/INEQUALITY
  zu machen (foreach über `cg.Elements(ns + "Column")` mit
  `if (string.Equals(c.Attribute("Descending")?.Value, "True",
  OrdinalIgnoreCase))`), und im INCLUDE-Zweig das Original-Pattern
  beizubehalten. Beides ist 1–3 zusätzliche Zeilen pro Zweig.

- **Linter-Disziplin:** Die Datei hat aktuell 450 LOC (≤ 500
  Limit) und die `BuildCreateIndexStatement`-Methode 46 LOC
  (≤ 60). Die Änderung in `ExtractMissingIndexWarnings` erhöht
  die Methode um ca. 5–10 LOC. Bleibt unter 60 LOC. Falls der
  Coder eine Variante wählt, die über 60 LOC stößt: in einen
  privaten Helper `FormatKeyColumn(XElement c)` extrahieren.
  **CC ≤ 12, Cognitive ≤ 15** ist mit jeder der drei
  Hauptvarianten sicher eingehalten.

- **Warum:** Reine Parser-Erweiterung. Keine neue externe
  Abhängigkeit. SQL-Server liefert das `Descending`-Attribut in
  genau dem Fall, in dem der Optimizer die Spalte absteigend
  indiziert sehen will (typisch: Bereichsfilter in
  `ORDER BY ... DESC`-Queries). Die XML-Schemata
  (https://schemas.microsoft.com/sqlserver/2004/07/showplan)
  definieren `Descending` als optionales Attribut vom Typ
  `xs:boolean` mit Default `False`.

### Datei 2: `tests/SqlToAi.Tests/Database/PerformanceMeasurementServiceTests.cs` (nach Zeile 251, im Anschluss an den letzten Missing-Index-Test)

- **Was:** Mindestens einen neuen Test hinzufügen, der eine
  `<Column>` mit `Descending="True"` enthält und prüft, dass
  der `DESC`-Suffix im DDL erscheint. Empfohlene zwei Tests
  (saubere Abdeckung des neuen Pfads + Edge-Case):

  1. **`ParseExecutionPlanXml_MissingIndex_DescendingColumn_RendersDescSuffix`**
     — XML mit:
     ```xml
     <MissingIndex Table="[dbo].[Orders]">
       <ColumnGroup Usage="EQUALITY">
         <Column Name="CustomerId" />
       </ColumnGroup>
       <ColumnGroup Usage="INEQUALITY">
         <Column Name="OrderDate" Descending="True" />
       </ColumnGroup>
       <ColumnGroup Usage="INCLUDE">
         <Column Name="Amount" />
       </ColumnGroup>
     </MissingIndex>
     ```
     Erwartet: `MissingIndexStatement` enthält
     `(CustomerId, OrderDate DESC)` und `INCLUDE (Amount)`,
     endet auf `;`. Außerdem Sanity-Check: `Assert.DoesNotContain("CustomerId DESC", ...)`
     (CustomerId hat kein `Descending`-Attribut → kein `DESC`).

  2. **`ParseExecutionPlanXml_MissingIndex_DescendingFalse_IsAscendingLikeBefore`**
     — XML mit `<Column Name="X" Descending="False" />` (explizit
     `False`, nicht fehlend). Erwartet: kein `DESC`-Suffix (d. h.
     exakt gleiches Verhalten wie ohne Attribut). Sanity-Check
     gegen Regressionsschutz: stellt sicher, dass die
     `Descending`-Auswertung case-insensitive „True"-exakt ist
     (nicht jedes nicht-leere Attribut).

  - **Optional** (wenn der Coder die Zeit hat): ein dritter Test
    **`…_MissingIndex_AllColumnsDescending_RendersAllDesc`** mit
    zwei EQUALITY-Spalten, beide `Descending="True"`, Erwartung
    `(ColA DESC, ColB DESC)`. Nicht zwingend — deckt aber einen
    Edge-Case ab, der bei nur einem Test nicht sichtbar wird.
  - **Optional** (wenn der Coder die Zeit hat): ein vierter
    Edge-Case-Test mit `Descending="True"` an einer INCLUDE-Spalte
    (`<ColumnGroup Usage="INCLUDE"><Column Name="Amount"
    Descending="True" /></ColumnGroup>`) — Erwartung: kein
    `DESC`-Suffix in `INCLUDE (Amount)`. Dieser Test
    verifiziert den INCLUDE-Spezialfall ausdrücklich.

- **Bestehende Tests bleiben unverändert grün:** alle vier
  bestehenden Missing-Index-Tests enthalten kein
  `Descending`-Attribut → kein `DESC`-Suffix im DDL → exakt das
  bisherige Verhalten. Auch der implizite Test ohne
  `ColumnGroup`-Kinder bleibt grün (kein Statement gebaut).

- **Warum:** DoD-Konformität: „neue Tests grün" + „bestehende
  Tests grün" + explizite Test-Abdeckung des neuen Pfads.

### Datei 3: `docs/architecture-spec.md`

- **Was:** **Keine Änderung.** Die Pflicht zur
  Doku-Synchronisation aus `SqlToAiRichtlinien.mdc#4` greift für
  API-, Tool- oder Markdown-Output-Änderungen. Diese Änderung
  betrifft nur den internen DDL-Render-Pfad: die öffentliche
  Aussage in §4 Nr. 14 (Stand `step-001`) lautet sinngemäß
  „liefert ein fertiges `CREATE NONCLUSTERED INDEX`-Statement mit
  den Spalten aus dem XML-Plan" — das bleibt korrekt (und mit
  `DESC` sogar treuer). Keine API-Änderung, keine
  Tool-Description-Änderung, kein neues Feld, keine
  JSON-Schema-Änderung.
- **Falls der Coder oder Kritiker eine Mini-Notiz für sinnvoll
  hält:** als optionale Ergänzung in §4 Nr. 14 könnte stehen
  „Spalten mit `Descending="True"` im XML-Plan werden mit
  nachgestelltem `DESC` gerendert". **Aber:** das ist
  Nice-to-have, nicht Pflicht, und nicht im Scope dieses
  Steps. Der Coder kann es weglassen.

### Datei 4: `README.md`

- **Was:** **Keine Änderung.** Selbe Begründung wie Datei 3.
  Das `sql_measure_performance`-Feature-Bullet sagt
  „with ready-to-execute CREATE NONCLUSTERED INDEX DDL
  statements per warning" (Stand `step-001`) — das ist
  weiterhin korrekt.

### Datei 5: `src/SqlToAi/Mcp/ToolRegistry.cs`

- **Was:** **Keine Änderung.** Die Tool-Description in
  `BuildMeasurePerformance()` (Z. 253–263, Stand `step-001`)
  beschreibt das `missing_index_statement`-Feld allgemein.
  `DESC` ist ein implementations-Detail der DDL-Generierung,
  kein eigenständiges API-Feature.

## Tests

- [ ] `ParseExecutionPlanXml_MissingIndex_DescendingColumn_RendersDescSuffix`
      — `Descending="True"` an einer INEQUALITY-Spalte → `DESC`-Suffix
      im DDL; `EQUALITY`-Spalte ohne `Descending` → kein Suffix.
- [ ] `ParseExecutionPlanXml_MissingIndex_DescendingFalse_IsAscendingLikeBefore`
      — `Descending="False"` (explizit, nicht fehlend) → kein Suffix
      (Regressionsschutz gegen zu-permissive Auswertung).
- [ ] (optional) `ParseExecutionPlanXml_MissingIndex_AllColumnsDescending_RendersAllDesc`
      — zwei EQUALITY-Spalten mit `Descending="True"` → beide mit
      `DESC`-Suffix.
- [ ] (optional) `ParseExecutionPlanXml_MissingIndex_DescendingInInclude_IsIgnored`
      — `Descending="True"` an einer INCLUDE-Spalte → kein
      `DESC`-Suffix in `INCLUDE (...)`.
- [ ] Bestehender Test
      `ParseExecutionPlanXml_MissingIndexAndImplicitConversion_ParsesWarningsCorrectly`
      bleibt grün (kein `ColumnGroup`-Kind → `MissingIndexStatement
      == null`).
- [ ] Bestehender Test
      `ParseExecutionPlanXml_MissingIndex_EqualityOnly_BuildsStatement`
      bleibt grün (kein `Descending` → kein `DESC`-Suffix →
      exakt bisheriges DDL).
- [ ] Bestehender Test
      `ParseExecutionPlanXml_MissingIndex_EqualityPlusInequalityPlusInclude_BuildsFullStatement`
      bleibt grün (selbe Begründung).
- [ ] Bestehender Test
      `ParseExecutionPlanXml_MissingIndex_EqualityOnlyWithInclude_BuildsStatementWithInclude`
      bleibt grün (selbe Begründung).
- [ ] Bestehende `MeasurePerformanceAsync_*`-Tests bleiben grün
      (keine Änderung am Validierungs-/Security-Pfad).
- [ ] `AiNetLinterTests.RecreateBaseline` läuft automatisch im
      `dotnet test`-Lauf und aktualisiert die Baseline —
      kein manueller Eingriff.

## Definition of Done

- [ ] `PerformanceMeasurementService.ExtractMissingIndexWarnings`
      wertet das `Descending`-Attribut der `Column`-Kinder aus und
      rendert die `DESC`-Direktive für `EQUALITY`/`INEQUALITY`-
      Spalten korrekt; `INCLUDE`-Spalten übernehmen den
      Spaltennamen unverändert (kein `DESC`).
- [ ] `BuildCreateIndexStatement` bleibt unverändert (oder nur
      minimal angepasst, falls der Coder eine Variante wählt, die
      das Rendering leicht verschiebt — semantisch identisch zum
      bisherigen Verhalten für `Descending`-freie XML-Pläne).
- [ ] Mindestens ein neuer Test verifiziert den `DESC`-Pfad mit
      `Descending="True"`; empfohlen: zwei Tests (einer mit `True`,
      einer mit explizit `False` als Regressionsschutz). Optionale
      Tests für `AllColumnsDescending` und `DescendingInInclude`
      sind Nice-to-have.
- [ ] Alle bestehenden Tests grün — insbesondere die vier
      bestehenden Missing-Index-Tests und der
      `MeasurePerformanceAsync_*`-Block.
- [ ] `dotnet build` grün, keine neuen Compiler-Warnungen
      (`TreatWarningsAsErrors=true`).
- [ ] `dotnet test` grün, inkl.
      `AiNetLinterTests.RecreateBaseline` (aktualisiert Baseline
      automatisch — kein manuelles Hash-Rechnen).
- [ ] `PerformanceMeasurementService.cs` bleibt unter
      AiNetLinter-Grenzwerte (`MaxLineCount 500`,
      `MaxMethodLineCount 60` für `ExtractMissingIndexWarnings` und
      `BuildCreateIndexStatement`; CC ≤ 12, Cognitive ≤ 15).
- [ ] **Keine** Änderung an `docs/architecture-spec.md`,
      `README.md` oder `ToolRegistry.cs` (interne
      DDL-Render-Logik, keine API-/Output-Änderung — siehe
      „Rules-Refs" Entkräftung).
- [ ] Commit auf Branch `main` (Conventional Commit, Deutsch,
      imperativ, Subject ≤ 72 Zeichen, Suffix
      `[sql-index-suggestions]`). Subject-Vorschlag:
      `feat(parser): berücksichtige Descending-Attribut in Missing-Index-DDL [sql-index-suggestions]`.
- [ ] `step-005/step-result.md` geschrieben.

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` —
  **Dokumentations-Synchronisation (Pflicht)**: „Bei jeder
  Entwicklung und Änderung an Features/Optionen müssen die
  Dokumentationen in `docs/architecture-spec.md` und `README.md`
  zwingend aktuell gehalten und synchronisiert werden (ohne
  Aufforderung)."
  - **Entkräftung:** Die Pflicht zielt auf Änderungen, die
    API-, Tool-Verhalten, Felder oder Markdown-Output betreffen.
    Diese Änderung erweitert eine private Methode um die
    Auswertung eines vorhandenen XML-Attributs; das öffentliche
    Verhalten (DDL-Statement wird generiert) bleibt unverändert,
    die Tool-Description ist weiterhin korrekt, das
    JSON-Schema (`missing_index_statement: string`) ist
    unverändert. Der Output ist mit `DESC` sogar treuer zur
    SQL-Server-Empfehlung, verstößt also gegen keine bestehende
    Doku-Aussage. **Konsequenz:** keine Änderung an
    `architecture-spec.md`, `README.md` oder
    `ToolRegistry.cs` erforderlich. (Falls der Coder oder
    Kritiker eine Mini-Notiz für sinnvoll hält, kann er eine
    Zeile in `architecture-spec.md` §4 Nr. 14 ergänzen — das
    ist Nice-to-have, nicht Pflicht-Punkt dieses Steps.)
- `.agents/rules/SqlToAiRichtlinien.mdc#4` —
  **Commits (Pflicht)**: Conventional Commit, Deutsch,
  imperativ, Suffix `[sql-index-suggestions]`, autonom in
  sinnvollen Abständen, Subject ≤ 72 Zeichen. Siehe DoD.
- `.agents/rules/SqlToAiRichtlinien.mdc#5` —
  **AiNetLinter-Hinweis**: `RecreateBaseline` läuft automatisch
  in jedem `dotnet test`, kein manuelles Hash-Rechnen.
  **Zero-Warning-Direktive:** `TreatWarningsAsErrors=true` — neue
  Compiler-Warnungen sind Build-Fehler. Der Coder schreibt
  neuen Code direkt konform (kein `dynamic`, keine leeren
  `catch`, `async` ordnungsgemäß, etc.).
- `.agents/rules/AiNetLinter.mdc` —
  **Grenzwerte Produktion**: `MaxLineCount 500` (Datei hat
  aktuell ~450 LOC, bleibt unter Limit auch nach Erweiterung),
  `MaxMethodLineCount 60` (gilt für `ExtractMissingIndexWarnings`
  und `BuildCreateIndexStatement`; Erweiterung von
  `ExtractMissingIndexWarnings` um ca. 5–10 LOC bleibt unter
  60), `MaxCyclomaticComplexity 12`,
  `MaxCognitiveComplexity 15`, `MaxMethodParameterCount 4`
  (unverändert — `ExtractMissingIndexWarnings` bekommt keine
  zusätzlichen Parameter, `BuildCreateIndexStatement` wird
  nicht angefasst), `sealed class` (Datei ist in `sealed
  class PerformanceMeasurementService` — bleibt), `#nullable
  enable` (Dateianfang, bleibt). Kein `dynamic`, keine leeren
  `catch`-Blöcke, kein `out` außerhalb `Try*`, alles
  eingehalten.

## Bekannte Ausnahmen

- **INCLUDE mit `Descending="True"`:** SQL-Server setzt das
  Attribut für `INCLUDE`-Spalten nie auf `True`, der Code
  ignoriert es aber defensiv (kein `DESC`-Suffix in der
  `INCLUDE`-Klausel). Das ist die richtige SQL-Semantik, kein
  Edge-Case-Fehler. Optionaler Test (siehe „Tests") deckt das
  explizit ab.

- **Index-Name-Format:** weiterhin `IX_<Table>_<FirstCol>[__<SecondCol>]`
  wie seit `step-001` (Konzept-Beispiel-Form bewusst NICHT
  übernommen, siehe TD-001 + `step-004` Resolution: Konzept
  wurde an Code angepasst). Nicht TD-002-relevant.

## Code-Skizze (optional)

Vorgeschlagene Umsetzung für Datei 1 (kompakte
Variante A + INCLUDE-Spezialfall nach Variante (iii),
robust und regelkonform):

```csharp
// In ExtractMissingIndexWarnings, Ersatz für Zeile 350-354 + switch (usage)
foreach (var cg in mi.Elements(ns + "ColumnGroup"))
{
    string usage = cg.Attribute("Usage")?.Value ?? string.Empty;
    var cols = cg.Elements(ns + "Column")
                 .Select(c => c.Attribute("Name")?.Value)
                 .Where(n => !string.IsNullOrEmpty(n))
                 .Select(n => n!)
                 .ToList();
    switch (usage)
    {
        case "EQUALITY":
            // Equality-Spalten mit Descending-Attribut korrekt rendern.
            equality.AddRange(FormatKeyColumns(cg, cols));
            break;
        case "INEQUALITY":
            inequality.AddRange(FormatKeyColumns(cg, cols));
            break;
        case "INCLUDE":
            // INCLUDE-Spalten tragen keine Sortierrichtung; Spaltenname
            // 1:1 übernehmen, Descending defensiv ignorieren.
            include.AddRange(cols);
            break;
    }
}

// Neue private static Helper-Methode in derselben Klasse:
private static List<string> FormatKeyColumns(XElement columnGroup, List<string> names)
{
    var result = new List<string>(names.Count);
    var columns = columnGroup.Elements(XNamespace.None + "Column").ToList();
    for (int i = 0; i < names.Count; i++)
    {
        string name = names[i];
        bool descending = i < columns.Count
            && string.Equals(
                columns[i].Attribute("Descending")?.Value, "True",
                StringComparison.OrdinalIgnoreCase);
        result.Add(descending ? name + " DESC" : name);
    }
    return result;
}
```

Alternativ (kompakter, ein LINQ-Ausdruck mit Tupel):

```csharp
// In ExtractMissingIndexWarnings, neuer Block innerhalb foreach (cg in ...)
foreach (var cg in mi.Elements(ns + "ColumnGroup"))
{
    string usage = cg.Attribute("Usage")?.Value ?? string.Empty;
    var keyCols = cg.Elements(ns + "Column")
        .Select(c => (
            Name: c.Attribute("Name")?.Value,
            Desc: string.Equals(
                c.Attribute("Descending")?.Value, "True",
                StringComparison.OrdinalIgnoreCase)))
        .Where(t => !string.IsNullOrEmpty(t.Name))
        .Select(t => t.Desc ? t.Name + " DESC" : t.Name!)
        .ToList();
    var includeCols = keyCols; // INCLUDE-Spalten werden unten gefiltert
    // ...
    switch (usage)
    {
        case "EQUALITY":
        case "INEQUALITY":
            (usage == "EQUALITY" ? equality : inequality).AddRange(keyCols);
            break;
        case "INCLUDE":
            // Descending-Suffix für INCLUDE entfernen (sicherer Cleanup).
            include.AddRange(includeCols.Select(s =>
                s.EndsWith(" DESC", StringComparison.Ordinal)
                    ? s[..^5] : s));
            break;
    }
}
```

Beide Varianten sind regelkonform (≤60 LOC, CC ≤ 12,
Cognitive ≤ 15). Der Coder wählt nach Lesbarkeits-Präferenz.
**Wichtig:** `BuildCreateIndexStatement` (Z. 380–425) bleibt
**unverändert** — `string.Join(", ", keyCols)` rendert
`"Name"` und `"Name DESC"` korrekt.

## Notes

- **Wiederverwendete Strukturen:** keine neue Klasse, keine
  neue Schnittstelle. Erweiterung einer bestehenden privaten
  Methode + ein neuer privater statischer Helper (oder
  Local-Function) in derselben Datei. Keine
  Architekturänderung, keine API-Änderung, keine
  DDL-Schema-Änderung.
- **Reihenfolge innerhalb einer `ColumnGroup`:** SQL-Server
  liefert die `Column`-Kinder in Schlüsselreihenfolge (wie
  die Optimizer-Empfehlung sie indiziert sehen will). Wenn
  eine Spalte `Descending="True"` hat, heißt das: genau diese
  Spalte in dieser Position soll absteigend indiziert werden.
  Die Reihenfolge `Equality → Inequality` (Z. 402) bleibt
  unverändert.
- **Mehrere `ColumnGroup`-Kinder mit `Usage="EQUALITY"`:**
  ungewöhnlich, aber laut XML-Schema zulässig. Der bestehende
  Code addiert alle in `equality` — die `Descending`-Auswertung
  pro Spalte funktioniert unabhängig von der Anzahl der
  ColumnGroups.
- **Performance:** LINQ-Allokation steigt minimal (Tupel oder
  zusätzlicher String-Konkat), bleibt im einstelligen
  Mikrosekundenbereich pro Warning — kein messbarer
  Performance-Impact.
- **Konzept vs. Code:** Konzept schweigt über `DESC`, dieser
  Fix ist eine explizit vom Nutzer angeordnete
  Konzept-Erweiterung. Keine Konzept-Änderung nötig (Konzept
  ist absichtlich Output-/Wirkungs-beschreibend, nicht
  implementations-detailliert).
- **Nach `approved`-Verdict:** TD-002-Eintrag aus
  `tech-debt.md` entfernen (per neuer Policy, siehe
  `tech-debt.md` Status-Policy). Kein neues Epic in
  `roadmap.md` anlegen — EPIC-04 wird mit `approved` von
  step-005 noch nicht abgeschlossen (step-006 und step-007
  sind noch offen).
- **Tool-Description in `ToolRegistry.BuildMeasurePerformance()`**
  bleibt unverändert; sie sagt „warnings[] (...
  missing_index_statement with a ready-to-execute CREATE
  NONCLUSTERED INDEX DDL string when key columns are present,
  null otherwise)" — das ist mit `DESC` korrekt und vollständig.
