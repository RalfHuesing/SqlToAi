# Audit SqlToAi — Aktionsplan (2026-07-24)

Alle Findings wurden mit dem Repo-Owner einzeln durchgesprochen (abgenickt/abgelehnt/kommentiert). Diese Datei ist der verbindliche Umsetzungsplan. Details, Code-Traces und Fehlerszenarien je Finding stehen in den vier Teilberichten:

- [01-security-guardrails.md](01-security-guardrails.md)
- [02-anonymisierung-tokenisierung.md](02-anonymisierung-tokenisierung.md)
- [03-code-qualitaet-architektur.md](03-code-qualitaet-architektur.md)
- [04-tests-doku-konsistenz.md](04-tests-doku-konsistenz.md)

Jedes Finding trägt dort zusätzlich einen **Status**-Vermerk (✅ Umsetzen / ⛔ Won't-Fix / ⏸️ nicht entschieden), der mit dieser Datei übereinstimmt.

---

## Fortschritt (laufend aktualisiert — falls ein Agent abschmiert, hier weitermachen)

Reihenfolge = Abarbeitungsreihenfolge (nicht identisch mit der Nummerierung unten, siehe Zuordnung).

- [x] 1. Alias-Leak bei Anonymisierung/Tokenisierung
- [x] 2. sp_executesql + COMMIT Guard-Bypass
- [x] 3. Rohe Fehlermeldung an KI filtern
- [x] 4. QueryValidationService Unit-Tests
- [x] 5. sql_validate_query Guard nachrüsten
- [x] 6. Schema-blindes Ausschluss-/Regel-Matching
- [x] 7. Regel-Präzedenz-Scoring
- [x] 8. AccessLevelProvider numerische Tests
- [x] 17. Gemeinsamer Test-Fake-Baustein
- [x] 9. Doku *Id-Beispiel
- [x] 10. Totes Config-Paar entfernen
- [ ] 11. Fehlercodes 0105/0106 implementieren
- [ ] 12. Glob/SecurityGuard Wildcard-Tests
- [ ] 13. .bak Secret-Maskierung
- [ ] 14. Cache-TTL Doku-Hinweis
- [ ] 15. README Grenzen ergänzen
- [ ] 16. Demo-Passwort Kommentar
- [ ] 18. Gemeinsamer SQL-Tokenizer
- [ ] 19. Generischer TtlCache
- [ ] 20. SchemaService Helper
- [ ] 21. RenderMarkdownTable konsolidieren
- [ ] 22. Glob-Matcher konsolidieren

Jeder Punkt = ein eigener Commit auf `main` (Code/Doku + Tests + Markdown-Update zusammen). Vor jedem Commit: `dotnet build` (0 Warnungen/Fehler) und `dotnet test --filter "Category!=Integration"` grün (bekannte Baseline-Ausnahme: `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch`, vorbestehend, nicht Teil des Audits). Bekannte weitere Baseline-Ausnahme in der Integrationssuite: `QueryExecutionServiceIntegrationTests.ExecuteQueryAsync_ShouldRespectDatabaseExclusions_AgainstRealTable` (fehlende `CREATE TABLE`-Rechte des Demo-Logins in DemoDB, vorbestehend).

---

## Phase 1 — Kritische Sicherheitslücken (zuerst)

### 1. Anonymisierung/Tokenisierung über Spalten-Alias aushebelbar
**Wo:** `Anonymizer.cs:88-113`, `QueryExecutionService.cs:307-400`
**Wie:** `IsColumnExcluded` darf nicht mehr primär auf `reader.GetName(i)` (Alias) prüfen, sondern auf den über `GetSchemaTable()` aufgelösten echten Ursprung (Basistabelle + Basisspaltenname). Ist kein Ursprung auflösbar (reines Berechnungsfeld), **nicht** ausschließen (konservativ anonymisieren). Betrifft auch den Token-Rückweg identisch.
→ Details: [01-security-guardrails.md](01-security-guardrails.md), Finding 1

### 2. Read-Only-Guard über `sp_executesql` + eingebettetes `COMMIT` umgehbar
**Wo:** `ReadOnlyGuard.cs:13-16`, `QueryExecutionService.cs:147-195,415-439`
**Wie:** `sp_executesql`/`sys.sp_executesql` explizit sperren (auch ohne `EXEC`-Prefix, auch als Literal-Inhalt). Zusätzlich vor/nach jeder Ausführung `@@TRANCOUNT` hart prüfen (z. B. mit `SET XACT_ABORT ON`) statt sich nur auf `Rollback` zu verlassen — bei Abweichung harter Fehler statt stillem Fehlschlag.
→ Details: [01-security-guardrails.md](01-security-guardrails.md), Finding 2

### 3. Rohe SQL-Fehlermeldung geht ungefiltert an die KI (kann Klartext-Werte enthalten)
**Wo:** `QueryExecutionService.cs:139-145,190-194`, `ToolDispatcher.cs:132,216`
**Wie:** `ex.Message` vor `SqlToAiError.QueryError(...)` filtern/generalisieren (z. B. nur Fehlercode + generische Kategorie an die KI). **Das Datei-Logging der Klartext-Query bleibt unverändert** — das ist bewusst so gewollt (Admin braucht die Query zur Fehlerverifikation, hat ohnehin Serverzugriff). Nur der KI-Antwortpfad wird gefiltert.
→ Details: [01-security-guardrails.md](01-security-guardrails.md) Finding 3 / [02-anonymisierung-tokenisierung.md](02-anonymisierung-tokenisierung.md) Finding 1

### 4. `QueryValidationService` — keine Unit-Tests für die Guard-Klauseln
**Wo:** `src/SqlToAi/Database/QueryValidationService.cs`
**Wie:** Neue `QueryValidationServiceTests.cs` analog `QueryExecutionServiceTests.cs` (Fakes für `ISecurityGuard`/`IAccessLevelProvider`, kein echter DB-Zugriff nötig). Mindestens: leere/leere-Whitespace Parameter, nicht erlaubte DB, `AccessLevel.None`, Rollback auch im Erfolgsfall.
→ Details: [04-tests-doku-konsistenz.md](04-tests-doku-konsistenz.md), Teil A

### 5. `sql_validate_query` ohne Read-Only-Guard/Multi-Statement-Schutz
**Wo:** `QueryValidationService.cs:47-97`
**Wie:** Dieselben `IReadOnlyGuard`- und `ContainsMultipleStatements`-Prüfungen wie in `QueryExecutionService` defensiv ergänzen, unabhängig davon ob die konkrete `PARSEONLY`-Umgehung real ausnutzbar ist.
→ Details: [01-security-guardrails.md](01-security-guardrails.md), Finding 4

---

## Phase 2 — Wichtige Härtung (Anonymisierungs-/Regel-Logik)

### 6. Ausschluss-/Regel-Matching ist schema-blind
**Wo:** `QueryExecutionService.cs:362-390` (`GetBaseTableNames`), `AnonymizerExclusionProvider.cs:167-186`, `sql-scripts/02_anonymizer_exclusions.sql`, `03_anonymization_rules.sql`
**Wie:** `BaseSchemaName` zusätzlich auflesen und in Ausnahme-/Regeltabellen als optionale Spalte/Musterkomponente ergänzen (Default `%`, rückwärtskompatibel), damit gleichnamige Tabellen in unterschiedlichen Schemas nicht dieselbe Freigabe erben.
→ Details: [02-anonymisierung-tokenisierung.md](02-anonymisierung-tokenisierung.md), Finding 2

### 7. Regel-Präzedenz: DB-Spezifität schlägt Spalten-Spezifität
**Wo:** `AnonymizationRuleProvider.cs:130-157` (`FindMostSpecificMatch`)
**Wie:** Scoring von gewichteter Summe (`DB*100 + Tabelle*10 + Spalte`) auf echten Tupel-Vergleich `(DB, Tabelle, Spalte)` umstellen, sodass eine breite DB-Regel eine exakte, universelle Spalten-Schutzregel nicht mehr aushebeln kann.
→ Details: [02-anonymisierung-tokenisierung.md](02-anonymisierung-tokenisierung.md), Finding 3

### 8. `AccessLevelProvider.ParseAccessLevel` — numerischer Zweig ungetestet
**Wo:** `AccessLevelProvider.cs:72-104,138-168`
**Wie:** Theory-Tests für alle numerischen Werte 0-4, einen ungültigen Wert (z. B. `99`), einen nicht-parsbaren String, den Einzelspalten-Fallback und den "keine Zeile"-Fall ergänzen.
→ Details: [04-tests-doku-konsistenz.md](04-tests-doku-konsistenz.md), Teil A

---

## Phase 3 — Doku & Konfigurationshygiene

### 9. Beispiel `*Id` in `ExcludedColumns` schließt fachliche PII-Spalten aus
**Wie:** Doku-Beispiel präzisieren bzw. Warnhinweis ergänzen, dass `*Id` auch fachliche Kennungen (`SteuerId`, `PassportId`) erfasst.
→ [02-anonymisierung-tokenisierung.md](02-anonymisierung-tokenisierung.md), Finding 4

### 10. Totes Config-Paar `SqlServer.EnforceSafetyCheck`/`SafetyCheckSql`
**Wie:** Entfernen (Options-Klasse, `ConfigurationResolver`-Auswertung, ggf. Migrationslogik in `AppSettingsMigrator`).
→ [04-tests-doku-konsistenz.md](04-tests-doku-konsistenz.md), Teil B

### 11. Fehlercodes `SQL-AI-0105`/`0106` werden nie erzeugt
**Wie:** In `QueryExecutionService`/`QueryValidationService` gezieltes Exception-Mapping ergänzen (`SqlException`/Verbindungsfehler → `InfrastructureError`, `TimeoutException` → `Timeout()`), statt alles auf `SQL-AI-0102` zu mappen.
→ [04-tests-doku-konsistenz.md](04-tests-doku-konsistenz.md), Teil B

### 12. `GlobPatternMatcher`/`SecurityGuard.MatchesPattern` — `?`-Wildcard & Sonderzeichen ungetestet
**Wie:** Je eine Theory-Testreihe ergänzen (analog `LikePatternMatcherTests.cs`): `?`-Wildcard, Regex-Sonderzeichen im Namen, leeres Pattern/Text, Timeout-Fallback.
→ [04-tests-doku-konsistenz.md](04-tests-doku-konsistenz.md), Teil A

### 13. `.bak`-Datei dupliziert Secrets unkontrolliert
**Wie:** Beim Erstellen des Backups das `Password`-Feld maskieren, sofern nicht per Env-Var referenziert.
→ [01-security-guardrails.md](01-security-guardrails.md), Finding 5

### 14. Cache-TTL kann Rechte-/Ausnahme-Downgrade verzögern
**Wie:** Nur Doku-Ergänzung — Hinweis "Server neu starten für sofortige Wirkung bei Incident Response" in README/mcp-specification.md.
→ [01-security-guardrails.md](01-security-guardrails.md) Info-1 / [02-anonymisierung-tokenisierung.md](02-anonymisierung-tokenisierung.md) Niedrig-1

### 15. README fehlen zwei bekannte Grenzen (nur in mcp-specification.md dokumentiert)
**Wie:** Hinweise "DDL-Inhalte werden nie anonymisiert" und "Nicht-String-PII (Geburtsdatum, numerische IDs) wird nie anonymisiert" zusätzlich ins README übernehmen.
→ [02-anonymisierung-tokenisierung.md](02-anonymisierung-tokenisierung.md), Info-1 und Info-2

### 16. Klartext-Demo-Passwort im Factory-Default
**Wie:** Kommentar im appsettings.json-Template ergänzen, dass das Demo-Passwort vor Produktivnutzung zu ändern ist.
→ [01-security-guardrails.md](01-security-guardrails.md), Info-2

---

## Phase 4 — Architektur-Aufräumarbeit (DRY, risikofrei bis niedrig-riskant)

### 17. Gemeinsamer Test-Fake-Baustein (bester Einstieg — reine Testinfrastruktur, kein Produktionsrisiko)
**Wo:** 4× ADO.NET-Fake-Stack in `tests/SqlToAi.Tests/Database/*MockDb.cs`, `Anonymization/AnonymizationRuleProviderMockDb.cs`, `Metadata/MetadataProviderMocks.cs`
**Wie:** Neuen Ordner `tests/SqlToAi.Tests/TestSupport/` mit generischer `FakeDbConnection`/`FakeDbCommand`/`FakeDbParameterCollection`/`FakeDbParameter` und einem tabellenbasierten Reader (Basis: bereits vorhandenes `MockDataTableReader`). Jede Testklasse liefert nur noch ihre Dispatch-Funktion. ~400-500 Zeilen Einsparung.
→ [03-code-qualitaet-architektur.md](03-code-qualitaet-architektur.md), DRY-Impact Hoch #2

### 18. Gemeinsamer SQL-Tokenizer-Primitiv
**Wo:** `QueryExecutionService.cs:406-490`, `ReadOnlyGuard.cs:49-137`, `SqlLiteralScanner.cs:16-103`
**Wie:** Internen `SqlCharScanner` extrahieren, der Kommentar-/String-Literal-/Bracket-Erkennung als gemeinsame Mechanik liefert; die drei Call-Sites setzen ihre bestehende, unveränderte Business-Logik (Semikolon-Zählung, Content-Blanking, Range-Erfassung) darauf auf. Bestehende Tests bleiben grün. ~150-180 Zeilen Einsparung.
→ [03-code-qualitaet-architektur.md](03-code-qualitaet-architektur.md), DRY-Impact Hoch #1

### 19. Generischer `TtlCache<TKey,TValue>`
**Wo:** `AnonymizerExclusionProvider.cs`, `AnonymizationRuleProvider.cs`, `AccessLevelProvider.cs`
**Wie:** Gemeinsame Cache-Klasse mit `GetOrLoadAsync(key, loader, ttl, ct)` bauen; die drei Provider reduzieren sich auf ihre fachliche Lade-Logik. Erledigt nebenbei den hartkodierten `300`-Fallback (Linter-Fund Teil A).
→ [03-code-qualitaet-architektur.md](03-code-qualitaet-architektur.md), DRY-Impact Mittel #2

### 20. `SchemaService` — gemeinsamer Helper für sechs Delegationsmethoden
**Wo:** `SchemaService.cs:218-348`
**Wie:** Privaten Helper `ExecuteDetailQueryAsync(databaseName, paramName, queryFunc, operationName, ct)` extrahieren, der Access-Check/Connection/Try-Catch/Logging einmal kapselt. ~130→~40 Zeilen.
→ [03-code-qualitaet-architektur.md](03-code-qualitaet-architektur.md), DRY-Impact Mittel #4

### 21. `RenderMarkdownTable` konsolidieren
**Wo:** `SchemaService.cs`, `DetailSchemaRenderer.cs`, `TableSchemaRenderer.cs`
**Wie:** In gemeinsame `internal static class MarkdownTableRenderer` extrahieren.
→ [03-code-qualitaet-architektur.md](03-code-qualitaet-architektur.md), DRY-Impact Mittel #1

### 22. Glob-Matcher konsolidieren
**Wo:** `GlobPatternMatcher.cs` (Anonymization), `SecurityGuard.cs:60-80` (Security)
**Wie:** `GlobPatternMatcher` als neutrales Utility erkennen und verschieben (z. B. `SqlToAi.Domain`, `public`), `SecurityGuard` ruft dieselbe Methode auf statt sie zu duplizieren.
→ [03-code-qualitaet-architektur.md](03-code-qualitaet-architektur.md), DRY-Impact Niedrig #1

---

## Bewusst nicht umgesetzt (Won't-Fix)

| Finding | Begründung |
|---|---|
| Klartext-Query im Error-Log (Teil von #3) | Admin braucht die Query zur Fehlerverifikation, hat ohnehin SQL-Server-Zugriff — akzeptiertes Risiko, kein Fremdzugriff auf die Log-Datei vorausgesetzt. |
| CI-Pipeline mit `dotnet test` | Bewusst abgelehnt — keine Testautomatisierung in CI gewünscht. Testsuite bleibt rein lokal/manuell ausgeführt. |

## Nicht zur Entscheidung vorgelegt (optional, niedrigste Priorität)

- `ToolRegistry`-Duplikation (6 identische Property-Schema-Fragmente) — reine Datenwiederholung ohne Logik-Risiko, siehe [03-code-qualitaet-architektur.md](03-code-qualitaet-architektur.md).

---

## Empfohlene Reihenfolge

Phase 1 (Sicherheit, 5 Punkte) → Phase 2 (Anonymisierungs-Härtung, 3 Punkte) → Phase 4 Punkt 17 (Test-Fakes, risikofrei, macht spätere Testarbeit leichter) → Phase 3 (Doku/Config, günstig) → restliche Phase 4 (DRY-Aufräumarbeit).
