# Audit: Security-Guardrails & Secrets

## Zusammenfassung

Der Audit deckt zwei kritische, vollständig durch Code-Trace verifizierte Umgehungen der zentralen Sicherheitsversprechen auf: (1) die Spalten-Alias-basierte Ausschlusslogik der Anonymisierung lässt sich mit gewöhnlichem SQL (`AS <ExcludedPattern>`) austricksen und hebelt PII-Shield und Tokenisierung vollständig aus, und (2) der mehrstufige Schreibschutz (Regex-Guard + Transaction-Rollback) lässt sich mit `sp_executesql` + eingebettetem `COMMIT` gleichzeitig umgehen, sodass echte, dauerhaft committete Mutationen möglich sind, obwohl das Tool "Query error" meldet. Zusätzlich leaken entschlüsselte (detokenisierte) Klartextwerte über Fehlermeldungen sowohl ins Error-Log als auch direkt in die Tool-Antwort an die KI. Insgesamt: 3 Kritisch, 1 Hoch (Verdacht, nicht vollständig verifiziert), 1 Niedrig-Mittel, 2 Info. Positiv: Fail-closed-Verhalten bei `AccessLevelProvider`-Exceptions, sauberer Pfad-Traversal-Schutz im MCP-Trail, und der Multi-Statement-/Kommentar-/String-Literal-Parser selbst ist grundsätzlich sorgfältig implementiert.

## Findings

### [SEVERITY: Kritisch] PII-Shield und Tokenisierung durch Spalten-Alias vollständig aushebelbar
**Status:** ✅ Erledigt (2026-07-24)

**Datei:** src/SqlToAi/Anonymization/Anonymizer.cs:88-113 (IsColumnExcluded), src/SqlToAi/Database/QueryExecutionService.cs:307-357 (AppendSerializedRow/AnonymizeCell), src/SqlToAi/Database/QueryExecutionService.cs:392-400 (GetColumnNames)

**Problem:** Die Entscheidung, ob eine Spalte anonymisiert wird, hängt in `IsColumnExcluded` ausschließlich vom **Ausgabe-Spaltennamen** ab:
```csharp
foreach (string excludedPattern in _options.Anonymizer.ExcludedColumns)
{
    if (GlobPatternMatcher.IsMatch(columnName, excludedPattern))
        return true;
}
```
`columnName` stammt in `QueryExecutionService.GetColumnNames` direkt aus `reader.GetName(i)` — das ist der **Alias**, den die Abfrage selbst vergibt, nicht der tatsächliche Ursprungs-Spaltenname. Die einzige Stelle, die den echten Ursprung berücksichtigt (`BaseTableNames` via `GetSchemaTable()`), wird nur für die datenbankspezifische Exklusionstabelle (`dbExclusions`, Schlüssel `"{tableName}.{columnName}"`) und für Central-Rule-Lookups verwendet — nicht für die einfache, glob-basierte `AnonymizerOptions.ExcludedColumns`-Liste, die laut Projektregeln (`.agents/rules/SqlToAiRichtlinien.mdc`, Abschnitt 2) gerade für technische Spalten wie `*Id`, `*Code`, `*Type`, `Status` empfohlen wird ("um LLM-Nutzen zu wahren").

**Konkretes Fehlerszenario:**
1. Admin konfiguriert (wie im Projekt selbst empfohlen) `ExcludedColumns: ["*Id", "*Code"]`, damit IDs für Joins lesbar bleiben.
2. Die KI (oder eine über Prompt-Injection kompromittierte KI) sendet an eine `ReadOnlyAnonymized`-Datenbank:
   ```sql
   SELECT Email AS SomeCode, SSN AS RecordId FROM Customers
   ```
3. `reader.GetName(i)` liefert `"SomeCode"` bzw. `"RecordId"` als Spaltennamen. `IsColumnExcluded("SomeCode", "Customers", ...)` matcht `"*Code"` → `true`. `IsColumnExcluded("RecordId", "Customers", ...)` matcht `"*Id"` → `true`.
4. Beide Spalten werden **nicht** anonymisiert und **nicht** tokenisiert — die realen `Email`- und `SSN`-Werte fließen unverändert in `result.Value.Data` und damit direkt an die KI zurück, ohne dass `WasAnonymized` gesetzt wird oder irgendeine Warnung erscheint.

