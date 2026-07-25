---
status: done (pending audit)
type: step-result
task: audit-2026-07-24
step: 008
title: "Punkt 22 — GlobMatcher in SqlToAi.Domain extrahieren und SecurityGuard darauf umstellen"
model_id: MiniMax-M3
model_knowledge_cutoff: 2026-01
coded_by: coder
coded_at: 2026-07-25T22:30:00+02:00
code_commit_hash: 6f12998
status_after: done
---

# Step 008 — Ergebnis

## Zusammenfassung

Die in `SecurityGuard.MatchesPattern` duplizierte Glob-zu-Regex-Logik (`*`/`?`-Wildcards, `Regex.Escape`, 200 ms-Timeout, fail-closed bei `RegexMatchTimeoutException`) wurde in eine neue `internal static class GlobMatcher` in `SqlToAi.Domain` extrahiert. `SecurityGuard.IsMatchedByAnyPattern` delegiert jetzt an `GlobMatcher.IsMatch`; die private Methode und das `using System.Text.RegularExpressions;` entfallen ersatzlos. 17 neue dedizierte Unit-Tests in `tests/SqlToAi.Tests/Domain/GlobMatcherTests.cs` decken Stern-/Fragezeichen-Wildcards, Regex-Metazeichen-Escaping, Case-Insensitivity sowie alle drei leeren Eingabekombinationen ab.

## Bit-Identitäts-Verifikation

Die neue `GlobMatcher.IsMatch`-Logik ist **bit-identisch** zur ursprünglichen `SecurityGuard.MatchesPattern`-Logik:

- `string.IsNullOrEmpty(pattern)` → `return false` (gleicher Early-Exit)
- `Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".")` zwischen `^…$` (gleicher Regex-Bau)
- `Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase, …)` (gleiches Matching)
- `TimeSpan.FromMilliseconds(200)` (jetzt als `private static readonly TimeSpan RegexTimeout`-Feld zwischengespeichert — Wert identisch)
- `catch (RegexMatchTimeoutException) → return false` (gleicher fail-closed-Pfad)

Die einzige semantische Differenz ist der Hosting-Ort: `internal static` in `SqlToAi.Domain` statt `internal static` in `SqlToAi.Security`; dadurch entfällt die `SecurityGuard`-Abhängigkeit von `System.Text.RegularExpressions`. Das Verhalten der `SecurityGuard.MatchesPattern_*`-Theories (eingeführt in `step-001`, Commit `5367a87`) bleibt unverändert, da die Test-Aufrufstellen jetzt gegen `GlobMatcher.IsMatch` umgebogen wurden (InlineData, Assertions und Test-Namen identisch).

## Geänderte Dateien

| Datei | Änderung |
|---|---|
| `src/SqlToAi/Domain/GlobMatcher.cs` | **Neu** — `internal static class` mit `IsMatch(string text, string pattern)`, bit-identisch zur ehemaligen `SecurityGuard.MatchesPattern`. XML-Doku dokumentiert die Neutralität (keine Security/Anonymization-Semantik) und das fail-closed-Verhalten. `RegexTimeout` als `private static readonly TimeSpan` zwischengespeichert. |
| `src/SqlToAi/Security/SecurityGuard.cs` | `using System.Text.RegularExpressions;` entfernt (nicht mehr benötigt). `using SqlToAi.Domain;` ergänzt. `IsMatchedByAnyPattern` ruft jetzt `GlobMatcher.IsMatch(...)` statt `MatchesPattern(...)`. Ganze `internal static bool MatchesPattern(...)`-Methode (Z. 60-80) ersatzlos entfernt. |
| `tests/SqlToAi.Tests/Domain/GlobMatcherTests.cs` | **Neu** — 7 Test-Methoden, 17 Test-Cases (4+3+4+3 Theories + 3 Facts). Deckt `*`, `?`, Regex-Metazeichen-Escaping, Case-Insensitivity und Empty-Input-Kombinationen ab. |
| `tests/SqlToAi.Tests/Security/SecurityGuardTests.cs` | `using SqlToAi.Domain;` ergänzt. Die beiden `MatchesPattern_*`-Theories rufen jetzt `GlobMatcher.IsMatch(text, pattern)` statt `SecurityGuard.MatchesPattern(text, pattern)` — InlineData, Test-Namen und Assertions unverändert. |
| `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` | **Auto-aktualisiert** durch `AiNetLinterTests.RecreateBaseline` während `dotnet test` (siehe unten). |

## Commit

- **Code-Commit-Hash:** `6f12998`
- **Message:**
  ```
  refactor(security): extrahiere GlobMatcher in SqlToAi.Domain und nutze ihn in SecurityGuard
  ```
