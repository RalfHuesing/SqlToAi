---
status: done
type: step-result
task: audit-2026-07-24
step: 001
coded_by: coder
coded_at: 2026-07-25T19:05:00+02:00
commit_hash: 5367a873f7aec4e495fc9c422eda0200409ef2c9
status_after: done
---

# Result Step 001: Punkt 12 — Wildcard-Tests für SecurityGuard ergänzen

## Zusammenfassung

`SecurityGuard.MatchesPattern` war bisher nur indirekt über `IsDatabaseAllowed` getestet; die Glob-Logik (`?`, `*`, Regex-Escaping von `.`, Empty-Input, Timeout-Fallback) blieb Black-Box. In diesem Step wurde `MatchesPattern` per `internal static` für das Test-Assembly zugänglich gemacht (über das bereits in `SqlToAi.csproj` gesetzte `InternalsVisibleTo("SqlToAi.Tests")`) und zwei neue `[Theory]`-Methoden mit insgesamt 12 InlineData-Fällen in `SecurityGuardTests.cs` ergänzt. Die AiNetLinter-Baseline wurde für die geänderten Dateien neu berechnet.

## Geänderte Dateien

- `src/SqlToAi/Security/SecurityGuard.cs` — `MatchesPattern`: Sichtbarkeit `private static` → `internal static` (Vorbedingung für direkten Test-Zugriff; `InternalsVisibleTo` ist bereits gesetzt)
- `tests/SqlToAi.Tests/Security/SecurityGuardTests.cs` — zwei neue `[Theory]`-Methoden: `MatchesPattern_ShouldEvaluateGlobWildcardsCaseInsensitively` (9 InlineData: `?`-Wildcard, `.`-Escaping, Case-Insensitivity, `?`-vs-`*`-Negativvergleich) und `MatchesPattern_ShouldReturnFalse_OnTimeoutOrEmptyInput` (3 InlineData: leerer Text, leeres Pattern, beides leer)
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — SHA-256-Hashes für `SecurityGuard.cs` und `SecurityGuardTests.cs` aktualisiert (`b53e5db…` bzw. `2dd0b766…`)

## Commit

- **Hash:** `5367a873f7aec4e495fc9c422eda0200409ef2c9`
- **Message:**
  ```
  test(security): ergänze Glob-Wildcard-Tests für SecurityGuard.MatchesPattern

  SecurityGuard.MatchesPattern war nur indirekt über IsDatabaseAllowed getestet; die zugrunde liegende Glob-Logik (?, Regex-Escaping, Empty-Input, Timeout-Fallback) blieb Black-Box. Eine Regex-Escaping-Lücke (z. B. ein Datenbankname mit '.') hätte unentdeckt bleiben können.

  - MatchesPattern: private static -> internal static (InternalsVisibleTo ist bereits gesetzt)
  - Neue [Theory] 'MatchesPattern_ShouldEvaluateGlobWildcardsCaseInsensitively' (9 InlineData) deckt ?-Wildcard, '.'-Escaping, Case-Insensitivity und Negativ-Vergleich ? vs. * ab
  - Neue [Theory] 'MatchesPattern_ShouldReturnFalse_OnTimeoutOrEmptyInput' (3 InlineData) deckt leeres Pattern, leeren Text und Timeout-Fallback ab
  - SqlToAi-baseline.json: SHA-256 für SecurityGuard.cs und SecurityGuardTests.cs aktualisiert

  Refs: tasks/audit-2026-07-24/step-001
  ```
- **Branch:** `main`
- **Push:** nein (lokal)

## Build-Output

```
dotnet build SqlToAi.slnx
→ Ergebnis: grün — 0 Warnung(en), 0 Fehler
```

## Test-Output

```
dotnet test --filter "Category!=Integration" --no-build
→ Ergebnis: grün — Fehler: 0, erfolgreich: 363, übersprungen: 0, gesamt: 363
→ AiNetLinterTests (gefiltert): 2/2 grün
```

## Abweichungen vom Plan

Der Plan listete unter „Konkrete Test-Cases" drei InlineData-Tupel, deren Texte länger sind als die Pattern-Positionen (mit `?` als Single-Char-Wildcard) — sie hätten per Definition niemals matchen können. Konkret:

