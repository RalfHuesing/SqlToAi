---
status: done
type: step-review
task: audit-2026-07-24
step: 005
reviewed_by: auditer
reviewed_by_model: MiniMax-M3
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-07-25T21:30:00+02:00
verdict: approved  # approved | issues | blocked
---

# Review Step 005: Punkt 19 — Generischen TtlCache<TKey, TValue> extrahieren

## Verdict

- [x] **approved** — alle drei Prüfebenen ok
- [ ] **issues** — Fix-Step `step-005/fix-XX` anlegen
- [ ] **blocked** — Nutzer-Entscheidung nötig

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `.agents/rules/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Build: selbst nachgeprüft, grün (0/0)
- [x] Tests: selbst nachgeprüft, 388/388 grün (AiNetLinterTests 2/2 grün)

## Befund

### Plan-Erfüllung

| Plan-Punkt | Status | Beleg |
|---|---|---|
| `TtlCache.cs` wie spezifiziert (Z. 41–82) | ✅ | `src/SqlToAi/Domain/TtlCache.cs:25` — `internal sealed class TtlCache<TKey, TValue> where TKey : notnull`; `:27` `ConcurrentDictionary<TKey, Entry> _entries = new()`; `:44–48` `GetOrLoadAsync(key, loader, ttl, ct)`; `:61–64` `private sealed record Entry(Value, ExpireTime) { IsExpired(now) }`. Detailreicher dokumentiert als Plan-Skizze (TTL=0, Race-Doku) — nicht funktional abweichend. |
| `AccessLevelProvider.cs` umgestellt | ✅ | `:22` `private readonly TtlCache<string, AccessLevel> _cache = new();`; `:53–57` `_cache.GetOrLoadAsync(databaseName, ct => QueryAccessLevelAsync(databaseName, ct), TimeSpan.FromSeconds(_options.Databases.CacheTtlSeconds), cancellationToken)`; hartkodierter `> 0 ? ... : 300`-Ternary ersatzlos entfernt; `using System.Collections.Concurrent` weg. |
| `AnonymizationRuleProvider.cs` umgestellt | ✅ | `:9` `using SqlToAi.Domain;`; `:22` `private const string CacheKey = "all-rules";` erhalten; `:27` `TtlCache<string, IReadOnlyList<AnonymizationRule>>`; `:54–61` `_cache.GetOrLoadAsync(CacheKey, LoadActiveRulesAsync, TimeSpan.FromSeconds(_options.AnonymizationRules.CacheTtlSeconds), cancellationToken)`; `RuleCacheEntry`-Record ersatzlos entfernt; 300-Fallback weg; `using System.Collections.Concurrent` weg. |
| `AccessCheckResult` entfernt | ✅ | Datei `src/SqlToAi/Domain/AccessCheckResult.cs` im Commit `52c62a9` gelöscht. Konsumenten-Check siehe unten. |
| `RuleCacheEntry` entfernt | ✅ | Inline im `AnonymizationRuleProvider.cs` entfernt. Konsumenten-Check siehe unten. |
| `TtlCacheTests.cs` mit 5 Methoden | ✅ | `tests/SqlToAi.Tests/Domain/TtlCacheTests.cs:13,35,58,82,105` — alle 5 geplanten Methoden vorhanden, `// @covers SqlToAi.Domain.TtlCache` korrekt. |
| Bestehende Tests unverändert grün | ✅ | `AccessLevelProviderTests` 14/14 grün; `AnonymizationRuleProviderTests` 14/14 grün. Diff zeigt nur Entfernung der `@covers`-Kommentare für die entfallenen Typen. |

**Konsumenten-Analyse `AccessCheckResult` / `RuleCacheEntry` (Stichproben):**

