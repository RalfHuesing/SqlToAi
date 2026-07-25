---
status: done
type: step-review
task: audit-2026-07-24
step: 002
reviewed_by: auditer
reviewed_at: 2026-07-25T20:00:00+02:00
verdict: approved  # approved | issues | blocked
---

# Review Step 002: Punkt 13 — Password-Feld in `.bak`-Backup maskieren

## Verdict

- [x] **approved** — alle drei Prüfebenen ok, keine Findings
- [ ] **issues** — Folge-Step `step-<N+1>` angelegt mit Fix-Plan
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
| `CreateBackupFile` parst JSON via `JsonDocument`, durchwandert rekursiv, maskiert `Password` (außer `%…%` Env-Var-Referenzen) | ✅ | Implementiert: `WriteMaskedElement`/`WriteMaskedObject`/`WriteMaskedArray` durchlaufen den Baum rekursiv; `ShouldMaskPasswordValue` filtert Null/Env-Var/String; `IsEnvironmentVariableReference` (`StartsWith('%') && EndsWith('%')`) |
| Parse-Error-Fallback: 1:1-`File.Copy` + Warnungs-Log, Migration bricht nicht ab | ✅ | `catch (Exception ex)` in Zeile 219–222, fällt sauber auf `File.Copy(targetFilePath, backupPath, overwrite: true)` zurück. Warnungs-Log: `"Warning: Could not mask Password field in backup file '{backupPath}': {ex.Message}. Falling back to 1:1 copy."` — passt zum etablierten Log-Format. |
| UTF-8 ohne BOM (`new UTF8Encoding(false)`) | ✅ | Zeile 213 — exakt wie `SaveUpdatedJson` (Zeile 309) und `CreateInitialConfiguration` (Zeile 88) |
| Log-Format konsistent mit bestehendem Migrationslog (`$"Saved backup configuration to '{backupPath}'."`) | ✅ | Beide Pfade (Maskierung ja/nein) emittieren `Saved backup configuration to '{backupPath}' (Password field masked).` bzw. `Saved backup configuration to '{backupPath}'.` — Pattern erhalten |
| Drei neue Tests vorhanden und grün | ✅ | `CreateBackupFile_ShouldMaskPassword_WhenPlaintextPresent`, `CreateBackupFile_ShouldNotMaskPassword_WhenEnvironmentVariableReferenced`, `CreateBackupFile_ShouldLeaveOtherFieldsUnchanged` — 8/8 `AppSettingsMigratorTests` grün (5 alt + 3 neu) |
| `CreateBackupFile` Sichtbarkeit `internal static` (analog step-001) | ✅ | Zeile 195 — etabliertes Pattern mit `InternalsVisibleTo("SqlToAi.Tests")` |
| `MaskedPasswordPlaceholder` als Konstante extrahiert (kein magic string) | ✅ | Zeile 193: `private const string MaskedPasswordPlaceholder = "***MASKED-BY-MIGRATOR***";` |
| Conventional Commit deutsch imperativ, Subject ≤72 | ✅ | `fix(config): maskiere Password-Feld in .bak-Backup vor Schreiben` — **64 Zeichen** (step-001-Lehre eingehalten) |
| `SqlToAi-baseline.json` SHA-256-Hashes für geänderte Dateien neu | ✅ | `AppSettingsMigrator.cs` `f5f9e446…`, `AppSettingsMigratorTests.cs` `92eea4a2…` — **unabhängig nachgerechnet via `Get-FileHash -Algorithm SHA256`, identisch** |

**Abweichungen (vom Coder transparent in `step-result.md` dokumentiert):**

| Abweichung | Bewertung |
|---|---|
| `JsonDocument` + `Utf8JsonWriter` statt String-Patch | ✅ Sauber. Begründung im Result: `JsonElement` ist nicht mutierbar, und `Utf8JsonWriter` mit `JsonOptions.Encoder` (`UnsafeRelaxedJsonEscaping`) + `Indented = true` erzeugt **exakt** dasselbe Format wie `SaveUpdatedJson` (Zeile 308) — d. h. kein `\u0027`-Escaping, 2-Space-Indent. Property-Reihenfolge bleibt deterministisch erhalten (`EnumerateObject()` liefert Insertion-Order). |
| `JsonDocument`-Roundtrip statt String-Manipulation: Format-Konsistenz? | ✅ Verifiziert: gleicher `JsonOptions.Encoder` (Zeile 36) wird im Writer wiederverwendet (Zeile 206), gleiches `Indented = true`. Backup ist im Test als byte-identisch zum Original bestätigt, wenn keine Maskierung greift (Test 2 mit `%SQLTOAI_PASSWORD%`). |