Das exakt gleiche Muster funktioniert auch für bereits ausgegebene Tokens: Da `QueryTokenResolver.ResolveTokens` einen Token in einem Literal (z. B. `SELECT '§§§tok§§§' AS SomeId FROM X`) korrekt zum echten Wert auflöst, bevor die Abfrage ausgeführt wird, und die aufgelöste Konstante anschließend als String-Spalte `SomeId` durch dieselbe (durch Alias umgehbare) Ausschlussprüfung läuft, wird auch der zuvor "sichere" Token-Rückweg zur Klartext-Exfiltration.

**Empfehlung:** Ausschlussentscheidung nicht (nur) auf den Alias, sondern primär auf den via `GetSchemaTable()` aufgelösten echten Ursprung (Basistabelle + Basis-Spaltenname) stützen; wenn kein Ursprung auflösbar ist (reines Berechnungs-/Literalfeld), sollte konservativ **nicht** ausgeschlossen werden dürfen (fail-safe: anonymisieren statt Alias vertrauen).

**Sicherheit der Einschätzung:** Verifiziert durch vollständigen Code-Trace über `Anonymizer.IsColumnExcluded` → `GlobPatternMatcher.IsMatch(columnName, ...)` und `QueryExecutionService.GetColumnNames` → `reader.GetName(i)`. Kein Test in `tests/SqlToAi.Tests/Database/QueryExecutionServiceTests.cs` deckt Alias-Umbenennung ab (alle Anonymisierungstests verwenden den unveränderten Spaltennamen als Alias).

---

### [SEVERITY: Kritisch] Mehrstufiger Schreibschutz durch `sp_executesql` + eingebettetes `COMMIT` gleichzeitig umgehbar
**Status:** ✅ Erledigt (2026-07-24)

**Datei:** src/SqlToAi/Security/ReadOnlyGuard.cs:13-16, src/SqlToAi/Database/QueryExecutionService.cs:415-439 (ContainsMultipleStatements), src/SqlToAi/Database/QueryExecutionService.cs:147-195 (ExecuteQueryInTransactionAsync)

**Problem:** `ReadOnlyGuard`s Regex verwendet `\b(...|exec|execute|...)\b` mit Wortgrenzen. `sp_executesql` ist (wegen des Unterstrichs, der als Wortzeichen zählt) ein einziges zusammenhängendes Token — `exec` taucht darin nicht als eigenständiges, wortgrenzen-begrenztes Vorkommen auf, der Regex matcht also nicht. Da T-SQL erlaubt, `sp_executesql` **ohne** vorangestelltes `EXEC`/`EXECUTE` als erste (und einzige) Anweisung eines Batches aufzurufen, entkommt der komplette Aufruf der Keyword-Prüfung. Die eigentlich mutierende Anweisung liegt zusätzlich als String-Literal-Inhalt vor (`N'...'`), wird also von `StripCommentsAndStringLiterals` vor der Regex-Prüfung ohnehin ausgeblendet.

Zusätzlich zählt `ContainsMultipleStatements` Semikola nur außerhalb von String-Literalen — ein `;` innerhalb des `N'...'`-Arguments von `sp_executesql` wird nicht als zweite Anweisung erkannt.

Die zweite Verteidigungslinie (immer Rollback außer bei `AccessLevel.ReadWrite`) greift nur, solange die Transaktion beim Rollback-Aufruf noch offen ist. Ein `COMMIT` **innerhalb** des dynamischen SQL-Batches, den `sp_executesql` ausführt, läuft in derselben Verbindung/demselben `@@TRANCOUNT`-Kontext wie die von `QueryExecutionService` extern per `BeginTransactionAsync` geöffnete Transaktion und committet diese vorzeitig — bevor der C#-Code überhaupt zur (dann wirkungslosen bzw. fehlschlagenden) `RollbackAsync()`-Anweisung kommt.