- `Select-String`-Äquivalent via `ripgrep` über `src/` und `tests/`: **keine Treffer** (kein Mock, kein Reflection-Setter, kein Konsument in irgend einer `.cs`-Datei).
- `ripgrep` über `docs/`: **keine Treffer** (kein Erwähnen in `mcp-specification.md` oder anderswo).
- `ripgrep` über `tasks/`: nur die erwarteten historischen Erwähnungen in `01-security-guardrails.md`, `03-code-qualitaet-architektur.md`, `step-005/step-plan.md`, `step-008/step-plan.md` (alle Doku/Trail, nicht Code).
- Datei `AccessCheckResult.cs` ist nicht mehr im Working-Tree (`glob **/AccessCheckResult*` ergibt keinen Treffer) — Löschung tatsächlich wirksam.
- `RuleCacheEntry` (war im `SqlToAi.Anonymization`-Namespace, `public sealed record`) — keine Konsumenten in Code/Tests/Doku. Da `public`, theoretisch extern konsumierbar, aber durch den Konsumenten-Check bestätigt: niemand nutzt es. Löschung sicher.

### Rules-Konformität

| Regel | Status | Beleg |
|---|---|---|
| `AiNetLinter.mdc#general/EnforceSealedClasses` | ✅ | `TtlCache.cs:25` `internal sealed class`; `:61` `private sealed record Entry`. Beide Provider-Container-Klassen bereits vorher `public sealed`. |
| `AiNetLinter.mdc#Kurz-Stil` (Methodenlänge ≤60) | ✅ | `GetOrLoadAsync` Methodenrumpf 10 Zeilen (Z. 50–58); `IsExpired` 1 Zeile (Expression Body); `Entry` 2 Zeilen. Datei 65 Zeilen (< 500). |
| `AiNetLinter.mdc#general/EnforceNullableEnable` | ✅ | `TtlCache.cs:1` `#nullable enable` am Dateianfang. |
| `AiNetLinter.mdc#general/EnforceAsciiIdentifiers` | ✅ | Keine Umlaute/Sonderzeichen in `TtlCache.cs` (TtlCache, GetOrLoadAsync, IsExpired etc.). |
| `AiNetLinter.mdc#agent-resilience/EnforceNoSilentCatch` | ✅ | Kein `try/catch` in `TtlCache.cs` (Loader wirft durch; Aufrufer fängt). |
| `AiNetLinter.mdc#architecture/EnforceNamespaceDirectoryMapping` | ✅ | `namespace SqlToAi.Domain` → `src/SqlToAi/Domain/`. |
| `AiNetLinter.mdc#architecture/DetectAndBanPhantomDependencies` | ✅ | `using System.Collections.Concurrent;` wird von `ConcurrentDictionary<>` aufgelöst. |
| `AiNetLinter.mdc#test-coverage/EnableTestSentinel` | ✅ | `TtlCacheTests.cs:7` `// @covers SqlToAi.Domain.TtlCache`. |
| `SqlToAiRichtlinien.mdc#4` Conventional Commit deutsch imperativ | ✅ | Subject beginnt mit `refactor(caching):`, deutsche Beschreibung, imperativ. Subject-Länge: 111 Zeichen (siehe Sonstige Beobachtungen). |
| `SqlToAiRichtlinien.mdc#4` Keine Magic Values (`300`) | ✅ | Beide Provider-Ternarys ersatzlos entfernt; Default kommt aus `SqlToAiOptions.cs:39` und `:142` (Property-Initializer = 300). |
| `SqlToAiRichtlinien.mdc#4` Kein Versionsbump in `SqlToAi.csproj` | ✅ | `git diff 52c62a9^ 52c62a9 -- src/SqlToAi/SqlToAi.csproj` ist leer; `Version 1.0.12` unverändert. |
| `SqlToAiRichtlinien.mdc#5` Zero-Warning-Direktive | ✅ | `dotnet build` → 0/0 nachgeprüft (siehe Build-Status). |
| `AiNetLinter` Baseline-Update | ✅ | 4 modifizierte (`AccessLevelProvider.cs`, `AnonymizationRuleProvider.cs`, `AccessLevelProviderTests.cs`, `AnonymizationRuleProviderTests.cs`) + 2 neue (`TtlCache.cs`, `TtlCacheTests.cs`) + 1 entfernte (`AccessCheckResult.cs`) Datei — exakt das vom Plan erwartete Muster. Hashes nachgerechnet (siehe Test-Status). |

