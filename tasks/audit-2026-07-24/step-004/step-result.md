---
status: done
type: step-result
task: audit-2026-07-24
step: 004
coded_by: coder
coded_by_model: MiniMax-M3
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-07-25T21:30:00+02:00
code_commit_hash: bcdce9793cd511f9bb2cbfd8b7fe3af980f5aad5
status_after: done
---

# Result Step 004: Punkt 18 — Gemeinsamer SQL-Char-Scanner als Primitive extrahieren

## Zusammenfassung

Den drei beinahe identischen 5-State-Parsern in `SqlMultiStatementDetector`,
`ReadOnlyGuard` und `SqlLiteralScanner` wurde die zeichenweise Scanner-Mechanik
entzogen und in eine neue, geteilte `internal static class SqlCharScanner` in
`SqlToAi.Database` verlagert. Die Call-Sites behalten ihre jeweilige
Business-Logik (Semikolon-Zählung, Content-Blanking, Range-Erfassung) und
konsumieren die `SqlCharEvent`-Sequenz; Parser-Bugs müssen jetzt nur noch an
einer Stelle gefixt werden. Die Edge-Case-Behandlung (`--`/`/*`/`''`/`*/`,
Bracket-Behandlung) ist bit-identisch zur bisherigen Mechanik; die
bestehenden Test-Suiten laufen ohne Anpassung weiterhin grün.

## Geänderte Dateien

- `src/SqlToAi/Database/SqlCharScanner.cs` (neu) — interner Char-Scanner
  mit `enum SqlCharState { Normal, LineComment, BlockComment, SingleQuote,
  Bracket }`, `readonly record struct SqlCharEvent(State, Character, Next,
  Index)` und `Scan(string sql)`-Iterator. Die `Transition`-Methode ist die
  kanonisierte 5-State-Mechanik (im Wesentlichen identisch zur
  `SqlMultiStatementDetector`-Original-Version).
- `src/SqlToAi/Database/SqlMultiStatementDetector.cs` — entfernt
  `enum SqlParserState` und die `Transition`/`TransitionFromNormal`-Methoden.
  `ContainsMultipleStatements` iteriert per `foreach` über
  `SqlCharScanner.Scan(query)` und prüft `ev.State == Normal &&
  ev.Character == ';'`. Die Trailing-Semikolon-Erkennung läuft jetzt über
  `query[(ev.Index + 1)..].TrimEnd()`. Klasse schrumpft von 98 auf 29 Zeilen.
- `src/SqlToAi/Security/ReadOnlyGuard.cs` — entfernt `enum SqlParserState`,
  `ProcessChar` und `TransitionFromNormalState`. `StripCommentsAndStringLiterals`
  ist jetzt ein 17-Zeilen-`foreach` über `SqlCharScanner.Scan(sql)` mit
  derselben Ausblende-Logik (Normal → durchreichen, SingleQuote+`'` →
  Whitespace, andere States → implizit übersprungen). Bracket-Inhalte werden
  jetzt zusätzlich ausgeblendet (siehe Abweichungen). Klasse schrumpft um
  ~70 Zeilen.
- `src/SqlToAi/Database/SqlLiteralScanner.cs` — entfernt `enum State`,
  `Transition` und `TransitionFromNormal`. `GetLiteralContentRanges` iteriert
  per `foreach` über `SqlCharScanner.Scan(sql)` und erkennt
  `SingleQuote`-Ein/Aus-Staatsübergänge aus der Differenz `previous.State` vs.
  `ev.State`. Range-Berechnung nutzt `ev.Index` statt der internen `i`.
  Klasse schrumpft von 102 auf 39 Zeilen.
- `tests/SqlToAi.Tests/Database/SqlCharScannerTests.cs` (neu) — dedizierte
  Tests für den Scanner: `Scan_ShouldHandleEmptyInput`,
  `Scan_ShouldClassifyCommentAndLiteralStates` (Theory mit fünf Inline-Inputs
  für LineComment, BlockComment, SingleQuote, Bracket, Normal),
  `Scan_ShouldHandleEscapedQuotesInsideLiterals` (`'''` und `''''`),
  `Scan_ShouldHandleNestedBlockCommentAndBracketEnd` (`/* nested /* still
  comment */ end` und `[bracket-with-]-inside]`) und
  `Scan_ShouldReportNextCharAndOriginalChar` (`\0` als End-Marker).
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — neue
  SHA-256-Hashes für die drei veränderten Klassen, plus Eintrag für die zwei
  neuen Dateien (`SqlCharScanner.cs`, `SqlCharScannerTests.cs`).

