---
status: done (pending audit)
type: step-plan
task: audit-hardening
step: 001
title: "CommandTimeout: Umbenennung ConnectTimeoutSeconds + neuer QueryExecutionOptions.CommandTimeoutSeconds"
epic: EPIC-01
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-04T09:00:00+02:00
related_to: []
---

# Step 001: CommandTimeout-Konfigurierbarkeit & Umbenennung

## Bezug

- **Task:** `audit-hardening`
- **Epic:** `EPIC-01` aus `roadmap.md` — CommandTimeout-Konfigurierbarkeit & Umbenennung
  (kompletter Scope des Epics, keine Teilung nötig: die Umbenennung und die neue Option
  hängen kausal zusammen — eine zweite `CommandTimeoutSeconds` einzuführen, ohne vorher die
  bestehende, missverständlich benannte umzubenennen, würde genau die Namenskollision
  erzeugen, die `konzept.md` explizit vermeiden will).
- **Konzept-Referenz:** `konzept.md` Muss-Haben 1 sowie „Wie" Schritt 1 und „Entdeckte
  Mängel" Eintrag „Hartkodiertes `CommandTimeout = 0`" und „Irreführende Benennung
  `SqlServerOptions.CommandTimeoutSeconds`".

## Aktueller Projektzustand (JIT-Kontext)

- **`QueryExecutionService.cs:251`:** `command.CommandTimeout = 0;` — hartkodiert, unbegrenzt.
  Der `command` wird lokal in `ExecuteAndSerializeAsync` gebaut; `_options` (Feld,
  `QueryExecutionOptions`) ist im selben Typ bereits injiziert und liegt in Scope — es muss
  nur um `CommandTimeoutSeconds` erweitert werden, keine neue Dependency nötig.
- **`SqlToAiOptions.cs`:**
  - `SqlServerOptions.CommandTimeoutSeconds` (Zeile 29) ist die umzubenennende Property. Sie
    wird **ausschließlich** in `SqlConnectionFactory.cs:44` als `ConnectTimeout` verwendet
    (per Grep verifiziert) — keine weiteren Referenzen im Code.
  - `QueryExecutionOptions` (Zeile 156-163) ist die Zielklasse für die neue
    `CommandTimeoutSeconds`-Property (analog zu `DefaultRowLimit`/`MaxRowLimit`, gleiches
    Muster: Property-Initializer als Default).
  - **Wichtig, nicht anfassen:** `AnonymizationRulesOptions.CommandTimeoutSeconds` (Zeile 128)
    und `MetadataProviderOptions.CommandTimeoutSeconds` (Zeile 148) sind korrekt benannte,
    echte Command-Timeouts (verwendet in `AnonymizationRuleProvider.cs` bzw.
    `MetadataProvider.cs:192` jeweils direkt als `CommandTimeout`/`commandTimeout`-Parameter,
    nicht als `ConnectTimeout`). Sie liegen **außerhalb** des Scopes von `konzept.md` (das nur
    `SqlServerOptions.CommandTimeoutSeconds` nennt) und dürfen nicht umbenannt werden.
  - **Auffällig, aber ebenfalls außerhalb des Scopes:** `SecondaryConnectionBuilder.cs`
    (interner Record `SecondaryConnectionSettings.CommandTimeoutSeconds`, gespeist aus
    `AnonymizationRulesOptions`/`MetadataProviderOptions`) verwendet seinen eigenen
    `CommandTimeoutSeconds`-Parameter an Zeile 54 ebenfalls als `ConnectTimeout` — dasselbe
    Bezeichnungs-Muster wie der zu behebende Fund, aber nicht Teil von `konzept.md`. Nicht in
    diesem Step anfassen; ggf. später als eigener Tech-Debt-Kandidat aufnehmen (nicht
    Aufgabe des Planers, siehe Skill §Step-Modus Schritt 3).
- **`SqlConnectionFactory.cs:44`:** `ConnectTimeout = _options.SqlServer.CommandTimeoutSeconds`
  — einzige Stelle, die nach der Umbenennung auf `_options.SqlServer.ConnectTimeoutSeconds`
  angepasst werden muss.
- **`appsettings.json`:** `SqlServer.CommandTimeoutSeconds: 30` (Zeile 18) muss zu
  `ConnectTimeoutSeconds` umbenannt werden; `QueryExecution` (Zeile 50-53) braucht einen
  neuen `CommandTimeoutSeconds`-Eintrag.