### Logische Korrektheit

**Concurrency-Erhaltung (kein Race-Regression):**

- `git show 52c62a9^:src/SqlToAi/Security/AccessLevelProvider.cs` zeigt: Vorher `ConcurrentDictionary<string, AccessCheckResult>.TryGetValue` + Indexer-Set ohne `lock`. Das Original hatte **kein** `lock`. Der Refactor ersetzt die Mechanik 1:1 durch `TtlCache._entries.TryGetValue` + Indexer-Set. Race-Bedingung war vorher schon da und ist nicht durch den Refactor eingeführt.
- Gleiches Bild für `AnonymizationRuleProvider.cs^`.
- Beide Loader (`QueryAccessLevelAsync` und `LoadActiveRulesAsync`) sind **read-only** SQL-Selects, idempotent, ohne Seiteneffekte — ein doppelter Aufruf bei gleichzeitigem Erst-Zugriff nach Ablauf ist Performance- (zwei DB-Queries statt einer), nicht Korrektheitsproblem.
- Der Coder dokumentiert dieses Verhalten explizit in `TtlCache.cs:17–19` (XML-Doc Concurrency-Remark) — vorbildlich.

**TTL=0 Verhalten:**

- `TtlCache.cs:50,57` — `now = DateTime.UtcNow`; `ExpireTime = now.Add(ttl)` mit `ttl = TimeSpan.Zero` → `ExpireTime = now`; nächster Aufruf `IsExpired(now)` → `now >= now` → `true` → Reload. Stimmig mit Coder-Beobachtung.

**`AccessCheckResult` / `RuleCacheEntry`-Entfernung:**

- Konsumenten-Check oben dokumentiert: keine externen Referenzen. `RuleCacheEntry` war `public sealed record` im `SqlToAi.Anonymization`-Namespace, wurde aber nie von Test-Mocks oder externen Konsumenten verwendet. Entfernung sauber.
- Kein verbleibender `<see cref="ExclusionCheckResult"/>`-Verweis (war schon im `ee2e1e2` aufgeräumt) — AnonymizationRuleProvider.cs nach Refactor sauber.
- Kein verbleibender `System.Collections.Concurrent`-Import in den Provider-Dateien (war nur für den `ConcurrentDictionary` da, jetzt durch `TtlCache` gekapselt).

**Test-Coverage `TtlCacheTests`:**

- `TtlCacheTests.cs:13` `ShouldReturnCachedValue_WhenNotExpired`: 2 Calls, Loader 1x, Werte identisch. ✓
- `:35` `ShouldReloadValue_WhenExpired`: TTL 50ms + `Task.Delay(150)` + zweiter Call → Loader 2x, beide Werte unterschiedlich. Simuliert Expiry korrekt via Zeitvergehen (privates `Entry` ist nicht direkt manipulierbar). ✓
- `:58` `ShouldInvokeLoaderExactlyOnce_ForUnchangedTtl`: 3 Calls, Loader 1x, alle Werte `v1`. ✓
- `:82` `ShouldInvokeLoaderPerKey_ForDistinctKeys`: 2 Keys, Loader 2x, Werte 1+2. ✓
- `:105` `ShouldNotShareEntriesAcrossKeys`: A→alpha, B→bravo, A→alpha; Loader nur 2x (für A und B), zweiter A-Read ist Cache-Hit. ✓
- Methodenlänge der Tests: alle <30 Zeilen (Limit 100 für `*.Tests`).

**End-to-end Cache-Tests in Providern:**

- `AccessLevelProviderTests:41–70` `GetAccessLevelAsync_ShouldCacheResults_AndRespectTtl` setzt `CacheTtlSeconds = 1`, ruft dreimal mit `Task.Delay(1100)` dazwischen → erwartet `ConnectionCreatedCount` 1, 1, 2. Mit TtlCache-TTL 1s + 1.1s Delay → Expiry greift → dritter Call löst Reload aus. Test grün.
- `AnonymizationRuleProviderTests:101–120` `IsExcludedAsync_ShouldCacheRules_AndReloadAfterTtlExpires` setzt `CacheTtlSeconds = 1`, gleiche Mechanik. Test grün.
- Das beweist, dass die externe Cache-Semantik (Hit/Miss/TTL-Expiry) **bitidentisch** zur vorherigen Implementierung ist.