**Konkretes Fehlerszenario:**
1. Datenbank hat `AccessLevel.ReadOnly` oder `ReadOnlyAnonymized` (nicht `ReadWrite`); der konfigurierte SQL-Login hat (wie in vielen realistischen Setups mit einem gemeinsamen Login über `AccessCheckSql`-Steuerung statt separater Logins) tatsächliche `DELETE`-Rechte auf der Zieltabelle.
2. Anfrage: `sp_executesql N'DELETE FROM dbo.Customers; COMMIT'`
3. `ReadOnlyGuard.IsQuerySafe(...)`: Nach Entfernen des String-Literal-Inhalts bleibt `"sp_executesql N "` übrig — kein Keyword-Match → **als sicher eingestuft**.
4. `ContainsMultipleStatements(...)`: Das Semikolon liegt innerhalb des `N'...'`-Literals (State `SingleQuote`) → **nicht als Mehrfach-Statement erkannt**.
5. Ausführung: `BeginTransactionAsync` (`@@TRANCOUNT`=1) → `sp_executesql` führt intern `DELETE FROM dbo.Customers` (innerhalb der Ambient-Transaktion) gefolgt von `COMMIT` aus → `COMMIT` committet die Ambient-Transaktion sofort und dauerhaft (`@@TRANCOUNT`→0).
6. Da `writeAllowed == false`, ruft der Code anschließend `transaction.RollbackAsync()` auf — die zugrunde liegende SQL-Transaktion existiert zu diesem Zeitpunkt aber nicht mehr; der Aufruf schlägt typischerweise mit einem SQL-Fehler fehl ("no corresponding BEGIN TRANSACTION" o. ä.), der im äußeren `catch`-Block landet und als `SQL-AI-0102 Query error: ...` an die KI zurückgemeldet wird.
7. Ergebnis: Die KI sieht eine Fehlermeldung und geht davon aus, dass **nichts** passiert ist — tatsächlich wurden die Daten aber bereits unwiderruflich gelöscht.

**Empfehlung:** `ReadOnlyGuard` um eine explizite Sperre für `sp_executesql`/`sys.sp_executesql` (auch ohne vorangestelltes `EXEC`) erweitern, unabhängig davon ob es als String-Literal-Inhalt oder Bezeichner erscheint; zusätzlich grundsätzlich verhindern, dass eine ausgeführte Abfrage `COMMIT`/`ROLLBACK`/`BEGIN TRAN` selbst enthalten kann (z. B. durch Nutzung von `SET XACT_ABORT ON` und Prüfung von `@@TRANCOUNT` vor/nach jeder Ausführung, mit hartem Fehler bei Abweichung statt eines stillen/verschleiernden Fehlschlags).

**Sicherheit der Einschätzung:** Die Umgehung der Regex- und Multi-Statement-Prüfung ist durch Code-Trace zweifelsfrei verifiziert. Das exakte Verhalten von `SqlTransaction.RollbackAsync()` nach einem serverseitigen `COMMIT` außerhalb der ADO.NET-Transaktionsverwaltung (Fehler vs. stiller No-op) wurde nicht gegen eine echte SQL-Server-Instanz getestet, ändert aber nichts am Kernbefund: Die Mutation ist bereits vor dem Rollback-Versuch dauerhaft committet.

---

### [SEVERITY: Kritisch] Detokenisierte Klartextwerte leaken über Fehlerpfad (Error-Log und direkte KI-Antwort)
**Status:** ⚠️ Geteilte Entscheidung:
- **Log-Datei-Pfad:** ⛔ Won't-Fix — **bewusst so gewollt.** Begründung des Nutzers: Als Admin hat man ohnehin Zugriff auf den SQL Server; die Klartext-Query im Log wird benötigt, um gemeldete Fehler verifizieren zu können. Bleibt unverändert bestehen.
- **KI-Antwort-Pfad (`ex.Message` an die KI):** ✅ Umsetzen — die rohe Fehlermeldung darf trotzdem nicht ungefiltert an die KI zurückgehen, da genau das die Tokenisierung/Anonymisierung an ihrer Zielgruppe (der KI) vorbei aushebelt. Log bleibt wie gehabt unverändert.

**Datei:** src/SqlToAi/Database/QueryExecutionService.cs:139-145, 147-195 (insbesondere Zeile 192 `LogQueryFailed`), src/SqlToAi/Domain/SqlToAiError.cs:29-30 (`QueryError`)

