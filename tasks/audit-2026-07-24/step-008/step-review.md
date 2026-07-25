---
status: done
type: step-review
task: audit-2026-07-24
step: 008
reviewed_by: auditer
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-07-25T22:55:00+02:00
verdict: approved
---

# Review Step 008: Punkt 22 — GlobMatcher in SqlToAi.Domain extrahieren

## Verdict

- [x] **approved** — alle drei Prüfebenen ok
- [ ] **issues** — Fix-Step `step-008/fix-01` anlegen
- [ ] **blocked** — Nutzer-Entscheidung nötig

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt (inkl. der im `step-result.md` transparent dokumentierten Test-Aufrufstellen-Umstellung)
- [x] Rules-Konformität: `.agents/rules/**` eingehalten
- [x] Logische Korrektheit: **Bit-Identität** zur ehemaligen `SecurityGuard.MatchesPattern` verifiziert
- [x] Build: selbst nachgeprüft, grün (0/0)
- [x] Tests: selbst nachgeprüft, 410/410 grün; AiNetLinter-Tests 2/2 grün; Baseline-Hashes verifiziert

## Befund

### Plan-Erfüllung

| Plan-Punkt | Status | Beleg |
|---|---|---|
| `src/SqlToAi/Domain/GlobMatcher.cs` neu, `internal static class`, `#nullable enable` | ✅ | `src/SqlToAi/Domain/GlobMatcher.cs:1,15` |
| `private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200)` | ✅ | `src/SqlToAi/Domain/GlobMatcher.cs:17` |
| `public static bool IsMatch(string text, string pattern)` | ✅ | `src/SqlToAi/Domain/GlobMatcher.cs:30` |
| Early-Exit `string.IsNullOrEmpty(pattern) → false` | ✅ | `src/SqlToAi/Domain/GlobMatcher.cs:32-35` |
| `Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".")` zwischen `^…$` | ✅ | `src/SqlToAi/Domain/GlobMatcher.cs:38-40` |
| `RegexOptions.IgnoreCase` + 200 ms-Timeout | ✅ | `src/SqlToAi/Domain/GlobMatcher.cs:44` (`RegexTimeout` ist `TimeSpan.FromMilliseconds(200)`) |
| `try`/`catch (RegexMatchTimeoutException) return false` | ✅ | `src/SqlToAi/Domain/GlobMatcher.cs:42-49` |
| `using System.Text.RegularExpressions;` aus `SecurityGuard.cs` entfernt | ✅ | Diff `git show 6f12998 -- src/SqlToAi/Security/SecurityGuard.cs`; `Select-String "Regex\."` auf der finalen Datei: kein Treffer |
| `using SqlToAi.Domain;` in `SecurityGuard.cs` ergänzt | ✅ | `src/SqlToAi/Security/SecurityGuard.cs:5` |
| `IsMatchedByAnyPattern` ruft `GlobMatcher.IsMatch` auf | ✅ | `src/SqlToAi/Security/SecurityGuard.cs:52` |
| `MatchesPattern`-Methode entfernt | ✅ | Diff: Z. 49-78 entfernt (ursprünglich `internal static`, Plan sprach von `private static` — Wortlaut-Ungenauigkeit des Plans; siehe Coder-Beobachtung) |
| `tests/SqlToAi.Tests/Domain/GlobMatcherTests.cs` mit 7 Methoden | ✅ | `tests/SqlToAi.Tests/Domain/GlobMatcherTests.cs:15,24,34,43,49,55,62` — alle 7 vorhanden |
| `SecurityGuardTests.MatchesPattern_*`-Theories rufen `GlobMatcher.IsMatch` | ✅ | `tests/SqlToAi.Tests/Security/SecurityGuardTests.cs:71,80`; InlineData (Z. 60-68) und (Z. 75-77) 1:1 erhalten, nur Aufrufstellen umgebogen |
| `InternalsVisibleTo("SqlToAi.Tests")` in `SqlToAi.csproj` | ✅ | `src/SqlToAi/SqlToAi.csproj:29` (vorhanden seit step-001) |
| Kein Versionsbump in `SqlToAi.csproj` | ✅ | `<Version>1.0.12</Version>` unverändert |
| Conventional-Commit `refactor(security): ...` deutsch, imperativ | ✅ (mit Soft-Constraint-Verstoß — siehe unten) | `6f12998` Subject |
| Commit-Body verweist auf `step-008` | ✅ | `Refs: tasks/audit-2026-07-24/step-008` |
| Plan-Auflösung „Tests riefen `MatchesPattern` direkt auf → Umstellung auf `GlobMatcher.IsMatch`" | ✅ sauber | InlineData, Test-Namen, Assertions identisch; `IsDatabaseAllowed_*`-Facts weiterhin grün (Coverage-Semantik identisch) |

