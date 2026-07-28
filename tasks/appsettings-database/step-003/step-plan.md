---
status: done
type: step-plan
task: appsettings-database
step: 003
title: "Tests und Dokumentation anpassen"
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-03
created_at: 2026-07-28T11:40:00+02:00
related_to:
  - tasks/appsettings-database/step-001/step-plan.md
  - tasks/appsettings-database/step-002/step-plan.md
---

# Step 003: Tests und Dokumentation anpassen

## Bezug

- **Task:** `appsettings-database`
- **Quelle:** `tasks/appsettings-database/Konzept.md#Definition-of-Done-/-Erfolgskriterien`

## Intention

Anpassung aller Unit- und Integrationstests an die neue ebenen-basierte Konfigurations- und Sicherheitslogik sowie zwingende Aktualisierung der Projektdokumentation in `README.md` und `docs/mcp-specification.md` in englischer Sprache.

## Konkrete Änderungen

### Datei 1: `tests/SqlToAi.Tests/Security/AccessLevelProviderTests.cs`

- **Was:**
  - Anpassen aller Tests für `AccessLevelProvider`.
  - Hinzufügen von Testfällen für:
    - Exakten case-insensitiven Vergleich.
    - Whitelisting: Nicht konfigurierte DB ergibt `AccessLevel.None`.
    - Konfliktauflösung: Wenn DB in `ReadWrite` und `ReadOnly`, gewinnt `ReadOnly`. Wenn in `ReadOnly` und `SchemaOnly`, gewinnt `SchemaOnly`.
  - Entfernen alter Tests für `AccessCheckSql` / SQL-Probes.
- **Warum:** Lückenlose Abdeckung der neuen Geschäftslogik.

### Datei 2: `tests/SqlToAi.Tests/Security/SecurityGuardTests.cs`

- **Was:** Anpassen von `SecurityGuardTests` an die vereinfachte `IsDatabaseAllowed` Logik und die neuen `DatabasesOptions`.
- **Warum:** Aktualisierung der Unit-Tests.

### Datei 3: `tests/SqlToAi.Tests/Integration/AccessLevelProviderIntegrationTests.cs` & `QueryExecutionServiceIntegrationTests.cs`

- **Was:** Anpassen von Mocks, `DatabasesOptions`-Setups und Test-Asserts in Integrationstests.
- **Warum:** Sicherstellen, dass alle Integrationstests grün durchlaufen.

### Datei 4: `docs/mcp-specification.md`

- **Was:** Aktualisierung der Konfigurations-Dokumentation (Abschnitt `Databases` options, Entfernen von `Allowed`, `Blocked`, `AccessCheckSql`, Erklären der ebenen-basierten Listen und Konfliktauflösung).
- **Warum:** Pflicht-Dokumentations-Synchronisation gemäß Projektdirektive in englischer Sprache.

### Datei 5: `README.md`

- **Was:** Aktualisierung des Konfigurations-Beispiels und der Beschreibung unter `Databases` in `README.md`.
- **Warum:** Pflicht-Dokumentations-Synchronisation gemäß Projektdirektive in englischer Sprache.

## Tests

- [ ] `dotnet test SqlToAi.slnx` (alle Unit- und Integrationstests grün)
- [ ] Baseline-Test-Lauf grün (`AiNetLinterTests`)

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] `dotnet build SqlToAi.slnx` grün (0 Warnings, 0 Errors)
- [ ] `dotnet test SqlToAi.slnx` 100% grün
- [ ] Dokumentation in `README.md` und `docs/mcp-specification.md` vollständig auf Englisch aktualisiert
- [ ] Code-Commit per `git add` und Conventional Commit in Deutsch
- [ ] `step-003/step-result.md` geschrieben und per Doku-Commit gesichert
- [ ] `status` in `step-plan.md` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4.-Updates,-Dokumentation-&-Sprachen` — Dokumentations-Synchronisation (Pflicht) & Sprachvorgaben
- `.agents/rules/SqlToAiRichtlinien.mdc#5.-Qualitätsdrift-Prävention-(AiNetLinter)` — Baseline-Aktualisierung
