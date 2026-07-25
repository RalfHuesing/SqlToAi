---
status: done
type: step-plan
task: audit-2026-07-24
step: 002
title: "Punkt 13 — Password-Feld in .bak-Backup maskieren"
created_by: planer
created_at: 2026-07-25T18:30:00+02:00
related_to:
  - tasks/audit-2026-07-24/01-security-guardrails.md (Finding 5, Niedrig-Mittel)
  - tasks/audit-2026-07-24/00-summary.md (Punkt 13)
---

# Step 002: Punkt 13 — Password-Feld in `.bak`-Backup maskieren

## Bezug

- **Task:** `audit-2026-07-24`
- **Quelle:** `01-security-guardrails.md` Finding 5 „Config-Migration dupliziert Secrets unkontrolliert in `.bak`-Datei"
- **Phase / Priorität:** Phase 3 — Doku & Konfigurationshygiene, Punkt 13

## Intention

`AppSettingsMigrator.CreateBackupFile` (`src/SqlToAi/Configuration/AppSettingsMigrator.cs:193-199`) kopiert die bestehende `appsettings.json` 1:1 nach `appsettings.json.bak`, **bevor** die migrierte Version geschrieben wird. Enthält das Original ein Klartext-Passwort (z. B. das `Agent/Agent!`-Demo-Login oder ein lokal konfiguriertes Produktiv-Login), landet dieses Passwort vollständig in der `.bak`-Datei, die nirgends automatisch aufgeräumt wird (`LogRetentionService` filtert nur App-Log und Error-Log, nicht das Konfig-Verzeichnis) und die denselben Dateisystem-Schutz wie das Original hat. Risiko: Wer zu Support-Zwecken das Installations-Verzeichnis ohne `appsettings.json` (aber mit `.bak`) versendet, schickt das Klartext-Passwort mit.

Ziel: Vor dem Schreiben des Backups das `Password`-Feld (und nur dieses — die Datei soll im Übrigen byte-für-byte identisch zur Vor-Migration sein) durch einen statischen Platzhalter ersetzen, sofern das Passwort nicht bereits per `%ENV_VAR%`-Syntax referenziert wird (in dem Fall ist der String kein Geheimnis, sondern eine Env-Var-Referenz, die auch im Original keinen Klartext enthält).

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Configuration/AppSettingsMigrator.cs`

- **Was:**
  1. In `CreateBackupFile` (Zeile 193-199) den String `targetFilePath` vor `File.Copy` per `File.ReadAllText` laden, per `System.Text.Json.JsonDocument` parsen, das `Password`-Feld im `SqlServer`-Knoten durch den Platzhalter `"***MASKED-BY-MIGRATOR***"` ersetzen (nur wenn der aktuelle Wert **nicht** mit `%` beginnt und **nicht** mit `%` endet — d. h. keine Env-Var-Referenz ist), und das Ergebnis per `File.WriteAllText(..., new UTF8Encoding(false))` zurückschreiben.
  2. Den Migrationslog erweitern um den Eintrag `Masked Password field in backup file '{backupPath}' (Password not referenced via environment variable).` (oder analog).
  3. Bei Parse-Fehlern oder fehlendem `Password`-Feld: **stillschweigend** die `.bak` ohne Maskierung schreiben und einen Warnungs-Logeintrag hinzufügen — der Migrations-Flow darf durch die Maskierung nicht abbrechen, die Migration selbst bleibt wichtiger als die Backup-Maskierung.

- **Warum:** Reine Verhaltensänderung im Backup-Pfad; das eigentliche Migrationsverhalten bleibt unangetastet. `JsonDocument` ist bereits als `using`-Import in der Datei vorhanden (siehe `AppSettingsMigrator.cs` `using System.Text.Json` indirekt über `JsonObject`).

- **Code-Skizze:**
  ```csharp
  private static string CreateBackupFile(string targetFilePath, List<string> logs)
  {
      string backupPath = targetFilePath + ".bak";
      try
      {
          string json = File.ReadAllText(targetFilePath);
          using var doc = JsonDocument.Parse(json);
          var masked = MaskPasswordField(doc.RootElement);
          if (masked != json)
          {
              File.WriteAllText(backupPath, masked, new UTF8Encoding(false));
              logs.Add($"Saved backup configuration to '{backupPath}' (Password field masked).");
              return backupPath;
          }
      }
      catch (Exception ex)
      {
          // Warnung loggen, dann Fallback auf 1:1-Kopie
          // (Migration selbst darf nicht abbrechen)
      }

      File.Copy(targetFilePath, backupPath, overwrite: true);
      logs.Add($"Saved backup configuration to '{backupPath}'.");
      return backupPath;
  }

  private static string MaskPasswordField(JsonElement root)
  {
      // Read, clone, set SqlServer.Password = "***MASKED-BY-MIGRATOR***" if not env-var-referenced
      // Return the serialized JSON, or the original if no masking needed
  }
  ```

### Datei 2: `tests/SqlToAi.Tests/Configuration/AppSettingsMigratorTests.cs`

- **Was:** Zwei neue Facts/Theories ergänzen:
  1. `CreateBackupFile_ShouldMaskPassword_WhenPlaintextPresent` — Backup-Datei lesen, `SqlServer.Password` muss `"***MASKED-BY-MIGRATOR***"` sein, alle anderen Felder müssen identisch sein.
  2. `CreateBackupFile_ShouldNotMaskPassword_WhenEnvironmentVariableReferenced` — Backup mit `"Password": "%SQLTOAI_PASSWORD%"` schreiben, `Password` muss unverändert `%SQLTOAI_PASSWORD%` sein.

- **Warum:** Der ursprüngliche Audit-Fund betont, dass die `.bak` denselben Geheimnis-Inhalt wie das Original hat. Der Fix muss verifizierbar sein, sonst entstehen Regressionen, falls jemand später das Masking-Verhalten „vereinfacht".

## Tests

- [ ] `CreateBackupFile_ShouldMaskPassword_WhenPlaintextPresent` — Klartext-Passwort `Agent!` wird zu `***MASKED-BY-MIGRATOR***` in `.bak`
- [ ] `CreateBackupFile_ShouldNotMaskPassword_WhenEnvironmentVariableReferenced` — `%SQLTOAI_PASSWORD%` bleibt unverändert
- [ ] `CreateBackupFile_ShouldLeaveOtherFieldsUnchanged` — `UserId`, `Server`, `CacheTtlSeconds` etc. sind byte-identisch
- [ ] Bestehende `AppSettingsMigratorTests` (Backup-Erstellung, Migration, etc.) bleiben grün
- [ ] `dotnet build SqlToAi.slnx` 0 Warnungen, 0 Fehler
- [ ] `dotnet test --filter "Category!=Integration"` grün

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command grün (0 Warnings, 0 Errors)
- [ ] Test-Command grün (Ausnahmen siehe „Bekannte Ausnahmen")
- [ ] Commit auf aktuellem Branch (`fix(config): maskiere Password-Feld in .bak-Backup vor Schreiben`)
- [ ] `step-002/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „xUnit v3 Tests: Pflicht für alle funktionalen Änderungen, Sicherheitsüberprüfungen" (Backup-Pfad ist sicherheitsrelevant, weil Secrets tangiert)
- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „Dokumentations-Synchronisation (Pflicht): … ohne Aufforderung" — kein Doku-Update nötig, da Verhalten intern (Backup-Datei) und im Migrationslog sichtbar