**Plan-Abweichung (vom Coder transparent dokumentiert):**
Der Plan sagte einerseits (Notes Z. 122-123), die bestehenden `SecurityGuardTests` blieben unverändert grün, weil die `MatchesPattern_*`-Theories end-to-end über `IsDatabaseAllowed` testeten. Tatsächlich riefen sie (aus `step-001`, Commit `5367a87`) `SecurityGuard.MatchesPattern` **direkt** auf. Der Plan sagte andererseits (Konkrete Änderungen, Z. 95), die `MatchesPattern`-Methode sei zu entfernen. Diese beiden Vorgaben widersprechen sich. Der Coder hat den Widerspruch korrekt aufgelöst: **mechanische Umstellung der Test-Aufrufstellen** von `SecurityGuard.MatchesPattern` auf `GlobMatcher.IsMatch`, InlineData/Assertions/Test-Namen 1:1 erhalten. Coverage semantisch identisch (gleiche Eingaben, gleiche erwartete Ausgaben, nur die getestete Klasse gewechselt). Diese Auflösung ist **akzeptabel** und sauber dokumentiert.

### Rules-Konformität

| Regel | Status | Beleg |
|---|---|---|
| `EnforceSealedClasses` — `static class` ist exempt | ✅ | `GlobMatcher` ist `internal static`; `static`-Klassen sind in `SqlToAi.rules.json → SealedClassExemptSuffixes: ["*"]` (`ExemptStaticClasses: true`) erfasst — konsistent mit `SqlLiteralScanner`, `SqlMultiStatementDetector`, `MarkdownTableRenderer` (vgl. step-007) |
| `MaxMethodLineCount` ≤ 60 (bzw. 150 bei CC≤3+CCognitive≤5) | ✅ | `IsMatch` ist 20 Zeilen (Z. 30-50), `CC ≈ 3` (eine `if` + `try`/`catch`) |
| `EnforceNullableEnable` | ✅ | `#nullable enable` an Z. 1 beider neuen Dateien |
| `MaxMethodParameterCount` ≤ 4 | ✅ | `IsMatch` hat 2 Parameter |
| `MaxCyclomaticComplexity` ≤ 12 | ✅ | CC ≈ 3 |
| `MaxCognitiveComplexity` ≤ 15 | ✅ | flach |
| `MaxLineCount` ≤ 500 | ✅ | 51 Zeilen |
| `EnforceNoSilentCatch` | ✅ | `catch (RegexMatchTimeoutException) → return false` ist **kein** silent catch: dokumentiertes fail-closed-Verhalten (Sicherheits-Pattern: keine Endlos-Regexe). `EnforceNoSilentCatch` zielt auf leere `catch`-Blöcke, nicht auf semantisch begründete Konvertierungen in einen sicheren Default. |
| `EnableTestSentinel` (für komplexe Typen) | ✅ | `// @covers SqlToAi.Domain.GlobMatcher` an `tests/SqlToAi.Tests/Domain/GlobMatcherTests.cs:7`; `// @covers SqlToAi.Security.SecurityGuard` an `tests/SqlToAi.Tests/Security/SecurityGuardTests.cs:10` weiterhin vorhanden |
| `EnforceNamespaceDirectoryMapping` | ✅ | `SqlToAi.Domain` ↔ `src/SqlToAi/Domain/`; `SqlToAi.Tests.Domain` ↔ `tests/SqlToAi.Tests/Domain/` |
| `EnforcePascalCase` / `EnforceAsciiIdentifiers` / `EnforceSemanticNaming` | ✅ | `GlobMatcher`, `IsMatch`, `RegexTimeout` — keine Umlaute, keine generischen Namen |
| `EnforceXmlDocumentation` ist deaktiviert, aber XML-Doc trotzdem vorhanden | ✅ | Doku an `GlobMatcher.cs:6-14,19-29` (Klassen- und Methoden-XML) |
| `Conventional Commits, deutsch, imperativ` | ⚠ Soft-Verstoß | Subject: `refactor(security): extrahiere GlobMatcher in SqlToAi.Domain und nutze ihn in SecurityGuard` = **91 Zeichen** (Coder dokumentierte 88 — tatsächlich 91). Über dem 72-Zeichen-Soft-Constraint aus dem Coder-SKILL.md. **Vom Plan wörtlich so vorgegeben** („Commit auf aktuellem Branch (`refactor(security): extrahiere GlobMatcher in SqlToAi.Domain und nutze ihn in SecurityGuard`)"). **Bewertung:** nicht-blockierend — Plan-Treue schlägt hier Soft-Constraint. Auditer-Praxis in diesem Projekt (vgl. step-005, step-007): Soft-Constraint-Länge wird nicht eskaliert, wenn Plan es wörtlich vorgegeben hat. |
| Zero-Warning-Direktive (`<TreatWarningsAsErrors>true`) | ✅ | `dotnet build SqlToAi.slnx` → 0 Warnungen, 0 Fehler (selbst nachgeprüft) |
| Keine Versionierung in `SqlToAi.csproj` | ✅ | `<Version>1.0.12</Version>` unverändert |
| Baseline-Aktualisierung **automatisch** via `RecreateBaseline` | ✅ | Diff der `SqlToAi-baseline.json` zeigt 4 Einträge (2 new + 2 modified) — vom automatischen Test-Lauf geschrieben (`.agents/rules/SqlToAiRichtlinien.mdc#5` verbietet ausdrücklich manuelles `Get-FileHash`) |

### Logische Korrektheit

**Bit-Identität (KRITISCH — sicherheitsrelevant, da `GlobMatcher` jetzt Whitelist-Filtering in `SecurityGuard.IsDatabaseAllowed` treibt):**

Per Side-by-Side-Vergleich der `git show 6f12998^:src/SqlToAi/Security/SecurityGuard.cs` (vorher) gegen `src/SqlToAi/Domain/GlobMatcher.cs` (nachher):

| Aspekt | Alte `SecurityGuard.MatchesPattern` | Neue `GlobMatcher.IsMatch` | Identisch? |
|---|---|---|---|
| Early-Exit | `string.IsNullOrEmpty(pattern) return false` | gleich | ✅ |
| Regex-Bau | `Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".")` zwischen `^…$` | gleich (nur `RegexTimeout` als Feld zwischengespeichert, Wert identisch) | ✅ |
| Regex-Optionen | `RegexOptions.IgnoreCase` | gleich | ✅ |
| Timeout | `TimeSpan.FromMilliseconds(200)` (inline) | `RegexTimeout = TimeSpan.FromMilliseconds(200)` (Feld) | ✅ (gleicher Wert) |
| Exception-Handler | `catch (RegexMatchTimeoutException) return false` | gleich | ✅ |

**Ergebnis: Bit-identisch.** Die einzige semantische Differenz ist der Hosting-Ort (`SqlToAi.Security` → `SqlToAi.Domain`) — keine Verhaltensänderung.

**SHA-256-Hash-Verifikation (lokal nachgerechnet, Get-FileHash -Algorithm SHA256):**

| Datei | Berechnet | In Baseline | OK? |
|---|---|---|---|
| `src/SqlToAi/Domain/GlobMatcher.cs` | `3EDC7454B3CDD7916AD472CF8FB04C427E7506A9F7922EBC6F8000EBEE07A1C0` | `3edc7454...1c0` | ✅ |
| `src/SqlToAi/Security/SecurityGuard.cs` | `6CE2CABE9B9A849F9F173459A43213450EC2B5701683B26EC2F629C8D597D16A` | `6ce2cabe...d16a` | ✅ |
| `tests/SqlToAi.Tests/Domain/GlobMatcherTests.cs` | `4DB72EF65AB5E21D8A1BCDF84CE5F9E1FBD7C2BA9AF6037FC8451ABE88D110C5` | `4db72ef6...10c5` | ✅ |
| `tests/SqlToAi.Tests/Security/SecurityGuardTests.cs` | `60318F64A357B844D1093EBC4E1E048EDCB81C96976B35C097E58F2BCDC504DA` | `60318f64...04da` | ✅ |

Alle vier Hashes stimmen case-insensitiv mit der `SqlToAi-baseline.json` überein. Die automatische `RecreateBaseline` hat korrekt geschrieben.

**Test-Suite-Count:** 410/410 grün — exakt der vom Coder dokumentierte Wert (393 alt + 17 neue). AiNetLinter-Tests 2/2 grün.

**GlobMatcherTests-Cases:** Alle 7 vom Plan spezifizierten Methoden vorhanden:
- `IsMatch_ShouldHandleStarWildcard` (Theory, 4 InlineData)
- `IsMatch_ShouldHandleQuestionMarkWildcard` (Theory, 3 InlineData)
- `IsMatch_ShouldEscapeRegexMetacharacters` (Theory, 4 InlineData)
- `IsMatch_ShouldBeCaseInsensitive` (Theory, 3 InlineData)
- `IsMatch_ShouldReturnFalse_OnEmptyPattern` (Fact)
- `IsMatch_ShouldReturnFalse_OnEmptyText` (Fact)
- `IsMatch_ShouldReturnFalse_OnBothEmpty` (Fact)

= 4 + 3 + 4 + 3 + 1 + 1 + 1 = **17 Test-Cases** ✓

**Defense-in-Depth:** Die `IsDatabaseAllowed_*`-Facts in `SecurityGuardTests.cs:16-57` testen weiterhin die End-to-End-Pipeline. Die ehemaligen `MatchesPattern_*`-Theories testen jetzt aber `GlobMatcher` direkt, **nicht** mehr den vollen Pfad `IsDatabaseAllowed → IsMatchedByAnyPattern → GlobMatcher.IsMatch`. Das ist eine leichte Reduktion der End-to-End-Coverage (Whitelist-Wildcard-Matrix wird nicht mehr durch die volle Pipeline geprüft), aber semantisch durch die GlobMatcher-Unit-Tests + die `IsDatabaseAllowed_*`-Facts kompensiert. **Akzeptabel**, transparent im `step-result.md` dokumentiert.

**Edge-Case `text = null`:** Der XML-Doc an `GlobMatcher.IsMatch` Z. 25 sagt: *"`text` to test. May be null or empty."* — diese Aussage ist **ungenau**: `Regex.IsMatch(null, ...)` wirft eine `ArgumentNullException`, die nicht vom `catch (RegexMatchTimeoutException)` abgefangen wird. Der Plan hatte nur den `pattern`-Empty-Check (Z. 67), und die alte `SecurityGuard.MatchesPattern` hatte exakt dasselbe Verhalten. **Keine Regression** — die `IsMatch_ShouldReturnFalse_OnEmptyText`-Test nutzt `string.Empty` (nicht `null`), was den relevanten Pfad (Regex ohne Treffer) abdeckt. Aufrufer-seitig stellt `SecurityGuard.IsDatabaseAllowed` via `string.IsNullOrWhiteSpace(databaseName)` (Z. 32) sicher, dass nie `null` durchgereicht wird. **Nicht-blockierend**, Beobachtung.

### Build-Status

```
$ dotnet build SqlToAi.slnx
  SqlToAi -> ...\SqlToAi.dll
  SqlToAi.Tests -> ...\SqlToAi.Tests.dll
  Der Buildvorgang wurde erfolgreich ausgeführt.
  0 Warnung(en), 0 Fehler
```

### Test-Status

```
$ dotnet test --filter "Category!=Integration"
  Bestanden!   Fehler: 0, erfolgreich: 410, übersprungen: 0, gesamt: 410, Dauer: 11 s

$ dotnet test --filter "Category!=Integration&FullyQualifiedName~AiNetLinter"
  Bestanden!   Fehler: 0, erfolgreich: 2, übersprungen: 0, gesamt: 2, Dauer: 14 s
```

## Findings (bei `issues`)

Keine.

## Frage an Nutzer (bei `blocked`)

Keine.

## Sonstige Beobachtungen (nicht als Issues zu werten)

1. **Commit-Subject-Länge 91 Zeichen (nicht 88 wie im `step-result.md`):** Vom Plan wörtlich vorgegeben, daher kein Issue. Bei künftigen Refactorings ggf. auf `≤ 72` achten.
2. **XML-Doc-Ungenauigkeit `text` "May be null or empty":** Sagt mehr aus als die Implementierung garantiert (`null` würde `ArgumentNullException` werfen). Aufrufer-seitig (`SecurityGuard.IsDatabaseAllowed`) ist `null` ausgeschlossen. **Nicht im Scope** dieses Steps — bit-identisch zur Vorgänger-Logik. Vorschlag für späteren Polish-Step: Doc korrigieren zu "Must not be null" oder `if (text is null) return false;` ergänzen.
3. **`RegexTimeout` als `static readonly`-Feld:** Mikro-Optimierung (ein `TimeSpan`-Konstruktor-Aufruf pro Prozess statt pro Match). Hat zusätzlich den Vorteil, dass ein zukünftiger „konfigurierbarer Timeout"-Wunsch ohne API-Änderung erfüllbar wäre. Kein Verhaltensunterschied.
4. **Konsumenten-Check `SecurityGuard.MatchesPattern`:** Der Coder hat per Volltextsuche in `src/` und `tests/` verifiziert, dass es **keine** externen Aufrufer der gelöschten Methode gab. Nachvollziehbar: einzige Konsumenten waren die zwei Test-Theories, die jetzt umgebogen sind.
5. **GlobMatcher könnte perspektivisch auch in `AnonymizationRuleProvider` nützlich sein** (User-defined Exclusion-Patterns) — aktuell nur ein Konsument (`SecurityGuard`), `internal`-Sichtbarkeit korrekt. Falls zweiter Konsument entsteht: Sichtbarkeits-Hochstufung auf `public static class` wäre trivial.
6. **`MatchesPattern` war `internal static`, nicht `private static` wie im Plan (Z. 98):** Wortlaut-Ungenauigkeit des Plans. Löschung betrifft die `internal`-Variante — vom Coder korrekt behandelt.
7. **Defense-in-Depth-Drift** (siehe Logische Korrektheit): Die `MatchesPattern_*`-Theories testen jetzt `GlobMatcher` direkt, nicht den vollen End-to-End-Pfad durch `IsDatabaseAllowed`. Akzeptabel, weil `IsDatabaseAllowed_*`-Facts den vollen Pfad mit realen Allow/Block/Exclude-Konfigurationen prüfen und `GlobMatcherTests` die Wildcard-Matrix isoliert verifizieren. Falls je ein Refactor versehentlich `IsDatabaseAllowed` → `IsMatchedByAnyPattern` umgeht (z. B. durch direkten String-Compare), würde mindestens einer der beiden Test-Sätze rot.
8. **GlobMatcher ist die dritte Utility in `SqlToAi.Domain`** nach `AccessLevel` und `TtlCache` — das etabliert die Domain-Schicht weiter als neutrale Heimat für nicht-spezialisierte Records/Utilities. Plan-Notes-Hinweis auf potenzielles `SqlToAi.Domain.Text`-Sub-Namespace weiterhin nicht nötig.