- **Branch:** `main`
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Schritt 7 des Coder-Skills) — siehe git log nach step-plan/step-result-Update.

## Build-Output

```
$ dotnet build SqlToAi.slnx
  SqlToAi -> ...\SqlToAi.dll
  SqlToAi.Tests -> ...\SqlToAi.Tests.dll
  Der Buildvorgang wurde erfolgreich ausgeführt.
  0 Warnung(en), 0 Fehler
```

## Test-Output

```
$ dotnet test --filter "Category!=Integration"
  Bestanden! Fehler: 0, erfolgreich: 410, übersprungen: 0, gesamt: 410, Dauer: 12 s

$ dotnet test --filter "Category!=Integration&FullyQualifiedName~AiNetLinter"
  Bestanden! Fehler: 0, erfolgreich: 2, übersprungen: 0, gesamt: 2
  (RunLinterShouldBeCleanOrBaselineMatch + RecreateBaseline, beide grün, Exit 0)
```

**Test-Count:** 410/410 grün (393 alt + 17 neue `GlobMatcherTests`).

AiNetLinter-Tests **vollständig grün** (Clean-Mode, Exit 0). Baseline wurde während des Test-Laufs automatisch durch `RecreateBaseline` neu geschrieben und in den Code-Commit aufgenommen.

## AiNetLinter-Baseline

Die `SqlToAi-baseline.json` wurde **automatisch** durch `AiNetLinterTests.RecreateBaseline` aktualisiert und ist Teil des Code-Commits `6f12998`. Es waren **keine** manuellen SHA-256-Berechnungen oder `Get-FileHash`-Aufrufe nötig.

Diff-Stat der Baseline (`git diff` Zeilen):
- **Neu:** `src/SqlToAi/Domain/GlobMatcher.cs` → Hash `3edc7454b3cdd7916ad472cf8fb04c427e7506a9f7922ebc6f8000ebee07a1c0`
- **Neu:** `tests/SqlToAi.Tests/Domain/GlobMatcherTests.cs` → Hash `4db72ef65ab5e21d8a1bcdf84ce5f9e1fbd7c2ba9af6037fc8451abe88d110c5`
- **Geändert:** `src/SqlToAi/Security/SecurityGuard.cs` (`b53e5db…` → `6ce2cabe…`) — MatchesPattern entfernt, Using geändert
- **Geändert:** `tests/SqlToAi.Tests/Security/SecurityGuardTests.cs` (`2dd0b766…` → `60318f64…`) — Aufrufstellen auf `GlobMatcher.IsMatch` umgestellt

## Abweichungen vom Plan

**Eine minimale, mechanische Test-Datei-Anpassung** (im Plan widersprüchlich formuliert):