**Problem:** `effectiveQuery` (die Abfrage **nach** `QueryTokenResolver.ResolveTokens`, d. h. mit bereits durch echte Werte ersetzten Tokens) wird als `query` an `ExecuteQueryInTransactionAsync` übergeben. Schlägt die Ausführung fehl, greift:
```csharp
catch (Exception ex)
{
    LogQueryFailed(_logger, databaseName, query, ex);            // query = bereits detokenisiert
    return SqlToAiError.QueryError(ex.Message);                   // geht direkt an die KI zurück
}
```
Zwei Leck-Pfade:
1. **Ins Error-Log-File** (persistiert, Standard-Retention 90 Tage): Die vollständige, bereits detokenisierte Abfrage inkl. der realen Werte wird über `LogQueryFailed` (Log-Level Error) in Klartext auf die Festplatte geschrieben.
2. **Direkt an die KI**: `ex.Message` wird unverändert in `SqlToAiError.QueryError(message)` verpackt und laut `ToolDispatcher.cs:132` (`ToolCallResult.Failure(result.Error.Code, result.Error.Message)`) als Tool-Antworttext zurückgegeben. SQL-Server-Fehlermeldungen bei Typkonvertierungsfehlern zitieren typischerweise den betroffenen Wert wörtlich (z. B. *"Conversion failed when converting the varchar value '…' to data type int."*). Wenn dieser Wert der gerade aufgelöste Klartext eines Tokens ist, sieht die KI den echten PII-Wert unmittelbar in der Fehlermeldung — unabhängig vom oben genannten Log-Leck.

**Konkretes Fehlerszenario:**
1. Tokenisierung ist aktiv (`AccessLevel.ReadOnlyAnonymized`), eine frühere Abfrage lieferte der KI einen Token `§§§tok123§§§` für eine IBAN-ähnliche Spalte `AccountRef` (int-Spalte in der DB, fälschlich als String erwartet).
2. KI verwendet den Token in einer Folgeabfrage: `SELECT * FROM Accounts WHERE AccountRef = '§§§tok123§§§'`.
3. `QueryTokenResolver.ResolveTokens` löst den Token zum echten, alphanumerischen Wert auf, z. B. `'DE89-CUST-4471'`.
4. SQL Server versucht die implizite Konvertierung von `varchar` nach `int` (Spaltentyp `AccountRef`) und wirft: *"Conversion failed when converting the varchar value 'DE89-CUST-4471' to data type int."*
5. Diese Exception-Message landet unverändert in `SqlToAiError.QueryError(...)` → die KI erhält den realen Wert `'DE89-CUST-4471'` im Klartext, obwohl die Datenbank als `ReadOnlyAnonymized` konfiguriert ist. Zusätzlich wird derselbe Wert (als Teil der Query) dauerhaft ins Error-Log geschrieben.

**Empfehlung:** Bei aktiver Anonymisierung/Tokenisierung niemals die detokenisierte Query-Variante loggen (stattdessen die Original-Query mit Tokens loggen) und `ex.Message` vor Rückgabe an die KI auf enthaltene, aus dem Vault bekannte Realwerte prüfen bzw. generische Fehlermeldungen ohne Payload-Zitat zurückgeben, wenn `anonymize == true`.

**Sicherheit der Einschätzung:** Verifiziert durch Code-Trace (Variable `effectiveQuery`/`query`-Shadowing, `LogQueryFailed`-Aufruf, `ToolDispatcher.cs:132`). Das exakte Verhalten von SQL Server bzgl. Wert-Zitierung in Fehlermeldungen ist gut dokumentiertes, allgemein bekanntes Verhalten (z. B. bei `CONVERT`/impliziter Typkonvertierung), aber nicht gegen eine echte Instanz mit den Projektdaten nachgestellt.

---

### [SEVERITY: Hoch — Verdacht, nicht vollständig verifiziert] `sql_validate_query` besitzt keinen ReadOnlyGuard- und keinen Multi-Statement-Schutz
**Status:** ✅ Umsetzen (bestätigt — defensiv nachrüsten, unabhängig vom Verifikationsstand)

**Datei:** src/SqlToAi/Database/QueryValidationService.cs:47-97, 99-122