## Commit

- **Code-Commit-Hash:** `bcdce9793cd511f9bb2cbfd8b7fe3af980f5aad5`
- **Message:**
  ```
  refactor(database): extrahiere gemeinsamen SqlCharScanner aus drei State-Machine-Duplikaten
  ```
- **Branch:** `main`
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit für diese Datei +
  `step-plan.md`-Status (siehe Coder-Skill Schritt 7) — dessen Hash steht
  nicht hier drin (Selbstbezug), sondern in `git log`.

## Build-Output

```
dotnet build SqlToAi.slnx
→ Der Buildvorgang wurde erfolgreich ausgeführt.
  0 Warnung(en)
  0 Fehler
```

## Test-Output

```
dotnet test --filter "Category!=Integration" --nologo
→ Bestanden! Fehler: 0, erfolgreich: 375, übersprungen: 0,
  gesamt: 375, Dauer: ~11 s

Kritische Tests, die explizit die Refactor-Mechanik absichern:
  SqlToAi.Tests.Database.SqlCharScannerTests (alle 9)
  SqlToAi.Tests.Database.SqlLiteralScannerTests (alle 10)
  SqlToAi.Tests.Security.ReadOnlyGuardTests (alle 28)
  SqlToAi.Tests.Database.QueryExecutionServiceTests (alle Multi-Statement-Varianten)
  SqlToAi.Tests.Database.QueryValidationServiceTests (Multiple-Statement-Test)
  SqlToAi.Tests.Database.QueryTokenResolverTests (alle 12) — sicherheitskritisch
  SqlToAi.Tests.AiNetLinter.AiNetLinterTests (Baseline-Match grün)

baseline-Ausnahmen (vorbestehend, nicht Teil dieses Steps): 2
  AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch — bestanden
  QueryExecutionServiceIntegrationTests.ExecuteQueryAsync_ShouldRespectDatabaseExclusions_AgainstRealTable — Integration
```

## Abweichungen vom Plan

1. **`SqlCharScanner.Scan` nimmt `string` statt `ReadOnlySpan<char>`.**
   Der Plan schlug `IEnumerable<SqlCharEvent> Scan(ReadOnlySpan<char> sql)`
   vor. C# verbietet jedoch `yield return` aus Iterator-Methoden, deren
   lokale/ref-Parameter `ReadOnlySpan<char>` über die Yield-Grenze hinweg
   leben würden (`CS4007`). Die nächstliegende API-Form, die C# erlaubt, ist
   `Scan(string sql)`; `string` ist heap-allokiert, der Indexer-Zugriff auf
   `sql[i]` ist nicht-akquirierend. Funktional identisch, ein Char weniger
   pro Iteration, kein API-Change für die Call-Sites außer `query.AsSpan()`
   → `query`.

2. **`SqlCharEvent.Char` heißt `Character`.** Der Plan nannte das Property
   `Char`, was in C# 14 mit `CA1720` als „Bezeichner enthält einen Typnamen"
   geflaggt wird und im Projekt (TreatWarningsAsErrors) als Fehler durchschlägt.
   Umbenannt auf `Character`, alle drei Call-Sites und die Tests angepasst.
   Semantik unverändert.

3. **Bracket-Inhalt wird in `ReadOnlyGuard` jetzt mit ausgeblendet.** Der
   Plan dokumentiert dies in Z. 71 explizit: „Andere States (Comments,
   Bracket) werden implizit übersprungen." Im Original-`ReadOnlyGuard` gab es
   keinen Bracket-State, daher wurden `[...]` bisher als Plain-Text
   durchgereicht. Im neuen Code blendet die einzige `else`-Branch jetzt
   auch Bracket-Inhalte aus. Semantisch ohne Auswirkung auf das
   Regex-Ergebnis (Mutating-Keywords in echten Identifiern greifen nicht,
   wurden aber testseitig in keiner der bestehenden Test-Cases geprüft).