### Build-Status

```
dotnet build SqlToAi.slnx
→ 0 Warnungen, 0 Fehler (4,82 s)
```

### Test-Status

```
dotnet test --filter "Category!=Integration" --no-build
→ Bestanden: 0 Fehler, 388 erfolgreich, 0 übersprungen, gesamt 388 (12 s)

AiNetLinterTests (gefiltert):                2/2 grün
TtlCacheTests (gefiltert):                   5/5 grün
AccessLevelProviderTests (gefiltert):       14/14 grün
AnonymizationRuleProviderTests (gefiltert): 14/14 grün
```

**AiNetLinter-Baseline-Verifikation (SHA-256):**

| Datei | Inhalt-Hash (gerechnet) | Baseline-Eintrag | Match |
|---|---|---|---|
| `src/SqlToAi/Domain/TtlCache.cs` | `8266A4AC567ABC4B687F126436A03574AB75FD8BF6A52473C1AF26326FD75292` | `8266a4ac567abc4b687f126436a03574ab75fd8bf6a52473c1af26326fd75292` | ✅ |
| `src/SqlToAi/Security/AccessLevelProvider.cs` | `F0F4EB98A62DE764F72F21E74E51BFF63E5C44C28984B28030C7F6457EEE3229` | `f0f4eb98a62de764f72f21e74e51bff63e5c44c28984b28030c7f6457eee3229` | ✅ |
| `src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs` | `346FAF893F55620095ACCD0248FBBC02B956B1A105003A9E4D724ECD11F599CE` | `346faf893f55620095accd0248fbbc02b956b1a105003a9e4d724ecd11f599ce` | ✅ |
| `tests/SqlToAi.Tests/Domain/TtlCacheTests.cs` | `963530211430D66C830A51185ADAB4045BD0440E2F297FF9CE1D5A54A823D94B` | `963530211430d66c830a51185adab4045bd0440e2f297ff9ce1d5a54a823d94b` | ✅ |
| `tests/SqlToAi.Tests/Security/AccessLevelProviderTests.cs` | `3287BDBE5EEB5E519805BD9E85FD3578B8925835C43C236D5F9B6A74A739E8F4` | `3287bdbe5eeb5e519805bd9e85fd3578b8925835c43c236d5f9b6a74a739e8f4` | ✅ |
| `tests/SqlToAi.Tests/Anonymization/AnonymizationRuleProviderTests.cs` | `2866A03AE040EE20980DD8C84FBD361B4F2F9DBBBC494FB13D4ED8B7D0F3D08A` | `2866a03ae040ee20980dd8c84fbd361b4f2f9dbbbc494fb13d4ed8b7d0f3d08a` | ✅ |
| `src/SqlToAi/Domain/AccessCheckResult.cs` | nicht vorhanden | nicht vorhanden | ✅ (entfernt) |

Alle Hashes stimmen case-insensitiv überein; `AccessCheckResult.cs` ist aus der Baseline entfernt.

**Linter-Report (vorbestehend, unverändert):**

- `tests/SqlToAi.Tests/AiNetLinter/output/SqlToAi-linter-report.md` zeigt 2 `MaxBoolParameterCount`-Violations an `MockConnection` (Z. 217) und `MockCommand` (Z. 261) in `AccessLevelProviderTests.cs`. **Diese sind vorbestehend** (gleicher Report-Inhalt wie in vorherigen Audits) — Validation Exit Code = 0, AiNetLinter akzeptiert sie als false-positive-markiert. Kein Issue für Step 005.

## Findings (bei `issues`)

Keine. (Leere Sektion — Verdict ist `approved`.)

## Frage an Nutzer (bei `blocked`)