**Problem:** `QueryValidationService.ValidateQueryAsync` prüft nur `IsDatabaseAllowed` und `AccessLevel != None`. Anders als `QueryExecutionService` ruft es weder `IReadOnlyGuard.IsQuerySafe` noch eine Mehrfach-Statement-Prüfung auf. Der einzige Schutz ist `SET PARSEONLY ON` gefolgt von der Nutzerabfrage als separatem Kommando, umschlossen von einer Transaktion, die im `finally`-Block **immer** zurückgerollt wird — unabhängig vom Access Level (auch bei `ReadWrite` wird hier nie committet, das ist konsistent).

**Konkretes Fehlerszenario (nicht vollständig verifiziert):** Falls `SET PARSEONLY OFF` als erste Anweisung *innerhalb* desselben an den Server gesendeten Kommandotexts wirksam würde (der als `query`-Parameter übergebene String selbst kann mehrere durch `;` getrennte Anweisungen enthalten, da hier keine `ContainsMultipleStatements`-Prüfung existiert), könnte folgender Aufruf an `sql_validate_query` reale, dauerhafte Mutationen auslösen:
```
SET PARSEONLY OFF; DELETE FROM dbo.Customers; COMMIT
```
Falls SQL Server den ganzen Batch (inkl. der `SET PARSEONLY OFF`-Anweisung selbst) wegen des zuvor per separatem Kommando gesetzten `PARSEONLY ON` als reinen Parse-Vorgang behandelt und **nicht** ausführt, greift diese Kette nicht — das ist die naheliegendste Lesart der offiziellen SET-PARSEONLY-Semantik, konnte aber mangels Zugriff auf eine echte SQL-Server-Instanz nicht verifiziert werden.

**Empfehlung:** Unabhängig vom Ausgang der obigen Unsicherheit: `QueryValidationService` sollte defensiv dieselben `IReadOnlyGuard`- und `ContainsMultipleStatements`-Prüfungen wie `QueryExecutionService` durchführen, bevor der Query-Text überhaupt an den Server geschickt wird. Aktuell verlässt sich die Sicherheit dieses Tools auf eine einzige, nicht redundante Verhaltensannahme über eine SQL-Server-interne Option.

**Sicherheit der Einschätzung:** Das Fehlen der Guard-Aufrufe ist zweifelsfrei per Code-Trace verifiziert. Ob das konkrete Ausnutzungsszenario tatsächlich funktioniert, ist unklar — explizit als Verdacht markiert.

---

### [SEVERITY: Niedrig-Mittel] Config-Migration dupliziert Secrets unkontrolliert in `.bak`-Datei
**Status:** ✅ Umsetzen (bestätigt)

**Datei:** src/SqlToAi/Configuration/AppSettingsMigrator.cs:193-199 (CreateBackupFile)

**Problem:** Bei jeder erkannten Änderung an den Factory-Defaults (z. B. nach einem Versions-Update) kopiert `AppSettingsMigrator` die bestehende `appsettings.json` (inkl. Klartext-Connection-Passwort, sofern nicht per Umgebungsvariable referenziert) unverändert nach `appsettings.json.bak`. Diese Datei wird nie durch `LogRetentionService` oder eine andere Aufräum-Logik entfernt und bleibt dauerhaft mit denselben (oder laxeren) Dateisystemrechten wie das Original liegen.

**Konkretes Fehlerszenario:** Ein Betreiber sichert/versendet zu Support-Zwecken das Installationsverzeichnis ohne `appsettings.json` (weil er weiß, dass dort das Passwort steht), übersieht aber `appsettings.json.bak`, die exakt dieselben Zugangsdaten in Klartext enthält.

**Empfehlung:** Entweder das Backup nach erfolgreicher Migration mit derselben Redigierungslogik behandeln wie andere sensible Artefakte (z. B. `Password`-Feld vor dem Kopieren maskieren, sofern nicht per Env-Var referenziert), oder zumindest im Migrationslog explizit warnen, dass die `.bak`-Datei dieselben Geheimnisse enthält wie das Original.

**Sicherheit der Einschätzung:** Verifiziert durch Code-Trace (`File.Copy(targetFilePath, backupPath, overwrite: true)`).