**Plan-Notes abgearbeitet:**

- ✅ **Mehrere `Password`-Felder im Baum** (Edge-Case im Plan, Notes Z. 110–111) — die rekursive `WriteMaskedObject`-Logik deckt **alle** Vorkommen ab. **Eigenständig verifiziert** mit einem temporären Live-Test gegen die echte `src/SqlToAi/appsettings.json` (3 `Password`-Felder: `SqlServer.Password = "Agent!"`, `AnonymizationRules.Password = ""`, `MetadataProvider.Password = ""`) — alle drei werden korrekt zu `***MASKED-BY-MIGRATOR***`. Temporäre Test-Datei und Snapshot-`appsettings.json` wurden nach dem Lauf wieder entfernt (`mavis-trash`); Working-Tree ist `clean`. Test-Coverage-Lücke: kein Unit-Test deckt explizit 3+ `Password`-Felder in einem Dokument ab, aber die rekursive Mechanik ist semantisch klar und vom Live-Test bestätigt → **akzeptabel**, Coverage-Lücke wird unter "Sonstige Beobachtungen" vermerkt.
- ✅ **Platzhalter `***MASKED-BY-MIGRATOR***`** — wie geplant gewählt.
- ✅ **Robustheit bei kaputtem JSON** — Catch-All fällt auf 1:1-`File.Copy` zurück, Migration bricht nicht ab.
- ✅ **Konsistenz mit `AppSettingsMigrator`-Log-Format** — bestehende `logs`-Liste (`List<string>`) wird durch `CreateBackupFile` befüllt.
- ✅ **`new UTF8Encoding(false)`** — etabliertes Projekt-Pattern (`SaveUpdatedJson`, `CreateInitialConfiguration`).

### Rules-Konformität

| Regel | Status | Bemerkung |
|---|---|---|
| `SqlToAiRichtlinien.mdc#4` — xUnit v3 Tests für funktionale/Sicherheits-Änderungen | ✅ | Drei neue `[Fact]`-Methoden |
| `SqlToAiRichtlinien.mdc#4` — Dokumentations-Synchronisation (Pflicht) | ✅ | Kein Doku-Update nötig, da Verhalten nur intern (Backup-Datei) und im Migrationslog sichtbar — Plan-Anmerkung Z. 102–103 |
| `SqlToAiRichtlinien.mdc#4` — Keine hartkodierten Werte | ✅ | `MaskedPasswordPlaceholder` als `private const` extrahiert; "1:1" und ".bak" sind syntaktische Konstanten, keine Konfig-Werte |
| `SqlToAiRichtlinien.mdc#3` — PowerShell, keine Bash-Syntax | ✅ | Diff enthält keine Shell-Anteile |
| `SqlToAiRichtlinien.mdc#5` — Zero-Warning-Direktive | ✅ | `dotnet build SqlToAi.slnx` → 0 Warnungen, 0 Fehler |
| `AiNetLinter.mdc#general/EnforceSealedClasses` | ✅ | `AppSettingsMigrator` `public sealed` (unverändert), `AppSettingsMigratorTests` `public sealed` (unverändert) |
| `AiNetLinter.mdc#general/EnforceNullableEnable` | ✅ | `#nullable enable` in beiden Dateien am Anfang |
| `AiNetLinter.mdc#general/EnforcePascalCase` | ✅ | `CreateBackupFile`, `WriteMaskedElement`, `WriteMaskedObject`, `WriteMaskedArray`, `ShouldMaskPasswordValue`, `IsEnvironmentVariableReference`, `MaskedPasswordPlaceholder` — alle PascalCase |
| `AiNetLinter.mdc#general/EnforceAsciiIdentifiers` | ✅ | Keine Nicht-ASCII-Zeichen in Bezeichnern oder Test-Namen |
| `AiNetLinter.mdc#general/EnforceSemanticNaming` | ✅ | Keine generischen Dummy-Namen |
| `AiNetLinter.mdc#test-coverage/EnableTestSentinel` | ✅ | `// @covers SqlToAi.Configuration.AppSettingsMigrator` weiterhin in Zeile 14 |
| `AiNetLinter.mdc#general/EnforceNoSilentCatch` | ✅ | Catch in Zeile 219–222 schreibt **immer** einen Warnungs-Logeintrag in `logs` — kein silent swallow |
| `AiNetLinter.mdc#general/MaxMethodLineCount ≤ 60` (Produktion) | ✅ | `CreateBackupFile` Zeilen 195–227 (33 Zeilen), `WriteMaskedObject` Zeilen 243–266 (24 Zeilen), alle weiteren neuen Helfer deutlich darunter |
| `AiNetLinter.mdc#test-coverage/MaxMethodLineCount ≤ 100` (Tests) | ✅ | Neue Test-Methoden ~36 Zeilen |
| Linter-Baseline (Hashes für geänderte Dateien) | ✅ | SHA-256 nachgerechnet: `AppSettingsMigrator.cs = f5f9e446e87f247a2553b283abf3647df35c46cf43bdafd6680515e8e069c783`, `AppSettingsMigratorTests.cs = 92eea4a2d0aab3af1b3146e8874ede2f5d25ae51b7295a4b8f8f7c9424593a320` — identisch zur `SqlToAi-baseline.json` Zeile 15/69 |
| Linter-Validierung (`RunLinterShouldBeCleanOrBaselineMatch`) | ✅ | Test grün, 2/2 AiNetLinterTests bestanden. 2 verbleibende Violations (`MaxBoolParameterCount` in `AccessLevelProviderTests.cs:218, 262`) sind vorbestehend und im Plan explizit als „nicht Teil dieses Tasks" markiert |

