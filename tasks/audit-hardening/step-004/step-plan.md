---
status: done (pending audit)
type: step-plan
task: audit-hardening
step: "004"
title: "QueryValidationService: Command-Timeout statt ConnectTimeoutSeconds verwenden"
epic: EPIC-04
estimated_risk: low
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-04T20:00:00+02:00
related_to: []
---

# Step 004: QueryValidationService: Command-Timeout statt ConnectTimeoutSeconds verwenden

## Bezug

- **Task:** `audit-hardening`
- **Epic:** `EPIC-04` aus `roadmap.md` — `QueryValidationService` verwendet
  `SqlServerOptions.ConnectTimeoutSeconds` (Connection-Timeout-Option) als
  `DbCommand.CommandTimeout` für die `SET NOEXEC ON`/Parse-Only/
  `SET NOEXEC OFF`-Befehle; semantisch falsche Options-Quelle seit der
  Umbenennung in Step 001. Ganzes Epic = dieser eine Step (drei
  Zeilenänderungen plus Options-Umverdrahtung).
- **Konzept-Referenz:** Direkt keine (kein `konzept.md`-Muss-Haben) — dieses
  Epic entstand aus `TD-001` (`tech-debt.md`), das der Nutzer im Chat
  explizit autorisiert hat, in ein reguläres Epic zu überführen (siehe
  `roadmap.md` Abschnitt „Tech-Debt-Epics (Nutzer-Entscheidung
  2026-08-04)"). Mittelbarer Bezug: `konzept.md` Muss-Haben 1 / EPIC-01
  hat die ursprüngliche Umbenennung von `SqlServerOptions.CommandTimeoutSeconds`
  → `ConnectTimeoutSeconds` vorgenommen, aus der dieser Nebenbefund entstand.

## Aktueller Projektzustand (JIT-Kontext)

- `src/SqlToAi/Database/QueryValidationService.cs` (`ExecuteParseonlyValidationAsync`,
  Zeilen 140-162): Alle drei Commands (`setNoexecCmd`, `queryCmd`,
  `resetCmd`) setzen `CommandTimeout = _dbOptions.ConnectTimeoutSeconds`,
  wobei `_dbOptions` = `options.Value.SqlServer` (Feld, das im Konstruktor
  gesetzt wird und in dieser Klasse **an keiner anderen Stelle** verwendet
  wird — reiner Single-Purpose-Zugriff auf den einen Timeout-Wert).
- `src/SqlToAi/Configuration/SqlToAiOptions.cs`: `QueryExecutionOptions`
  (aus Step 001/EPIC-01) hat bereits ein passendes Feld,
  `CommandTimeoutSeconds` (Zeile 167), mit XML-Doku „Command execution
  timeout in seconds applied to every query run via `sql_execute_query`" —
  diese Doku ist aktuell auf `sql_execute_query`/`QueryExecutionService`
  verengt und muss beim Wiederverwenden für die Validierung erweitert
  werden, sonst wird die Doku irreführend.
- **Entscheidung — Wiederverwendung statt neue Option:** `QueryValidationService`
  bekommt keine eigene, dedizierte Timeout-Option, sondern liest
  `options.Value.QueryExecution.CommandTimeoutSeconds` (bereits per
  `IOptions<SqlToAiOptions>` injiziert, kein neuer DI-Parameter nötig).
  Begründung: Die Parse-Only-Validierung läuft über dieselben `DbCommand`-
  Objekte gegen dieselbe Verbindung, mit demselben Zweck (verhindern, dass
  eine Abfrage unbegrenzt lange läuft, bevor sie überhaupt ausgeführt/
  validiert wird) wie `QueryExecutionService`. Eine dritte, separate
  `ValidationTimeoutSeconds`-Option (wie in `tech-debt.md` als Alternative
  genannt) würde die Options-Fläche unnötig vergrößern, ohne dass
  `konzept.md` oder ein beobachtetes Betriebsszenario einen tatsächlich
  unterschiedlichen Timeout-Bedarf zwischen Ausführung und
  Parse-Only-Validierung nahelegt (Validierung ist typischerweise sogar
  schneller als echte Ausführung — derselbe oder ein großzügigerer Wert
  ist unproblematisch). `appsettings.json` braucht dadurch **keine**
  neue Property; `QueryExecution.CommandTimeoutSeconds` (Default 30,
  bereits vorhanden) deckt beide Verwendungen ab.
- **Entscheidung — `SecondaryConnectionBuilder.cs:54` bleibt unangetastet:**
  Geprüft (siehe `tech-debt.md` TD-001, Verweis auf „strukturell
  identisch"). Der dortige Fund ist zwar ebenfalls ein Timeout-Namens-/
  Verwendungs-Mismatch (`SecondaryConnectionSettings.CommandTimeoutSeconds`
  wird als `SqlConnectionStringBuilder.ConnectTimeout` verwendet — korrekt
  benannt, aber falsch verwendet, also die **umgekehrte** Richtung des
  hier behandelten Mismatches), aber: andere Klasse, anderer
  Verwendungskontext (Connection-String-Aufbau statt `DbCommand.CommandTimeout`),
  andere Aufrufer (`AnonymizationRuleProvider.cs:73`, `MetadataProvider.cs:186`
  — beide unabhängig von `SqlServerOptions`/`QueryExecutionOptions`, beide
  speisen `SecondaryConnectionSettings` aus `AnonymizationRulesOptions`
  bzw. `MetadataProviderOptions`). Ein Fix dort würde eine Umbenennung oder
  Neuinterpretation von `AnonymizationRulesOptions.CommandTimeoutSeconds`
  und `MetadataProviderOptions.CommandTimeoutSeconds` erfordern (beide
  ebenfalls fälschlich als Connect-Timeout verwendet) — das ist eine
  eigene, in sich geschlossene Baustelle mit eigenem Blast-Radius (zwei
  weitere Options-Klassen, zwei weitere Call-Sites), nicht dieselbe wie
  `QueryValidationService`. Bleibt außerhalb des Scopes dieses Steps;
  als eigene Beobachtung für den Kritiker im „Notes"-Abschnitt unten
  festgehalten (kein neuer Tech-Debt-Eintrag durch den Planer — das ist
  Kritiker-Aufgabe).

## Intention

`QueryValidationService.ExecuteParseonlyValidationAsync` soll seinen
`CommandTimeout` aus der semantisch korrekten Options-Quelle
(`QueryExecutionOptions.CommandTimeoutSeconds`, gedacht für Befehlsausführung)
beziehen statt aus `SqlServerOptions.ConnectTimeoutSeconds` (gedacht für den
Verbindungsaufbau). Funktional identisches Verhalten bei Standard-Config
(beide Werte sind aktuell `30`), aber der Name der verwendeten Option passt
danach wieder zu ihrem tatsächlichen Zweck — TD-001 damit geschlossen.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/QueryValidationService.cs`

- **Was:**
  - Feld `_dbOptions` (Typ `SqlServerOptions`) durch `_queryExecutionOptions`
    (Typ `QueryExecutionOptions`) ersetzen; im Konstruktor
    `options.Value.QueryExecution` statt `options.Value.SqlServer`
    zuweisen.
  - In `ExecuteParseonlyValidationAsync` alle drei
    `xxxCmd.CommandTimeout = _dbOptions.ConnectTimeoutSeconds`-Zeilen
    (140-162, konkret Zeilen 143, 151, 160) auf
    `_queryExecutionOptions.CommandTimeoutSeconds` umstellen.
- **Warum:** Kern des Fixes — Command-Timeout für Parse-Only-Validierung
  kommt jetzt aus einer Command-Timeout-Option, nicht mehr aus der
  Connection-Timeout-Option.

### Datei 2: `src/SqlToAi/Configuration/SqlToAiOptions.cs`

- **Was:** XML-Doku-Kommentar von
  `QueryExecutionOptions.CommandTimeoutSeconds` (Zeile 166) erweitern:
  nicht mehr ausschließlich „applied to every query run via
  `sql_execute_query`", sondern zusätzlich erwähnen, dass
  `QueryValidationService` (Parse-Only-Validierung, `sql_validate_query`)
  denselben Wert für seine `SET NOEXEC`-Befehle nutzt.
- **Warum:** Verhindert, dass die Doku nach der Wiederverwendung
  irreführend eng gefasst bleibt (dieselbe Art von Problem, die TD-001
  ursprünglich ausgelöst hat — Options-Name/-Doku vs. tatsächliche
  Verwendung).

## Tests

- [ ] Bestehende `QueryValidationServiceTests.cs`/
  `QueryValidationServiceIntegrationTests.cs` bleiben unverändert grün
  (keine der bestehenden Tests prüft den konkreten `CommandTimeout`-Wert
  oder unterscheidet zwischen den beiden Options-Quellen — verifiziert per
  Grep, kein Treffer für `ConnectTimeoutSeconds`/`CommandTimeout` in
  diesen beiden Dateien).
- [ ] Neuer Unit-Test in `QueryValidationServiceTests.cs`:
  `ValidateQueryAsync_ShouldUseQueryExecutionCommandTimeout_NotConnectTimeout`
  — `SqlToAiOptions` mit unterschiedlichen Werten für
  `SqlServer.ConnectTimeoutSeconds` (z. B. `99`) und
  `QueryExecution.CommandTimeoutSeconds` (z. B. `42`) konstruieren, Service
  über den bestehenden `ValidationMockConnectionFactory`/Fake-Command-Pfad
  eine Validierung ausführen lassen, und über die lokale Fake-Command-
  Instanz (siehe bestehende `ValidationMockConnectionFactory` in derselben
  Testdatei) prüfen, dass `CommandTimeout` auf allen drei ausgeführten
  Commands `42` ist, nicht `99`.

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`roadmap.md`) grün
- [ ] Test-Command aus Tech-Stack-Notiz grün
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-004/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc` — keine hartkodierten Werte:
  betroffen, da der Fix bewusst **keine neue** `appsettings.json`-Property
  einführt, sondern eine bestehende, bereits appsettings-gespiegelte
  Option wiederverwendet; wichtig für die Review, dass kein neuer Hardcode
  entsteht und `AppSettingsMigrator`-Pflicht hier nicht greift (keine neue
  Property).
- `.agents/rules/AiNetLinter.mdc` — `MaxMethodParameterCount`: unverändert
  (Konstruktor-Signatur bleibt exakt gleich, nur die interne Feld-Zuweisung
  ändert sich).

## Bekannte Ausnahmen

- Keine.

## Code-Skizze (optional)

```csharp
// Feld + Konstruktor
private readonly QueryExecutionOptions _queryExecutionOptions;
...
_queryExecutionOptions = options.Value.QueryExecution;

// ExecuteParseonlyValidationAsync — alle drei Stellen analog:
setNoexecCmd.CommandTimeout = _queryExecutionOptions.CommandTimeoutSeconds;
queryCmd.CommandTimeout = _queryExecutionOptions.CommandTimeoutSeconds;
resetCmd.CommandTimeout = _queryExecutionOptions.CommandTimeoutSeconds;
```

## Notes

- **Für den Kritiker — separate Beobachtung, nicht Teil dieses Steps:**
  `src/SqlToAi/Database/SecondaryConnectionBuilder.cs:54` verwendet
  `SecondaryConnectionSettings.CommandTimeoutSeconds` (korrekt benannt) als
  `SqlConnectionStringBuilder.ConnectTimeout` (falsch verwendet — die
  umgekehrte Richtung des hier gefixten Mismatches). Betrifft zwei weitere
  Options-Klassen (`AnonymizationRulesOptions.CommandTimeoutSeconds`,
  `MetadataProviderOptions.CommandTimeoutSeconds`, beide über
  `AnonymizationRuleProvider.cs:73` bzw. `MetadataProvider.cs:186`
  eingespeist) — bewusst nicht in diesem Step mit angefasst, da andere
  Klasse/andere Aufrufer/anderer Blast-Radius als `QueryValidationService`.
  Falls das ebenfalls behoben werden soll, sollte es ein eigenes Epic sein.
- Keine Verhaltensänderung bei Standard-`appsettings.json` (beide Werte
  aktuell `30`) — der Test in diesem Step muss deshalb bewusst
  unterschiedliche Werte setzen, um den Unterschied überhaupt sichtbar zu
  machen (siehe „Tests" oben).
