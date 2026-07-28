---
status: done
type: step-plan
task: appsettings-aktuell-halten
step: step-001
title: "Zeitstempel-Backup in AppSettingsMigrator implementieren & Tests anpassen"
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: Gemini 3.6 Flash
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-07-28T11:35:00+02:00
related_to: []
---

# Step 001: Zeitstempel-Backup in AppSettingsMigrator implementieren & Tests anpassen

## Bezug

- **Task:** `appsettings-aktuell-halten`
- **Quelle:** [Konzept.md](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tasks/appsettings-aktuell-halten/Konzept.md#L19) — Backup mit Zeitstempel (`appsettings.json.YYYYMMDD_HHMMSS.bak`)
- **Phase / Priorität:** Hauptfunktionalität / Hoch

## Tech-Stack-Notiz

- **Build-Command:** `dotnet build`
- **Test-Command:** `dotnet test`
- **Lint-Command:** Entfällt (AiNetLinter läuft im Test `RecreateBaseline` mit)
- **Code-Style:** C# 14 / .NET 10 (`#nullable enable`, `sealed` Klassen, PascalCase, kein `dynamic`, keine ungeloggten `catch`)
- **Commit-Konventionen:** Conventional Commits (Deutsch, imperativ, z. B. `feat(config): ...`, `test(config): ...`)

## Intention

Die Erstellung von Backup-Dateien bei Konfigurations-Änderungen soll statt einer statischen `.bak`-Datei neu ein zeitstempel-basiertes Backup-Dateiformat (`appsettings.json.YYYYMMDD_HHMMSS.bak`) verwenden. Dadurch werden bestehende Backups bei Mehrfachstarts nicht überschrieben. Zudem müssen die Unit-Tests in `AppSettingsMigratorTests.cs` angepasst und erweitert werden.

## Konkrete Änderungen

### Datei 1: [AppSettingsMigrator.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Configuration/AppSettingsMigrator.cs) (Zeile 195-227)

- **Was:** In `CreateBackupFile(string targetFilePath, List<string> logs)` den Backup-Pfad ändern von `targetFilePath + ".bak"` auf `targetFilePath + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak"`.
- **Warum:** Entspricht den Anforderungen in `Konzept.md` (Zeitstempel-Sicherung).

### Datei 2: [AppSettingsMigratorTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Configuration/AppSettingsMigratorTests.cs) (Zeilen 120, 178, etc.)

- **Was:**
  1. Tests anpassen, die bisher auf ein genaues `targetFilePath + ".bak"` geprüft haben, so dass sie das Zeitstempel-Muster `appsettings.json.*.bak` berücksichtigen.
  2. Einen neuen Test hinzufügen, der gezielt verifiziert, dass die Backup-Datei dem Namensschema `appsettings.json.YYYYMMDD_HHMMSS.bak` entspricht.
- **Warum:** Sicherstellen aller DoD-Kriterien bezüglich Zeitstempel-Backups.

## Tests

- [ ] `AppSettingsMigratorTests.Migrate_ShouldAddNewKeysAndRemoveObsoleteKeys_AndPreserveUserValues` (grün)
- [ ] `AppSettingsMigratorTests.Migrate_ShouldNotModifyFile_WhenSchemaMatches` (grün)
- [ ] `AppSettingsMigratorTests.CreateBackupFile_ShouldUseTimestampInFilename` (neuer Test, grün)
- [ ] Alle weiteren Tests in `AppSettingsMigratorTests` (grün)

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command `dotnet build` grün (0 Warnings, 0 Errors)
- [ ] Test-Command `dotnet test` grün
- [ ] Commit auf `main` (Conventional Commit)
- [ ] `tasks/appsettings-aktuell-halten/step-001/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#3-windows-umgebung--tool-regeln` — `dotnet build` & `dotnet test`
- `.agents/rules/AiNetLinter.mdc#kurz-stil` — `#nullable enable`, `sealed` Klassen, flache Methoden
