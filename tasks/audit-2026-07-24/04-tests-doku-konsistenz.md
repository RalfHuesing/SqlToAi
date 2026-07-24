# Audit: Tests & Doku-Konsistenz

## Zusammenfassung

Die Sicherheitskern-Klassen (`ReadOnlyGuard`, `SecurityGuard`, `SqlLiteralScanner`, `QueryTokenResolver`, `Anonymizer`, `AnonymizerExclusionProvider`, `AnonymizationRuleProvider`, `QueryExecutionService`) sind überraschend gründlich und mit vielen Edge-Cases getestet. Die auffälligste Lücke ist `QueryValidationService`: Die security-relevante Guard-Logik (leere Parameter, geblockte Datenbank, `AccessLevel.None`) hat **keinerlei** Unit-Test und wird auch in der Integrationstest-Suite nicht abgedeckt — und diese Integrationstests laufen ohnehin nie in CI, da die einzige GitHub-Actions-Pipeline (`release.yml`) nur baut/published, aber nie `dotnet test` aufruft. Bei der Doku-Konsistenz wurde ein verwaistes, unbenutztes und undokumentiertes Konfigurationspaar (`SqlServer.EnforceSafetyCheck`/`SafetyCheckSql`) sowie zwei nie erzeugte, aber als aktiv dokumentierte Fehlercodes (`SQL-AI-0105`, `SQL-AI-0106`) gefunden; ansonsten sind README.md und mcp-specification.md bemerkenswert genau mit dem Code synchron (Tool-Liste, AccessLevel-Werte, Fehlercode-Katalog).

## Teil A: Testabdeckungslücken

### [SEVERITY: Kritisch] QueryValidationService hat keine Unit-Tests und die Guard-Klauseln sind auch integrationstestseitig ungetestet
**Status:** ✅ Erledigt (2026-07-24)

**Datei/Klasse:** `src/SqlToAi/Database/QueryValidationService.cs`
**Fehlende Abdeckung:** Es existiert keine `tests/SqlToAi.Tests/Database/QueryValidationServiceTests.cs` (oder gleichwertig) mit Mocks. Die einzige Testklasse ist `tests/SqlToAi.Tests/Integration/QueryValidationServiceIntegrationTests.cs`, die einen laufenden SQL Server voraussetzt (`SqlServerFixture`) und nur 4 Fälle prüft: gültige Query, gültige Query gegen bekannte Tabelle, ungültige Syntax, syntaktisch gültige Mutation. Die eigentlichen Sicherheits-Guards von `ValidateQueryAsync` — leerer `databaseName`/`query` (Zeilen 52–59), `!_securityGuard.IsDatabaseAllowed` (Zeile 61–64, `SafetyCheckFailed`), `accessLevel == AccessLevel.None` (Zeile 67–70, `WriteOperationBlocked`) — werden **von keinem einzigen Test** angesteuert, weder unit- noch integrationsseitig. `ToolDispatcherTests.cs` verwendet für `sql_validate_query` nur ein `FakeQueryValidationService` und testet damit ausschließlich das Routing, nicht die echte Klasse.
**Risiko:** Genau die Guard-Logik, die laut Regelwerk (Abschnitt 4) "Pflicht" ist (Safety-Check, Read-Only-nahe Zugriffsprüfung), könnte durch eine zukünftige Änderung stillschweigend brechen (z. B. Reihenfolge der Checks vertauscht, ein Guard versehentlich entfernt) und kein Test würde es bemerken. Da `sql_validate_query` denselben Sicherheitspfad wie `sql_execute_query` durchläuft (Whitelist + AccessLevel), aber ohne die dort vorhandene Testtiefe, besteht eine reale Lücke im "Defense-in-Depth"-Nachweis.
**Empfehlung:** Eine `QueryValidationServiceTests.cs` nach dem Muster von `QueryExecutionServiceTests.cs` ergänzen (Fakes für `ISecurityGuard`/`IAccessLevelProvider`, kein echter DB-Zugriff nötig für die Guard-Klauseln), die mindestens folgende Fälle abdeckt: leere/leere-Whitespace `database`/`query` → `InvalidParametersCode`; nicht erlaubte Datenbank → `SafetyCheckFailedCode`; `AccessLevel.None` → `WriteOperationBlockedCode`; erfolgreicher Pfad mit Rollback (`transaction.RollbackAsync` immer aufgerufen, auch bei Erfolg).

### [SEVERITY: Kritisch] Keine der Testsuiten läuft automatisiert in CI
**Status:** ⛔ Won't-Fix — **bewusst abgelehnt.** Keine CI-Testautomatisierung gewünscht; bleibt unverändert (nur lokale/manuelle Testausführung).

