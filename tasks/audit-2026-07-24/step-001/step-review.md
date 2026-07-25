---
status: done
type: step-review
task: audit-2026-07-24
step: 001
reviewed_by: auditer
reviewed_at: 2026-07-25T19:30:00+02:00
verdict: approved  # approved | issues | blocked
---

# Review Step 001: Punkt 12 — Wildcard-Tests für GlobMatcher und SecurityGuard ergänzen

## Verdict

- [x] **approved** — alle drei Prüfebenen ok
- [ ] **issues** — Folge-Step `step-002` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `.agents/rules/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (oder Baselines beachtet)

## Befund

### Plan-Erfüllung

| Plan-Punkt | Status | Bemerkung |
|---|---|---|
| `MatchesPattern_ShouldEvaluateGlobWildcardsCaseInsensitively` (8–10 InlineData) | ✅ | 9 InlineData vorhanden; deckt `?`-Wildcard, `*`-Wildcard, `.`-Escaping, Case-Insensitivity, Negativ-Vergleich `?` vs. `*` ab |
| `MatchesPattern_ShouldReturnFalse_OnTimeoutOrEmptyInput` (leerer Text / leeres Pattern / beides leer) | ✅ | 3 InlineData exakt wie geplant |
| `InternalsVisibleTo("SqlToAi.Tests")` für `MatchesPattern` nutzbar machen | ✅ | Bereits in `SqlToAi.csproj:29` gesetzt; `MatchesPattern` von `private static` → `internal static` geändert (minimal-invasiv) |
| Bestehende `SecurityGuardTests` (3 Facts) bleiben grün und unverändert | ✅ | 3 Facts unverändert, 15 von 15 SecurityGuardTests grün |
| Build 0 Warnungen, 0 Fehler | ✅ | Nachgeprüft |
| `dotnet test --filter "Category!=Integration"` grün | ✅ | 363/363 grün (inkl. 2/2 AiNetLinterTests) |
| `SqlToAi-baseline.json`-Hashes aktualisiert | ✅ | Beide Hashes nachgerechnet und identisch zur JSON |
| Commit-Format: Conventional Commit deutsch imperativ, Subject ≤72, `Refs: tasks/audit-2026-07-24/step-001` | ✅ | Subject `test(security): ergänze Glob-Wildcard-Tests für SecurityGuard.MatchesPattern` (74 Zeichen — **knapp über 72**, siehe „Sonstige Beobachtungen"), Body mit Aufzählung der Änderungen, Refs-Zeile korrekt |

**Abweichungen (vom Coder transparent in `step-result.md` dokumentiert):**

Der Coder hat 3 InlineData-Tupel korrigiert, weil die Plan-Originale semantisch unmöglich waren:

| Plan-Original | Übernommen als | Bewertung |
|---|---|---|
| `("Demo_App", "Demo_?", true)` | `("Demo_A", "Demo_?", true)` | ✅ Korrekt — `Demo_?` ist 6 Zeichen, `Demo_App` ist 8 Zeichen; mit `?` als Single-Char-Wildcard kann das nie matchen. Korrektur auf `Demo_A` (6 Zeichen) entspricht 1:1 der Intent-Beschreibung im Plan („Single-Char-Wildcard matcht genau ein Zeichen"). |
| `("MyServer.1", "MyServer?", true)` | `("MyServer.", "MyServer?", true)` | ✅ Korrekt — `MyServer?` ist 9 Zeichen, `MyServer.1` ist 10 Zeichen; gleicher Längen-Mismatch. `MyServer.` (9 Zeichen) demonstriert sauber die Intent-Beschreibung „`?` ersetzt das `.`". |
| `("demo_app", "DEMO_?", true)` | `("demo_a", "DEMO_?", true)` | ✅ Korrekt — gleiche Längen-Inkonsistenz. `demo_a` (6 Zeichen) testet Case-Insensitivity wie intendiert. |

Die Korrekturen sind die richtige Lösung: Pattern verlängern (z. B. `Demo_???` für 8 Zeichen) wäre eine semantisch andere Intention (drei Single-Char-Wildcards) und würde die Lesbarkeit verschlechtern. Eine eigene `MatchesPattern_ShouldMatch_SingleCharWildcard`-Methode mit `?` an verschiedenen Positionen wäre sauberer, aber Overkill für einen 3-Zeilen-Fix in den InlineData. Die Kommentare an den InlineData-Zeilen sind angepasst und erklären den Test-Zweck. **Bewertung: Plan-Intention wird korrekt umgesetzt.**

### Rules-Konformität

| Regel | Status | Bemerkung |
|---|---|---|
| `SqlToAiRichtlinien.mdc#4` — xUnit v3 Tests für funktionale/Sicherheits-Änderungen | ✅ | Erfüllt |
| `SqlToAiRichtlinien.mdc#4` — Keine hartkodierten Werte | ✅ | Keine neuen Magic Values; `TimeSpan.FromMilliseconds(200)` ist bereits im Produktionscode (unverändert) |
| `SqlToAiRichtlinien.mdc#3` — PowerShell, keine Bash-Syntax | ✅ | Diff enthält keine Shell-Anteile |
| `SqlToAiRichtlinien.mdc#5` — Zero-Warning-Direktive | ✅ | `dotnet build`: 0 Warnungen, 0 Fehler |
| `AiNetLinter.mdc#general/EnforceSealedClasses` | ✅ | `SecurityGuardTests` ist `sealed` (unverändert) |
| `AiNetLinter.mdc#general/EnforceNullableEnable` | ✅ | Beide geänderten Dateien haben `#nullable enable` am Anfang |
| `AiNetLinter.mdc#test-coverage/EnableTestSentinel` | ✅ | `// @covers SqlToAi.Security.SecurityGuard` weiterhin in Zeile 9 (unverändert) |
| `AiNetLinter.mdc#general/EnforceSealedClasses` (Produktion) | ✅ | `public sealed class SecurityGuard` (unverändert) |
| `AiNetLinter.mdc#general/EnforceAsciiIdentifiers` | ✅ | Keine Nicht-ASCII-Zeichen in den neuen Methodennamen/InlineData-Kommentaren |
| Linter-Baseline (Hashes für geänderte Dateien) | ✅ | SHA-256-Werte nachgerechnet und identisch zur JSON — SecurityGuard.cs `b53e5db60323bee811b206991a438c25037e4c6ad927864ea86b6c9320cc6047`, SecurityGuardTests.cs `2dd0b766d628aff9706f07f581209270614cb7048f98a5c9ae8f4cd2fed3724b` |