- **Bestehende Tests als Vorlage:**
  - `tests/SqlToAi.Tests/Database/SqlConnectionFactoryTests.cs` — referenziert aktuell
    `options.SqlServer.CommandTimeoutSeconds` nicht direkt (nur `Server`/`UserId`/`Password`/
    `IntegratedSecurity`), daher keine Änderung an bestehenden Tests nötig, nur ggf. ein
    neuer Test für `ConnectTimeoutSeconds`.
  - `tests/SqlToAi.Tests/Database/QueryExecutionServiceMockDb.cs`:
    `MockQueryConnectionFactory.LastConnection!.LastCommand` gibt das
    `FakeDbCommand`-Objekt der tatsächlich ausgeführten Query zurück — `FakeDbCommand`
    (`tests/SqlToAi.Tests/TestSupport/FakeDbCommand.cs`) hat eine normale
    `CommandTimeout`-Property (kein Fake-Verhalten), die exakt den Wert speichert, den
    `QueryExecutionService` setzt. Das ist der bestehende Mechanismus, mit dem sich
    `command.CommandTimeout` verifizieren lässt, ohne eine neue Mock-Infrastruktur zu bauen.

## Intention

Nach diesem Step ist `CommandTimeout = 0` vollständig aus dem Code entfernt und durch einen
appsettings-konfigurierbaren Wert (`QueryExecutionOptions.CommandTimeoutSeconds`) ersetzt.
Gleichzeitig ist die bestehende, irreführend benannte `SqlServerOptions.CommandTimeoutSeconds`
(die de facto ein `ConnectTimeout` ist) zu `ConnectTimeoutSeconds` umbenannt — konsistent in
Options-Klasse, `appsettings.json` und der einzigen Verwendungsstelle
(`SqlConnectionFactory.cs`). Beide Änderungen gehören in denselben Step, weil die Umbenennung
laut `konzept.md` bewusst *vor* der neuen Option steht, um eine Namenskollision zu vermeiden.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Configuration/SqlToAiOptions.cs`

- **Was:**
  - `SqlServerOptions.CommandTimeoutSeconds` (Zeile 29) → umbenennen zu
    `ConnectTimeoutSeconds` (Property-Name und ggf. XML-Doku-Kommentar, falls vorhanden,
    anpassen, um den tatsächlichen Zweck — ADO.NET `ConnectTimeout` — korrekt zu
    beschreiben).
  - `QueryExecutionOptions` um eine neue Property `CommandTimeoutSeconds` mit
    Property-Initializer-Default ergänzen (Default z. B. `30`, konsistent mit den
    bestehenden `CommandTimeoutSeconds`-Defaults in `SqlServerOptions`/
    `AnonymizationRulesOptions`/`MetadataProviderOptions`), inkl. XML-Doku-Kommentar analog
    zu `DefaultRowLimit`/`MaxRowLimit`.
- **Warum:** Kern der Umbenennung + neuen Option, wie in `konzept.md` „Wo im Projekt" und
  „Wie" Schritt 1 vorgegeben.

### Datei 2: `src/SqlToAi/Database/SqlConnectionFactory.cs` (Zeile 44)

- **Was:** `ConnectTimeout = _options.SqlServer.CommandTimeoutSeconds` →
  `ConnectTimeout = _options.SqlServer.ConnectTimeoutSeconds`.
- **Warum:** Einzige Verwendungsstelle der umbenannten Property; muss synchron mit der
  Umbenennung angepasst werden, sonst Build-Fehler.

### Datei 3: `src/SqlToAi/Database/QueryExecutionService.cs` (Zeile 251)

- **Was:** `command.CommandTimeout = 0;` → `command.CommandTimeout = _options.CommandTimeoutSeconds;`
  (`_options` ist das bereits injizierte `QueryExecutionOptions`-Feld, siehe Konstruktor
  Zeile 74 — keine neue Dependency nötig).
- **Warum:** Entfernt den unbegrenzten Timeout (konzept.md Muss-Haben 1 / Audit-Fund).

### Datei 4: `src/SqlToAi/appsettings.json`

- **Was:**
  - `SqlServer.CommandTimeoutSeconds` (Zeile 18) → umbenennen zu `ConnectTimeoutSeconds`,
    Wert `30` unverändert übernehmen.
  - `QueryExecution` (Zeile 50-53) um `"CommandTimeoutSeconds": 30` ergänzen (neben
    `DefaultRowLimit`/`MaxRowLimit`).
- **Warum:** Pflicht laut `SqlToAiRichtlinien.mdc` §4 („Jede neu eingeführte
  Konfigurationsoption muss zwingend auch lückenlos in der Haupt-`appsettings.json`
  definiert sein") — betrifft hier sowohl die Umbenennung als auch die neue Property.

### Datei 5 (Doku-Sync-Pflicht, siehe Rules-Refs): `docs/architecture-spec.md` und `README.md`

- **Was:** Alle Stellen, die `SqlServer.CommandTimeoutSeconds`/„Command Timeout" im
  Zusammenhang mit der Connection-Konfiguration erwähnen, auf `ConnectTimeoutSeconds`
  korrigieren; neue `QueryExecution.CommandTimeoutSeconds`-Option dokumentieren, sofern die
  bestehende `QueryExecution`-Optionsliste dort bereits `DefaultRowLimit`/`MaxRowLimit`
  aufführt (Coder prüft das beim Umsetzen selbst per Grep, da der genaue Ist-Stand dieser
  Docs hier nicht gelesen wurde).
- **Warum:** `SqlToAiRichtlinien.mdc` §4 „Dokumentations-Synchronisation (Pflicht)".

## Tests

- [ ] Neuer/angepasster Test in `SqlConnectionFactoryTests.cs`: `CreateConnection` mit
      `options.SqlServer.ConnectTimeoutSeconds = <Wert>` gesetzt → Assert, dass
      `connection.ConnectionString` `Connect Timeout=<Wert>` enthält (Property-Rename
      nachweisen; bestehende 4 Tests bleiben inhaltlich unverändert, da sie diese Property
      bisher nicht setzen).
- [ ] Neuer Test in `QueryExecutionServiceTests.cs` (oder Erweiterung eines bestehenden Setup):
      `BuildService` mit `options.QueryExecution.CommandTimeoutSeconds = <Wert>` (≠ 0) →
      nach `ExecuteQueryAsync` Assert, dass
      `factory.LastConnection!.LastCommand!.CommandTimeout == <Wert>` (verifiziert, dass
      `command.CommandTimeout = 0` tatsächlich entfernt und durch den konfigurierten Wert
      ersetzt wurde — Kernkriterium des Audit-Funds).
- [ ] `dotnet build` (TreatWarningsAsErrors) grün.
- [ ] `dotnet test` grün (inkl. `AiNetLinterTests` — Baseline aktualisiert sich automatisch).

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`roadmap.md`) grün
- [ ] Test-Command aus Tech-Stack-Notiz grün
- [ ] Commit auf aktuellem Branch (Conventional Commit, Deutsch, imperativ)
- [ ] `step-001/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` (Abschnitt „Updates, Dokumentation & Sprachen") —
  „Keine hartkodierten Werte & AppSettings-Pflicht": verbietet exakt das bestehende
  `CommandTimeout = 0` und verlangt lückenlose `appsettings.json`-Spiegelung jeder neuen
  Option; außerdem Doku-Sync-Pflicht (`docs/architecture-spec.md`/`README.md`) und
  Commit-Konventionen (Deutsch, Conventional Commits, autonom nach Abschluss).
- `.agents/rules/AiNetLinter.mdc` — beim Hinzufügen der neuen Property/des neuen Feldzugriffs
  zu beachten (Namenskonventionen, keine Methodenlängen-/Parameterzahl-Verstöße erwartet, da
  nur ein einzeiliger Zuweisungs-Ersatz + Property-Ergänzung).

## Bekannte Ausnahmen

Keine.

## Code-Skizze (optional)

```csharp
// QueryExecutionService.cs, ExecuteAndSerializeAsync — vorher:
command.CommandTimeout = 0;
// nachher:
command.CommandTimeout = _options.CommandTimeoutSeconds;
```

```json
// appsettings.json, Ausschnitt nachher:
"SqlServer": {
  ...
  "ConnectTimeoutSeconds": 30
},
...
"QueryExecution": {
  "DefaultRowLimit": 100,
  "MaxRowLimit": 1000,
  "CommandTimeoutSeconds": 30
},
```

## Notes

- Bewusst **nicht** angefasst in diesem Step (aus Scope-Gründen, siehe „Aktueller
  Projektzustand" oben): `AnonymizationRulesOptions.CommandTimeoutSeconds`,
  `MetadataProviderOptions.CommandTimeoutSeconds`, sowie das strukturell ähnliche
  `SecondaryConnectionSettings.CommandTimeoutSeconds` in `SecondaryConnectionBuilder.cs`
  (Zeile 54, dort ebenfalls als `ConnectTimeout` verwendet, obwohl der Name
  „CommandTimeoutSeconds" lautet) — `konzept.md` benennt ausschließlich
  `SqlServerOptions.CommandTimeoutSeconds` als umzubenennen. Diese zweite Fundstelle mit
  demselben Bezeichnungs-Muster ist real, aber ein neuer, vom Nutzer noch nicht
  freigegebener Scope-Punkt — nicht in diesem Step mit umbenennen, sonst Scope-Creep
  gegenüber `konzept.md`.
- Der `_options`-Feldname in `QueryExecutionService.cs` ist bereits `QueryExecutionOptions`
  (Zeile 52/74) — keine Verwechslungsgefahr mit `SqlServerOptions`, da beide Typen komplett
  getrennt sind und nur der gleiche Property-Name „CommandTimeoutSeconds" doppelt vorkommt
  (in zwei unterschiedlichen Options-Klassen) — genau das vom Konzept intendierte Ergebnis
  nach der Umbenennung (keine Kollision mehr, weil `SqlServerOptions` jetzt
  `ConnectTimeoutSeconds` heißt).