**Datei/Klasse:** `.github/workflows/release.yml` (einzige vorhandene Workflow-Datei)
**Fehlende Abdeckung:** Der Workflow wird nur bei Tag-Push (`on: push: tags: v*`) ausgelöst und führt ausschließlich `dotnet publish` pro Plattform aus; `dotnet test` erscheint an keiner Stelle. Es gibt keinen weiteren Workflow (z. B. für Pull Requests oder Pushes auf `main`), der die xUnit-v3-Suite ausführt.
**Risiko:** Selbst die sehr gründlichen vorhandenen Tests (siehe Positive Beobachtungen) bieten keinerlei automatisierten Schutz vor Regressionen, da sie nie im Rahmen von PR-Checks oder Merges laufen — nur lokal, wenn ein Entwickler/Agent selbst `dotnet test` aufruft. Ein sicherheitsrelevanter Regressionsfehler (z. B. im Read-Only-Guard) könnte unbemerkt bis zum Release-Tag durchrutschen, weil der Build-Job keine Testphase hat und rein auf `dotnet publish` (das Testprojekt nicht einmal kompiliert) beruht.
**Empfehlung:** Einen CI-Workflow ergänzen, der bei jedem Push/PR mindestens `dotnet test --filter "Category!=Integration"` ausführt (die Integration-Tests benötigen einen echten SQL Server und können optional in einem separaten, manuell/nightly getriggerten Job mit Testcontainer/lokaler Instanz laufen).

### [SEVERITY: Hoch] AccessLevelProvider — numerische AccessLevel-Werte, Fallback-Spalte und "keine Zeile"-Pfad ungetestet
**Status:** ✅ Erledigt (2026-07-24)

**Datei/Klasse:** `src/SqlToAi/Security/AccessLevelProvider.cs`, Methode `ParseAccessLevel` (Zeile 138–168) und `QueryAccessLevelAsync` (Zeile 72–104)
**Fehlende Abdeckung:** `tests/SqlToAi.Tests/Security/AccessLevelProviderTests.cs` deckt nur drei Fälle ab: leeres `AccessCheckSql` (→ `ReadOnlyAnonymized`), String-Rückgabe `"ReadOnly"` mit Cache/TTL, und eine geworfene Exception (→ `None`). Ungetestet bleiben: (1) der numerische Zweig `int.TryParse` in `ParseAccessLevel` (Werte `0`–`4` sowie ein außerhalb der Switch-Range liegender Wert wie `99`, der laut Code auf `AccessLevel.None` fallen soll); (2) der Fallback-Pfad in `ParseResult` (Zeile 123–127), wenn die Ergebniszeile *keine* Spalte namens `AccessLevel` besitzt, sondern nur eine beliebig benannte Einzelspalte; (3) der Zweig, in dem `QueryFirstOrDefaultAsync` `null` liefert (keine Zeile zurückgegeben) → Warn-Log + `AccessLevel.None` (Zeile 91–95); (4) ein nicht parsbarer String-Wert (weder Zahl noch gültiger Enum-Name) → `AccessLevel.None` (Zeile 167).
**Risiko:** Der numerische Rückgabewert ist laut Doku (`mcp-specification.md`, Abschnitt B) ein offiziell unterstützter Rückgabetyp für `AccessCheckSql` (Tabelle mit Werten `0`–`4`). Ein Regressionsfehler in der `switch`-Zuordnung (z. B. vertauschte Werte 2/3 zwischen `ReadOnlyAnonymized`/`ReadOnly` — sicherheitskritisch, da das über Klartext vs. anonymisiert entscheidet) würde von keinem Test erkannt.
**Empfehlung:** Theory-Tests für `ParseAccessLevel` über alle Werte `0`–`4`, einen ungültigen Wert (`99`), einen nicht parsbaren String (`"foo"`) sowie einen Test für den Einzelspalten-Fallback und den "keine Zeile"-Fall ergänzen.

### [SEVERITY: Mittel] GlobPatternMatcher hat keine eigene Testklasse
**Status:** ✅ Umsetzen (bestätigt)

