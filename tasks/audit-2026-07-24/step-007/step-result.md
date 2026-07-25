---
status: done (pending audit)
type: step-result
task: audit-2026-07-24
step: 007
title: "Punkt 21 — MarkdownTableRenderer konsolidieren"
model_id: MiniMax-M3
model_knowledge_cutoff: 2026-01
---

# Step 007 — Ergebnis

## Zusammenfassung

Die dreifach identisch kopierte private `RenderMarkdownTable`-Methode in `SchemaService`, `DetailSchemaRenderer` und `TableSchemaRenderer` wurde durch eine einzige `internal static class MarkdownTableRenderer` ersetzt. Die neue Klasse enthält die Render-Logik byte-identisch zur Original-Implementierung; alle acht Aufrufer (1 + 5 + 2) wurden umgestellt, die drei privaten Methoden entfernt und die jetzt überflüssigen `using System.Text;`-Direktiven in `SchemaService.cs` und `DetailSchemaRenderer.cs` aufgeräumt. Vier neue Unit-Tests (`MarkdownTableRendererTests`) decken Header/Separator, Pipe-Escaping, leere Row-Liste und Null-Zellen ab.

## Bit-Identitäts-Verifikation

Vor dem Refactor wurden die drei Original-Methoden-Körper (Z. 279-289, 334-344, 281-291) zeilenweise extrahiert und mit SHA-256 gehasht. Alle drei Hashes waren identisch:

```
675ce12b63911a00d8fed23df8c45836d4ad9138dc72f73116558ad7d242cc08
```

Daher war die Konsolidierung gefahrlos möglich. Die neue `MarkdownTableRenderer.Render`-Implementierung enthält exakt denselben Rumpf (selber `StringBuilder`-Code, selbe Header-/Separator-/Row-Logik, selbes `r?.Replace("|", "\\|") ?? ""`-Escaping), und die dedizierten Unit-Tests prüfen das Verhalten bit-genau mit `Assert.Equal` auf exakte String-Erwartungen (inkl. `\r\n`-Zeilenenden).

## Geänderte Dateien

| Datei | Änderung |
|---|---|
| `src/SqlToAi/Database/MarkdownTableRenderer.cs` | **Neu** — `internal static class` mit `Render(string[] headers, List<string[]> rows)`, identisch zur Original-Logik. XML-Doku dokumentiert Bit-Identität und Konsolidierungs-Hintergrund. |
| `src/SqlToAi/Database/SchemaService.cs` | Private `RenderMarkdownTable` (Z. 279-289) entfernt. Aufruf in Z. 167 auf `MarkdownTableRenderer.Render(...)` umgestellt. `using System.Text;` entfernt (nicht mehr benötigt). |
| `src/SqlToAi/Database/DetailSchemaRenderer.cs` | Private `RenderMarkdownTable` (Z. 334-344) entfernt. Alle 5 Aufrufe (Z. 88, 161, 215, 279, 330) auf `MarkdownTableRenderer.Render(...)` umgestellt. `using System.Text;` entfernt. |
| `src/SqlToAi/Database/TableSchemaRenderer.cs` | Private `RenderMarkdownTable` (Z. 281-291) entfernt. Beide Aufrufe (Z. 144, 168) auf `MarkdownTableRenderer.Render(...)` umgestellt. `using System.Text;` bleibt — Datei nutzt `StringBuilder` weiterhin für andere Markdown-Konstruktionen. |
| `tests/SqlToAi.Tests/Database/MarkdownTableRendererTests.cs` | **Neu** — 4 Unit-Tests (Header/Separator, Pipe-Escaping, leere Rows, Null-Zellen). |
| `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` | **Auto-aktualisiert** durch `AiNetLinterTests.RecreateBaseline` (siehe unten). |

## Commit

- **Code-Commit:** `085cb4a026c9957225675456894b80cff5e0b517`
- **Message:** `refactor(database): konsolidiere RenderMarkdownTable in gemeinsame MarkdownTableRenderer`
- **Kein Push.**

## Build- und Test-Output

```
$ dotnet build SqlToAi.slnx
  SqlToAi -> .../SqlToAi.dll
  SqlToAi.Tests -> .../SqlToAi.Tests.dll
  Der Buildvorgang wurde erfolgreich ausgeführt.
  0 Warnung(en), 0 Fehler

$ dotnet test --filter "Category!=Integration"
  Bestanden! Fehler: 0, erfolgreich: 393, übersprungen: 0, gesamt: 393, Dauer: 18 s

  Davon relevant:
  - MarkdownTableRendererTests.Render_ShouldHandleNullCell              [31 ms]
  - MarkdownTableRendererTests.Render_ShouldEscapePipeCharacter...     [ 2 ms]
  - MarkdownTableRendererTests.Render_ShouldHandleEmptyRows             [< 1 ms]
  - MarkdownTableRendererTests.Render_ShouldProduceHeaderAndSeparatorRow [< 1 ms]
  - AiNetLinterTests.RecreateBaseline                                   [ 3 s]
  - AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch              [ 7 s]
```

