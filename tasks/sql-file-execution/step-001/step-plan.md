---
status: done (Korrektur ausstehend)
type: step-plan
task: sql-file-execution
step: 001
corrects: null
title: "GO-aware SQL script batch splitter foundation"
epic: EPIC-01
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: GPT-5
created_by_model_knowledge_cutoff: not provided by runtime
created_at: 2026-08-29T07:10:19+02:00
related_to: []
---

# Step 001: GO-aware SQL script batch splitter foundation

## Bezug

- **Task:** `sql-file-execution`
- **Epic:** `EPIC-01` aus `roadmap.md` — lokale SQL-Skript-Batches mit stabilen Quellzeilen und `GO`-Semantik vorbereiten.
- **Konzept-Referenz:** `konzept.md`, Scope „Multi-Batch & `GO`-Unterstützung“ sowie Schritt 2 unter „Wie“.

## Aktueller Projektzustand (JIT-Kontext)

Es gibt noch keinen `SqlBatch`-Typ, keinen `SqlScriptBatchSplitter` und keinen Datei-Ausführungsservice. Der vorhandene [`SqlScriptDomParser`](../../src/SqlToAi/Database/SqlScriptDomParser.cs#L12-L65) liefert ASTs für T-SQL, ist aber nicht die passende Abstraktion für das SQLCMD-Batch-Schlüsselwort `GO`. Der vorhandene [`SqlMultiStatementDetector`](../../src/SqlToAi/Database/SqlMultiStatementDetector.cs#L12-L60) wird von [`QuerySafetyValidator`](../../src/SqlToAi/Database/QuerySafetyValidator.cs#L65-L118) für den bestehenden Single-Query-Pfad verwendet; dieser Pfad darf durch den neuen Splitter nicht verändert werden.

Die Testassembly hat über `InternalsVisibleTo` in [`SqlToAi.csproj`](../../src/SqlToAi/SqlToAi.csproj#L31-L33) Zugriff auf interne Produktions-Typen. Die bestehenden Tests [`SqlScriptDomParserTests.cs`](../../tests/SqlToAi.Tests/Database/SqlScriptDomParserTests.cs) und [`SqlMultiStatementDetectorTests.cs`](../../tests/SqlToAi.Tests/Database/SqlMultiStatementDetectorTests.cs) verwenden die passende `#nullable enable`-Struktur und den `// @covers`-Sentinel. Es existiert kein vorheriger Step, kein `step-result.md`/`step-review.md` und kein `tech-debt.md`; daher gibt es keinen Roadmap-Abgleich und keine bekannte Vorbelastung für diesen Bereich.

## Intention

Nach diesem Step steht eine kleine, interne und deterministische Grundlage zur Verfügung, die lokale SQL-Skripttexte in ausführbare Batches zerlegt. Jeder Batch trägt den 1-basierten inklusiven Quellzeilenbereich und einen optionalen positiven `GO`-Wiederholungszähler; die bestehende Single-Query-Sicherheitsprüfung bleibt unverändert.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/SqlBatch.cs` (neue Datei, geplante Zeilen 1-18)

- **Was:** Einen internen, versiegelten Record `SqlBatch` mit den unveränderlichen Daten `Text`, `StartLine`, `EndLine` und `RepeatCount` anlegen. `RepeatCount` erhält für ein `GO` ohne Zähler den Wert `1`; der Splitter erzeugt ausschließlich positive Werte.
- **Warum:** Das Modell trennt die Batch-Metadaten vom späteren Ausführungsservice und bewahrt die für Fehlerberichte benötigte Quellposition, ohne einen öffentlichen API-Typ vorzeitig einzuführen.

### Datei 2: `src/SqlToAi/Database/SqlScriptBatchSplitter.cs` (neue Datei, geplante Zeilen 1-180)

- **Was:** Einen internen statischen `SqlScriptBatchSplitter` mit `Split(string? script)` implementieren. Die Methode verarbeitet `\n` und `\r\n`, liefert bei `null`/Whitespace keinen Batch, schließt leere Abschnitte aus und gibt die nicht expandierten Batches in Quellreihenfolge zurück.
- **Was:** Eine zeilenbasierte Separator-Erkennung kapseln, die `GO` case-insensitive und mit beliebigen führenden/nachfolgenden Leerzeichen akzeptiert, optional einen positiven Dezimalzähler liest und nachgestellte `--`- bzw. Block-Kommentare toleriert. `GO` innerhalb von String-Literalen, `--`-Kommentaren, mehrzeiligen `/* ... */`-Kommentaren oder längeren Bezeichnern darf keinen Split auslösen.
- **Was:** Separatorzeilen aus dem Batchtext ausschließen, den übrigen Quelltext einschließlich SQL-Kommentaren erhalten und `StartLine`/`EndLine` als 1-basierte inklusive Zeilen des jeweiligen Quellabschnitts setzen. `GO n` wird als `RepeatCount = n` gespeichert und nicht in mehrere Listeneinträge expandiert; ungültige oder nicht-positive Zähler werden nicht stillschweigend als gültige Separatoren interpretiert.
- **Warum:** Eine kontrollierte, line-aware Erkennung ist erforderlich, weil `GO` kein T-SQL-AST-Knoten ist und in Literalen/Kommentaren vorkommen kann. Die Trennung als eigene reine Komponente hält die bestehende `SqlScriptDomParser`-/`SqlMultiStatementDetector`-Logik für `sql_execute_query` unangetastet und gibt dem späteren Executor belastbare Zeilen- und Wiederholungsdaten.

### Datei 3: `tests/SqlToAi.Tests/Database/SqlScriptBatchSplitterTests.cs` (neue Datei, geplante Zeilen 1-170)

- **Was:** Eine versiegelte xUnit-v3-Testklasse mit `#nullable enable`, Namespace `SqlToAi.Tests.Database` und `// @covers SqlToAi.Database.SqlScriptBatchSplitter` ergänzen. Abdecken: `null`/Whitespace, ein Skript ohne Separator, case-insensitive `GO` mit Leerzeichen und korrekten 1-basierten Zeilenbereichen, `GO n` mit `RepeatCount`, nachgestellte Inline-Kommentare, `GO` in String-/Single-Line-Kommentaren, `GO` in einem mehrzeiligen Blockkommentar, sowie leere Abschnitte zwischen Separatoren.
- **Warum:** Die Tests sichern die syntaktisch leicht verwechselbaren Separatorgrenzen und die später für Diagnosen relevanten Metadaten ab, ohne eine SQL-Server-Integration für diese reine Textkomponente zu benötigen.

## Tests

- [ ] Die neue Testklasse prüft `Split(null)` und Whitespace auf eine leere Liste sowie ein einzelnes Batch ohne `GO` auf Text, Bereich und `RepeatCount = 1`.
- [ ] Tests prüfen `GO`, `gO`, führende/nachfolgende Leerzeichen, `GO 3`, `GO -- comment` und `GO /* comment */` einschließlich Batch-Reihenfolge, Quellzeilen und Wiederholungszähler.
- [ ] Tests prüfen, dass `GO` in Stringliteralen, `--`-Kommentaren und mehrzeiligen `/* ... */`-Kommentaren nicht trennt, und dass leere Batchabschnitte nicht ausgegeben werden.
- [ ] Der Coder führt nach Abschluss aller Code- und Teständerungen den vollständigen Test-Command aus `roadmap.md` **genau einmal** und grün vor dem Code-Commit aus: `dotnet test SqlToAi.slnx`. Dieser grüne Nachweis wird einzeilig in `step-result.md` dokumentiert.
- [ ] Der Coder führt zusätzlich den Build-Command aus `roadmap.md` aus und dokumentiert `dotnet build SqlToAi.slnx` grün.
- [ ] Der Kritiker führt den vollständigen Test-Command bei vorhandenem grünem Coder-Nachweis nicht erneut aus, sondern prüft Diff, Semantik, Regeln und Konzept-Treue unabhängig; nur bei einem konkreten Risiko darf er den gezielten Test `dotnet test tests/SqlToAi.Tests --filter FullyQualifiedName~SqlScriptBatchSplitterTests` nachholen.

## Definition of Done

- [ ] `SqlBatch` und `SqlScriptBatchSplitter` sind intern, versiegelt bzw. statisch passend zur bestehenden Database-Helferstruktur und erfüllen die beschriebenen `GO`-/Kommentar-/Zeilenbereichsregeln.
- [ ] Die neuen xUnit-v3-Tests decken alle Separator-, Wiederholungs-, Kommentar- und Randfallregeln ab und sind aussagekräftig für die Produktionslogik.
- [ ] `dotnet build SqlToAi.slnx` ist grün.
- [ ] `dotnet test SqlToAi.slnx` wurde vom Coder genau einmal nach Abschluss der Änderungen und vor dem Code-Commit grün ausgeführt; der Kritiker wiederholt diesen vollständigen Lauf bei grünem Nachweis nicht.
- [ ] AiNetLinter-MCP-Prüfungen für die geänderten C#-Symbole/Dateien zeigen keine relevanten Regelverstöße; der Kritiker prüft zusätzlich unabhängig gegen die Rules-Refs und den Konzeptumfang.
- [ ] Die bestehende `SqlScriptDomParser`-/`SqlMultiStatementDetector`-/`QuerySafetyValidator`-Implementierung sowie `README.md`, `docs/architecture-spec.md`, Konfiguration und Fehlerkatalog bleiben unverändert, weil dieser Step ausschließlich eine noch nicht öffentliche interne Textkomponente einführt.
- [ ] Der Coder aktualisiert `codemap.md` nur um einen Pointer auf die neuen Database-Dateien, schreibt `step-001/step-result.md`, setzt den Planstatus wie im Workflow vorgesehen auf `done (pending audit)` und erstellt auf dem aktuellen Branch einen deutschen imperativen Conventional-Commit mit dem Suffix `[sql-file-execution]`.

## Rules-Refs

- `.agents/rules/AiNetLinter-McpWorkflow.mdc#Verbindliche Priorität` — C#-Symbole, Abhängigkeiten, Tests und Regelverstöße werden vor bzw. während der Prüfung über die passenden AiNetLinter-MCP-Abfragen verifiziert.
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — `#nullable enable`, versiegelte konkrete Typen, kurze flache Methoden, keine blockierende oder stille Fehlerbehandlung.
- `.agents/rules/AiNetLinter.mdc#Grenzwerte (Produktion)` — Produktionsdatei unter 500 Zeilen, Methoden unter 60 Zeilen und keine unnötige Kopplung; die Testdatei folgt dem Test-Override.
- `.agents/rules/AiNetLinter.mdc#test-coverage` — Produktionslogik erhält eine zugeordnete Testklasse bzw. den `// @covers`-Sentinel.
- `.agents/rules/SqlToAiRichtlinien.mdc#4. Updates, Dokumentation & Sprachen (Updates, Documentation & Languages)` — xUnit-v3-Tests für funktionale Änderungen, englische Quell-/Artefaktsprache und nur entscheidungsrelevante Kommentare.
- `.agents/rules/SqlToAiRichtlinien.mdc#5. Qualitätsdrift-Prävention & Tech Debt (AiNetLinter)` — Null-Warnungen, AiNetLinter-Konformität und Prüfung auf unnötige Duplikation.

## Bekannte Ausnahmen

- Keine bekannten Flakiness- oder Infrastruktur-Ausnahmen; der Step ist eine reine In-Memory-Textverarbeitung und benötigt keinen SQL-Server.

## Code-Skizze (optional)

```csharp
internal sealed record SqlBatch(string Text, int StartLine, int EndLine, int RepeatCount = 1);

internal static class SqlScriptBatchSplitter
{
    public static IReadOnlyList<SqlBatch> Split(string? script);
}
```

## Notes

- `GO` ist ein zeilenweiser SQLCMD-Batchmarker und darf nicht über `SqlScriptDomParser` oder eine Änderung am bestehenden Single-Query-Guard interpretiert werden.
- Der Splitter soll den Batchtext nicht semantisch umschreiben; ausschließlich Separatorzeilen werden entfernt. Zeilenangaben bleiben 1-basiert und inklusiv und werden nicht aus einer nachträglich normalisierten Zeichenkette abgeleitet.
- `RepeatCount` beschreibt die SQLCMD-Wiederholung und wird für diesen Foundation-Step nicht ausgeführt oder expandiert. Der Step implementiert weder Datei-I/O, Encoding-Erkennung, Dateigrößenlimits, Fehlercodes, Transaktionen, Guardrails, MCP-Registrierung noch Markdown-Reporting.
- Keine Roadmap-Änderung ist erforderlich: Es gibt keinen abgeschlossenen Vorgänger und keine neue Muss-Haben-Anforderung außerhalb des bereits bestehenden `EPIC-01`.
