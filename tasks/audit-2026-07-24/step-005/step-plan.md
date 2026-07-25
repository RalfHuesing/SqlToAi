---
status: done (pending audit)
type: step-plan
task: audit-2026-07-24
step: 005
title: "Punkt 19 — Generischen TtlCache<TKey, TValue> extrahieren und in AccessLevelProvider + AnonymizationRuleProvider einsetzen"
created_by: planer
created_at: 2026-07-25T18:30:00+02:00
coded_by: coder
coded_at: 2026-07-25T20:55:00+02:00
commit_code: 52c62a9
related_to:
  - tasks/audit-2026-07-24/03-code-qualitaet-architektur.md (DRY-Impact Mittel #2 + Teil-A-Fund hartkodiertes `300`)
  - tasks/audit-2026-07-24/00-summary.md (Punkt 19)
---

# Step 005: Punkt 19 — Generischen TtlCache extrahieren

## Bezug

- **Task:** `audit-2026-07-24`
- **Quelle:** `03-code-qualitaet-architektur.md` Teil B „Gecachter Wert mit TTL"-Muster dreimal separat implementiert (DRY-Impact Mittel #2), inkl. Teil-A-Fund „Hartkodierter TTL-Fallback-Wert `300`"
- **Phase / Priorität:** Phase 4 — Architektur-Aufräumarbeit, Punkt 19

## Vorbefund (relevant für den Scope)

Der Audit-Bericht nennt drei Stellen mit identischem `ConcurrentDictionary<string, TCacheEntry>(IsExpired)`-Muster:

1. `src/SqlToAi/Security/AccessLevelProvider.cs:23,47-70` — `AccessCheckResult` als `record(AccessLevel, DateTime ExpireTime)`, Cache-Key = Datenbankname
2. `src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs:20,38,65-80` — `RuleCacheEntry` als `record(IReadOnlyList<AnonymizationRule> Rules, DateTime ExpireTime)`, konstanter Cache-Key `"all-rules"` (siehe Zeile 33)
3. ~~`src/SqlToAi/Anonymization/AnonymizerExclusionProvider.cs`~~ — **bereits entfernt** durch Commit `ee2e1e2` („refactor(anonymization): veraltete lokale Ausschlüsse entfernen und rein auf AnonymizationRules konsolidieren"). Diese Datei existiert nicht mehr, daher nur **zwei** Konsumenten.

Zusätzlich enthält jeder Konsument den hartkodierten `> 0 ? _options.X.CacheTtlSeconds : 300`-Fallback (siehe Audit-Fund Teil A). Der `300`-Wert ist redundant: `DatabasesOptions.CacheTtlSeconds` (`Configuration/SqlToAiOptions.cs:41`) und `AnonymizationRulesOptions.CacheTtlSeconds` (`Configuration/SqlToAiOptions.cs:158`) haben bereits `= 300` als Property-Initializer. **Dieser Step räumt beides in einem Rutsch auf**, wie im Audit explizit vorgeschlagen.

## Intention

Einen `internal sealed class TtlCache<TKey, TValue>` in `SqlToAi.Domain` (oder einem neuen `SqlToAi.Caching`-Ordner — siehe Notes) einführen, der das identische Cache-Muster kapselt. Die zwei Provider-Konsumenten reduzieren sich auf ihre fachliche Lade-Logik (SQL-Query, Regeln laden) plus einen `await _cache.GetOrLoadAsync(key, LoadAsync, ttl, ct)`-Aufruf. Der `300`-Fallback entfällt ersatzlos, weil der Options-Default `= 300` bereits im Cache-Wrapper greift (TTL kommt aus den Options, der Cache nutzt sie ohne Fallback).

## Konkrete Änderungen

### Datei 1 (neu): `src/SqlToAi/Domain/TtlCache.cs`

- **Was:**
  ```csharp
  #nullable enable
  using System.Collections.Concurrent;

  namespace SqlToAi.Domain;

  /// <summary>
  /// Thread-safe cache with a per-entry time-to-live. Each key maps to exactly one
  /// (value, expire-time) pair; expired entries are re-loaded lazily on next access.
  /// </summary>
  internal sealed class TtlCache<TKey, TValue> where TKey : notnull
  {
      private readonly ConcurrentDictionary<TKey, Entry> _entries = new();

      /// <summary>
      /// Returns the cached value if present and unexpired, otherwise invokes
      /// <paramref name="loader"/>, caches the result with an absolute expiry of
      /// <paramref name="ttl"/> from now, and returns it.
      /// </summary>
      public async Task<TValue> GetOrLoadAsync(
          TKey key,
          Func<CancellationToken, Task<TValue>> loader,
          TimeSpan ttl,
          CancellationToken cancellationToken = default)
      {
          var now = DateTime.UtcNow;
          if (_entries.TryGetValue(key, out var cached) && !cached.IsExpired(now))
          {
              return cached.Value;
          }

          var value = await loader(cancellationToken);
          _entries[key] = new Entry(value, now.Add(ttl));
          return value;
      }

      private sealed record Entry(TValue Value, DateTime ExpireTime)
      {
          public bool IsExpired(DateTime now) => now >= ExpireTime;
      }
  }
  ```
- **Warum:** Eine einzige Implementierung des `ConcurrentDictionary + IsExpired + Reload`-Musters, geteilt von beiden Providern. `TValue` ohne Einschränkung, weil der AnonymizationRule-Provider `IReadOnlyList<AnonymizationRule>` cached, der AccessLevel-Provider `AccessLevel`.

### Datei 2: `src/SqlToAi/Security/AccessLevelProvider.cs`

- **Was:**
  1. `using SqlToAi.Domain;` ist bereits vorhanden.
  2. Feld `_cache` (Zeile 23) durch `private readonly TtlCache<string, AccessLevel> _cache = new();` ersetzen.
  3. `GetAccessLevelAsync` (Zeile 47-70): Der gesamte Try-Get-Block (`TryGetValue` + `IsExpired`-Check) durch `return await _cache.GetOrLoadAsync(databaseName, ct => QueryAccessLevelAsync(databaseName, ct), TimeSpan.FromSeconds(_options.Databases.CacheTtlSeconds), cancellationToken);` ersetzen.
  4. Die `record AccessCheckResult` (Datei `src/SqlToAi/Domain/AccessCheckResult.cs`) wird **nicht** entfernt, weil sie als semantischer Cache-Entry-Typ ggf. extern (in `IAccessLevelProvider`-Mocks) verwendet wird. **Prüfen:** ob `AccessCheckResult` außerhalb von `AccessLevelProvider` und seiner Tests referenziert wird; falls nein, in diesem Step mit-aufräumen (Begleitschritt, saubererer Zustand). Andernfalls belassen.
  5. **Hartkodierten `300`-Fallback entfernen** (Audit-Teil-A-Fund): Der `> 0 ? ... : 300`-Ternary in Zeile 65 entfällt, weil der Options-Default bereits `300` ist und `TimeSpan.FromSeconds(0)` zwar keinen sinnvollen Cache bedeutet, aber auch nicht crasht (sofortiger Reload bei jedem Aufruf). Falls ein expliziter Schutz gegen `0` gewünscht ist: in `SqlToAiOptions.Validate()` (oder einem zukünftigen `IValidateOptions<T>`) als Konfigurations-Validierung, **nicht** im Cache-Wrapper.
- **Warum:** Reduziert die `GetAccessLevelAsync`-Methode auf das fachliche Minimum.

### Datei 3: `src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs`

- **Was:**
  1. `using SqlToAi.Domain;` ergänzen.
  2. Feld `_cache` (Zeile 38) durch `private readonly TtlCache<string, IReadOnlyList<AnonymizationRule>> _cache = new();` ersetzen.
  3. Konstante `CacheKey = "all-rules"` (Zeile 33) bleibt.
  4. `GetActiveRulesAsync` (Zeile 65-80): Den Try-Get-Block durch `return await _cache.GetOrLoadAsync(CacheKey, LoadActiveRulesAsync, TimeSpan.FromSeconds(_options.AnonymizationRules.CacheTtlSeconds), cancellationToken);` ersetzen.
  5. **Hartkodierten `300`-Fallback entfernen** (Audit-Teil-A-Fund, Zeile 76).
  6. `RuleCacheEntry`-Record (Zeile 20-24) entfernen (wird durch das `Entry`-Record im `TtlCache` ersetzt).
- **Warum:** Identische Mechanik wie AccessLevelProvider — ein Cache, ein Loader, eine TTL.

### Datei 4: `tests/SqlToAi.Tests/Domain/TtlCacheTests.cs` (neu)

- **Was:** Dedizierte Unit-Tests für den neuen Cache:
  - `GetOrLoadAsync_ShouldReturnCachedValue_WhenNotExpired`
  - `GetOrLoadAsync_ShouldReloadValue_WhenExpired` (Cache-Wert mit `ExpireTime = DateTime.UtcNow.AddMilliseconds(-1)` injizieren oder `Task.Delay` + kurze TTL)
  - `GetOrLoadAsync_ShouldInvokeLoaderExactlyOnce_ForUnchangedTtl` (drei Aufrufe mit Key X → Loader wird nur einmal gerufen)
  - `GetOrLoadAsync_ShouldInvokeLoaderPerKey_ForDistinctKeys` (zwei Keys → zwei Loader-Aufrufe)
  - `GetOrLoadAsync_ShouldNotShareEntriesAcrossKeys` (Key A und Key B liefern verschiedene Werte ohne Cross-Talk)
- **Warum:** Verifiziert die Korrektheit des Wrappers isoliert von den Provider-Tests, bevor die Provider umgestellt werden.

### Datei 5: `tests/SqlToAi.Tests/Security/AccessLevelProviderTests.cs` und `tests/SqlToAi.Tests/Anonymization/AnonymizationRuleProviderTests.cs`

- **Was:** Bestehende Tests müssen **nicht** geändert werden — sie testen das Verhalten von außen (über `GetAccessLevelAsync` bzw. `IsExcludedAsync`). Der `TtlCache` ist ein internes Implementierungsdetail, und die Tests prüfen nur die externe Semantik (Cache-Hit, Cache-Miss, Ablauf nach TTL, Fail-Safe). Diese Tests sind bereits grün und bleiben es.
- **Aber:** Falls die Test-Mocks/Reflection-Setter auf `AccessCheckResult` oder `RuleCacheEntry` zugreifen, müssen sie an die neue Struktur angepasst werden. **Prüfen** mit `grep -rn "AccessCheckResult\|RuleCacheEntry" tests/`.

## Tests

- [ ] `TtlCacheTests.GetOrLoadAsync_ShouldReturnCachedValue_WhenNotExpired`
- [ ] `TtlCacheTests.GetOrLoadAsync_ShouldReloadValue_WhenExpired`
- [ ] `TtlCacheTests.GetOrLoadAsync_ShouldInvokeLoaderExactlyOnce_ForUnchangedTtl`
- [ ] `TtlCacheTests.GetOrLoadAsync_ShouldInvokeLoaderPerKey_ForDistinctKeys`
- [ ] Bestehende `AccessLevelProviderTests` bleiben grün (Cache-Hit/Miss/TTL-Verhalten bereits getestet in Commit `319d0fe`)
- [ ] Bestehende `AnonymizationRuleProviderTests` bleiben grün (Cache-TTL-Verhalten bereits getestet)
- [ ] `dotnet build SqlToAi.slnx` 0 Warnungen, 0 Fehler
- [ ] `dotnet test --filter "Category!=Integration"` grün

## Definition of Done

- [ ] Alle „Konkreten Änderungen" umgesetzt
- [ ] Build-Command grün (0 Warnings, 0 Errors)
- [ ] Test-Command grün (Ausnahmen siehe „Bekannte Ausnahmen")
- [ ] Commit auf aktuellem Branch (`refactor(caching): extrahiere generischen TtlCache und nutze ihn in AccessLevel- und AnonymizationRule-Provider`)
- [ ] `step-005/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „Keine hartkodierten Werte (No Magic Values)" — dieser Step entfernt die `300`-Fallbacks und räumt damit genau diesen Verstoß auf (Audit-Teil-A-Fund)
- `.agents/rules/SqlToAiRichtlinien.mdc#5` — Result-Pattern — keine Änderung
- `.agents/rules/AiNetLinter.mdc#general/EnforceSealedClasses` — `TtlCache` als `internal sealed class` (siehe Code-Skizze)
- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — Methodenlänge ≤60 Zeilen; `GetOrLoadAsync` bleibt unter 15 Zeilen

## Bekannte Ausnahmen

- `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` — vorbestehend, **nicht** Teil dieses Tasks. Wahrscheinliche Baseline-Aktualisierungen:
  - `src/SqlToAi/Security/AccessLevelProvider.cs` (verändert — Cache-Feld und Methode reduziert)
  - `src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs` (verändert — `RuleCacheEntry` entfernt, Cache-Aufruf reduziert)
  - **Neu:** `src/SqlToAi/Domain/TtlCache.cs` (muss zur Baseline hinzugefügt werden)
  - **Neu:** `tests/SqlToAi.Tests/Domain/TtlCacheTests.cs` (muss zur Baseline hinzugefügt werden)
  - SHA-256-Hashes der finalen Inhalte berechnen und in `SqlToAi-baseline.json` eintragen.
- `QueryExecutionServiceIntegrationTests.ExecuteQueryAsync_ShouldRespectDatabaseExclusions_AgainstRealTable` — vorbestehende Integrations-Ausnahme, unverändert.

## Notes

- **Speicherort von `TtlCache`:** Audit-Bericht schlägt `SqlToAi.Domain` oder einen neuen `SqlToAi.Caching`-Ordner vor. Empfehlung: `SqlToAi.Domain` (passt zu `AccessCheckResult` als Domain-Typ, vermeidet einen neuen Ordner, der nur eine einzige Klasse enthielte). Falls weitere Caching-Bausteine dazukommen, kann später `SqlToAi.Caching` entstehen.
- **Concurrency-Modell:** `ConcurrentDictionary<TKey, Entry>` mit lock-freiem TryGetValue und Indexer-Set. Das ist genau das Muster der bestehenden zwei Provider — keine Concurrency-Verschlechterung.
- **Kein Hintergrund-Refresh:** Der Cache ist rein lazy (Reload bei nächstem Zugriff nach Ablauf), kein Timer oder Hintergrund-Job. Das ist konsistent mit der bestehenden Implementierung.
- **Schnittstelle `IValidateOptions<T>`:** Falls `CacheTtlSeconds = 0` als Konfigurationsfehler behandelt werden soll (statt als „sofortiger Reload bei jedem Aufruf"), ist das **nicht** Aufgabe dieses Steps, sondern eine separate Konfigurations-Validierung. Der Audit-Bericht nennt diese Option im Empfehlungsteil, priorisiert sie aber nicht — daher als Optional im Notes dokumentiert.
- **Wahl der generischen Constraints:** `TKey : notnull` ist C# 14-Standard, verhindert `null`-Keys, die der `ConcurrentDictionary` ohnehin nicht akzeptiert. `TValue` ohne Constraint, weil AnonymizationRule-Liste und AccessLevel-Enum verschiedene Anforderungen haben.
- **Vorhandener `AccessCheckResult`:** Diese `record` ist im `SqlToAi.Domain`-Namespace und wird möglicherweise von externen Konsumenten (z. B. Test-Mocks) verwendet. Konservative Empfehlung: belassen, auch wenn er intern nicht mehr aktiv genutzt wird. Falls die Konsumenten-Analyse zeigt, dass `AccessCheckResult` nirgendwo extern verwendet wird, kann er in einem Folge-Step aufgeräumt werden.
