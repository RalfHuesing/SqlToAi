---
status: ready
type: konzept
project_kind: brownfield
estimated_scope: small
rules_dir: .agents/rules
last_updated: 2026-07-28T11:39:15Z
open_questions: []
---

# Konzept: Überarbeitung Datenbank-Konfiguration (Databases) in appsettings.json

## Ziel (Was)

Die Konfiguration der erlaubten Datenbanken und ihrer Berechtigungsstufen (`AccessLevel`) in `appsettings.json` wird vereinfacht und direkt gestaltet:
- Die separate `Blocked`-Liste entfällt vollständig (alles, was nicht explizit in einer AccessLevel-Liste eingetragen ist, ist automatisch geblockt - Fail-Safe Whitelisting).
- Die bisherige `AccessCheckSql`-Abfrage (dynamische Rechteprüfung via SQL) entfällt ersatzlos.
- Die Zuweisung von Datenbanken zu Zugriffsebenen erfolgt direkt in der Konfiguration über ebenen-basierte Listen (`ReadWrite`, `ReadOnly`, `ReadOnlyAnonymized`, `SchemaOnly`).

## Warum / Kontext

Aktuell ist die Kombination aus `Allowed`-Liste, `Blocked`-Liste und einer SQL-Sicherheitsabfrage (`AccessCheckSql`) in `appsettings.json` unübersichtlich und fehleranfällig. Die Flexibilität von SQL-Checks wird in der Praxis nicht benötigt und erzeugt unnötigen Wartungsaufwand. Eine direkte Deklaration von Datenbanken pro `AccessLevel` macht die Konfiguration klarer, performanter und fehlerresistenter.

## Scope

### Muss-Haben

- Neue ebenen-basierte Konfigurationsstruktur in `appsettings.json` unter `"Databases"`:
  ```json
  "Databases": {
    "CacheTtlSeconds": 300,
    "ReadWrite": [ "DemoDB" ],
    "ReadOnly": [],
    "ReadOnlyAnonymized": [],
    "SchemaOnly": []
  }
  ```
- Exakter Vergleich von Datenbanknamen (case-insensitive, keine Wildcards/Globs).
- Entfernen des `Blocked`-Arrays und der `AccessCheckSql`-Logik aus `appsettings.json`, `DatabasesOptions`, `SecurityGuard` und `AccessLevelProvider`.
- Konfliktbehandlung bei Mehrfachnennung einer Datenbank in verschiedenen Listen: Restriktivster Zugriff gewinnt (Fail-Safe: `SchemaOnly` > `ReadOnlyAnonymized` > `ReadOnly` > `ReadWrite`).
- Anpassung von `SqlToAiOptions.cs`, `SecurityGuard.cs`, `AccessLevelProvider.cs` und allen betroffenen Unit-/Integrationstests.

### Nice-to-Have (optional, spätere Iteration)

- Startup-Warnung im Log, falls veraltete Konfigurationsschlüssel (`Blocked`, `AccessCheckSql`, `Allowed`) in `appsettings.json` erkannt werden.

### Non-Goals (bewusst NICHT Teil davon)

- Dynamische Berechtigungsprüfung zur Laufzeit via SQL-Queries (`AccessCheckSql` entfällt).
- Beibehaltung einer expliziten Blacklist (`Blocked` entfällt, reines Whitelisting).
- Wildcard-/Glob-Matching für Datenbanknamen (nur exakte Namen).

## Zielplattformen / Technischer Rahmen

- .NET 10 / C# 14
- Standard `Microsoft.Extensions.Options` Bindung aus `appsettings.json`.

## Verworfene Alternativen

- **Beibehaltung von `AccessCheckSql`:** Verworfen, weil dynamische SQL-Abfragen in der Praxiskonfiguration schwer zu pflegen, unübersichtlich und fehleranfällig sind.
- **Explicit Blacklisting (`Blocked`):** Verworfen, da ein striktes Whitelisting (Default-Deny) sicherer ist und `Blocked` redundant war.
- **Wildcard / Glob-Matching:** Verworfen zugunsten exakter Nennung aller erlaubten Datenbanken zur Vermeidung versehentlicher Freigaben.
- **Dictionary-Struktur (`"Levels": { "DemoDB": "ReadWrite" }`):** Verworfen zugunsten ebenen-basierter Listen, da Listen pro Berechtigungsstufe in JSON übersichtlicher zu gruppieren und zu konfigurieren sind.

## Wo im Projekt

- `src/SqlToAi/appsettings.json` (Anpassung der Standardkonfiguration)
- `src/SqlToAi/Configuration/SqlToAiOptions.cs` (`DatabasesOptions` Klasse mit `ReadWrite`, `ReadOnly`, `ReadOnlyAnonymized`, `SchemaOnly` Listen)
- `src/SqlToAi/Security/SecurityGuard.cs` (Prüfung ob Datenbank in mindestens einer erlaubten Liste enthalten ist)
- `src/SqlToAi/Security/AccessLevelProvider.cs` (Direkter In-Memory Lookup des AccessLevels aus den Options ohne SQL-Execution)
- `tests/SqlToAi.Tests/` (Anpassung aller Unit- & Integrationstests)

## Wie (grober Ansatz)

1. `DatabasesOptions` in `SqlToAiOptions.cs` anpassen: Properties für `ReadWrite`, `ReadOnly`, `ReadOnlyAnonymized`, `SchemaOnly` als `List<string>` bereitstellen (`Allowed`, `Blocked`, `AccessCheckSql` entfernen).
2. `AccessLevelProvider.GetAccessLevelAsync` anpassen:
   - Ist die DB in keiner Liste -> `AccessLevel.None`.
   - Ist die DB in mehreren Listen -> wähle den restriktivsten `AccessLevel` (Fail-Safe: `SchemaOnly` < `ReadOnlyAnonymized` < `ReadOnly` < `ReadWrite`).
3. `SecurityGuard.IsDatabaseAllowed` vereinfachen: Gibt `true` zurück, wenn `GetAccessLevelAsync` einen Wert `!= AccessLevel.None` ermittelt.
4. Bereinigen der SQL-Probe-Logik und Anpassen von `appsettings.json` sowie der Tests.

## Definition of Done / Erfolgskriterien

- `appsettings.json` ist auf die neue ebenen-basierte Struktur umgestellt.
- `Blocked` und `AccessCheckSql` sind aus der Codebasis vollständig entfernt.
- `dotnet build` baut ohne Warnungen/Fehler.
- `dotnet test` führt alle Tests erfolgreich durch.

## Offene Punkte

- keine (status: ready)