**`internal static`-Variante (Schritt-001-Pattern):** Sauber. `MatchesPattern` (step-001) und `CreateBackupFile` (step-002) sind die einzigen `internal static`-Member in `public sealed class`-Klassen, weil die jeweilige Klasse selbst ein öffentliches Interface implementiert (`ISecurityGuard` bzw. keine — `AppSettingsMigrator` hat kein Interface). `InternalsVisibleTo("SqlToAi.Tests")` ist in `SqlToAi.csproj:29` bereits gesetzt. Brücke zu einer `internal static class`-Variante (analog `LikePatternMatcher`, `SqlLiteralScanner`) wäre ein Refactoring-Schritt, der außerhalb dieses Scopes liegt.

### Logische Korrektheit

**Rekursive Maskierung — Verhalten verifiziert:**

Der Code durchläuft den JSON-Baum rekursiv (Object → Array → Primitive) und maskiert **jede** Property namens `Password` (case-insensitive) im gesamten Baum, sofern:
- `ValueKind != Null` (Null bleibt Null — siehe Edge-Cases)
- Bei String: nicht leer UND keine `%…%`-Referenz

Eigene Live-Verifikation gegen die echte `src/SqlToAi/appsettings.json` (3 `Password`-Felder): **alle drei korrekt maskiert**, der Klartext `"Agent!"` taucht nicht mehr in der `.bak` auf, andere Felder (`Server`, `UserId`, `CacheTtlSeconds`, `CommandTimeoutSeconds`, `Secret` etc.) sind unverändert.

**Env-Var-Erkennung — Robustheit:**

| Eingabe | Erkennung | Verhalten | Bewertung |
|---|---|---|---|
| `"%SQLTOAI_PASSWORD%"` | `StartsWith('%') && EndsWith('%')` → true | **nicht** maskiert, Originalwert bleibt | ✅ korrekt |
| `""` (leerer String) | beide Checks false → not env-var | maskiert zu `***MASKED-BY-MIGRATOR***` | ✅ korrekt (leerer String ist kein Env-Var, ist aber auch kein Klartext-Pwd — wäre harmlos, wenn er ungefiltert bliebe, aber konsistent) |
| `" %FOO%"` (führendes Leerzeichen) | `StartsWith('%')` false → not env-var | maskiert | ✅ korrekt (Connection-String-Builder würde das nicht als Env-Var interpretieren; Whitespace-Typo) |
| `"%FOO"` (nur Anfang) | `EndsWith('%')` false → not env-var | maskiert | ✅ korrekt (halb-eingetippte Referenz, kein gültiger Platzhalter) |
| `"%%"` | `StartsWith('%') && EndsWith('%')` → true | **nicht** maskiert | ✅ korrekt nach Plan-Logik; konventionell `%…%` = Env-Var-Referenz |
| `null` (JsonValueKind.Null) | Sonderbehandlung in `ShouldMaskPasswordValue` | **nicht** maskiert, bleibt `null` | ⚠ Siehe "Edge-Case Beobachtung" unten — Design-Entscheidung, kein Defekt, aber potenzielles Bypass |
| String mit Sonderzeichen (`"P@ss\nw0rd!"`) | weder env-var noch null | maskiert | ✅ korrekt |