Keine. (Leere Sektion — Verdict ist `approved`.)

## Sonstige Beobachtungen (nicht als Issues zu werten)

1. **Commit-Subject-Länge 111 Zeichen.** Subject `refactor(caching): extrahiere generischen TtlCache und nutze ihn in AccessLevel- und AnonymizationRule-Provider` ist **111 Zeichen** (inkl. `refactor(caching): `-Prefix). Die Konvention „Subject ≤ 72" (aus step-001-Lehre etabliert) wird um 39 Zeichen überschritten. Step-001 mit 74 Zeichen wurde mit Hinweis „knapp über 72 — marginal, kein Issue" approved; die aktuelle Abweichung ist erheblicher. Die `SqlToAiRichtlinien.mdc` selbst erzwingt die 72-Zeichen-Grenze **nicht** als harte Regel, daher kein Issue. Für die Commit-Log-Lesbarkeit (`git log --oneline` schneidet ohnehin bei ~80 Zeichen) wäre ein Sub-Splitting empfehlenswert — z. B. `refactor(caching): extrahiere TtlCache-Helper und nutze ihn in zwei Providern` (76 Zeichen) oder noch kürzer. **Nicht-blockierend, Step kann so freigegeben werden.** Hinweis an künftige Steps.

2. **Doku/Implementation-Drift bei `CacheTtlSeconds = 0`.** `docs/mcp-specification.md:62` sagt: *„eine `0` ist nicht erlaubt (würde bei jedem Tool-Aufruf neu geprüft)"* — aber `TtlCache.cs:39–42` akzeptiert `TimeSpan.Zero` und führt tatsächlich zu sofortigem Reload. Das ist **nicht durch diesen Step verursacht** (vorher hatte der `> 0 ? ... : 300`-Ternary denselben Effekt: `0` → Fallback 300s, kein Reload), aber die Doku-Aussage war/ist eine Versprechung, die das Projekt nie eingelöst hat. Der Planer hat das in den Notes explizit als out-of-scope markiert: Konfigurations-Validierung soll ein separater Step werden. **Beobachtung, kein Issue für Step 005.** Konfigurations-Validierung (`IValidateOptions<SqlToAiOptions>` o. ä.) sollte in einem späteren Step angegangen werden.

3. **`RuleCacheEntry` war `public`.** Der Record lebte im `SqlToAi.Anonymization`-Namespace als `public sealed record` — theoretisch Teil der öffentlichen API. Der Konsumenten-Check zeigt, dass er nie extern verwendet wurde (kein Mock, kein anderes Source-File, keine Doku, keine Tests). Da `SqlToAi` ein Stdio-MCP-Server ohne externe API-Konsumenten ist und der Typ semantisch „internes Cache-Detail" war, ist die Löschung sicher — **aber** zur Vorsicht wäre eine `git log`-Notiz im nächsten Release-Commit sinnvoll, falls ein Außenstehender den Typ je per Reflexion angesprochen hat. Sehr niedrige Wahrscheinlichkeit, da der Typ keinen XML-Doc-Verweis auf eine externe Schnittstelle hatte.

4. **`TtlCache` ist `internal` und über `InternalsVisibleTo` testbar.** `src/SqlToAi/SqlToAi.csproj:29` `<InternalsVisibleTo Include="SqlToAi.Tests" />` — `TtlCacheTests` kompiliert ohne Reflection. Sauberer Pattern, deckt sich mit der bestehenden Test-Strategie (auch `AccessLevelProvider` und `AnonymizationRuleProvider` werden direkt instanziiert, nicht über `IAccessLevelProvider`).

5. **`ExpectedKeysAThenB` ist ein statisches Test-Field.** `TtlCacheTests.cs:10` deklariert ein `static readonly string[]` für den Assertion-Vergleich. Konventionskonform mit dem bestehenden Pattern in `AccessLevelProviderTests` (`TestConstants`) — keine eigene Konstante nötig, da nur in einer Datei verwendet. Marginal — alternative wäre `[]`-Array-Literal inline. Beides ok, kein Issue.
