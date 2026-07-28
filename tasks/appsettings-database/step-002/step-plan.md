---
status: open
type: step-plan
task: appsettings-database
step: 002
title: "AccessLevelProvider und SecurityGuard Refactoring"
estimated_risk: high
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-03
created_at: 2026-07-28T11:40:00+02:00
related_to:
  - tasks/appsettings-database/step-001/step-plan.md
---

# Step 002: AccessLevelProvider und SecurityGuard Refactoring

## Bezug

- **Task:** `appsettings-database`
- **Quelle:** `tasks/appsettings-database/Konzept.md#Wie-(grober-Ansatz)`

## Intention

Umstellung der Berechtigungsermittlung in `AccessLevelProvider` auf in-memory Prüfung der ebenen-basierten Listen aus `DatabasesOptions` mit Fail-Safe Konfliktauflösung (`SchemaOnly` > `ReadOnlyAnonymized` > `ReadOnly` > `ReadWrite`). Entfernung aller SQL-Probe-Logiken (`AccessCheckSql`, `ISqlExecutionService`-Abhängigkeiten für Access-Checks) und Vereinfachung von `SecurityGuard.IsDatabaseAllowed`.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Security/AccessLevelProvider.cs` & `IAccessLevelProvider.cs`

- **Was:**
  - Entfernen der SQL-Check-Logik (`AccessCheckSql`) und der Abhängigkeit von `ISqlExecutionService` / `IQueryExecutionService` falls nur für Probe genutzt.
  - Anpassen von `GetAccessLevelAsync(string databaseName)`:
    - Exakter, case-insensitiver Vergleich des `databaseName` gegen `DatabasesOptions.SchemaOnly`, `ReadOnlyAnonymized`, `ReadOnly`, `ReadWrite`.
    - Ist der DB-Name in KEINER Liste enthalten -> `AccessLevel.None`.
    - Ist der DB-Name in MEHREREN Listen enthalten -> restriktivster Zugriff gewinnt (`SchemaOnly` > `ReadOnlyAnonymized` > `ReadOnly` > `ReadWrite`).
- **Warum:** Direkte in-memory Ermittlung ohne SQL-Overhead und Fail-Safe Whitelisting.

### Datei 2: `src/SqlToAi/Security/SecurityGuard.cs` & `ISecurityGuard.cs`

- **Was:**
  - Anpassen von `IsDatabaseAllowed(string databaseName)` bzw. `IsDatabaseAllowedAsync`:
    - Gibt `true` zurück, wenn `GetAccessLevelAsync(databaseName)` einen Wert `!= AccessLevel.None` zurückgibt.
  - Entfernen alter Referenzen auf `DatabasesOptions.Allowed` oder `Blocked`.
- **Warum:** Strikte Ausrichtung am AccessLevel-Ergebnis.

### Datei 3: `src/SqlToAi/Configuration/ConfigurationResolver.cs` / DI setup / Services

- **Was:** Anpassen von `AccessLevelProvider` Konstruktor-Aufrufen oder DI-Registrierungen in `ConfigurationResolver.cs` oder `ServiceCollectionExtensions.cs` (Entfernen des SQL-Execution-Parameters falls entfallen).
- **Warum:** Anpassung der DI-Signaturen.

### Datei 4: `src/SqlToAi/Database/QueryExecutionService.cs` / `QueryValidationService.cs`

- **Was:** Prüfen und Entfernen von veralteten Aufrufen/Prüfungen bezüglich `AccessCheckSql` oder `Blocked`-Listen.
- **Warum:** Code-Bereinigung von Altlasten.

## Tests

- [ ] `dotnet build SqlToAi.slnx`
- [ ] Erste schnelle Überprüfung der Kompilierung und grundlegender Sicherheitstests.

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] `dotnet build SqlToAi.slnx` baut ohne Warnungen und Fehler
- [ ] Code-Commit per `git add` und Conventional Commit in Deutsch
- [ ] `step-002/step-result.md` geschrieben und per Doku-Commit gesichert
- [ ] `status` in `step-plan.md` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#2.-Architektur-&-Guardrail-Konzepte` — Datenbank-Zugriffssteuerung & Whitelisting
- `.agents/rules/AiNetLinter.mdc` — Zero-Warning-Direktive