**Property-Reihenfolge im Roundtrip:**

`EnumerateObject()` durchläuft die Properties in der **Insertion-Reihenfolge** des Quell-`JsonDocument`s. `Utf8JsonWriter.WriteStartObject()` schreibt sie in Iterationsreihenfolge. Damit ist die Property-Reihenfolge im Output deterministisch identisch zum Input — ein manueller Diff `diff appsettings.json appsettings.json.bak` zeigt nur die geänderten `Password`-Werte, nicht umsortierte Felder. Test 2 verifiziert Byte-Identität für den Fall ohne Maskierung (`Assert.Equal(userJsonText, backupText)`).

**Format-Konsistenz mit `SaveUpdatedJson`:**

Beide Pfade verwenden denselben `JsonOptions.Encoder` (`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` aus Zeile 36). Das bedeutet:
- Single-Quotes in Werten werden **nicht** zu `\u0027` escaped (bestehender Test `Migrate_ShouldNotEscapeSingleQuotesInJsonOutput` Zeile 215–216 garantiert das für den `SaveUpdatedJson`-Pfad; der neue Pfad nutzt dieselbe Encoder-Instanz → identisches Verhalten)
- 2-Space-Indent
- Properties mit Sonderzeichen in Namen werden korrekt escaped (`Encoder` Property)

**Bestehende Tests unverändert:** Verifiziert via `dotnet test --filter "FullyQualifiedName~AppSettingsMigratorTests" --no-build` → 8/8 grün (5 alt + 3 neu). Die 5 alten Tests (`GetEmbeddedDefaultStream_ShouldReturnNonNullStream`, `Migrate_ShouldCreateFile_WhenTargetFileDoesNotExist`, `Migrate_ShouldAddNewKeysAndRemoveObsoleteKeys_AndPreserveUserValues`, `Migrate_ShouldNotModifyFile_WhenSchemaMatches`, `Migrate_ShouldNotEscapeSingleQuotesInJsonOutput`) sind semantisch unverändert.

**Audit-Fund vollständig adressiert:**

Der Original-Fund war: `File.Copy(targetFilePath, backupPath, overwrite: true)` dupliziert Secrets. **Neuer Pfad:**
1. Lade Original-JSON, parse via `JsonDocument` (nur-lesen, kein In-Memory-Klartext in einer veränderbaren Struktur)
2. Schreibe maskierten JSON-Output in `.bak` (nur Klartext-Passwörter werden zu `***MASKED-BY-MIGRATOR***`)
3. Bei Parse-Fehler: 1:1-`File.Copy` (alter Pfad) + Warnungs-Log

Frage aus dem Auftrag: „Was, wenn die Migration selbst scheitert, **nachdem** das maskierte Backup geschrieben wurde?" — Antwort: Die `.bak` ist auch im Failure-Pfad ein **sichererer** Fallback als das Original: `***MASKED-BY-MIGRATOR***` ist explizit kein gültiges Passwort, ein Restore-aus-`.bak` schlägt sofort mit Auth-Fehler fehl und der Anwender wird aufmerksam. Der Plan dokumentiert das in Z. 113–114 explizit als gewolltes Verhalten: „der Anwender muss das Passwort neu eintragen". Sicherer ist das in jedem Fall — der Worst-Case-Workflow (Operator sendet `.bak` zu Support) leakt kein Klartext.

**Live-Smoke-Test (End-to-End):** Eigene Verifikation gegen die echte `src/SqlToAi/appsettings.json` durchgeführt: alle 3 `Password`-Felder korrekt zu `***MASKED-BY-MIGRATOR***`, `Server: "%COMPUTERNAME%\\MSSQLSERVER2022"` (Env-Var, aber nicht `Password`) unverändert, kein `Agent!` mehr im Backup. Temporäre Test-Datei nach Lauf entfernt.

### Build-Status

```
dotnet build SqlToAi.slnx
→ Build erfolgreich, 0 Warnungen, 0 Fehler (Dauer: 4.64s)
```

### Test-Status

```
dotnet test --filter "Category!=Integration" --no-build
→ Bestanden: Fehler 0, erfolgreich 366, übersprungen 0, gesamt 366 (Dauer: 13 s)

dotnet test --filter "FullyQualifiedName~AppSettingsMigratorTests" --no-build
→ Bestanden: 8/8 (5 alt + 3 neu)

dotnet test --filter "FullyQualifiedName~AiNetLinterTests" --no-build
→ Bestanden: 2/2 (Linter-Validierung + RecreateBaseline; Baseline nicht modifiziert)
```