---

### [SEVERITY: Info] Cache-TTL der AccessLevel-Prüfung kann Rechte-Downgrade verzögern
**Status:** ✅ Umsetzen (bestätigt — nur Doku-Hinweis, kein Codefix)

**Datei:** src/SqlToAi/Security/AccessLevelProvider.cs:47-70, src/SqlToAi/Domain/AccessCheckResult.cs

**Beobachtung:** Der `AccessLevel`-Cache ist rein zeitbasiert (`CacheTtlSeconds`, Default 300s) und bietet keine Möglichkeit zur sofortigen Invalidierung. Wird `AccessCheckSql` serverseitig geändert, um einer Datenbank dringend die Berechtigung zu entziehen (z. B. Incident Response), bleibt der zuvor zwischengespeicherte großzügigere Level (z. B. `ReadWrite`) bis zu 5 Minuten wirksam. Dies ist ein bewusster, im Projekt dokumentierter Trade-off (Performance vs. Reaktionszeit) und keine Fehlfunktion — als Betriebs-Hinweis dennoch erwähnenswert, insbesondere da die Default-TTL relativ hoch ist.

**Empfehlung:** Für Incident-Response-Szenarien einen Mechanismus zum manuellen Invalidieren des Caches (z. B. Prozess-Neustart als dokumentierter Workaround, oder ein administratives Tool/Signal) explizit dokumentieren.

---

### [SEVERITY: Info] Faktorischer Default-appsettings.json enthält ein Klartext-Demo-Passwort
**Status:** ✅ Umsetzen (bestätigt — nur Kommentar im Template, kein Verhaltens-Fix)

**Datei:** src/SqlToAi/appsettings.json:16-21

**Beobachtung:** Das eingebettete Factory-Default (`"UserId": "Agent"`, `"Password": "Agent!"`) wird bei Erstinstallation automatisch in die lokale `appsettings.json` jedes Nutzers geschrieben (`AppSettingsMigrator.CreateInitialConfiguration`). Klar erkennbar als Demo-Konfiguration für eine lokale `DemoDB`-Instanz (der zugehörige `AccessCheckSql` gewährt `ReadWrite` nur, wenn `DB_NAME() = 'DemoDB' AND SYSTEM_USER = 'Agent'`), daher kein akutes Risiko. Erwähnenswert als Hygiene-Punkt, falls Nutzer dieses Muster unreflektiert in echte Produktivkonfigurationen übernehmen.

## Positive Beobachtungen

- `AccessLevelProvider.QueryAccessLevelAsync` schlägt bei **jeder** Exception (Verbindungsfehler, Timeout, Parsing-Fehler) sauber auf `AccessLevel.None` fehl — durch Tests (`AccessLevelProviderTests.GetAccessLevelAsync_ShouldReturnNone_WhenSqlThrowsException`) abgedeckt und im Code nachvollziehbar korrekt (auch der Fall "Query liefert keine Zeile" und "Wert ist DBNull" fallen sauber auf `None` zurück).
- `McpTrailWriter.SanitizeForFileName` behandelt die vom Client gelieferte JSON-RPC-`id` korrekt als potenziell böswillige Eingabe und verhindert Path-Traversal in Log-Dateinamen — eine Detailschärfe, die man leicht übersehen könnte.
- Der Multi-Statement-/Kommentar-/String-Literal-Parser (`SqlLiteralScanner`, `ContainsMultipleStatements`, `StripCommentsAndStringLiterals`) ist für den Kernfall (Kommentare, escapte Quotes, Bracket-Identifier) sorgfältig und mit guter Testabdeckung implementiert; die gefundenen Lücken betreffen ausschließlich die Interaktion mit `sp_executesql`/eingebettetem Transaktionsmanagement, nicht die Grundmechanik selbst.
- Die Reihenfolge "immer Rollback außer bei explizitem `ReadWrite`" ist als Grundprinzip richtig und wird durch Tests wie `ExecuteQueryAsync_ShouldStillRollBack_WhenAccessLevelIsNotReadWrite` sauber abgesichert.
- Passwörter werden nirgends explizit geloggt; `ConnectionString`-Werte werden nicht in Log- oder Trail-Dateien geschrieben.
