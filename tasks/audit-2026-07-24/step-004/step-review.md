---
status: done
type: step-review
task: audit-2026-07-24
step: 004
reviewed_by: auditer
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-07-25T22:15:00+02:00
verdict: issues
---

# Review Step 004: Punkt 18 — Gemeinsamer SQL-Char-Scanner als Primitive extrahieren

## Verdict

- [ ] **approved** — alle drei Prüfebenen ok
- [x] **issues** — Fix-Step `step-004/fix-01/` ist nötig (siehe Findings)
- [ ] **blocked** — Nutzer-Entscheidung nötig

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen umgesetzt
- [x] Rules-Konformität: `.agents/rules/**` eingehalten
- [x] Logische Korrektheit: **eine sicherheitsrelevante Verhaltensdivergenz gefunden** (siehe Findings #1)
- [x] Build: selbst nachgeprüft, 0/0 grün
- [x] Tests: selbst nachgeprüft, 375/375 grün

## Befund

### Plan-Erfüllung

| Plan-Punkt | Status | Bemerkung |
|---|---|---|
| Datei 1: `SqlCharScanner.cs` neu (interne statische Klasse, 5-State-Enum, Event-Struct, Scan-Methode, Transition-Mechanik) | **erfüllt** | Exakt wie spezifiziert. `internal static class SqlCharScanner`, `public enum SqlCharState { Normal, LineComment, BlockComment, SingleQuote, Bracket }`, `public readonly record struct SqlCharEvent(SqlCharState State, char Character, char Next, int Index)`, `IEnumerable<SqlCharEvent> Scan(string sql)`. |
| Datei 2: `SqlMultiStatementDetector.ContainsMultipleStatements` nutzt `SqlCharScanner.Scan` | **erfüllt** | Foreach über `SqlCharScanner.Scan(query)`, prüft `ev.State == Normal && ev.Character == ';'`. Trailing-Semikolon-Erkennung nutzt `query[(ev.Index + 1)..].TrimEnd()`. Klasse schrumpft von 74 auf 29 Zeilen. |
| Datei 3: `ReadOnlyGuard.StripCommentsAndStringLiterals` nutzt `SqlCharScanner.Scan` mit Schleifen-Logik aus Plan Z. 56–73 | **erfüllt mit semantischer Abweichung** | Code-Struktur entspricht der Vorlage. **Aber:** die semantische Wirkung unterscheidet sich vom Original — siehe Findings #1. |
| Datei 4: `SqlLiteralScanner.GetLiteralContentRanges` nutzt `SqlCharScanner.Scan` mit `Normal`→`SingleQuote` / `SingleQuote`→`Normal`-Übergangserkennung | **erfüllt** | Foreach über `SqlCharScanner.Scan(sql)`, prüft `previous != SingleQuote && ev.State == SingleQuote` (Literal-Start) und `previous == SingleQuote && ev.State != SingleQuote` (Literal-Ende). Range-Berechnung `ev.Index + 1` und `ev.Index - literalStart`. |
| Datei 5 (neu): `SqlCharScannerTests.cs` mit Tests aus Plan Z. 86–90 | **erfüllt** | 5 Testmethoden: `Scan_ShouldHandleEmptyInput`, `Scan_ShouldClassifyCommentAndLiteralStates` (Theory mit 5 InlineData), `Scan_ShouldHandleEscapedQuotesInsideLiterals`, `Scan_ShouldHandleNestedBlockCommentAndBracketEnd`, `Scan_ShouldReportNextCharAndOriginalChar`. |
| Bestehende Tests bleiben unverändert grün | **erfüllt** | `SqlLiteralScannerTests` (10), `ReadOnlyGuardTests` (28), `QueryExecutionServiceTests`, `QueryValidationServiceTests`, `QueryTokenResolverTests` (12) — alle grün. |
| `dotnet build SqlToAi.slnx` 0/0 | **erfüllt** | Selbst nachgeprüft. |
| `dotnet test --filter "Category!=Integration"` grün | **erfüllt** | Selbst nachgeprüft, 375/375. |
| Commit `bcdce979` mit Conventional-Commit-Message | **erfüllt** | Subject `refactor(database): extrahiere gemeinsamen SqlCharScanner aus drei State-Machine-Duplikaten` (96 Zeichen — **länger als die in `SqlToAiRichtlinien.mdc` Z. 64 empfohlenen 72 Zeichen**; siehe Findings #2). |
| `SqlToAi-baseline.json` SHA-256-Hashes aktualisiert | **erfüllt** | Alle 5 betroffenen Dateien haben korrekte Hashes (siehe Rules-Konformität). |
| **Abweichung 1: `string` statt `ReadOnlySpan<char>`** | **akzeptabel** | C#-Compiler-Fehler `CS4007` für `yield return` über `ReadOnlySpan<char>`-Parameter ist real. Call-Sites rufen `Scan(query)` statt `Scan(query.AsSpan())` auf, ein impliziter Allokationspunkt pro Aufruf. Bei SQL-Queries im einstelligen KB-Bereich irrelevant. |
| **Abweichung 2: `Character` statt `Char`** | **akzeptabel** | `CA1720` (Identifier enthält Typnamen) wird im Projekt mit `TreatWarningsAsErrors` zum Build-Fehler. Umbenennung ist die saubere Lösung. |
| **Abweichung 3: Bracket-Ausblendung in `ReadOnlyGuard`** | **NICHT akzeptabel** | Siehe Findings #1 — Verhaltensdivergenz mit Sicherheitsimpact. |

### Rules-Konformität

**`AiNetLinter.mdc`:**

| Regel | Bewertung | Beleg |
|---|---|---|
| `EnforceSealedClasses` | **eingehalten** | `SqlCharScanner` ist `internal static` (kein `sealed` nötig bei `static`). Andere Klassen unverändert: `ReadOnlyGuard` ist weiter `public sealed class` (ReadOnlyGuard.cs:12). |
| `Kurz-Stil` (Methodenlänge ≤60 Zeilen) | **eingehalten** | `Scan` ist 13 Zeilen (SqlCharScanner.cs:63-76), `Transition` 31 Zeilen (SqlCharScanner.cs:78-108), `TransitionFromNormal` 15 Zeilen (SqlCharScanner.cs:110-125). `StripCommentsAndStringLiterals` ist 19 Zeilen (ReadOnlyGuard.cs:57-77). `ContainsMultipleStatements` 15 Zeilen (SqlMultiStatementDetector.cs:13-29). `GetLiteralContentRanges` 23 Zeilen (SqlLiteralScanner.cs:18-42). Alle ≤60. |
| `EnforceNoSilentCatch` | **eingehalten** | `SqlCharScanner` hat kein `try/catch`. Die `IsQuerySafe`-Methode in `ReadOnlyGuard` behält das `try/catch (RegexMatchTimeoutException)` aus dem Original (ReadOnlyGuard.cs:39-48). |
| `EnforceNullableEnable` | **eingehalten** | Alle 5 geänderten Dateien beginnen mit `#nullable enable`. |
| `EnforcePascalCase` | **eingehalten** | `SqlCharState`, `SqlCharEvent`, `SqlCharScanner`, alle Properties in PascalCase. |
| `MaxLineCount` ≤500 | **eingehalten** | `SqlCharScanner.cs` 126 Zeilen, alle anderen refactor-Dateien geschrumpft. |
| `MaxPublicMembersPerType` ≤15 | **eingehalten** | `SqlCharState` (5 Werte) und `SqlCharEvent` (4 Properties) sind beide unter 15. |
| `MaxMethodParameterCount` ≤4 | **eingehalten** | `Transition` und `TransitionFromNormal` haben 4 Parameter (state, c, next, ref i) — am Limit aber ok. |

**`SqlToAiRichtlinien.mdc`:**

| Regel | Bewertung | Beleg |
|---|---|---|
| Conventional Commits, deutsch, imperativ | **eingehalten** | `refactor(database): extrahiere gemeinsamen SqlCharScanner aus drei State-Machine-Duplikaten` |
| Subject ≤72 Zeichen (implizit, aus dem 50-Zeichen-Limit in Standard-Convention; User-Memory und übliche Praxis) | **leicht verletzt** | Subject ist 96 Zeichen. **Siehe Findings #2.** |
| Kein Versionsbump in `SqlToAi.csproj` | **eingehalten** | Diff des Commits zeigt keine Änderung an `.csproj`. |
| Zero-Warning-Direktive | **eingehalten** | Build 0/0, selbst nachgeprüft. |
| `appsettings.json` für lokale Credentials, nicht hardcoded | **eingehalten** | Keine Änderungen an Konfigurations-Dateien im Diff. |
| `SqlToAi-baseline.json` SHA-256-Hashes aktualisiert | **eingehalten, korrekt** | Alle 5 betroffenen Datei-Hashes verifiziert (siehe unten). |
| Sprache englisch in Code/XML-Kommentaren | **eingehalten** | Alle XML-Kommentare in `SqlCharScanner.cs` sind englisch. |

**`SqlToAi-baseline.json` Verifikation (SHA-256):**

| Datei | Erwartet (baseline) | Tatsächlich (`Get-FileHash`) | OK? |
|---|---|---|---|
| `src/SqlToAi/Database/SqlCharScanner.cs` | `61b301138cfa4edecf3708a682d3ab1436a568cef8a7aea54cd7fe7207cc5cd6` | `61B301138CFA4EDECF3708A682D3AB1436A568CEF8A7AEA54CD7FE7207CC5CD6` | ✓ |
| `src/SqlToAi/Database/SqlMultiStatementDetector.cs` | `b2ac60cb7bbe9211afc56498699e2c2636531f0ddc1a42fb3f74f3dcf4b76007` | `B2AC60CB7BBE9211AFC56498699E2C2636531F0DDC1A42FB3F74F3DCF4B76007` | ✓ |
| `src/SqlToAi/Security/ReadOnlyGuard.cs` | `55c4d8a67a7f2d3d42abdb878fd28862b106ec7f924e00e732ec7a48dfcd229d` | `55C4D8A67A7F2D3D42ABDB878FD28862B106EC7F924E00E732EC7A48DFCD229D` | ✓ |
| `src/SqlToAi/Database/SqlLiteralScanner.cs` | `ded4b2a275f7ad4f6614b13ef2a295bd6aabf8479fd34871b37747ae0b7539f9` | `DED4B2A275F7AD4F6614B13EF2A295BD6AABF8479FD34871B37747AE0B7539F9` | ✓ |
| `tests/SqlToAi.Tests/Database/SqlCharScannerTests.cs` | `264d8fe7a805a2dfbb434c7b56e394098add0d6e87408023b4b35bec5c97e72e` | `264D8FE7A805A2DFBB434C7B56E394098ADD0D6E87408023B4B35BEC5C97E72E` | ✓ |

Alle 5 Hashes korrekt (case-insensitiv). Baseline-System ist intakt.

### Logische Korrektheit

**Sicherheitsrelevante Verhaltensdivergenz in `ReadOnlyGuard` (Details in Findings #1):**

Kurz: Die `Bracket`-State-Behandlung in `SqlCharScanner` ist mechanisch korrekt und für `SqlMultiStatementDetector` und `SqlLiteralScanner` semantisch unverändert. **Aber** für `ReadOnlyGuard.StripCommentsAndStringLiterals` ist die Wirkung eine andere als vor dem Refactor:

- **Original-`ReadOnlyGuard`** hatte nur 4 States (Normal/LineComment/BlockComment/SingleQuote). `[id]` wurde zeichenweise im Normal-State verarbeitet und durchgereicht. Der Mutating-Regex sah also `[insert]` und matchte `insert` (Wortgrenzen an `[` und `]`).
- **Neuer Code** nutzt den 5-State-Scanner. `[id]` wird als `Bracket`-State emittiert und im Strip-Loop **übersprungen**. Der Mutating-Regex sieht nur `]` und matcht nichts.

**Beleg:** Direkt-Test gegen die gebaute `ReadOnlyGuard.dll`:

| Query | Original-Verhalten | Neues Verhalten | Divergenz |
|---|---|---|---|
| `SELECT [insert] FROM t` | `IsQuerySafe = false` (regex matcht) | `IsQuerySafe = true` (regex matcht nicht) | **JA — Sicherheitsrelevanz** |
| `SELECT [drop] FROM t` | `false` | `true` | **JA** |
| `SELECT * FROM [delete]` | `false` | `true` | **JA** |
| `SELECT [update] FROM t WHERE [truncate] = 1` | `false` | `true` | **JA** |
| `INSERT INTO [insert] VALUES (1)` | `false` | `false` | nein (außerhalb des Brackets matcht `INSERT`) |
| `DELETE FROM [delete]` | `false` | `false` | nein (außerhalb matcht `DELETE`) |

**Mechanik des Scanners verifiziert:** `SqlCharScanner.Transition` und `TransitionFromNormal` sind bit-identisch zu den Originalen in `SqlMultiStatementDetector.Transition/TransitionFromNormal` (Stand `2cfedb5^`) und zu `SqlLiteralScanner.Transition/TransitionFromNormal` (Stand `2cfedb5^`). Verifiziert per `git show 2cfedb5^:src/SqlToAi/Database/SqlLiteralScanner.cs` (102 Zeilen) und `git show 2cfedb5^:src/SqlToAi/Database/SqlMultiStatementDetector.cs` (98 Zeilen).

**Multi-Statement-Detector Trailing-Semikolon-Logik:** `query[(ev.Index + 1)..].TrimEnd()` ist semantisch äquivalent zur alten `query[(i + 1)..].TrimEnd()` (da `ev.Index == i` per Konstruktion). Stichprobe `SELECT 1;SELECT 2`: zwei Statements, Scanner meldet `;` an Index 8 im Normal-State, `query[9..].TrimEnd() = "SELECT 2"` (Length > 0) → return true. ✓

**`SqlLiteralScanner.GetLiteralContentRanges`:** Forward-Edge-Cases (verschachtelte Block-Comments, Escaped Quotes, Bracket-Inhalte) durch die bestehenden Tests abgedeckt (`SqlLiteralScannerTests.GetLiteralContentRanges_ShouldIgnoreContentInsideBracketIdentifiers`, `_ShouldHandleEscapedQuotes_AsLiteralContent`, `_ShouldIgnoreContentInsideBlockComments`, `_ShouldIgnoreContentInsideLineComments`).

**Edge-Cases die nicht explizit vom Plan verlangt aber sinnvoll wären:**

- Performance: kein Microbenchmark — Test-Suite 375 Tests in ~11s, davon SqlCharScanner-Tests <100ms. Kein Handlungsbedarf.
- Unicode in Literalen: Scanner ist byte-für-byte (char-für-char). `'日本語'` würde als 3 SingleQuote-Char-Events erkannt. Kein Bug, aber Verhalten nicht getestet.
- Backticks: Scanner behandelt Backticks als Plain-Char (Normal-State). SQL Server kennt keine Backticks, kein Issue.

**Test-Coverage-Lücke:** Die `ReadOnlyGuardTests` enthalten **keinen** Test mit Bracket-Identifier-Queries. Die Lücke besteht seit dem ursprünglichen Anlegen der Tests und wird durch diesen Refactor sichtbar, weil das Verhalten sich für diese Klasse von Queries ändert. Der Refactor deckt die Lücke nicht auf, sondern **erzeugt** eine Regression in diesem Pfad.

### Build-Status

```
dotnet build SqlToAi.slnx
→ Der Buildvorgang wurde erfolgreich ausgeführt.
  0 Warnung(en)
  0 Fehler
  Verstrichene Zeit 00:00:05.34
```

Selbst nachgeprüft um 2026-07-25T22:10.

### Test-Status

```
dotnet test --filter "Category!=Integration" --nologo
→ Bestanden! Fehler: 0, erfolgreich: 375, übersprungen: 0, gesamt: 375, Dauer: 13 s
```

Selbst nachgeprüft um 2026-07-25T22:10. Test-Count stimmt mit Coder-Ergebnis überein. **Aber:** kein Test deckt den Bracket-Identifier-Fall in `ReadOnlyGuard` ab (siehe Findings #1).

## Findings (bei `issues`)

### Finding #1 — Sicherheitsrelevante Verhaltensdivergenz in `ReadOnlyGuard` (KRITISCH)

**Datei:** `src/SqlToAi/Security/ReadOnlyGuard.cs:57-78` (Methode `StripCommentsAndStringLiterals`)

**Was:** Der Refactor hat den `SqlCharScanner` mit 5 States (inkl. `Bracket`) eingeführt. `StripCommentsAndStringLiterals` hat in der `else`-Branch nur `Normal` und `SingleQuote` + `'` explizit behandelt; alle anderen States (inkl. `Bracket`) werden implizit übersprungen. Im **Original**-`ReadOnlyGuard` gab es keinen `Bracket`-State, daher wurden `[id]`-Inhalte als Plain-Text durchgereicht und vom Mutating-Regex gesehen.

**Auswirkung:** Queries, die Mutating-Keyword-ähnlichen Inhalt **innerhalb** von Bracket-Identifiern `[...]` enthalten, werden vom Read-Only-Guard nicht mehr abgewiesen. Konkret mit der aktuellen Implementierung:

- `SELECT [insert] FROM t` → vorher `IsQuerySafe=false` (regex matcht `insert`), jetzt `true` (regex matcht nichts) → Query **wird ausgeführt**, vorher abgewiesen.
- Analog: `SELECT [drop] FROM t`, `SELECT * FROM [delete]`, `SELECT [update] FROM t WHERE [truncate] = 1` — alle vorher `false`, jetzt `true`.

Da `SqlCharScanner` für `SqlMultiStatementDetector` und `SqlLiteralScanner` schon vorher einen `Bracket`-State hatte und dort auch semantisch korrekt verwendet wurde, ist der Fehler **nicht** im Scanner, sondern in der Art, wie `ReadOnlyGuard` den Scanner konsumiert.

**Coder's Begründung im `step-result.md` Z. 134:**
> Semantisch ohne Auswirkung auf das Regex-Ergebnis (Mutating-Keywords in echten Identifiern greifen nicht, wurden aber testseitig in keiner der bestehenden Test-Cases geprüft).

Diese Aussage ist **falsch**: `\[` und `]` sind im .NET-Regex keine Wortzeichen, daher bilden sie Wortgrenzen für `\b...\b`. `[insert]` matcht das Pattern `insert` (Wortgrenzen an Index 0 und 6). Dies wurde mit einem isolierten Test-Programm verifiziert (siehe Befund-Block oben).

**Fix-Vorschlag:** Der `Bracket`-Inhalt muss im `ReadOnlyGuard.StripCommentsAndStringLiterals` an die Regex weitergereicht werden, sodass das Verhalten bit-identisch zum Original ist. Konkret: in `ReadOnlyGuard.cs:60-75` einen zusätzlichen `else if` für `Bracket`-State einfügen, der `ev.Character` unverändert an `sb` anhängt. Die `Strip`-Semantik wird dann:
- `Normal`: durchreichen (wie bisher)
- `SingleQuote` + `'` : Whitespace (wie bisher)
- `Bracket`: durchreichen (neu, entspricht dem Original-Verhalten vor dem Refactor)
- `LineComment`, `BlockComment`, sonstige: skip (wie bisher)

**Alternative Fix-Optionen:**
- Eine zweite `Scan`-Methode im Scanner anbieten, die nur die für `ReadOnlyGuard` relevanten 4 States liefert (kein `Bracket`-State). Dann wäre der Strip-Loop-Code identisch zum Plan und der `Bracket`-Inhalt würde automatisch als `Normal` durchgereicht. Diese Variante hält den Scanner generisch und ReadOnlyGuard-spezifisch.

**Zusätzlich (Pflicht für Fix-Step):** Mindestens 2 `ReadOnlyGuardTests`-Cases ergänzen, die Bracket-Identifier mit mutating-keyword-ähnlichem Inhalt enthalten — einer der `true` erwartet (z. B. `[insert]`-Spalte in einem harmlosen SELECT, wenn gewünscht) und einer der `false` erwartet (das aktuelle Verhalten ist `true`, Fix muss bestätigen dass die Erwartung jetzt stabil ist). Ohne diese Tests ist Findings #1 jederzeit wieder reaktivierbar.

### Finding #2 — Commit-Subject > 72 Zeichen (geringfügig)

**Datei:** `bcdce979` (Commit-Message Subject)

**Was:** `refactor(database): extrahiere gemeinsamen SqlCharScanner aus drei State-Machine-Duplikaten` ist **96 Zeichen** lang.

**Auswirkung:** Geringfügige Verletzung der User-üblichen Konvention "Subject ≤72 Zeichen". `SqlToAiRichtlinien.mdc` schreibt dies nicht explizit vor, aber es ist Branchen-Standard und im User-Memory als Vorliebe dokumentiert. Kein Build-Issue, aber unsauber.

**Fix-Vorschlag:** Subject kürzen auf z. B. `refactor(database): extrahiere gemeinsamen SqlCharScanner`. Der Body erklärt den Rest. (Nur falls der Commit noch nicht auf origin gepusht ist — laut `step-result.md` Z. 75 ist er lokal; ein rebase/Squash wäre möglich. Falls schon gepusht, kein Muss-Fix, nur Notiz.)

### Bewertung der drei dokumentierten Abweichungen aus dem Plan

1. **`string` statt `ReadOnlySpan<char>`:** **akzeptabel.** `CS4007` ist real. Ein allokations-ärmer Span-basierter Iterator wäre möglich, ist aber explizit nicht im Plan-Scope (Plan Z. 136: „Performance-Verlust ist bei SQL-Queries im einstelligen KB-Bereich irrelevant"). Die `query.AsSpan()` → `query`-Vereinfachung an den Call-Sites ist harmlos.

2. **`Character` statt `Char`:** **akzeptabel.** `CA1720` + `TreatWarningsAsErrors` macht jede andere Lösung zu einem Build-Bruch. Saubere Umbenennung ist die richtige Wahl. Coder hat konsequent alle drei Call-Sites und die Tests mit angepasst — keine Inkonsistenzen.

3. **Bracket-Inhalt wird in `ReadOnlyGuard` jetzt ausgeblendet:** **NICHT akzeptabel** — siehe Findings #1. Der Coder hat die Verhaltensänderung explizit dokumentiert und korrekt erkannt, dass keine Tests sie abdecken. Die Folgerung „semantisch ohne Auswirkung" ist jedoch falsch. Bracket-Inhalt in `ReadOnlyGuard` muss durchgereicht werden, damit die Regex-Wortgrenzen an `[` und `]` greifen können.

## Frage an Nutzer (bei `blocked`)

(nicht zutreffend — Verdict ist `issues`)

## Sonstige Beobachtungen (nicht als Issues zu werten)

- **Methodenlänge Beobachtung:** Der `Scan`-Methodenkörper ist 13 Zeilen + Generator-Pattern — gut. `Transition` ist 31 Zeilen mit 4 States + 1 `default` — am unteren Ende des 60-Zeilen-Limits. Wenn der Scanner künftig weitere States (z. B. `Backtick`, `NationalStringLiteral` `N'...'`) aufnehmen soll, sollte `Transition` in `StateToHandler`-Strategie oder eine Tabelle zerlegt werden. Nicht im Scope dieses Steps.
- **`Next`-Property wird im Scanner-Output mitgeführt, aber von keiner Call-Site verwendet:** `SqlMultiStatementDetector`, `ReadOnlyGuard` und `SqlLiteralScanner` greifen nur auf `ev.State`, `ev.Character` und `ev.Index` zu. Die `Next`-Property ist nur durch den Test `Scan_ShouldReportNextCharAndOriginalChar` dokumentiert. Sie ist nützlich, weil sie den Scanner selbst vollständig spezifiziert (kein "magic" über `next = sql[i+1]` in `Transition`), und der zukünftige `Bracket`-Fix in `ReadOnlyGuard` braucht sie nicht — aber sie sollte in Erwägung gezogen werden, wenn die API in einem späteren Refactor komprimiert wird. Nicht im Scope dieses Steps.
- **Scan-Methode allokiert einen `IEnumerator`:** Bei `yield return` wird für jeden Aufruf ein State-Machine-Objekt allokiert. Bei `SqlLiteralScanner` (Token-Resolver-Pfad, mehrere Literale pro Query, oft aufgerufen) ist das pro Call ein zusätzliches Heap-Objekt. Wenn das jemals ein Hot-Path-Problem wird, ist ein `ref struct`-Iterator die Lösung — aber explizit nicht im Plan-Scope. Beobachtung für den globalen Audit.
- **`SqlCharState` ist `public`, der Scanner selbst ist `internal`:** Asymmetrie. Wenn der Scanner `internal` ist, warum ist der Enum `public`? Im Test-Projekt ist `InternalsVisibleTo` wahrscheinlich gesetzt, sonst wäre der Test rot. Beide Lesarten (Enum public für externe Konsumenten, oder Enum public weil es im Test gebraucht wird) sind vertretbar, aber `public enum` mit `internal static class` ist eine Design-Inkonsistenz. Nicht im Scope, aber auffällig.
- **Test-Datei `SqlCharScannerTests.cs` verwendet `Assert.Single(... , predicate)`:** xUnit v3 API. Konsistent mit dem Rest des Test-Projekts. Kein Issue.
- **Doku-Commit `3a8aa23` korrekt:** Setzt `step-plan.md` Status auf `done (pending audit)` und schreibt `step-result.md`. Body referenziert den Code-Commit-Hash. Conventional-Commit-konform.

## Sonderpunkt: Bewertung der drei dokumentierten Abweichungen

Siehe oben — Finding #1 (Sicherheitsrelevanz) blockiert den Approve. Findings #2 ist niedrig-prior, kann optional gefixt werden. Findings #1 + ergänzende Tests sind der minimale Fix-Step.
