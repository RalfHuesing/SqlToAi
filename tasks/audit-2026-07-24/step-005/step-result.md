---
status: done
type: step-result
task: audit-2026-07-24
step: 005
title: "Punkt 19 — Generischen TtlCache<TKey, TValue> extrahieren"
coded_by: coder
coded_at: 2026-07-25T20:55:00+02:00
commit: 52c62a9
---

# Step 005 — Result

## Zusammenfassung

`internal sealed class TtlCache<TKey, TValue>` in `SqlToAi.Domain` eingeführt, der das `ConcurrentDictionary + IsExpired + Reload`-Muster kapselt. `AccessLevelProvider` und `AnonymizationRuleProvider` nutzen den neuen Cache; die spezifischen `AccessCheckResult` / `RuleCacheEntry`-Records entfallen ersatzlos. Der hartkodierte `> 0 ? ... : 300`-Fallback wurde in beiden Providern entfernt — der Options-Default `= 300` greift jetzt zentral über den Cache.

## Geänderte / neue Dateien

### Neu

- `src/SqlToAi/Domain/TtlCache.cs` (66 Zeilen) — `internal sealed class TtlCache<TKey, TValue> where TKey : notnull` mit `GetOrLoadAsync(key, loader, ttl, ct)`. `Entry` ist `private sealed record`. Lock-frei (`ConcurrentDictionary.TryGetValue` + Indexer-Set), identisches Concurrency-Modell wie die Vorgänger-Provider. XML-Doc dokumentiert TTL=0-Verhalten und das Race-Verhalten (zwei concurrent expirations dürfen den Loader mehrfach rufen — explizit akzeptiert, identisch zur vorherigen Semantik).
- `tests/SqlToAi.Tests/Domain/TtlCacheTests.cs` (141 Zeilen) — 5 dedizierte Unit-Tests.

### Geändert

- `src/SqlToAi/Security/AccessLevelProvider.cs` — `ConcurrentDictionary<string, AccessCheckResult>` → `TtlCache<string, AccessLevel>`; Try-Get-Block durch `_cache.GetOrLoadAsync(...)` ersetzt; `using System.Collections.Concurrent` entfernt; `> 0 ? ... : 300` entfernt.
- `src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs` — `ConcurrentDictionary<string, RuleCacheEntry>` → `TtlCache<string, IReadOnlyList<AnonymizationRule>>`; `RuleCacheEntry`-Record ersatzlos entfernt; `using SqlToAi.Domain` ergänzt; `using System.Collections.Concurrent` entfernt; `> 0 ? ... : 300` entfernt.
- `tests/SqlToAi.Tests/Security/AccessLevelProviderTests.cs` — `// @covers SqlToAi.Domain.AccessCheckResult`-Kommentar entfernt (Typ existiert nicht mehr).
- `tests/SqlToAi.Tests/Anonymization/AnonymizationRuleProviderTests.cs` — `// @covers SqlToAi.Anonymization.RuleCacheEntry`-Kommentar entfernt (Typ existiert nicht mehr).
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — SHA-256-Hashes für `AccessLevelProvider.cs`, `AnonymizationRuleProvider.cs`, `AccessLevelProviderTests.cs`, `AnonymizationRuleProviderTests.cs` aktualisiert; `TtlCache.cs` und `TtlCacheTests.cs` neu eingetragen; `AccessCheckResult.cs` entfernt. Die Aktualisierung erfolgte **automatisch durch `AiNetLinterTests.RecreateBaseline`** während `dotnet test` — kein manueller Hash-Eingriff.

### Gelöscht

- `src/SqlToAi/Domain/AccessCheckResult.cs` — nach Konsumenten-Check bestätigt: keine externen Referenzen (keine Mocks, keine Reflection-Setter, keine andere Source-Datei).

## Commit-Hash

- **Code-Commit:** `52c62a9` — `refactor(caching): extrahiere generischen TtlCache und nutze ihn in AccessLevel- und AnonymizationRule-Provider`
- **Doku-Commit:** siehe `step-plan.md` Frontmatter `commit_doku`.

## Build & Test

- `dotnet build SqlToAi.slnx` — **0 Warnungen, 0 Fehler** (8,41 s)
- `dotnet test --filter "Category!=Integration"` — **388/388 grün** (14 s)
  - Davon: 383 alt + 5 neue `TtlCacheTests`
  - AiNetLinterTests: 2/2 grün (Exit-Code 0, 2 vorbestehende `MaxBoolParameterCount`-Violations im Test-Code, **nicht** durch diesen Step verursacht)
