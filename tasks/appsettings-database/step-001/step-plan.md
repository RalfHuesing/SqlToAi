---
status: done
type: step-plan
task: appsettings-database
step: 001
title: "DatabasesOptions und appsettings.json Refactoring"
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-03
created_at: 2026-07-28T11:40:00+02:00
related_to: []
---

# Step 001: DatabasesOptions und appsettings.json Refactoring

## Bezug

- **Task:** `appsettings-database`
- **Quelle:** `tasks/appsettings-database/Konzept.md#Scope`

## Tech-Stack-Notiz

- **Build-Command:** `dotnet build SqlToAi.slnx`
- **Test-Command:** `dotnet test SqlToAi.slnx`
- **Lint-Command:** `dotnet test --filter "FullyQualifiedName~AiNetLinter"`
- **Code-Style:** C# 14 / .NET 10, zero warnings (`<TreatWarningsAsErrors>true`), sealed classes, async/await pattern
- **Commit-Konventionen:** Conventional Commits (Deutsch, imperativ, z. B. `feat:`, `refactor:`, `chore:`)
- **Rules-Dir:** `.agents/rules`

## Intention

Die Konfigurationsstruktur in `SqlToAiOptions.cs` (`DatabasesOptions`) und `appsettings.json` wird auf ebenen-basierte Listen (`ReadWrite`, `ReadOnly`, `ReadOnlyAnonymized`, `SchemaOnly`) umgestellt. Veraltete Properties (`Allowed`, `Blocked`, `AccessCheckSql`) werden entfernt.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Configuration/SqlToAiOptions.cs`

- **Was:** `DatabasesOptions` anpassen:
  - Entfernen der Properties `Allowed`, `Blocked`, `AccessCheckSql`.
  - Hinzufügen der List-Properties:
    - `public List<string> ReadWrite { get; set; } = [];`
    - `public List<string> ReadOnly { get; set; } = [];`
    - `public List<string> ReadOnlyAnonymized { get; set; } = [];`
    - `public List<string> SchemaOnly { get; set; } = [];`
  - `CacheTtlSeconds` (default 300) beibehalten.
- **Warum:** Direkte ebenen-basierte Konfiguration von Datenbanken.

### Datei 2: `src/SqlToAi/appsettings.json`

- **Was:** `"Databases"` Sektion aktualisieren:
  ```json
  "Databases": {
    "CacheTtlSeconds": 300,
    "ReadWrite": [ "DemoDB" ],
    "ReadOnly": [],
    "ReadOnlyAnonymized": [],
    "SchemaOnly": []
  }
  ```
- **Warum:** Entsprechende Anpassung der Standardkonfiguration.

### Datei 3: `tests/SqlToAi.Tests/Configuration/SqlToAiOptionsTests.cs`

- **Was:** Tests anpassen für die neuen `DatabasesOptions` Properties (`ReadWrite`, `ReadOnly`, `ReadOnlyAnonymized`, `SchemaOnly`) und Entfernung von `Allowed`/`Blocked`/`AccessCheckSql`.
- **Warum:** Absicherung der neuen Options-Struktur.

### Datei 4: `tests/SqlToAi.Tests/Configuration/AppSettingsMigratorTests.cs`

- **Was:** Referenzen auf `AccessCheckSql`, `Allowed` oder `Blocked` in Migrations-Tests anpassen, falls vorhanden.
- **Warum:** Verhindern von Kompilier- oder Testfehlern.

## Tests

- [ ] `dotnet test --filter "FullyQualifiedName~SqlToAiOptionsTests"`
- [ ] `dotnet test --filter "FullyQualifiedName~AppSettingsMigratorTests"`
- [ ] `dotnet build SqlToAi.slnx`

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command `dotnet build SqlToAi.slnx` grün (0 Warnings, 0 Errors)
- [ ] Betroffene Unit-Tests grün
- [ ] Code-Commit per `git add` und Conventional Commit in Deutsch
- [ ] `step-001/step-result.md` geschrieben und in separatem Doku-Commit gesichert
- [ ] `status` in `step-plan.md` auf `done (pending audit)` aktualisiert

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4.-Updates,-Dokumentation-&-Sprachen` — No Magic Values & AppSettings-Pflicht
- `.agents/rules/AiNetLinter.mdc` — Zero-Warning-Direktive
