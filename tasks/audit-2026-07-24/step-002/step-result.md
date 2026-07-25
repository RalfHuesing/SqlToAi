---
status: done
type: step-result
task: audit-2026-07-24
step: 002
coded_by: coder
coded_at: 2026-07-25T19:05:00+02:00
commit_hash: bc3778ac947cb33c148bdc68d60688547e441267
status_after: done  # done | blocked
---

# Result Step 002: Punkt 13 — Password-Feld in `.bak`-Backup maskieren

## Zusammenfassung

`AppSettingsMigrator.CreateBackupFile` legte vor diesem Step die Original-`appsettings.json` 1:1 nach `.bak`, was bei Klartext-`Password`-Einträgen (z. B. `Agent/Agent!`) das Klartext-Secret ungefiltert persistierte. Der Fix lädt jetzt das Original-JSON, parst via `JsonDocument`, durchwandert den Baum rekursiv via `Utf8JsonWriter` und ersetzt jeden `Password`-Property-Wert (case-insensitive) durch den statischen Platzhalter `***MASKED-BY-MIGRATOR***`, sofern der Wert nicht bereits eine `%ENV_VAR%`-Referenz ist. Bei Parse-Fehlern oder unlesbaren Strukturen fällt der Pfad auf den bisherigen 1:1-`File.Copy` mit Warnungs-Log zurück, sodass die Migration selbst nicht abbrechen kann. Drei neue xUnit-Facts verifizieren das Verhalten.

## Geänderte Dateien

- `src/SqlToAi/Configuration/AppSettingsMigrator.cs` — `CreateBackupFile` von `private static` auf `internal static` (für direkten Test-Zugriff über bestehendes `InternalsVisibleTo("SqlToAi.Tests")`); neuer Masking-Pfad mit `JsonDocument` + `Utf8JsonWriter`; Helfer `WriteMaskedElement`/`WriteMaskedObject`/`WriteMaskedArray` (rekursiv); `ShouldMaskPasswordValue` (prüft `ValueKind`, lässt `Null` und Env-Var-Strings aus); `IsEnvironmentVariableReference` (`StartsWith('%') && EndsWith('%')`); Konstante `MaskedPasswordPlaceholder`; UTF-8 ohne BOM via `new UTF8Encoding(false)` (Projekt-Pattern, identisch zu `SaveUpdatedJson`).
- `tests/SqlToAi.Tests/Configuration/AppSettingsMigratorTests.cs` — `using System.Collections.Generic;` ergänzt; drei neue `[Fact]`-Methoden: `CreateBackupFile_ShouldMaskPassword_WhenPlaintextPresent`, `CreateBackupFile_ShouldNotMaskPassword_WhenEnvironmentVariableReferenced`, `CreateBackupFile_ShouldLeaveOtherFieldsUnchanged`.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — SHA-256-Hashes für `AppSettingsMigrator.cs` (alt `906ed051…` → neu `f5f9e446…`) und `AppSettingsMigratorTests.cs` (alt `391e33eb…` → neu `92eea4a2…`) aktualisiert (Begleitschritt gemäß Plan-Notes).

## Commit

- **Hash:** `bc3778ac947cb33c148bdc68d60688547e441267`
- **Message:**
  ```
  fix(config): maskiere Password-Feld in .bak-Backup vor Schreiben
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
→ Ergebnis: grün — Fehler: 0, erfolgreich: 366, übersprungen: 0, gesamt: 366
→ AppSettingsMigratorTests (gefiltert): 8/8 grün (5 alt + 3 neu)
→ AiNetLinterTests: 2/2 grün (Baseline-Hashes automatisch durch Linter aktualisiert)
```

## Abweichungen vom Plan

- **`JsonDocument`-Roundtrip statt String-Manipulation:** Der Plan ließ offen, ob per `JsonDocument`-Roundtrip oder per String-Patch maskiert wird. Ich habe mich für `JsonDocument` (nur-Lesen) + `Utf8JsonWriter` (Schreiben) entschieden, weil `JsonElement` selbst nicht mutierbar ist und der `Utf8JsonWriter` mit `Indented = true` + `Encoder = JsonOptions.Encoder` (JavaScriptEncoder.UnsafeRelaxedJsonEscaping) das exakt gleiche Format-Set erzeugt wie `SaveUpdatedJson`. So bleibt die Formatierung (2-Space-Indent, kein `\u0027`-Escaping) im maskierten Backup identisch zur migrierten `appsettings.json`.
- **Vergleichs-Hash nicht über `--create-baseline` neu erzeugt:** Die `SqlToAi-baseline.json` wurde durch den Lauf von `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` automatisch aktualisiert (das Linter-Tool synchronisiert die Hashes in seinem Validation-Pass), nicht durch einen expliziten Aufruf von `--create-baseline`. Die neuen Hashes sind deterministisch aus `Get-FileHash -Algorithm SHA256` reproduzierbar und in der Datei verifiziert.

## Beobachtungen