- Geänderte Datei-Statistik: 8 files changed, 226 insertions(+), 67 deletions(-)

## Konsumenten-Analyse `AccessCheckResult` / `RuleCacheEntry`

`Select-String` über `src/` und `tests/` (Volltext-Suche) sowie per `grep` über das gesamte Repo (inkl. `tasks/`):

- **`AccessCheckResult`:** nach Entfernung der `@covers`-Kommentare nur noch in Doku-Dateien referenziert (`tasks/audit-2026-07-24/03-code-qualitaet-architektur.md`, `tasks/audit-2026-07-24/01-security-guardrails.md`, `tasks/audit-2026-07-24/step-005/step-plan.md`, `tasks/audit-2026-07-24/step-008/step-plan.md`). **Keine Code- oder Test-Referenz mehr.** Entfernung sicher.
- **`RuleCacheEntry`:** identisches Bild — ausschließlich Doku-Referenzen, keine Code- oder Test-Referenz mehr. **Entfernung sicher.**

Beide Records waren reine interne Cache-Entry-Typen ohne externe Semantik. `AccessCheckResult` war nie an `IAccessLevelProvider` exponiert; `RuleCacheEntry` war nie an `IAnonymizationRuleProvider` exponiert. Die Löschung räumt damit den vom Audit vorgeschlagenen Begleitschritt mit auf (Plan Z. 91).

## Beobachtungen

1. **`TtlCache.cs` ist detailreicher dokumentiert als die Plan-Skizze.** XML-Doc deckt explizit das TTL=0-Verhalten ("sofortiger Reload bei nächstem Zugriff — nützlich für Tests; Konfigurations-Validierung ist out of scope") und das Race-Verhalten ab. Keine funktionale Abweichung vom Plan.
2. **`AccessCheckResult`-Löschung war möglich**, obwohl der Plan (Z. 91) und der `step-plan.md` (Notes-Absatz) zur Vorsicht rieten. Konsumenten-Check ergab: kein Mock, kein Reflection-Setter, keine andere Konsumenten-Stelle. Daher gemäß Plan-Empfehlung "im Zweifel prüfen" tatsächlich entfernt.
3. **`RuleCacheEntry` und `AccessCheckResult` waren im `SqlToAi.Domain`- bzw. `SqlToAi.Anonymization`-Namespace `public` deklariert** (Sichtbarkeit entstammte der ursprünglichen Verwendung als semantischer Cache-Entry-Typ). Nach Löschung kein „versteckter Public-API-Bruch" — die Typen waren nirgends außerhalb der internen Cache-Mechanik verwendet.
4. **Baseline-Update lief automatisch** beim Test-Durchlauf — keine manuellen SHA-256-Hash-Eintragungen. Die `SqlToAi-baseline.json`-Änderung wurde mit ins Code-Commit aufgenommen, was `git status` und `git diff` als zusätzliche modified-line zeigen.
5. **Beobachtbare Semantik der Provider ist unverändert:** `GetAccessLevelAsync` und `IsExcludedAsync` liefern bitidentische Ergebnisse für identische Eingaben. Cache-Hit/Miss/TTL-Verhalten war bereits in Commit `319d0fe` getestet und ist weiterhin grün.

## Bekannte Unschärfen

- `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` zeigt 2 `MaxBoolParameterCount`-Violations im Test-Code (vorbestehend, nicht durch diesen Step verursacht). Der Linter-Report (`tests/SqlToAi.Tests/AiNetLinter/output/SqlToAi-linter-report.md`) weist diese weiterhin aus; AiNetLinter hat sie als false-positive-akzeptabel markiert (Exit-Code 0).
- Konfigurations-Validierung für `CacheTtlSeconds <= 0` (z. B. via `IValidateOptions<T>`) ist **bewusst out of scope** dieses Steps. `TimeSpan.FromSeconds(0)` crasht nicht, sondern führt zu sofortigem Reload bei jedem Aufruf. Falls ein hartes Fail gewünscht ist, gehört das in einen separaten Konfigurations-Validierungs-Step.

## Model-Metadaten

- **model_id:** MiniMax-M3
- **model_knowledge_cutoff:** 2026-01