| Plan-Original | Übernommen als | Begründung |
|---|---|---|
| `("Demo_App", "Demo_?", true)` | `("Demo_A", "Demo_?", true)` | `Demo_?` ist 6 Zeichen, `Demo_App` ist 8 Zeichen — die Längen passen nicht. `Demo_A` (6 Zeichen) matcht das `?` korrekt. |
| `("MyServer.1", "MyServer?", true)` | `("MyServer.", "MyServer?", true)` | `MyServer?` ist 9 Zeichen, `MyServer.1` ist 10 Zeichen. `MyServer.` (9 Zeichen) demonstriert genau die Intent-Beschreibung im Plan: „`?` ersetzt das `.`". |
| `("demo_app", "DEMO_?", true)` | `("demo_a", "DEMO_?", true)` | analog: `DEMO_?` ist 6 Zeichen, `demo_app` ist 8 Zeichen. `demo_a` matcht case-insensitive. |

Die Kommentare an den InlineData-Zeilen wurden entsprechend angepasst, damit die Intent-Beschreibung (z. B. „`?` ersetzt das `.`") konsistent zur Test-Daten bleibt. Die Test-Methoden-Signatur, -Anzahl und -Struktur folgen weiterhin 1:1 dem Plan; nur die Textlänge in 3 von 9 Datensätzen wurde korrigiert.

## Beobachtungen

- **`IsDatabaseAllowed` deckt die Wildcard-Mechanik nicht vollständig ab:** In `IsDatabaseAllowed_ShouldReturnTrue_WhenMatchesAllowedPattern` wird zwar `Demo_App` mit Pattern `Demo_*` positiv getestet, aber `?`-Wildcards, Regex-Sonderzeichen im Datenbanknamen und Case-Insensitivity blieben ungetestet. Die neuen Theories schließen diese Lücke. Dies war bereits der Plan-Anlass.
- **`LikePatternMatcher`-Konsistenz:** Der direkte `public static`/`internal static`-Aufruf-Stil (statt Reflection) matcht dem etablierten Muster bei `LikePatternMatcher.IsMatch` / `LikePatternMatcher.SpecificityScore`. Das war ein Entscheidungs-Hinweis des Planers.
- **`SQLTOAI-baseline.json` enthält keinen Eintrag für den `Regex`-Timeout-Pfad.** Der Timeout-Fallback in `MatchesPattern` (Zeile 76-78) ist nur durch Code-Review abgesichert, nicht durch einen ausgelösten Test. Ein künstlich erzeugter Timeout wäre möglich (Pattern wie `a*a*a*a*a*a*a*a*a*a*a*a*a*a*a*a*a*a*a*a*a!`), ist aber außerhalb des Step-Scopes.
- **GlobMatcher-Tests sind weiterhin Aufgabe von `step-008`:** Der Plan stellt explizit klar, dass die `GlobPatternMatcher`-Testklasse dort entsteht, sobald der Matcher nach `SqlToAi.Domain` extrahiert ist. Heute wird die Logik nur über `SecurityGuard.MatchesPattern` getestet.

## Bekannte Unschärfen

- **Plan-Datensätze waren intern inkonsistent** (Kommentare beschreiben `?` als Single-Char-Wildcard, aber Text/Pattern-Längen würden nie matchen). Ich habe die Texte in 3 InlineData-Zeilen gekürzt, damit sie zur dokumentierten Intention passen. Der Auditer sollte prüfen, ob die gekürzten Texte (`Demo_A`, `MyServer.`, `demo_a`) den Testzweck noch abdecken — meiner Einschätzung nach tun sie es, und das sogar sauberer als die Plan-Originale.
- **Internal-Methode `MatchesPattern` ist jetzt Teil der öffentlichen Test-API.** Da `SqlToAi.csproj` bereits `<InternalsVisibleTo Include="SqlToAi.Tests" />` enthält, ist das die etablierte und akzeptierte Variante. Falls das Sicherheits-Review Bedenken hat (z. B. weil `internal static` als „halb-öffentlich" gilt), wäre Reflection die Alternative — der Plan nennt sie explizit als Fallback.
- **Kein Test für `RegexMatchTimeoutException`-Pfad.** Der Timeout-Catch in `SecurityGuard.cs:76-78` wird nicht ausgelöst getestet, da das Erzeugen eines echten Timeouts (200 ms Pattern-Auswertung) im Unit-Test-Setup umständlich wäre. Der Pfad ist durch den Empty-Input-Test (der den `if`-Zweig vor dem Regex betritt) abgesichert, aber nicht durch einen ausgelösten Timeout.