## Bekannte Ausnahmen

- `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` — vorbestehend, **nicht** Teil dieses Tasks. Falls `AppSettingsMigrator.cs` oder `AppSettingsMigratorTests.cs` so viel neue Substanz erhält, dass der Hash in `SqlToAi-baseline.json` neu berechnet werden muss, ist das ein zulässiger Begleitschritt.

## Notes

- **Edge-Case: mehrere `Password`-Felder.** Das `JsonDocument`-Root kann theoretisch mehrere `Password`-Felder haben (z. B. auch unter `AnonymizationRules`, `MetadataProvider`). Der Fix sollte **alle** `Password`-Felder im gesamten Baum maskieren, nicht nur unter `SqlServer`. Im `JsonElement`-Tree: rekursiv durchwandern, bei Property `Password` (case-insensitive) den Wert ersetzen, sofern nicht `%…%`.
- **Platzhalter-String:** `***MASKED-BY-MIGRATOR***` ist explizit gewählt, damit:
  1. auffällt, wenn jemand das Backup-File versehentlich als Config nutzt (das echte Passwort ist weg, Migration läuft mit dem Platzhalter sofort gegen einen Auth-Fehler → Anwender sieht das Problem sofort)
  2. ein späterer Restore der `.bak` auf die Original-Datei nicht naiv möglich ist — der Anwender muss das Passwort neu eintragen.
- **Robustheit:** Wenn das JSON kaputt ist oder der `JsonDocument.Parse` fehlschlägt, **darf die Migration nicht abbrechen**. Der ursprüngliche `File.Copy` (1:1-Backup) ist dann der Fallback, ergänzt um einen Warnungs-Log. Diese „Maskierung schlägt fehl → wir machen halt das alte Verhalten"-Semantik ist sicherer als „Maskierung schlägt fehl → Migration bricht ab".
- **Integration mit `AppSettingsMigrator`-Log-Format:** Die bestehende `logs`-Liste wird in `AppSettingsMigrator` als `List<string>` geführt (siehe `CreateBackupFile(string, List<string> logs)` Signatur) — der neue Logeintrag passt zum bestehenden Format (`$"Saved backup configuration to '{backupPath}'."`).
- **Ascii-Encoding:** `new UTF8Encoding(false)` ist die im Projekt etablierte Variante (siehe `SaveUpdatedJson`, Zeile 201-205). Konsistenz wahren.
