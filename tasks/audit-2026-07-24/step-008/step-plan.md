---
status: open
type: step-plan
task: audit-2026-07-24
step: 008
title: "Punkt 22 — Glob-Matcher in SqlToAi.Domain extrahieren und SecurityGuard darauf umstellen"
created_by: planer
created_at: 2026-07-25T18:30:00+02:00
related_to:
  - tasks/audit-2026-07-24/03-code-qualitaet-architektur.md (DRY-Impact Niedrig #1)
  - tasks/audit-2026-07-24/00-summary.md (Punkt 22 — Rest nach Commit bcef6a9)
---

# Step 008: Punkt 22 — Glob-Matcher konsolidieren (Rest)

## Bezug

- **Task:** `audit-2026-07-24`
- **Quelle:** `03-code-qualitaet-architektur.md` Teil B „Glob-Pattern-Matching zweimal implementiert, Modul-Grenze verwischt" (DRY-Impact Niedrig #1)
- **Phase / Priorität:** Phase 4 — Architektur-Aufräumarbeit, Punkt 22 (letzter offener Punkt)

## Ausgangslage (wichtig — Sonderfall)

Commit `bcef6a9` ("docs(anonymization): Dokumentation & README synchronisieren und unbenutzten GlobPatternMatcher entfernen") hat bereits **einen** Teil von Punkt 22 erledigt:

- ✅ `src/SqlToAi/Anonymization/GlobPatternMatcher.cs` wurde entfernt (war seit Commit `ee2e1e2` unbenutzt, weil die `IsColumnExcluded`-Logik durch `AnonymizationRuleProvider` ersetzt wurde).
- ✅ `README.md` und `docs/mcp-specification.md` wurden an die neue Anonymisierungs-Architektur angepasst.

**Verbleibender Rest:**

- ❌ `src/SqlToAi/Security/SecurityGuard.cs:60-80` enthält weiterhin eine **Duplikat-Implementierung** des Glob-zu-Regex-Algorithmus (`private static bool MatchesPattern(string text, string pattern)` mit identischer `Regex.Escape` + `\*`→`.*` + `\?`→`.` + 200ms-Timeout-Logik wie die frühere `GlobPatternMatcher.IsMatch`).

Ziel dieses Steps: Den verbleibenden Rest des Audit-Funds aufräumen — eine **gemeinsame** `internal static class GlobMatcher` in `SqlToAi.Domain` einführen, `SecurityGuard.MatchesPattern` durch einen Aufruf dieser Utility ersetzen. Damit ist die Duplikation vollständig eliminiert.

## Intention

Der Audit-Bericht stellt fest, dass die Glob-Matching-Logik (`*`-Wildcard + `?`-Single-Char-Wildcard, mit `Regex.Escape` für Regex-Sonderzeichen, 200ms-Timeout) ein **modulübergreifend relevantes Utility** ist, das nichts mit Anonymisierung oder Security im Speziellen zu tun hat. Ein Bugfix (z. B. ein Escaping-Edge-Case) müsste aktuell an zwei Stellen gepflegt werden, und die Gefahr unbemerkten Auseinanderlaufens ist real.

Ziel: Die `MatchesPattern`-Logik aus `SecurityGuard` in eine **neutrale** `SqlToAi.Domain.GlobMatcher`-Utility extrahieren, `SecurityGuard` ruft sie auf. `SecurityGuardTests` (mit den in `step-001` ergänzten Wildcard-Theorie-Tests) bleibt grün und verifiziert das Verhalten **end-to-end** über `IsDatabaseAllowed`. Eine dedizierte `GlobMatcherTests`-Klasse verifiziert die Utility isoliert.

## Konkrete Änderungen

### Datei 1 (neu): `src/SqlToAi/Domain/GlobMatcher.cs`

- **Was:**
  ```csharp
  #nullable enable
  using System.Text.RegularExpressions;

  namespace SqlToAi.Domain;

  /// <summary>
  /// Matches text against simple glob-style patterns (<c>*</c> and <c>?</c>).
  /// Case-insensitive, single-pass Regex with a 200 ms timeout; on
  /// <see cref="RegexMatchTimeoutException"/> returns <c>false</c> (fail-closed).
  /// Lives in <c>SqlToAi.Domain</c> because the matcher is a generic string
  /// utility, used today by <c>SecurityGuard</c> (database whitelist) and
  /// previously by <c>Anonymizer</c> (column exclusion) — no anonymization- or
  /// security-specific semantics.
  /// </summary>
  internal static class GlobMatcher
  {
      private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

      public static bool IsMatch(string text, string pattern)
      {
          if (string.IsNullOrEmpty(pattern))
          {
              return false;
          }

          string regexPattern = "^" + Regex.Escape(pattern)
              .Replace("\\*", ".*")
              .Replace("\\?", ".") + "$";

          try
          {
              return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase, RegexTimeout);
          }
          catch (RegexMatchTimeoutException)
          {
              return false;
          }
      }
  }
  ```
- **Warum:** Genau das, was `SecurityGuard.MatchesPattern` heute tut — nur eben in einer neutralen Utility, die für jeden Konsumenten offen ist (via `InternalsVisibleTo` für die Tests).

### Datei 2: `src/SqlToAi/Security/SecurityGuard.cs`

- **Was:**
  1. `using System.Text.RegularExpressions;` (Zeile 3) entfernen, falls nicht mehr gebraucht (nach Entfernen von `MatchesPattern`).
  2. `using SqlToAi.Domain;` ergänzen.
  3. `IsMatchedByAnyPattern` (Zeile 48-58): Aufruf von `MatchesPattern(databaseName, pattern)` ersetzen durch `GlobMatcher.IsMatch(databaseName, pattern)`.
  4. Ganze `private static bool MatchesPattern(string text, string pattern)` (Zeile 60-80) entfernen — die Logik lebt jetzt in `GlobMatcher`.
- **Warum:** Duplikation entfernen; `SecurityGuard` delegiert an die Utility.

### Datei 3: `tests/SqlToAi.Tests/Domain/GlobMatcherTests.cs` (neu)

- **Was:** Dedizierte Unit-Tests für den neuen Matcher, **identisch** zu den in `step-001` ergänzten `SecurityGuardTests.MatchesPattern_*`-Theorie-Cases (kopieren + ggf. um weitere Edge-Cases ergänzen):
  - `IsMatch_ShouldHandleStarWildcard` — `("Demo_App", "Demo_*", true)`, `("OtherApp", "Demo_*", false)`
  - `IsMatch_ShouldHandleQuestionMarkWildcard` — `("Demo_A", "Demo_?", true)`, `("Demo_AB", "Demo_?", false)`
  - `IsMatch_ShouldEscapeRegexMetacharacters` — `("MyServer.1", "MyServer.1", true)`, `("MyServerX1", "MyServer.1", false)`
  - `IsMatch_ShouldBeCaseInsensitive` — `("demo_app", "DEMO_*", true)`
  - `IsMatch_ShouldReturnFalse_OnEmptyPattern` — `("Demo_App", "", false)`
  - `IsMatch_ShouldReturnFalse_OnEmptyText` — `("", "Demo_*", false)`
  - `IsMatch_ShouldReturnFalse_OnBothEmpty` — `("", "", false)`
- **Warum:** Die Utility ist jetzt die **einzige** Quelle der Glob→Regex-Logik — eigene Tests verifizieren sie unabhängig von der SecurityGuard-Pipeline. `SecurityGuardTests.MatchesPattern_*` (aus `step-001`) testet die Integration, `GlobMatcherTests` testet die Unit.

## Tests

- [ ] `GlobMatcherTests.IsMatch_ShouldHandleStarWildcard` — Theory mit mehreren Inline-Daten
- [ ] `GlobMatcherTests.IsMatch_ShouldHandleQuestionMarkWildcard` — Theory
- [ ] `GlobMatcherTests.IsMatch_ShouldEscapeRegexMetacharacters` — Theory
- [ ] `GlobMatcherTests.IsMatch_ShouldBeCaseInsensitive`
- [ ] `GlobMatcherTests.IsMatch_ShouldReturnFalse_OnEmptyPattern`
- [ ] `GlobMatcherTests.IsMatch_ShouldReturnFalse_OnEmptyText`
- [ ] `GlobMatcherTests.IsMatch_ShouldReturnFalse_OnBothEmpty`
- [ ] **Bestehende Tests bleiben grün ohne Änderung:**
  - `SecurityGuardTests` (alle 3 bestehenden Facts + die in `step-001` ergänzten `MatchesPattern_*`-Theories testen das Verhalten end-to-end über `IsDatabaseAllowed` und decken damit implizit auch `GlobMatcher` ab)
- [ ] `dotnet build SqlToAi.slnx` 0 Warnungen, 0 Fehler
- [ ] `dotnet test --filter "Category!=Integration"` grün

## Definition of Done

- [ ] Alle „Konkreten Änderungen" umgesetzt
- [ ] Build-Command grün (0 Warnings, 0 Errors)
- [ ] Test-Command grün (Ausnahmen siehe „Bekannte Ausnahmen")
- [ ] Commit auf aktuellem Branch (`refactor(security): extrahiere GlobMatcher in SqlToAi.Domain und nutze ihn in SecurityGuard`)
- [ ] `step-008/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „xUnit v3 Tests: Pflicht für alle funktionalen Änderungen" (neue Utility → Tests)
- `.agents/rules/AiNetLinter.mdc#general/EnforceSealedClasses` — `GlobMatcher` ist `internal static` (kein `sealed` nötig bei `static`)
- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „xUnit v3 Tests: Pflicht für alle funktionalen Änderungen" (GlobMatcher ist ein sicherheitsrelevantes Utility hinter `IsDatabaseAllowed`)

## Bekannte Ausnahmen

- `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` — vorbestehend, **nicht** Teil dieses Tasks. Wahrscheinliche Baseline-Aktualisierungen:
  - `src/SqlToAi/Security/SecurityGuard.cs` (Methode entfernt, `using SqlToAi.Domain;` ergänzt, ggf. `using System.Text.RegularExpressions;` entfernt)
  - **Neu:** `src/SqlToAi/Domain/GlobMatcher.cs` (muss zur Baseline hinzugefügt werden)
  - **Neu:** `tests/SqlToAi.Tests/Domain/GlobMatcherTests.cs` (muss zur Baseline hinzugefügt werden)
  - SHA-256-Hashes der finalen Inhalte berechnen und in `SqlToAi-baseline.json` eintragen.

## Notes

- **`internal static` vs. `public static`:** Der Audit-Bericht schlägt `public` vor (damit auch andere Module darauf zugreifen können, falls je nötig). Empfehlung: **zunächst `internal`**, da aktuell nur `SecurityGuard` (und per `InternalsVisibleTo` die Tests) zugreifen. Eine spätere Hochstufung auf `public` ist trivial, sollte aber erst erfolgen, wenn ein zweiter Konsument entsteht — `public` ist eine API-Zusage, die nicht leicht zurückgenommen werden kann.
- **Sichtbarkeit `internal` + `InternalsVisibleTo("SqlToAi.Tests")`:** Voraussetzung dafür, dass `GlobMatcherTests` direkt auf die Utility zugreifen kann. Falls `InternalsVisibleTo` für `SqlToAi.Tests` fehlt (siehe `step-001` für die Verifikation), muss es in `SqlToAi.csproj` (oder `AssemblyInfo.cs`) ergänzt werden.
- **Reihenfolge im Commit:** Erst `GlobMatcher` hinzufügen, **dann** `SecurityGuard.MatchesPattern` entfernen und durch `GlobMatcher.IsMatch` ersetzen — in **einem** Commit. Sicherstellen, dass `dotnet build` zwischen den beiden Schritten (die im selben Commit passieren) nicht in einem Hybrid-Zustand ist.
- **Test-Strategie:** Die `SecurityGuardTests.MatchesPattern_*`-Theories aus `step-001` werden nicht redundant — sie testen jetzt indirekt `GlobMatcher` über `IsDatabaseAllowed`. Die `GlobMatcherTests` in diesem Step testen `GlobMatcher` direkt. Beide zusammen geben Defense-in-Depth: wenn `SecurityGuard` versehentlich `GlobMatcher` durch eine andere Logik ersetzt, fällt mindestens einer der beiden Test-Sätze auf.
- **Wahl `Domain` als Namespace:** Konsistent mit `AccessCheckResult` und (ab `step-005`) `TtlCache`. `Domain` ist die etablierte Heimat für neutrale Domain-Records/Utilities, die kein Datenbank-, Security- oder Anonymization-spezifisches Wissen haben. Falls weitere Domain-Utilities dazukommen, könnte `SqlToAi.Domain` ein Sub-Namespace wie `SqlToAi.Domain.Text` für reine String-Utilities bekommen — für eine einzelne Klasse nicht nötig.
- **Linter-Konformität:** `GlobMatcher.IsMatch` hat ~15 Zeilen, klar unter dem 60-Zeilen-Limit. Eine `MaxCyclomaticComplexity`-Überschreitung ist nicht zu erwarten (eine `try`/`if`/`Replace`-Kette mit ~4 Verzweigungen).
- **Nach diesem Step ist Punkt 22 vollständig abgeschlossen.** Die ursprüngliche Audit-Empfehlung („GlobPatternMatcher als neutrales Utility erkennen und verschieben") ist durch die `bcef6a9`+dieser-Step-Sequenz umgesetzt: Die matcher-Logik liegt jetzt in `SqlToAi.Domain.GlobMatcher` (`internal static`), und `SecurityGuard` delegiert dorthin. Der Punkt steht im `task-summary.md` als erledigt.