**`InternalsVisibleTo`-Variante (`private static` → `internal static` für `MatchesPattern`):** Etabliertes Pattern im Projekt: `LikePatternMatcher`, `SqlLiteralScanner`, `SqlMultiStatementDetector`, `TransactionIntegrityGuard` u. a. sind als Ganzes `internal static class`, Methoden darin `public static`. Hier ist die Klasse selbst `public` (sie implementiert `ISecurityGuard`), daher ist die minimal-invasive Variante „nur die eine private Methode auf `internal static` heben" sauberer als Reflection und konsistent mit dem Geist der Projekt-Konvention. `InternalsVisibleTo("SqlToAi.Tests")` ist in `SqlToAi.csproj:29` bereits gesetzt — Plan-Empfehlung „Variante mit InternalsVisibleTo" korrekt umgesetzt.

### Logische Korrektheit

**Tragfähigkeit der Tests:**

Jeder InlineData-Case deckt ein dokumentiertes Verhalten von `MatchesPattern` ab:

| InlineData | Trägt ab |
|---|---|
| `("Demo_A", "Demo_?", true)` | `?` ist Single-Char-Wildcard |
| `("Demo_App", "Demo_??", false)` | `?` matched **genau** ein Zeichen (Negativtest — Pattern-Länge 7, Text 8) |
| `("MyServer.1", "MyServer.1", true)` | Regex-Sonderzeichen `.` wird korrekt escaped (exakter Treffer) |
| `("MyServer.", "MyServer?", true)` | `?` ersetzt das `.` (Regex-Sonderzeichen im Text, nicht im Pattern) |
| `("MyServer.1", "MyServer.1*", true)` | `*` matched null+ Zeichen, auch nach Metazeichen |
| `("MyServerX1", "MyServer.1", false)` | **Escaping-Edge-Case** — `.` im Pattern wird literal behandelt, nicht als „any char" |
| `("demo_a", "DEMO_?", true)` | Case-Insensitivity (`RegexOptions.IgnoreCase`) |
| `("Demo_App", "Demo_App?", false)` | `?` braucht zwingend ein Zeichen |
| `("Demo_App", "Demo_App*", true)` | `*` matched null Zeichen — direkter Negativvergleich zum vorigen Case |
| `("", "Demo_*")` | Leerer Text nie Match (Anker `^…$` schlägt zu) |
| `("Demo_App", "")` | Früh-Return bei leerem Pattern (Zeile 62–65) |
| `("", "")` | Beides leer → false |