393 Tests grün (389 alt + 4 neu). Beide AiNetLinter-Tests inklusive — `RecreateBaseline` hat die `SqlToAi-baseline.json` automatisch neu geschrieben, `RunLinterShouldBeCleanOrBaselineMatch` ist grün (Exit-Code 0, keine Lint-Verletzungen).

## AiNetLinter-Baseline

Die `SqlToAi-baseline.json` wurde **automatisch** durch `AiNetLinterTests.RecreateBaseline` aktualisiert und ist Teil des Code-Commits (`085cb4a`). Es waren **keine** manuellen SHA-256-Berechnungen oder `Get-FileHash`-Aufrufe nötig — der bestehende Test-Lauf hat alles erledigt.

Diff-Stat: `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json | 8 ++++++--` — 5 Datei-Hashes aktualisiert (drei modifizierte `.cs` + zwei neue `.cs`).

## Abweichungen vom Plan

Keine substanziellen. Eine kleine Implementierungs-Abweichung beim Null-Cell-Test: Die geplante Variante `rows: [[null]]` lässt sich unter `#nullable enable` mit `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` nicht kompilieren, weil der Collection-Expression-Compiler den Element-Typ als `string` (non-nullable) inferiert. Behoben mit expliziter `new string[] { null! }`-Konstruktion; Verhalten exakt wie geplant (getestet, dass `null` zu leerem String wird).

## Beobachtungen (nicht im Scope)

- **`TableSchemaRenderer.RenderMarkdownTable` vs. `MarkdownTableRenderer.Render`:** Die Namensgebung ist nun sauber differenziert — `TableSchemaRenderer` ist die *Klasse*, die ganze Schema-Markdown-Dokumente baut; `MarkdownTableRenderer` ist der *Helper* für reine Tabellen. Beide koexistieren, wie im Plan angemerkt.
- **Zukünftige Erweiterung (siehe Plan-Notes):** Falls weitere Tabellen-Formate (HTML, CSV) hinzukommen, könnte `MarkdownTableRenderer` Teil eines `IRenderer`-Patterns werden. Aktuell nicht relevant.
- **DetailSchemaRenderer.cs `using System.Text;` Entfernung:** Die Datei war nach Entfernen der Methode vollständig `StringBuilder`-frei, daher konnte der Import weg. `TableSchemaRenderer.cs` nutzt `StringBuilder` weiterhin (für `AppendLine`-Aufrufe in `GetTableSchemaMarkdownAsync`, `GetViewDefinitionMarkdownAsync`, `GetRoutineSchemaMarkdownAsync`), dort bleibt der Import.
- **Linter-Hinweis `MaxMethodLineCount`:** `Render` hat 6 Logik-Zeilen, weit unter dem 60-Zeilen-Limit. Kein Lint-Treffer.

## Bekannte Unschärfen für den Auditer

- **Bit-Identität formal:** Vor dem Commit wurde die Hash-Gleichheit der drei Original-Methoden verifiziert (SHA-256 `675ce12b...`). Nach dem Commit kann der Auditer nur noch die finale Implementierung in `MarkdownTableRenderer.cs` gegen die Tests prüfen — die Original-Kopien existieren nicht mehr. Die Tests prüfen exakte String-Erwartungen mit `\r\n`-Zeilenenden, was die zukünftige Format-Konformität sichert.
- **`using System.Linq;`** wird in `MarkdownTableRenderer.cs` nicht explizit importiert, weil `GlobalUsings` (vermutlich `ImplicitUsings`) `System.Linq` einschließt — sichtbar an der bestehenden Verwendung in `SchemaService.cs` ohne expliziten Import. Falls der Auditer explizite `using`-Direktiven fordert, wäre das eine Style-Frage (Follow-up).
- **Linter-Override `ExemptStaticClasses: true`:** In `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi.rules.json` ist `ExemptStaticClasses: true` gesetzt — daher ist `internal static class MarkdownTableRenderer` linter-konform, ohne `sealed` (konsistent mit `SqlLiteralScanner` und `SqlMultiStatementDetector`).