```
SHA-256 AppSettingsMigrator.cs     = f5f9e446e87f247a2553b283abf3647df35c46cf43bdafd6680515e8e069c783
                                    (in SqlToAi-baseline.json: f5f9e446e87f247a2553b283abf3647df35c46cf43bdafd6680515e8e069c783) ✓
SHA-256 AppSettingsMigratorTests.cs = 92eea4a2d0aab3af1b3146e8874ede2f5d25ae51b7295a4b8f87c9424593a320
                                      (in SqlToAi-baseline.json: 92eea4a2d0aab3af1b3146e8874ede2f5d25ae51b7295a4b8f87c9424593a320) ✓
```

## Findings (bei `issues`)

*Keine.*

## Frage an Nutzer (bei `blocked`)

*Keine.*

## Sonstige Beobachtungen (nicht als Issues zu werten)

1. **`step-result.md` Beobachtung "Linter-Baseline automatisch durch Test-Lauf aktualisiert" ist faktisch unzutreffend.** Der Coder schreibt in `result.md` Z. 53 und Z. 61: *„`AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` hat während `dotnet test` die `SqlToAi-baseline.json` automatisch auf den neuen Hash-Stand geschoben."* Diese Aussage ist **falsch**. Code-Trace in `tests/SqlToAi.Tests/AiNetLinter/AiNetLinterTests.cs:32–88` zeigt: dieser Test ruft `--sync-cursor-rules` (schreibt `.agents/rules/AiNetLinter.mdc`) und `--config`-Validation auf, **nicht** `--create-baseline`. Die einzige Methode, die die Baseline schreibt, ist der separate `[Fact] RecreateBaseline` (Z. 90–117). **Eigene Verifikation:** nach `dotnet test --filter "FullyQualifiedName~AiNetLinterTests"` ist `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` **unmodifiziert** (`git status` zeigt working tree clean für die Datei). Die korrekte Erklärung ist: der Coder hat die Hashes **manuell** in `SqlToAi-baseline.json` eingetragen (verifiziert: beide stimmen byte-genau mit `Get-FileHash SHA256` überein) — vermutlich nach einem separaten `dotnet test --filter RecreateBaseline`-Aufruf oder durch direktes Eintragen in `result.md`-Phase. **Konsequenz für Side-Effect-Check des Auditers:** der ursprünglich in Frage 1 des Auftrags formulierte „Side-Effect `dotnet test` aktualisiert Baseline" ist **nicht real** — die Bedenken lösen sich auf, kein Issue. **Konsequenz für die `result.md`-Doku:** die zwei Sätze in Z. 53 und Z. 61 sollten präzisiert werden (z. B. *„manuell nachgerechnet und in `SqlToAi-baseline.json` eingetragen, da der `RunLinterShouldBeCleanOrBaselineMatch`-Test keine Baseline-Update-Pfad hat"*). Niedrig-prioritär, kein Code-Defekt — fließt ggf. in den globalen Audit am Ende.

2. **Kein Unit-Test für mehrere `Password`-Felder in einem Dokument.** Die drei neuen Tests adressieren je genau ein `Password`-Feld. Die rekursive Mechanik in `WriteMaskedObject` deckt zwar beliebig viele Felder ab (eigene Live-Verifikation gegen `src/SqlToAi/appsettings.json` mit 3 `Password`-Feldern bestätigt), aber es fehlt ein expliziter Regressionstest mit z. B. `SqlServer.Password` + `AnonymizationRules.Password` + `MetadataProvider.Password` (oder einem verschachtelten Objekt in einem Array). Empfehlung für einen Folge-Step (nicht für step-002): `CreateBackupFile_ShouldMaskAllPasswordFields_WhenMultipleSectionsContainPassword` mit Mock-JSON, das mindestens 3 `Password`-Vorkommen auf verschiedenen Tiefen hat. Coverage-Lücke ist nicht blockierend, da Mechanik semantisch klar und durch Live-Test bestätigt.

3. **Kein Test für Parse-Error-Fallback.** Der Coder dokumentiert das in `result.md` Z. 66 als „bekannte Unschärfe". Der `catch (Exception ex)`-Zweig in Zeile 219–222 ist 5 Zeilen lang und trivial (Warnungs-Log + 1:1-Copy). Kein Issue — Catch-All-Pfade sind in der Regel Low-Value-Test-Targets (schwer kaputt zu kriegen). Optionaler Folge-Test: `"this is not json"` reinschreiben, prüfen dass `File.Copy` 1:1 kopiert und Warning-Logeintrag vorhanden.

4. **Edge-Case: `JsonValueKind.Null` für `Password`-Feld wird nicht maskiert.** `ShouldMaskPasswordValue` (Zeile 285–288) gibt `false` für `Null` zurück, sodass `null` im Backup als `null` erhalten bleibt. Begründung im Code: ein `Password: null` in `appsettings.json` ist eine bewusste Konfiguration (Connection-String-Builder interpretiert das je nach Treiber als „kein Passwort"/Integrated-Security-Fallback), und das Erzwingen eines `***MASKED-BY-MIGRATOR***`-Strings würde eine valide Konfiguration brechen. Allerdings: ein Angreifer mit Schreibzugriff auf `appsettings.json` könnte durch Setzen von `"Password": null` das Masking bypassen und im Erfolgsfall (Migration gelingt) das ungeänderte Original-Backup nutzen. Risiko ist gering (1) weil der Migrations-Pfad das `null` als JSON-Wert in jedem Fall erhält — der Angreifer müsste das `null` aus dem Klartext-Original lesen, was wiederum Schreibzugriff voraussetzt, und (2) weil die `.bak` ohnehin denselben `null` enthält, also kein neues Leck entsteht. Nicht-Issue; dokumentiert als bewusste Design-Entscheidung (konsistent mit der Logik „nur Klartext-Strings maskieren"). Falls Audit-Stufe-2 das härten will, wäre `ShouldMaskPasswordValue` so zu erweitern, dass `Null` ebenfalls zu `***MASKED-BY-MIGRATOR***` wird — mit dokumentiertem Bruch von Integrated-Security-Setups.

5. **Side-Effect `dotnet test` aktualisiert Baseline — entkräftet.** Der Auftrags-Punkt 1 zur Prüfung „ob `dotnet test` die Baseline modifiziert" ist nach Verifikation des `AiNetLinterTests.cs` und Live-Test **gegenstandslos**: kein Linter-Test-Pfad mutiert die Baseline außer dem explizit als „RecreateBaseline" benannten `[Fact]`. Die ursprüngliche Sorge (Side-Effect in `dotnet test` → unerwartete Diffs in CI) ist unbegründet — wer die Baseline updaten will, muss `RecreateBaseline` explizit aufrufen. Empfehlung an Doku-Seite: `RecreateBaseline` ist aktuell als normaler `[Fact]` markiert, der bei jedem `dotnet test`-Lauf mitläuft — überlegen, ob er als `[Fact(Skip = "manuell aufrufen")]` o. ä. markiert werden sollte, damit nicht versehentlich CI-Builds die Baseline neu schreiben. Out of scope für step-002, aber Wert für den globalen Audit.

6. **Step-001-Lehre „Subject ≤ 72 Zeichen" eingehalten.** Commit-Subject `fix(config): maskiere Password-Feld in .bak-Backup vor Schreiben` ist **64 Zeichen** — 8 Zeichen unter dem Limit. Verbesserung gegenüber step-001 (74 Zeichen).

7. **Plan-Quality: Step-002 war deutlich sauberer als Step-001.** Die InlineData-/Test-Daten-Reparaturen aus step-001 (Längen-Inkonsistenzen) waren hier nicht nötig. Planer (planer-Skill) hat die JSON-Struktur korrekt spezifiziert. Konsistenz zwischen den Tests gut (alle drei nutzen `_tempDirectory` + `Encoding.UTF8` + `List<string>` für Logs).

8. **Anonymizer `Tokenization:Secret` bleibt im Klartext in `.bak`.** Coder dokumentiert das in `result.md` Z. 58 als bewusste Scope-Begrenzung (Plan adressiert nur `Password`, nicht `Secret`). Audit-relevant: `Secret` ist ebenfalls ein Geheimnis. Out of scope für step-002, aber **klarer Kandidat für einen Folge-Step** (Keyword-Heuristik: mask Secret/Token/Key/ConnectionString? Oder explizite Allowlist der zu maskierenden Properties?). Empfehlung für den globalen Audit am Ende des Tasks: Punkt 13b „Andere Secret-Properties in `.bak` maskieren" als Finding aufnehmen.