- **Mehrere `Password`-Felder im Baum:** Die `appsettings.json` enthält bereits jetzt mindestens zwei `Password`-Felder (`SqlToAi:SqlServer:Password` und `SqlToAi:AnonymizationRules:Password`, `SqlToAi:MetadataProvider:Password`). Die rekursive Walk-Logik (`WriteMaskedObject`) maskiert alle auf einmal — die Notes im Plan („Edge-Case: mehrere `Password`-Felder … rekursiv durchwandern") sind damit abgedeckt, ohne dass ein einzelner `Password`-Treffer in `SqlServer` hardcoded wäre.
- **Anonymizer-Secret hat keinen Schaden:** `Anonymizer:Tokenization:Secret` heißt `Secret`, nicht `Password`, und wird daher **nicht** maskiert. Das ist im Plan-Scope korrekt (nur `Password`-Properties), aber ein Side-Effect: das `Secret`-Feld (das ebenfalls ein Geheimnis ist) bleibt im Klartext in der `.bak`. Falls der Audit später auch `Secret` und ggf. `ConnectionString` mitnehmen will, wäre das ein eigener Folge-Step (Keyword-Heuristik oder explizite Allowlist).
- **Migration behält `JsonNode`-Pfad:** Der `SyncExistingConfiguration`-Pfad nutzt weiterhin `JsonNode.Parse` (Zeile 99) und nicht `JsonDocument`. Eine Vereinheitlichung wäre denkbar, gehört aber nicht in den Step-Scope.
- **BOM-Test nicht enthalten:** Es ist kein expliziter Test, dass `new UTF8Encoding(false)` tatsächlich BOM-frei schreibt. Verhalten folgt aber dem bereits getesteten `SaveUpdatedJson`-Pattern (Zeile 306-310 in der jetzt verschobenen Datei) — eine Regression in `UTF8Encoding` würde den ganzen Migrations-Output treffen.
- **Linter-Baseline-Hash wurde durch Test-Lauf mit-aktualisiert:** `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` hat während `dotnet test` die `SqlToAi-baseline.json` automatisch auf den neuen Hash-Stand geschoben. Der `RecreateBaseline`-Pfad (zweite `[Fact]` in der gleichen Testklasse) wäre explizit, war aber nicht nötig. Die `2` verbleibenden Linter-Violations (`MaxBoolParameterCount` in `AccessLevelProviderTests.cs:218, 262`) sind vorbestehend und im Plan explizit als „nicht Teil dieses Tasks" markiert.
- **`internal static` Sichtbarkeit:** Genau wie in Step 001 für `SecurityGuard.MatchesPattern` wurde `CreateBackupFile` von `private static` auf `internal static` geändert, damit die Tests die Methode direkt aufrufen können. Das vorhandene `<InternalsVisibleTo Include="SqlToAi.Tests" />` macht das zur etablierten Variante im Projekt.

## Bekannte Unschärfen

- **Kein Test für Parse-Error-Fallback:** Der `catch (Exception ex)`-Zweig in `CreateBackupFile` (Zeile 219-222) ist implementiert, aber nicht durch einen Unit-Test abgesichert. Ein Test müsste absichtlich kaputtes JSON in den Temp-Pfad schreiben; das war nicht im 3-Tests-Plan enthalten. Empfehlung: optionaler Folge-Test, der `"this is not json"` reinschreibt und prüft, dass die `.bak` per `File.Copy` 1:1 entsteht und ein Warning-Logeintrag vorhanden ist.
- **Kein Test für `Password`-Feldtiefe ≠ 1:** `ShouldMaskPasswordValue` wird nur an String-`Password`-Feldern getestet. Ein `Password` als Number/Bool/Object/Array würde ebenfalls maskiert (konservativ). Das ist im Plan so gewollt (`„alle ValueKind außer Null"`), aber durch die existierenden Tests nicht abgedeckt.
- **Re-Serialisierung ändert Whitespace nicht — aber bei Property-Reihenfolge im Roundtrip:** Der `Utf8JsonWriter` schreibt Properties in der Reihenfolge, wie `EnumerateObject()` sie liefert (Insertion-Order der Quell-Datei). Bei `appsettings.json` ist das deterministisch, aber eine manuell umsortierte Quelle könnte sich minimal unterscheiden. Akzeptabel für ein Backup-Format, das nur der Notfall-Restore dient.
- **Backup-Encoding-Mismatch bei BOM-Quell-Datei:** Wenn der Anwender die `appsettings.json` ursprünglich mit BOM geschrieben hätte (z. B. weil `File.WriteAllText(..., Encoding.UTF8)` statt `new UTF8Encoding(false)` benutzt wurde), würde der Roundtrip die BOM-Eigenschaft des Backups auf „keine BOM" normalisieren. Im aktuellen Migrations-Output wird durchgehend `UTF8Encoding(false)` benutzt, daher konsistent; aber für Bestands-Installationen, die mit BOM-Tool geschrieben wurden, wäre das ein theoretischer Drift. Nicht in Scope dieses Steps, aber als Beobachtung wert.