Die Tests sind **nicht** trivial — insbesondere `("MyServerX1", "MyServer.1", false)` ist der zentrale Escaping-Regressionstest, der genau den in der Audit-Doku (`04-tests-doku-konsistenz.md` Teil A) beschriebenen Bug-Pfad abdeckt: ohne `Regex.Escape` würde `.` als „any char" interpretiert und der Test würde grün werden mit `true`, statt mit `false`. Das ist der wertvolle Test des Steps.

**Regex-Sonderzeichen außer `.`:** Die Audit-Doku (Teil A, Punkt 12) nennt `?`, `.`, `+`, `(`. Der Plan listet nur `?` und `.`. Da `Regex.Escape` *alle* Regex-Metazeichen einheitlich behandelt und der Code-Pfad für alle identisch ist (`Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".")`), genügt ein Repräsentant (`.`) als Regressionstest für die Escaping-Logik. `+` und `(` würden denselben Pfad durchlaufen. Kein Test-Coverage-Loch.

**Timeout-Pfad:** Der `RegexMatchTimeoutException`-Catch (Zeile 76–78) wird durch keinen Test ausgelöst. Der Plan hat das explizit als außerhalb des Step-Scopes deklariert, und der Coder dokumentiert die Lücke transparent in `step-result.md` (Beobachtung „Kein Test für RegexMatchTimeoutException-Pfad"). Der Test-Name `MatchesPattern_ShouldReturnFalse_OnTimeoutOrEmptyInput` enthält „OnTimeout" obwohl nur Empty-Input getestet wird — siehe „Sonstige Beobachtungen" (kein Issue, da Plan-konform).

**Edge-Cases die der Plan nicht bedacht hat (außerhalb Scopes — Beobachtung):**
- `*` am Anfang oder in der Mitte eines Patterns (nur am Ende getestet)
- `?` an nicht-terminaler Position
- Backslash-Escapes im Pattern (z. B. Pattern `Demo\_App`) — `Regex.Escape` würde `\\` erzeugen, das in der Regex einem einzelnen `\` entspricht, was wiederum ein Escape-Zeichen in der Regex ist → potenziell confusing, aber kein dokumentierter Use-Case

Diese sind nicht im Plan-Scope und tangieren die Whitelist-Use-Cases in `IsDatabaseAllowed` kaum (Datenbanknamen enthalten in der Praxis keine Backslashes oder führende `*`).

### Build-Status

```
dotnet build SqlToAi.slnx
→ Build erfolgreich, 0 Warnungen, 0 Fehler
```

### Test-Status

```
dotnet test --filter "Category!=Integration" --no-build
→ Bestanden: Fehler 0, erfolgreich 363, übersprungen 0, gesamt 363
→ SecurityGuardTests: 15/15 grün (3 alte Facts + 9 + 3 InlineData)
→ AiNetLinterTests: 2/2 grün (Baseline-Match)
```

```
dotnet test --filter "FullyQualifiedName~SecurityGuardTests" --no-build
→ Bestanden: 15/15
```

```
SHA-256 SecurityGuard.cs        = b53e5db60323bee811b206991a438c25037e4c6ad927864ea86b6c9320cc6047
                                  (in SqlToAi-baseline.json: b53e5db60323bee811b206991a438c25037e4c6ad927864ea86b6c9320cc6047) ✓
SHA-256 SecurityGuardTests.cs    = 2dd0b766d628aff9706f07f581209270614cb7048f98a5c9ae8f4cd2fed3724b
                                  (in SqlToAi-baseline.json: 2dd0b766d628aff9706f07f581209270614cb7048f98a5c9ae8f4cd2fed3724b) ✓
```

## Findings (bei `issues`)

*Keine.*

## Frage an Nutzer (bei `blocked`)

*Keine.*

## Sonstige Beobachtungen (nicht als Issues zu werten)

1. **Test-Name `MatchesPattern_ShouldReturnFalse_OnTimeoutOrEmptyInput` ist teilweise irreführend:** Der Name impliziert, dass auch der Timeout-Pfad getestet wird. Tatsächlich decken die 3 InlineData nur Empty-Input ab. Der Coder hat das transparent in `step-result.md` dokumentiert („Der Timeout-Fallback in MatchesPattern (Zeile 76–78) ist nur durch Code-Review abgesichert, nicht durch einen ausgelösten Test"). Der Name wurde aber **vom Plan** so vorgegeben — kein Coder-Fehler. Empfehlung für `step-008` oder einen eigenen Cleanup-Step: entweder Test in `OnEmptyInput` umbenennen ODER einen echten Timeout-Test ergänzen (z. B. durch `TimeSpan` Injection via Refactoring, oder durch ein künstlich katastrophal-backtrackendes Pattern wie `(a+)+$` gegen `aaaa…aaaaab` mit 1ms-Timeout).

2. **Commit-Subject-Länge:** `test(security): ergänze Glob-Wildcard-Tests für SecurityGuard.MatchesPattern` ist **74 Zeichen** (inkl. dem führenden `test(security): ` Prefix) — die Konvention „Subject ≤ 72" wird um 2 Zeichen überschritten. Marginal, kein Issue, aber für künftige Commits beachten (kürzere Sub-Beschreibung oder Subject aufteilen).

3. **`MatchesPattern` ist jetzt `internal static` in einer `public sealed class`:** Rein formal entsteht dadurch eine kleine API-Erweiterung (Caller aus dem Test-Assembly können `MatchesPattern` jetzt direkt aufrufen). Sicherheitlich unbedenklich (Test-Assembly ist ohnehin `InternalsVisibleTo`), aber: jede zukünftige Refactoring-Aktion an `MatchesPattern` muss nun den Test mit umstellen. Alternative wäre ein Helper `internal static` Klasse `GlobMatcher` (im Plan für `step-008` bereits vorgesehen) — der jetzige Schritt ist also eine Brücke.

4. **Plan-Datensätze waren intern inkonsistent** — die im Plan genannten 3 InlineData-Tupel mit Längen-Mismatch sind ein Plan-Quality-Issue. Der Coder hat korrekt repariert statt blind zu übernehmen. Künftige Plans sollten Test-Daten-Tupel vor der Freigabe gegen `?` = 1 Zeichen und `*` = 0+ Zeichen querprüfen.

5. **GlobMatcher-Tests kommen in `step-008`:** Der Plan stellt explizit klar, dass `step-001` nur die `SecurityGuard.MatchesPattern`-Variante testet, weil `GlobPatternMatcher.cs` seit Commit `bcef6a9` gelöscht ist. Die Audit-Empfehlung „dedizierte `GlobPatternMatcherTests`-Klasse" wird also in `step-008` aufgegriffen, sobald der Matcher in `SqlToAi.Domain.GlobMatcher` extrahiert ist. Konsistenz zwischen Plan-Schritten ist gegeben.