**Datei/Klasse:** `src/SqlToAi/Anonymization/GlobPatternMatcher.cs`
**Fehlende Abdeckung:** Anders als das strukturell fast identische `LikePatternMatcher` (mit eigener `LikePatternMatcherTests.cs`, 10 Theory-Fälle) existiert für `GlobPatternMatcher` keine dedizierte Testklasse. Er wird nur indirekt über `AnonymizerTests.cs`/`AnonymizationPolicyResolverTests.cs` mit wenigen Mustern (`"Id"`, `"*Id"`, `"*Code"`, `"Status"`) mitgetestet. Ungetestet: das `?`-Einzelzeichen-Wildcard, Muster mit Regex-Sonderzeichen (z. B. ein Spaltenname mit `.` oder `+`, die durch `Regex.Escape` literal behandelt werden müssen), leeres `text`-Argument, sowie der Timeout-Fallback (`RegexMatchTimeoutException` → `false`) analog zu dem, was `SecurityGuardTests`/`LikePatternMatcherTests` für ihre jeweiligen Matcher-Pendants tun.
**Risiko:** Gering bis mittel — die PII-Ausschlussmuster (`ExcludedColumns`) hängen direkt von diesem Matcher ab; ein Regex-Escaping-Fehler würde bedeuten, dass ein Spaltenname mit Sonderzeichen fälschlich (nicht) ausgeschlossen wird.
**Empfehlung:** Eine `GlobPatternMatcherTests.cs` analog zu `LikePatternMatcherTests.cs` ergänzen (Theory über `*`, `?`, Sonderzeichen, leeres Pattern/Text).

### [SEVERITY: Mittel] SecurityGuard — keine Tests für `?`-Wildcard und Regex-Sonderzeichen in Datenbanknamen
**Status:** ✅ Umsetzen (bestätigt)

**Datei/Klasse:** `src/SqlToAi/Security/SecurityGuard.cs`, Methode `MatchesPattern` (Zeile 60–80)
**Fehlende Abdeckung:** `SecurityGuardTests.cs` testet nur `*`-Wildcards (`"Demo_*"`) und exakte Treffer. Der `?`-Wildcard-Zweig (`\\?` → `.`) sowie ein Datenbankname mit Regex-Metazeichen (z. B. ein SQL-Server-Instanzname mit `.` wie `MyServer.1`) werden nicht getestet — beides Teil der dokumentierten Wildcard-Unterstützung ("Unterstützung von einfachen Wildcards wie `*`" in `mcp-specification.md`, Abschnitt A, dort wird `?` nicht mal erwähnt, siehe Teil B).
**Risiko:** Gering — dieselbe Escape-Logik wie bei `GlobPatternMatcher`, aber hier zusätzlich sicherheitskritisch, da sie direkt die Datenbank-Whitelist steuert.
**Empfehlung:** Theory-Fälle für `?` und einen Datenbanknamen mit `.`/`+`/`(` ergänzen.

## Teil B: Doku-Inkonsistenzen

### [SEVERITY: Mittel] `SqlServer.EnforceSafetyCheck`/`SafetyCheckSql` existieren im Code, sind aber weder dokumentiert noch im Template noch tatsächlich wirksam
**Status:** ✅ Umsetzen (bestätigt — entfernen, inkl. Migrationslogik)

**Quelle:** README.md (Konfigurationstabelle Zeile 46, Abschnitt "SqlServer") und docs/mcp-specification.md (Abschnitt A "Multi-Datenbank-Sicherheit") — keiner von beiden erwähnt diese Optionen; src/SqlToAi/appsettings.json (Zeilen 16–22, `SqlServer`-Block) enthält sie ebenfalls nicht.
**Code-Realität:** src/SqlToAi/Configuration/SqlToAiOptions.cs:30-31 definiert `public bool EnforceSafetyCheck { get; set; } = true;` und `public string SafetyCheckSql { get; set; } = string.Empty;` auf `SqlServerOptions`. `ConfigurationResolver.cs:37,89` expandiert/löst `SafetyCheckSql` (Env-Vars, `.sql`-Dateipfade), aber **keine** Klasse in `Security/`, `Database/` oder sonstwo liest `EnforceSafetyCheck` oder wertet `SafetyCheckSql` inhaltlich aus — `SecurityGuard.cs` prüft ausschließlich `Databases.Allowed`/`Blocked` und `SqlServer.ExcludedDatabases`.
**Diskrepanz:** Zwei Konfigurationsoptionen existieren im Options-Modell und werden beim Start verarbeitet (Env-Var-Expansion, `.sql`-Dateiauflösung inkl. möglicher `FileNotFoundException` beim Start!), haben aber keinerlei Wirkung auf das Verhalten und sind nirgends dokumentiert — vermutlich Überbleibsel einer älteren, einstufigen "Safety Check"-Vorstufe, die durch das heutige, dokumentierte `Databases.AccessCheckSql`-Konzept abgelöst wurde, ohne dass die alten Felder entfernt wurden. Das README-Feature "🚦 Safety/Demo Probe Check" (Zeile 14) beschreibt inhaltlich exakt `AccessCheckSql`, trägt aber einen Namen, der stark an das verwaiste `SafetyCheckSql` erinnert — verwirrend für jemanden, der den Code liest.
**Empfehlung:** `EnforceSafetyCheck`/`SafetyCheckSql` entweder entfernen (inkl. Migrationslogik in `AppSettingsMigrator`, falls betroffen) oder, falls für eine geplante Funktion vorgesehen, mit TODO/Issue-Verweis kennzeichnen und in `SqlToAiOptionsTests.cs`/Doku klarstellen, dass sie aktuell wirkungslos sind.

