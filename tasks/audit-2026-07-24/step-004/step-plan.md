---
status: done
type: step-plan
task: audit-2026-07-24
step: 004
title: "Punkt 18 — Gemeinsamer SQL-Char-Scanner als interne Primitive extrahieren"
created_by: planer
created_at: 2026-07-25T18:30:00+02:00
reviewed_at: 2026-07-25T22:15:00+02:00
fix_completed_at: 2026-07-25T22:45:00+02:00
fix_commit: 9b4482a
verdict: issues -> fixed (audit skipped per user request)
related_to:
  - tasks/audit-2026-07-24/03-code-qualitaet-architektur.md (DRY-Impact Hoch #1)
  - tasks/audit-2026-07-24/00-summary.md (Punkt 18)
---

# Step 004: Punkt 18 — Gemeinsamer SQL-Char-Scanner als Primitive extrahieren

## Bezug

- **Task:** `audit-2026-07-24`
- **Quelle:** `03-code-qualitaet-architektur.md` Teil B, „Drei fast identische SQL-Zeichen-Zustandsautomaten" (DRY-Impact Hoch #1)
- **Phase / Priorität:** Phase 4 — Architektur-Aufräumarbeit, Punkt 18 (umfangreichster DRY-Step)

## Intention

Drei Klassen scannen SQL zeichenweise mit demselben `State { Normal, LineComment, BlockComment, SingleQuote, Bracket }`-Zustandsautomaten, nur um unterschiedliche Dinge mit jedem Span zu tun:

1. `src/SqlToAi/Database/SqlMultiStatementDetector.cs` (ehem. `QueryExecutionService.ContainsMultipleStatements`) — Semikolon-Zählung
2. `src/SqlToAi/Security/ReadOnlyGuard.cs:64-143` (`StripCommentsAndStringLiterals`/`ProcessChar`/`TransitionFromNormalState`) — String-Literal-Inhalt ausblenden, dann Regex drauf
3. `src/SqlToAi/Database/SqlLiteralScanner.cs:28-102` (`GetLiteralContentRanges`/`Transition`/`TransitionFromNormal`) — Range-Erfassung von Literal-Inhalten

Der Audit-Bericht nennt eine geschätzte Einsparung von 150-180 Zeilen sowie den Vorteil, dass Parsing-Bugs nur an einer Stelle gefixt werden müssen. Der bestehende `SqlLiteralScanner.cs`-Kommentar (Zeile 11-14) hält die Duplizierung explizit für „bewusst in Kauf genommen, um den bereits getesteten, sicherheitskritischen Multi-Statement-Detector nicht anzufassen" — eine nachvollziehbare Motivation für die ursprüngliche Aufteilung, die aber für die *Konsumlogik* gilt (was mit jedem State passiert), nicht für die *mechanische* Scan-Schleife.

Ziel dieses Steps: Einen gemeinsamen `internal static class SqlCharScanner` in `SqlToAi.Database` einführen, der pro Zeichen ein `(State, Char, Next, Index)`-Event liefert (oder ein `IEnumerable<SqlCharEvent>`-Stream); die drei Call-Sites setzen ihre bestehende, unveränderte Business-Logik (Semikolon-Zählung, Content-Blanking, Range-Erfassung) darauf auf. **Bestehende Tests bleiben unverändert grün** (siehe `SqlLiteralScannerTests.cs`, `ReadOnlyGuardTests.cs`, Multi-Statement-Tests in `QueryExecutionServiceTests.cs`).

## Konkrete Änderungen

### Datei 1 (neu): `src/SqlToAi/Database/SqlCharScanner.cs`

- **Was:** Eine neue `internal static class SqlCharScanner` mit:
  - `public enum SqlCharState { Normal, LineComment, BlockComment, SingleQuote, Bracket }`
  - `public readonly record struct SqlCharEvent(SqlCharState State, char Char, char Next, int Index)`
  - `public static IEnumerable<SqlCharEvent> Scan(ReadOnlySpan<char> sql)` — iteriert zeichenweise durch `sql`, ruft intern eine `Transition`-Methode auf, die identisch zur bestehenden Mechanik in den drei Originalklassen ist.
  - **Wichtig:** `Transition` selbst bleibt im neuen Scanner gekapselt. Die drei Call-Sites konsumieren die `SqlCharEvent`-Sequenz und setzen ihre eigene (unterschiedliche) Business-Logik auf.
- **Warum:** Eine **einzige** Stelle für die Parser-Mechanik, drei Stellen für die anwendungsspezifische Auswertung. Die `Transition`/`TransitionFromNormal`-Methoden sind mechanisch Zeile-für-Zeile identisch über alle drei Originalklassen — sie zu duplizieren ist die Code-Smell, die dieser Step auflöst.
- **Edge-Cases für `Transition`:** Behandelt alle Zustandsübergänge genauso wie die bestehenden drei Klassen: `--` → `LineComment`, `/*` → `BlockComment`, `'` → `SingleQuote`, `[` → `Bracket`, `''` (Escaped Quote) bleibt in `SingleQuote`, `*/` schließt `BlockComment`.

### Datei 2: `src/SqlToAi/Database/SqlMultiStatementDetector.cs`

- **Was:** Die `enum SqlParserState`, `Transition`- und `TransitionFromNormal`-Methoden entfernen. `ContainsMultipleStatements` iteriert per `foreach (var ev in SqlCharScanner.Scan(query.AsSpan()))` und prüft `if (ev.State == SqlCharState.Normal && ev.Char == ';')`. Die `i++`-Skip-Logik in `Transition` entfällt, weil der Scanner bereits die korrekten Übergänge liefert (jeder `SqlCharEvent` repräsentiert genau das Zeichen, das die aufrufende Logik verarbeiten soll).
- **Warum:** Die `Multi-Statement`-Erkennung selbst (Semikolon außerhalb von Literalen/Kommentaren zählen) ist die unveränderliche Business-Logik. Nur die Mechanik wird ausgelagert.
- **Achtung:** Der bestehende `query[(i + 1)..].TrimEnd()`-Aufruf (zur Erkennung des Trailing-Semikolons) muss auf den `Index` aus `SqlCharEvent` umgestellt werden.

### Datei 3: `src/SqlToAi/Security/ReadOnlyGuard.cs`

- **Was:** Die `enum SqlParserState`, `StripCommentsAndStringLiterals`, `ProcessChar`, `TransitionFromNormalState` reduzieren auf:
  ```csharp
  private static string StripCommentsAndStringLiterals(string sql)
  {
      var sb = new StringBuilder(sql.Length);
      foreach (var ev in SqlCharScanner.Scan(sql.AsSpan()))
      {
          // Original-Logik: Zeichen in 'Normal' durchreichen,
          // in 'SingleQuote' durch Leerzeichen ersetzen, sonst überspringen.
          if (ev.State == SqlCharState.Normal)
          {
              sb.Append(ev.Char);
          }
          else if (ev.State == SqlCharState.SingleQuote && ev.Char == '\'')
          {
              sb.Append(' ');
          }
          // Andere States (Comments, Bracket) werden implizit übersprungen
      }
      return sb.ToString();
  }
  ```
- **Warum:** Die Ausblende-Logik bleibt semantisch identisch (Kommentare und String-Literal-Inhalte durch Whitespace ersetzen), aber die Mechanik kommt aus dem Scanner.

### Datei 4: `src/SqlToAi/Database/SqlLiteralScanner.cs`

- **Was:** Die `enum State`, `Transition`, `TransitionFromNormal` entfernen. `GetLiteralContentRanges` iteriert per `foreach` über `SqlCharScanner.Scan(sql.AsSpan())` und erkennt `SingleQuote`→`Normal`-Übergänge (vorheriger State war SingleQuote, aktueller ist Normal = Literal-Ende) und `Normal`→`SingleQuote`-Übergänge (Literal-Start). Die Range-Berechnung `i + 1` → `Index + 1` und `i - literalStart` → `Index - literalStart` umstellen.
- **Warum:** Range-Erfassung ist die Business-Logik; Mechanik wird ausgelagert.

### Datei 5: `tests/SqlToAi.Tests/Database/SqlCharScannerTests.cs` (neu)

- **Was:** Dedizierte Unit-Tests für den neuen Scanner:
  - `Scan_ShouldClassifyCommentAndLiteralStates` — Eingabe `-- foo /* bar */ 'literal' [bracket]`, prüft für jedes Zeichen den erwarteten `State`.
  - `Scan_ShouldHandleEscapedQuotesInsideLiterals` — `'''` und `''''` (Escaped-Quote-Varianten).
  - `Scan_ShouldHandleNestedBracketAndCommentEnd` — `/* nested /* still comment */ end` und `[bracket-with-]-inside]`.
  - `Scan_ShouldHandleEmptyInput` — leerer String liefert leere Sequenz.
- **Warum:** Verifiziert, dass der extrahierte Scanner mechanisch identisch zu den drei Originalklassen ist, BEVOR die Migration abgeschlossen ist. Falls der Auditer beim Refactor einen Bug findet, fallen diese Tests zuerst.

## Tests

- [ ] `SqlCharScannerTests.Scan_ShouldClassifyCommentAndLiteralStates` — Theory über mehrere Inline-Inputs
- [ ] `SqlCharScannerTests.Scan_ShouldHandleEscapedQuotesInsideLiterals` — `'''` und verwandte Edge-Cases
- [ ] `SqlCharScannerTests.Scan_ShouldHandleEmptyInput` — leerer String
- [ ] **Bestehende Tests bleiben grün ohne Änderung:**
  - `SqlLiteralScannerTests` (komplett) — die `GetLiteralContentRanges`-Signatur ist unverändert
  - `ReadOnlyGuardTests` (komplett) — die `IsQuerySafe`-Black-Box-Semantik ist unverändert
  - `QueryExecutionServiceTests` Multi-Statement-Tests — die `ContainsMultipleStatements`-Black-Box-Semantik ist unverändert
  - `QueryValidationServiceTests` — gleich
- [ ] `dotnet build SqlToAi.slnx` 0 Warnungen, 0 Fehler
- [ ] `dotnet test --filter "Category!=Integration"` grün

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command grün (0 Warnings, 0 Errors)
- [ ] Test-Command grün (Ausnahmen siehe „Bekannte Ausnahmen")
- [ ] Commit auf aktuellem Branch (`refactor(database): extrahiere gemeinsamen SqlCharScanner aus drei State-Machine-Duplikaten`)
- [ ] `step-004/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#5` — „Result-Pattern: Bevorzuge `Result<T>`" — unverändert, keine Tool-Grenze betroffen
- `.agents/rules/AiNetLinter.mdc#general/EnforceSealedClasses` — `SqlCharScanner` ist `internal static` (kein `sealed` nötig bei `static`)
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — Methodenlänge ≤60 Zeilen; `Scan` selbst bleibt unter 20 Zeilen
- `.agents/rules/AiNetLinter.mdc#agent-resilience/EnforceNoSilentCatch` — keine leeren `catch`-Blöcke; `SqlCharScanner` hat keine `try/catch`, die Fehlerbehandlung bleibt in den Call-Sites

## Bekannte Ausnahmen

- `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` — vorbestehend, **nicht** Teil dieses Tasks. **Dieser Step wird sehr wahrscheinlich eine Baseline-Aktualisierung für mehrere Dateien auslösen:**
  - `src/SqlToAi/Database/SqlMultiStatementDetector.cs` (verändert)
  - `src/SqlToAi/Security/ReadOnlyGuard.cs` (verändert)
  - `src/SqlToAi/Database/SqlLiteralScanner.cs` (verändert)
  - **Neu:** `src/SqlToAi/Database/SqlCharScanner.cs` (muss zur Baseline hinzugefügt werden)
  - **Neu:** `tests/SqlToAi.Tests/Database/SqlCharScannerTests.cs` (muss zur Baseline hinzugefügt werden)
  - **Neu:** `tests/SqlToAi.Tests/Database/SqlCharScannerTests.cs` (Hash muss eingetragen werden)
  - Für **jede** dieser Dateien den SHA-256-Hash des finalen Inhalts berechnen und in `SqlToAi-baseline.json` eintragen. Das ist ein rein mechanischer Begleitschritt.

## Notes

- **Größter Step im Plan:** Geschätzt 150-180 Zeilen Netto-Einsparung, plus die neue Scanner-Klasse (~50-70 Zeilen) und ihre Tests (~80-120 Zeilen). Netto: ~100-200 Zeilen weniger Code.
- **Reihenfolge der Refactor-Phasen:** Empfohlen, die drei Call-Sites **nacheinander** in separaten Commits umzustellen (eine Commit pro Datei), statt alle drei auf einmal — das macht den Diff reviewbar. **ABER:** Da `SqlCharScanner` zwischen den Schritten nicht existieren darf, muss der gemeinsame Scanner **zuerst** hinzugefügt werden, dann alle drei Call-Sites in **einem** Commit umgestellt. Die Alternative (drei separate Commits) würde temporär einen Hybrid-Zustand erzeugen, in dem `SqlCharScanner` und die alten `Transition`-Methoden parallel existieren.
- **Alternative: yield return vs. Span-Iterator:** Der Scanner könnte `IEnumerable<SqlCharEvent>` per `yield return` zurückgeben (klare API) oder einen `ref struct`-basierten Iterator (allocation-frei). Für xUnit-v3-Tests und Lesbarkeit ist `yield return` die pragmatische Wahl; Performance-Verlust ist bei SQL-Queries im einstelligen KB-Bereich irrelevant.
- **Risiko:** `SqlLiteralScanner` ist im Token-Resolver-Pfad sicherheitskritisch (siehe `QueryTokenResolver.ResolveTokens`). Der Refactor muss mit äußerster Sorgfalt erfolgen, und die Tests müssen **zwingend** vor dem Commit grün sein. Empfehlung: lokales Testen + manueller Smoke-Test mit einem bekannten Token-Substitutions-Szenario vor dem Commit.
- **Kein Verhaltens-Refactor:** Die Reihenfolge der `State`-Enum-Werte, die Skip-Logik und die Edge-Case-Behandlung bleiben **bit-identisch** zu den drei Originalklassen. Nur die Mechanik wird geteilt.
- **Nicht im Scope dieses Steps:** `SqlCharScanner` exportiert vorerst nur die `Scan`-Methode und den `SqlCharState`-Enum. Eine künftige Erweiterung um `Skip`, `Peek` o.ä. ist möglich, gehört aber nicht in diesen Audit-Step.