- Der Plan sagt einerseits in den „Wichtigen Hinweisen" (Z. 7): „Bestehende `SecurityGuardTests` bleiben unverändert grün" — die zugehörige Begründung („Die Tests sind end-to-end über `IsDatabaseAllowed`") trifft auf den aktuellen Stand der Datei **nicht** zu: die in `step-001` (Commit `5367a87`) ergänzten `MatchesPattern_*`-Theories rufen `SecurityGuard.MatchesPattern(text, pattern)` **direkt** auf, nicht über `IsDatabaseAllowed`.
- Der Plan sagt andererseits in „Konkrete Änderungen → Datei 2" (Z. 95): „Ganze `private static bool MatchesPattern(string text, string pattern)` (Zeile 60-80) entfernen" — was die direkten Aufrufe in den bestehenden Tests zwingend bricht.

**Auflösung:** Die Test-Aufrufstellen in `SecurityGuardTests` wurden **mechanisch** von `SecurityGuard.MatchesPattern(text, pattern)` auf `GlobMatcher.IsMatch(text, pattern)` umgestellt. InlineData, Test-Namen, Assertions und Klassen-Kommentar (`// @covers SqlToAi.Security.SecurityGuard`) bleiben **unverändert**. Die drei end-to-end `IsDatabaseAllowed_*`-Facts sind von der Änderung gar nicht betroffen (sie rufen `GlobMatcher` implizit über `IsDatabaseAllowed` auf und sind weiterhin grün).

**Konsequenz:** Coverage bleibt identisch (gleiche InlineData-Sätze, gleicher Test-Count pro Theorie), nur die zu testende Klasse hat sich geändert. Die `GlobMatcherTests` ergänzen das Bild mit dedizierten Unit-Tests der Utility.

**Commit-Subject-Länge:** Der vorgeschlagene Subject überschreitet die 72-Zeichen-Regel aus dem Coder-SKILL.md um 16 Zeichen (88 Zeichen). Wurde wie vorgeschlagen übernommen, da der Auftrag den Subject explizit so formuliert hat.

**Konsumenten-Check `SecurityGuard.MatchesPattern`:** Es gibt **keine** externen Aufrufer (Volltextsuche über `src/` und `tests/`) — nur die zwei Test-Theories in `SecurityGuardTests`, die jetzt auf `GlobMatcher.IsMatch` umgebogen sind. Daher ist die Löschung von `SecurityGuard.MatchesPattern` risikolos.

## Beobachtungen (nicht im Scope)

- **`RegexTimeout` als `static readonly TimeSpan`-Feld** statt Inline-`TimeSpan.FromMilliseconds(200)` in jedem Aufruf. Mikro-Optimierung (ein Konstruktor-Aufruf pro Prozess statt pro Match), unterstützt aber auch eventuelle zukünftige Konfigurierbarkeit ohne API-Änderung. Kein Verhaltensunterschied.
- **`SecurityGuard.MatchesPattern` war `internal static`, nicht `private static`** wie im Plan-Zitat („private static bool MatchesPattern"). Die Diskrepanz im Plan ist eine Wortwahl-Ungenauigkeit; die Löschung betrifft die `internal`-Variante. Habe das im Diff-Stat korrekt als `internal static`-Entfernung dokumentiert.
- **GlobMatcher ist `internal static class`** — der `SealedClassExemptSuffixes`-Hinweis aus `.agents/rules/AiNetLinter.mdc` (`ExemptStaticClasses: true` in `SqlToAi.rules.json`) macht `sealed` für `static`-Klassen obsolet; konsistent mit `SqlLiteralScanner`, `SqlMultiStatementDetector` und `MarkdownTableRenderer` (vgl. `step-007`).
- **`SqlToAi.Domain` als Namespace-Heimat** bewährt sich zum dritten Mal (`AccessLevel`, `TtlCache`, jetzt `GlobMatcher`). Der Plan-Notes-Hinweis auf ein potenzielles `SqlToAi.Domain.Text`-Sub-Namespace ist weiterhin nicht nötig.
- **GlobMatcher könnte perspektivisch auch in `AnonymizationRuleProvider` nützlich sein**, falls dort jemals wieder Glob-Matching benötigt wird (z. B. für User-defined Exclusion-Patterns). Aktuell wird der Matcher nur in `SecurityGuard` verwendet — die `internal`-Sichtbarkeit ist korrekt.

## Bekannte Unschärfen für den Auditer

- **Bit-Identität formal:** Vor dem Commit wurde die Logik-Differenz manuell verifiziert (zeilenweiser Vergleich der `if`/`try`/`Replace`-Sequenz gegen die ehemalige `MatchesPattern`-Implementierung). Ein Hash-Vergleich war nicht möglich, da die Original-Implementierung im selben Commit entfernt wurde. Die `GlobMatcherTests` + die umgebogenen `SecurityGuardTests.MatchesPattern_*`-Theories testen jetzt aber **exakt** das Verhalten, das vorher `MatchesPattern` hatte — ein zukünftiger Regress auf die alte `MatchesPattern`-Logik würde von mindestens einem der beiden Test-Sätze gefangen.
- **`SqlToAi-baseline.json` Hash-Werte:** Die in der Diff-Stat oben angegebenen SHA-256-Hashes sind die **aktuellen** Werte nach dem `RecreateBaseline`-Lauf. Sie sind im JSON nach `git diff` sichtbar; der Auditer kann sie gegen `Get-FileHash` der finalen `.cs`-Dateien verifizieren (das war nicht Teil dieses Steps, da `RecreateBaseline` autoritativ ist).
- **GlobMatcher als `internal`:** Sollte ein zweiter Konsument außerhalb von `SqlToAi.Security` entstehen, müsste die Sichtbarkeit auf `public static class` hochgestuft werden. Der Plan dokumentiert dies in den Notes; der jetzige Stand ist `internal`.
- **`SecurityGuardTests`-Klassen-Kommentar `// @covers SqlToAi.Security.SecurityGuard`** ist formal weiterhin korrekt, da die drei `IsDatabaseAllowed_*`-Facts in der Klasse nach wie vor `SecurityGuard` testen. Die `MatchesPattern_*`-Theories testen jetzt faktisch `GlobMatcher` (über die alte Test-Klassen-Heimat), aber der Linter-Sentinel-Kommentar ist Klassen-weit und nicht pro Methode.

## Model-Metadaten

- **model_id:** MiniMax-M3
- **model_knowledge_cutoff:** 2026-01