### [SEVERITY: Niedrig] Fehlercodes SQL-AI-0105 (Infrastrukturfehler) und SQL-AI-0106 (Timeout) werden dokumentiert, aber von keinem Code-Pfad erzeugt
**Status:** ✅ Umsetzen (bestätigt — gezieltes Exception-Mapping implementieren)

**Quelle:** docs/mcp-specification.md:286-287 (Fehlercode-Tabelle, Abschnitt 5) listet `SQL-AI-0105` "Infrastrukturfehler" und `SQL-AI-0106` "Timeout" als reguläre, vom Server zurückgegebene Fehler.
**Code-Realität:** src/SqlToAi/Domain/SqlToAiError.cs:38-42 definiert `InfrastructureError(...)` und `Timeout()` als Factory-Methoden, aber eine repo-weite Suche zeigt: Keine einzige Stelle in `src/SqlToAi/**/*.cs` ruft `SqlToAiError.InfrastructureError(...)` oder `SqlToAiError.Timeout()` auf. Alle tatsächlich auftretenden Exceptions in `QueryExecutionService.cs` (Zeile 190-194) und `QueryValidationService.cs` (Zeile 92-96) werden stattdessen einheitlich auf `SqlToAiError.QueryError(ex.Message)` (SQL-AI-0102) gemappt — unabhängig davon, ob es sich um einen echten Verbindungsabbruch, einen Timeout oder einen SQL-Syntaxfehler handelt. Getestet werden die beiden Factory-Methoden nur isoliert in `SqlToAiErrorTests.cs` (Codes/Message-Format), nie im Kontext eines echten Fehlerpfads.
**Diskrepanz:** Die Doku suggeriert differenzierte Fehlercodes für Infrastruktur- vs. Timeout- vs. generische Query-Fehler, die ein aufrufendes LLM/Client zur gezielten Fehlerbehandlung nutzen könnte — tatsächlich liefert der Server für all diese Fälle einheitlich `SQL-AI-0102`.
**Empfehlung:** Entweder die Unterscheidung in `QueryExecutionService`/`QueryValidationService` tatsächlich implementieren (z. B. `SqlException`/`TimeoutException`/`SocketException` gezielt auf `InfrastructureError`/`Timeout` mappen) oder die Doku-Tabelle um einen Hinweis ergänzen, dass diese beiden Codes aktuell reserviert, aber noch nicht produktiv erzeugt werden.

## Positive Beobachtungen

* Die Kernsicherheits-Klassen sind vorbildlich getestet: `ReadOnlyGuard` (Kommentare/String-Literale, geschachtelte Anführungszeichen, alle mutierenden Keywords inkl. `INTO`/`EXEC`), `SqlLiteralScanner` (Bracket-Identifier, Escaped Quotes, Offset-Rundtrip), `QueryTokenResolver` (IN-Listen, LIKE-Wildcards, Escaping, vollständiger Egress/Ingress-Rundtrip mit dem echten `Anonymizer`) und `QueryExecutionService` (Rollback/Commit-Zählung per Fake-Transaction, Row-Limit-Kappung, Tokenisierung vs. reguläre Maskierung, zentrale Regel-Ausschlüsse) — alle mit echten Edge-Cases statt nur Happy-Path.
* `AnonymizationRuleProvider` und `AnonymizerExclusionProvider` testen explizit Cache/TTL-Verhalten (inkl. Ablauf nach `Task.Delay`), Tabellen-Existenzprüfung und Fail-Safe-Verhalten bei SQL-Fehlern.
* Die Tool-Liste (12 Tools), die AccessLevel-Werte (`0`-`4`) und der komplette Fehlercode-Katalog (`SQL-AI-0001`, `0101`-`0110`) stimmen zwischen `docs/mcp-specification.md`, `src/SqlToAi/Domain/AccessLevel.cs`, `src/SqlToAi/Domain/SqlToAiError.cs` und `src/SqlToAi/Mcp/ToolRegistry.cs` exakt überein — keine Abweichung gefunden.
* `ConfigurationResolver` (Env-Var-Expansion, `%COMPUTERNAME%`-Fallback, `.sql`-Dateiauflösung inkl. `FileNotFoundException`) ist entgegen erster Vermutung gut getestet, siehe `SqlToAiOptionsTests.cs`.
