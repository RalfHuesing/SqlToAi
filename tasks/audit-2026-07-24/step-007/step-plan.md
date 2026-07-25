---
status: done (pending audit)
type: step-plan
task: audit-2026-07-24
step: 007
title: "Punkt 21 — RenderMarkdownTable in gemeinsame MarkdownTableRenderer konsolidieren"
created_by: planer
created_at: 2026-07-25T18:30:00+02:00
related_to:
  - tasks/audit-2026-07-24/03-code-qualitaet-architektur.md (DRY-Impact Mittel #1)
  - tasks/audit-2026-07-24/00-summary.md (Punkt 21)
---

# Step 007: Punkt 21 — RenderMarkdownTable konsolidieren

## Bezug

- **Task:** `audit-2026-07-24`
- **Quelle:** `03-code-qualitaet-architektur.md` Teil B „`RenderMarkdownTable` dreifach identisch kopiert" (DRY-Impact Mittel #1)
- **Phase / Priorität:** Phase 4 — Architektur-Aufräumarbeit, Punkt 21

## Intention

Drei Klassen enthalten eine textuell **identische** private `RenderMarkdownTable(string[] headers, List<string[]> rows)`-Methode:

- `src/SqlToAi/Database/SchemaService.cs:350-360`
- `src/SqlToAi/Database/DetailSchemaRenderer.cs:334-344`
- `src/SqlToAi/Database/TableSchemaRenderer.cs:281-291`

Alle drei Versionen erzeugen dieselbe Markdown-Tabellen-Ausgabe: `| h1 | h2 | … |`-Header-Zeile, `| --- | --- | … |`-Trennzeile, und `| … |`-Datenzeilen mit `|`-Escaping. Reines Copy-Paste ohne jede Abweichung. Risiko: Wenn das Escaping einmal angepasst wird (z. B. um Newlines in Zellen abzufangen), passiert es leicht nur an zwei von drei Stellen — und die Ausgabe divergiert unbemerkt.

Ziel: Eine `internal static class MarkdownTableRenderer` im `SqlToAi.Database`-Namespace einführen. Alle drei Aufrufer referenzieren dieselbe Methode. Bonus: verhindert künftiges Auseinanderlaufen.

## Konkrete Änderungen

### Datei 1 (neu): `src/SqlToAi/Database/MarkdownTableRenderer.cs`

- **Was:**
  ```csharp
  #nullable enable
  using System.Text;

  namespace SqlToAi.Database;

  /// <summary>
  /// Renders an in-memory table (headers + rows of cell strings) as a GitHub-flavored
  /// Markdown pipe-table, with the only required escaping being the pipe character
  /// inside cell values (newlines and other Markdown-significant characters are
  /// intentionally NOT escaped — cell content is trusted, single-line content).
  /// </summary>
  internal static class MarkdownTableRenderer
  {
      public static string Render(string[] headers, List<string[]> rows)
      {
          var sb = new StringBuilder();
          sb.Append("| ").Append(string.Join(" | ", headers)).AppendLine(" |");
          sb.Append("| ").Append(string.Join(" | ", headers.Select(_ => "---"))).AppendLine(" |");
          foreach (var row in rows)
          {
              sb.Append("| ").Append(string.Join(" | ", row.Select(r => r?.Replace("|", "\\|") ?? ""))).AppendLine(" |");
          }
          return sb.ToString();
      }
  }
  ```
- **Warum:** Eine einzige Implementierung der Markdown-Tabellen-Generierung, identisches Verhalten zu den drei Original-Kopien.

### Datei 2: `src/SqlToAi/Database/SchemaService.cs`

- **Was:** Private `RenderMarkdownTable`-Methode (Zeile 350-360) entfernen. Aufruf in Zeile 167 (`SearchObjectsAsync`) durch `MarkdownTableRenderer.Render(["Schema", "Name", "Type"], renderedRows)` ersetzen.
- **Warum:** Identische Methode existiert dreimal — eine davon verschwindet.

### Datei 3: `src/SqlToAi/Database/DetailSchemaRenderer.cs`

- **Was:** Private `RenderMarkdownTable`-Methode (Zeile 334-344) entfernen. Alle fünf Aufrufe (Zeilen 89, 162, 216, 280, 331) auf `MarkdownTableRenderer.Render(headers, renderedRows)` umstellen.
- **Warum:** Wie oben.

### Datei 4: `src/SqlToAi/Database/TableSchemaRenderer.cs`

- **Was:** Private `RenderMarkdownTable`-Methode (Zeile 281-291) entfernen. Beide Aufrufe (Zeilen 144, 168) auf `MarkdownTableRenderer.Render(headers, renderedRows)` umstellen.
- **Warum:** Wie oben.

### Datei 5: `tests/SqlToAi.Tests/Database/MarkdownTableRendererTests.cs` (neu)

- **Was:** Dedizierte Unit-Tests für den neuen Renderer:
  - `Render_ShouldProduceHeaderAndSeparatorRow` — Header und `---`-Zeile korrekt formatiert
  - `Render_ShouldEscapePipeCharacter_InCellValues` — `a|b` in einer Zelle wird zu `a\|b`
  - `Render_ShouldHandleEmptyRows` — leere Row-Liste
  - `Render_ShouldHandleNullCell` — `null` in einer Zelle wird zu leerem String (siehe Original-Verhalten: `r?.Replace("|", "\\|") ?? ""`)
- **Warum:** Verifiziert das Verhalten isoliert, **bevor** die drei Klassen migriert werden — ein Bug in der neuen Methode würde sonst drei Stellen gleichzeitig betreffen.

## Tests

- [ ] `MarkdownTableRendererTests.Render_ShouldProduceHeaderAndSeparatorRow` — exakte Header/Trennzeile
- [ ] `MarkdownTableRendererTests.Render_ShouldEscapePipeCharacter_InCellValues` — Escaping
- [ ] `MarkdownTableRendererTests.Render_ShouldHandleEmptyRows`
- [ ] `MarkdownTableRendererTests.Render_ShouldHandleNullCell`
- [ ] **Bestehende Tests bleiben grün ohne Änderung:**
  - `SchemaServiceTests` (Markdown-Tabellen-Ausgabe in `SearchObjectsAsync` wird weiterhin korrekt sein)
  - `TableSchemaRendererTests` (Tabellen-Inhalte bleiben byte-identisch, weil die Methode bit-identisch ist)
- [ ] `dotnet build SqlToAi.slnx` 0 Warnungen, 0 Fehler
- [ ] `dotnet test --filter "Category!=Integration"` grün

## Definition of Done

- [ ] Alle „Konkreten Änderungen" umgesetzt
- [ ] Build-Command grün (0 Warnings, 0 Errors)
- [ ] Test-Command grün (Ausnahmen siehe „Bekannte Ausnahmen")
- [ ] Commit auf aktuellem Branch (`refactor(database): konsolidiere RenderMarkdownTable in gemeinsame MarkdownTableRenderer`)
- [ ] `step-007/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „xUnit v3 Tests: Pflicht für alle funktionalen Änderungen" (neue Utility-Klasse → Tests)
- `.agents/rules/AiNetLinter.mdc#general/EnforceSealedClasses` — `MarkdownTableRenderer` ist `internal static` (kein `sealed` nötig bei `static`)

## Bekannte Ausnahmen

- `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` — vorbestehend, **nicht** Teil dieses Tasks. Wahrscheinliche Baseline-Aktualisierungen:
  - `src/SqlToAi/Database/SchemaService.cs` (Methode entfernt, Aufruf umgestellt)
  - `src/SqlToAi/Database/DetailSchemaRenderer.cs` (Methode entfernt, 5 Aufrufe umgestellt)
  - `src/SqlToAi/Database/TableSchemaRenderer.cs` (Methode entfernt, 2 Aufrufe umgestellt)
  - **Neu:** `src/SqlToAi/Database/MarkdownTableRenderer.cs` (muss zur Baseline hinzugefügt werden)
  - **Neu:** `tests/SqlToAi.Tests/Database/MarkdownTableRendererTests.cs` (muss zur Baseline hinzugefügt werden)
  - SHA-256-Hashes der finalen Inhalte berechnen und in `SqlToAi-baseline.json` eintragen.

## Notes

- **Bit-Identität prüfen:** Vor dem Commit manuell verifizieren, dass die Ausgabe von `MarkdownTableRenderer.Render(...)` **byte-identisch** zur jeweiligen privaten Original-Methode ist. Der einfachste Weg: zwei identische Test-Inputs in einem Debug-Snapshot-Vergleich. Falls die Ausgabe auch nur um ein Leerzeichen abweicht, könnten AI-Clients, die auf exakte Formatierung angewiesen sind, anders reagieren.
- **`using System.Text;` und `using System.Linq;`** sind die einzigen Importe, die der neue Renderer braucht (`StringBuilder` + `Select`). Keine externen Abhängigkeiten.
- **Sichtbarkeit `internal static`:** Konsistent mit anderen Database-Helpern wie `SqlLiteralScanner` und `SqlMultiStatementDetector`. Die Methode ist eine reine Implementierungs-Detail, nicht Teil der öffentlichen API.
- **Reihenfolge im Commit:** Erst die neue Klasse hinzufügen, **dann** die drei Originale entfernen — wie bei Step 004, alles in **einem** Commit, damit kein Hybrid-Zustand existiert.
- **Linter-Hinweis `MaxMethodLineCount`:** Die `Render`-Methode hat 6-8 Zeilen (je nach Zählweise), klar unter dem 60-Zeilen-Limit.
- **Optionale zukünftige Erweiterung (nicht im Scope):** Falls weitere Tabellen-Renderer hinzukommen (z. B. ein HTML- oder CSV-Renderer), könnte `MarkdownTableRenderer` Teil eines `IRenderer`/`IRenderer<TInput,TOutput>`-Patterns werden. Für diesen Step nicht relevant.
- **Nicht zu verwechseln mit `TableSchemaRenderer`:** `TableSchemaRenderer.cs` ist die *Klasse*, die ganze Schema-Markdown-Dokumente erzeugt (mit `StringBuilder` und mehreren Appends); `MarkdownTableRenderer` ist die *Helper-Klasse* für reine Tabellen. Beide koexistieren.