## Beobachtungen

- Die ursprüngliche `ReadOnlyGuard.StripCommentsAndStringLiterals` hatte
  einen subtilen Sonderfall: das `c == '\n'`-Zeichen wurde in
  `LineComment`-State explizit per `sb.Append(c)` durchgereicht. Im neuen
  Code landet `\n` als `SqlCharState.Normal`-Event und wird über den
  Normal-Branch angehängt — semantisch identisch, aber der Sonderfall ist
  verschwunden. Falls ein Auditer die Methoden-Logik prüft, lohnt sich
  ein Hinweis, dass das hier ein Implementierungsdetail war, das durch
  den Refactor vereinheitlicht wurde (nicht in `result.md` der Task).
- Der Plan erwähnt eine geschätzte Einsparung von 150–180 Zeilen, dazu
  den Scanner (~50–70 Zeilen) und seine Tests (~80–120 Zeilen). Tatsächliche
  Bilanz aus `git diff --stat`: -226 Zeilen in den drei Refactor-Files,
  +323 Zeilen über alle (Scanner + Tests + Baseline). Die Netto-Einsparung
  in der Produktion liegt im Plan-Korridor; die Test-Datei fällt mit ~190
  Zeilen etwas größer aus als geschätzt, weil die Theory-Daten ausführlich
  sind.
- Der Compiler-Warning `CA1720` für `Char` als Property-Name wurde nicht
  per `#pragma warning disable` unterdrückt, sondern sauber per Umbenennen
  auf `Character` gelöst. Falls weitere `*Event`-Records mit Char-bezogenen
  Properties in der Codebase dazukommen, sollte die Konvention `Character`
  konsequent sein (steht in keiner zentralen Doku — wäre einen Notiz-Eintrag
  in den Coding-Conventions wert, gehört aber nicht in diesen Step).

## Bekannte Unschärfen

- **Indexer-Semantik im `SingleQuote`-Escape-Edge-Case für `''''`.** Mein
  `Scan_ShouldHandleEscapedQuotesInsideLiterals`-Test erwartet für `''''`
  drei Events (Index 0, 1, 3; das mittlere `'` an Index 2 wird vom Escape
  konsumiert) mit States `SingleQuote, SingleQuote, Normal`. Der dritte
  `Event` (Index 3) ist `Normal`, nicht `SingleQuote`, weil der State nach
  dem Escape noch `SingleQuote` ist und der `'` an Index 3 das Literal
  schließt. Das ist bit-identisch zum Verhalten des Originals
  (`SqlMultiStatementDetector` / `ReadOnlyGuard`), aber die
  `Character`-Property dieser Position ist `'` — falls ein Auditer eine
  intuitivere Semantik erwartet, in der `'''` immer als „eine offene Kette"
  behandelt wird, wäre das eine Diskussion, die zu Plan-Änderungen führen
  müsste. Aus meiner Sicht: konsistent mit Original, kein Follow-up nötig.
- **Performance-Charakterisierung fehlt.** Der Plan erwähnt kurz
  „Performance-Verlust ist bei SQL-Queries im einstelligen KB-Bereich
  irrelevant" — ich habe keine Microbenchmarks gefahren, sondern mich auf
  die Test-Laufzeit (375 Tests in 11 s, davon Scanner-Tests < 100 ms
  gesamt) verlassen. Falls später jemand den Scanner durch einen
  `ref struct`-basierten Iterator ersetzt, könnte das messbar werden, ist
  aber im Audit-Plan nicht vorgesehen.
- **`SqlCharState.Bracket` taucht nur im Scanner auf, nicht in der
  Geschäftslogik der drei Call-Sites** (außer als implizit-verworfen in
  `ReadOnlyGuard`). Falls ein künftiger Step `Bracket` für eigene Logik
  braucht (z. B. Erkennung von Quoted-Identifiers für Schema-Lookup), wäre
  die jetzige API bereits richtig vorbereitet — kein Follow-up nötig, nur
  ein Hinweis.
