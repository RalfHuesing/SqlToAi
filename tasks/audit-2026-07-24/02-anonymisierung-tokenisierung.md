# Audit: Anonymisierung & Tokenisierung

## Zusammenfassung

Das Anonymisierungs-Kernmodell (Default-Deny mit expliziten Ausnahmen, Fail-Safe bei Fehlkonfiguration, Regex-Timeouts mit sicherer Schließrichtung) ist grundsätzlich solide gebaut und gut getestet. Der schwerwiegendste Fund betrifft jedoch nicht die Anonymisierung selbst, sondern den Rückweg der Tokenisierung: Bei einem Ausführungsfehler wird die bereits **detokenisierte** (= mit Klartext-Werten aufgelöste) Query sowohl in die App-Log-Datei geschrieben als auch – über die rohe SQL-Server-Fehlermeldung – direkt an die KI zurückgegeben, wodurch reale PII-Werte trotz aktiver Tokenisierung offengelegt werden können. Daneben besteht ein struktureller Blind Spot bei gleichnamigen Tabellen in unterschiedlichen Schemas (Ausschlüsse/Regeln kennen kein Schema), sowie ein Konflikt zwischen der dokumentierten Regel-Gewichtung (`DatabasePattern > TablePattern > ColumnPattern`) und der Erwartung "spezifischste Spalten-Regel schützt immer".

## Findings

### [SEVERITY: Kritisch] Detokenisierte (Klartext-)Query leckt über Fehlermeldung an die KI und ins Log

**Status:** ⚠️ Geteilte Entscheidung — umgesetzt:
- **Log-Datei-Pfad:** ⛔ Won't-Fix — bewusst so gewollt (unverändert, siehe Begründung unten).
- **KI-Antwort-Pfad (`ex.Message` an die KI):** ✅ Erledigt (2026-07-24) — Fehlermeldung an die KI wird bei aktiver Anonymisierung generalisiert, Log bleibt unverändert.

**Datei:** src/SqlToAi/Database/QueryExecutionService.cs:139-145, :190-194, :39-43; src/SqlToAi/Mcp/ToolDispatcher.cs:132,216

**Problem:**
In `ExecuteQueryAsync` wird die Query, sofern die Datenbank tokenisiert wird, vor der Ausführung per `_queryTokenResolver.ResolveTokens(query)` in `effectiveQuery` umgewandelt — d. h. alle Tokens werden durch die **echten Klartextwerte** ersetzt (Zeilen 139-141). Dieses `effectiveQuery` wird als `query`-Parameter an `ExecuteQueryInTransactionAsync` weitergereicht (Zeile 143-144) und dort im Fehlerfall unverändert geloggt:

```csharp
catch (Exception ex)
{
    LogQueryFailed(_logger, databaseName, query, ex);   // query = bereits detokenisiert!
    return SqlToAiError.QueryError(ex.Message);
}
```

Der Logging-Aufruf wurde erst mit Commit `35d090b` ("fix(logging): Protokolliere SQL-Abfrage bei Ausführungs- und Validierungsfehlern", heute) eingeführt und schreibt die Klartext-Query 1:1 in die App-Log-Datei — ganz ohne den Anonymisierungs-/Tokenisierungs-Layer zu durchlaufen.

Noch gravierender: `ex.Message` (die rohe .NET/SqlClient-Fehlermeldung) wird über `SqlToAiError.QueryError(ex.Message)` unverändert an `ToolDispatcher` weitergereicht (`result.Error.Message`, ToolDispatcher.cs:132/216) und landet damit direkt in der Tool-Antwort an die KI — **ohne jede Filterung**. SQL Server bettet bei sehr gängigen Laufzeitfehlern den betroffenen Wert direkt in die Fehlermeldung ein, z. B. Fehler 245: *"Conversion failed when converting the varchar value '<Wert>' to data type int."*

**Konkretes Fehlerszenario:**
1. `Anonymizer.Tokenization` ist aktiv. Eine `Kunden.Email`-Spalte wird tokenisiert an die KI ausgegeben, z. B. als `§§§AbC123...§§§`.
2. Die KI verwendet dieses Token (wie dokumentiert vorgesehen) in einer Folgeabfrage, z. B. versehentlich in einem Vergleich gegen eine numerische Spalte (Schema-Verwechslung, Tippfehler, JOIN auf falsche Spalte): `SELECT * FROM Bestellungen WHERE KundenNr = §§§AbC123...§§§`.
3. `QueryTokenResolver.ResolveTokens` ersetzt das Token vor Ausführung durch den echten Wert, z. B. `max.mustermann@firma.de`.
4. SQL Server wirft: `Conversion failed when converting the varchar value 'max.mustermann@firma.de' to data type int.`
5. Diese Meldung landet unverändert (a) in der App-Log-Datei (`Query: SELECT * FROM Bestellungen WHERE KundenNr = 'max.mustermann@firma.de'`) und (b) als Tool-Fehlerantwort direkt bei der KI — die reale E-Mail-Adresse ist damit an genau der Stelle offengelegt, die die Tokenisierung verhindern sollte.

Das Szenario ist nicht exotisch: Typkonflikte, abgelaufene/falsch kopierte Tokens oder einfache AI-Fehler beim Wiederverwenden eines Tokens sind der Normalfall bei Fehlerpfaden, nicht die Ausnahme.

**Empfehlung:**
- Für den Log-Pfad: In `ExecuteQueryInTransactionAsync`/`ExecuteQueryAsync` zusätzlich die **ursprüngliche** (noch tokenisierte) Query mitführen und ausschließlich diese loggen, niemals `effectiveQuery`.
- Für den KI-Antwortpfad: `ex.Message` vor der Rückgabe an `SqlToAiError.QueryError` filtern/generalisieren (z. B. nur Fehlercode + generische Kategorie an die KI, Volltext nur ins Log — und dort dann mit der *unaufgelösten* Query gemäß obigem Punkt), oder zumindest bekannte wertentragende SQL-Server-Fehlermeldungen (Konvertierungsfehler etc.) erkennen und redigieren.
- Dasselbe Muster prüfen für `QueryValidationService.cs` (dort wird aktuell die *unaufgelöste* Query geloggt, das ist unkritisch) und für jede zukünftige Stelle, die `effectiveQuery`/aufgelöste Werte an Logger oder Fehlermeldungen weiterreicht.

**Sicherheit der Einschätzung:** Hoch. Der Datenfluss wurde Zeile für Zeile nachvollzogen (Variable `query` in `ExecuteQueryInTransactionAsync` ist nachweislich `effectiveQuery` aus `ExecuteQueryAsync`), und `ToolDispatcher.cs` gibt `result.Error.Message` nachweislich ungefiltert weiter. Das SQL-Server-Verhalten für Konvertierungsfehler (Fehler 245) ist gut dokumentiertes Standardverhalten.

---

### [SEVERITY: Hoch] Ausschluss-/Regel-Abgleich ist schema-blind — gleichnamige Tabelle in anderem Schema erbt fremde Freigabe

**Status:** ✅ Umsetzen (bestätigt)

**Datei:** src/SqlToAi/Database/QueryExecutionService.cs:362-390 (`GetBaseTableNames`), :295-305 (`ResolveCentralExclusionsAsync`); src/SqlToAi/Anonymization/AnonymizerExclusionProvider.cs:167-186 (`ParseExclusionRows`); sql-scripts/02_anonymizer_exclusions.sql; sql-scripts/03_anonymization_rules.sql

**Problem:**
Sowohl die datenbankspezifischen Ausnahmen (`AnonymizerExclusionSql`/`ExclusionTableName`) als auch die zentralen `AnonymizationRules` identifizieren eine Tabelle ausschließlich über ihren **bloßen Namen** (`TableName`/`TablePattern`), ohne Schema. Zur Laufzeit wird der Tabellenname über `reader.GetSchemaTable()`-Spalte `BaseTableName` aufgelöst (`GetBaseTableNames`), `BaseSchemaName` wird nirgends ausgelesen oder verglichen. Das SQL-Skript für die Ausnahmetabelle (`02_anonymizer_exclusions.sql`) hat ebenfalls keine `SchemaName`-Spalte; genauso wenig das Regel-Skript (`03_anonymization_rules.sql`, `TablePattern` matcht nur den Tabellennamen).

**Konkretes Fehlerszenario:**
Eine Kundendatenbank enthält `dbo.Kunden` (Testtabelle, Inhalte bewusst nicht sensibel) und `Archiv.Kunden` (echte, historische Kundendaten mit `Email`). Ein Administrator trägt in `AnonymizerExclusions` `('Kunden', 'Email')` ein, um die harmlose `dbo.Kunden.Email`-Testspalte im Klartext zu sehen. Da die Auflösung zur Laufzeit nur `BaseTableName = "Kunden"` liefert (unabhängig vom Schema), gilt dieselbe Ausnahme automatisch auch für `Archiv.Kunden.Email` — echte historische Kunden-E-Mails werden unbeabsichtigt im Klartext an die KI ausgegeben. Dasselbe gilt identisch für `AnonymizationRules.TablePattern`.

**Empfehlung:** `SchemaName`/`BaseSchemaName` als optionale zusätzliche Spalte/Musterkomponente einführen (rückwärtskompatibel mit Default `%`), und `GetBaseTableNames` um `BaseSchemaName` erweitern, sodass Ausnahmen/Regeln wahlweise schema-qualifiziert eingetragen werden können. Mindestens: In der Dokumentation explizit auf dieses Risiko bei Mehrschema-Datenbanken mit gleichnamigen Tabellen hinweisen.

**Sicherheit der Einschätzung:** Hoch. Code-Pfad eindeutig nachvollzogen; kein Test in `tests/SqlToAi.Tests/Anonymization/*` deckt Mehrschema-Szenarien ab.

---

### [SEVERITY: Hoch] Regel-Präzedenz gewichtet Datenbank- vor Spalten-Spezifität — breite DB-Regel kann gezielten Spalten-Schutz aushebeln

**Status:** ✅ Umsetzen (bestätigt — Scoring auf Tupel-Vergleich umstellen statt gewichteter Summe)

**Datei:** src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs:130-157 (`FindMostSpecificMatch`); docs/mcp-specification.md:136

**Problem:**
`FindMostSpecificMatch` berechnet den Score als `Spezifität(DB)*100 + Spezifität(Tabelle)*10 + Spezifität(Spalte)`. Das ist exakt wie in der Doku beschrieben ("gewichtet `DatabasePattern` > `TablePattern` > `ColumnPattern`") und **kein Implementierungsfehler**, sondern dokumentiertes Design. Es führt aber zu einem nicht offensichtlichen Sicherheitsrisiko: Eine Regel mit exaktem, aber breitem Datenbank-Treffer (`Anonymize=false`) schlägt *immer* eine Regel mit exaktem Spalten-Treffer (`Anonymize=true`), selbst wenn Letztere universell (`%` für DB und Tabelle) eine bestimmte, hochsensible Spalte in **jeder** Datenbank schützen soll. Die Gewichtung erfolgt rein pro Ebene, nicht anhand der tatsächlichen "Trefferschärfe" der Kombination — das gewünschte Verhalten "spezifischste Regel gewinnt" wird nur innerhalb derselben Ebenen-Kombination korrekt abgebildet (wie im mitgelieferten Test `IsExcludedAsync_ShouldPreferMoreSpecificRule_ThatReAnonymizesOneColumn` gezeigt, wo DB- und Tabellen-Muster bei beiden Regeln identisch sind), nicht aber ebenenübergreifend.

**Konkretes Fehlerszenario:**
Zentrale Regel A (soll überall gelten): `('%', '%', 'SSN', Anonymize=1)` — Score = 0·100+0·10+2 = 2.
Lokale Ausnahme B (für eine bestimmte Datenbank gedacht, z. B. eine temporäre Staging-Kopie): `('StagingDB', '%', '%', Anonymize=0)` — Score = 2·100+0·10+0 = 200.
Für `(StagingDB, Mitarbeiter, SSN)` matchen beide Regeln; Regel B gewinnt (200 > 2), obwohl Regel A explizit universellen Schutz für `SSN` vorsah. `StagingDB.Mitarbeiter.SSN` wird im Klartext ausgegeben. Genau dieses Muster (Datenbank-Ebene locker, um eine einzelne Spalte pauschal freizugeben) ist im mitgelieferten Demo-Skript (`03_anonymization_rules.sql`, Zeile 44-48, `FakeHighSecurityDb`/`ContactEmail`) als "normaler" Anwendungsfall vorgesehen — ein Administrator, der nach diesem Muster arbeitet, kann leicht eine ebenenübergreifende Kollision mit einer global gedachten Schutzregel erzeugen, ohne dass das System warnt.

**Empfehlung:** Entweder (a) bei Regelkonflikten grundsätzlich "schützende" Regeln (`Anonymize=1`) gegenüber "freigebenden" Regeln (`Anonymize=0`) bei gleicher oder niedrigerer Gesamt-Spezifität bevorzugen, oder (b) die Spezifitätsberechnung ändern, sodass eine exakte Spalten-Angabe nicht durch reine Wildcard-Kombinationen auf DB/Tabellen-Ebene geschlagen werden kann (z. B. Score als Tupel-Vergleich `(DB, Tabelle, Spalte)` statt gewichteter Summe), oder (c) das Risiko in der Dokumentation (Abschnitt E) explizit mit einem Warnhinweis versehen, da es aktuell nur implizit aus der Scoring-Formel ableitbar ist.

**Sicherheit der Einschätzung:** Hoch (Berechnung nachvollzogen und mit konkreten Zahlen durchgerechnet). Dass dies "by design" ist, ändert nichts daran, dass es ein reales, unkommentiertes Leck-Potenzial in der zentralen Cross-Datenbank-Schutzschicht darstellt — dem Feature, dessen Existenzgrund gerade konsistenter Schutz über viele Datenbanken hinweg ist.

---

### [SEVERITY: Mittel] Dokumentiertes Beispiel-Muster `*Id` in `ExcludedColumns` schließt PII-tragende Spalten aus

**Status:** ✅ Umsetzen (bestätigt — Doku-Präzisierung/Warnhinweis)

**Datei:** src/SqlToAi/Anonymization/Anonymizer.cs:104-110 (`IsColumnExcluded`); src/SqlToAi/Anonymization/GlobPatternMatcher.cs; docs/mcp-specification.md (Abschnitt D, Beispiel-Konfiguration); tests/SqlToAi.Tests/Anonymization/AnonymizerTests.cs:47

**Problem:**
Sowohl `docs/mcp-specification.md` (Abschnitt D) als auch die Tests verwenden `"ExcludedColumns": ["*Id", "Id", "*Code", "*Type", "Status", "State", "Category"]` als empfohlenes Beispiel. Das Glob-Muster `*Id` matcht aber jede Spalte, deren Name auf "Id" endet — nicht nur technische Primär-/Fremdschlüssel, sondern auch fachliche, PII-tragende Bezeichner wie `NationalId`, `PassportId`, `SocialSecurityId`, `TaxpayerId`, `SteuerId`. Diese würden bei wörtlicher Übernahme des dokumentierten Beispiels ungewollt im Klartext ausgegeben.

**Konkretes Fehlerszenario:**
Ein Kunde übernimmt die Beispielkonfiguration unverändert. Eine Tabelle `Mitarbeiter` hat eine Spalte `SteuerId` (Steueridentifikationsnummer, eindeutig PII). `GlobPatternMatcher.IsMatch("SteuerId", "*Id")` liefert `true` (Regex `^.*Id$`, `IgnoreCase`) → die Spalte wird als ausgeschlossen behandelt und im Klartext an die KI übergeben, obwohl die generelle Absicht ("alles anonymisieren außer technischen Spalten") das nicht vorsah.

**Empfehlung:** Beispiel in README/Doku präzisieren, z. B. `"*_Id"`/`"Id"` nur für exakte technische Suffixe empfehlen, oder explizit davor warnen, dass `*Id` auch fachliche Kennungen erfasst, die eigentlich schützenswert sind. Alternativ in der Doku eine Negativ-Liste bekannter Risiko-Muster (`*Id`, `*Nr`, `*Number`) mit Hinweis auf Fehlklassifikationsrisiko ergänzen.

**Sicherheit der Einschätzung:** Mittel-Hoch bezüglich der Korrektheit der Code-Analyse (Regex-Verhalten zweifelsfrei geprüft), Einschätzung der praktischen Häufigkeit basiert auf Plausibilität (übliche deutsche/englische Spaltennamenskonventionen), nicht auf einer echten Kundendatenbank.

---

### [SEVERITY: Niedrig] Cache-TTL verzögert Wirksamkeit einer nachträglich entfernten (zu freizügigen) Ausnahme

**Status:** ✅ Umsetzen (bestätigt — nur Doku-Hinweis, kein Codefix)

**Datei:** src/SqlToAi/Anonymization/AnonymizerExclusionProvider.cs:61-75; src/SqlToAi/Anonymization/AnonymizationRuleProvider.cs:63-78; src/SqlToAi/Security/AccessLevelProvider.cs:54-70

**Problem:**
Alle drei Provider cachen ihr Ergebnis für `CacheTtlSeconds` (Default 300s) in einem `ConcurrentDictionary` ohne Invalidierungs-API. Entfernt ein Administrator während des laufenden Betriebs eine fälschlich zu freizügige Ausnahme (z. B. weil ihm auffällt, dass eine sensible Spalte im Klartext sichtbar war), bleibt die alte, freizügigere Regel bis zu 5 Minuten wirksam. Die umgekehrte Richtung (neu hinzugefügte *Schutz*-Regel) ist unkritisch, da bei keinem Treffer ohnehin anonymisiert wird (secure-by-default).

**Konkretes Fehlerszenario:** Admin bemerkt in einer laufenden KI-Sitzung, dass `Kunden.Email` unerwartet im Klartext erscheint (z. B. wegen des `*Id`-Problems oben oder einer falsch gesetzten Regel), entfernt den Exclusion-Eintrag sofort in der DB — der laufende MCP-Serverprozess zeigt die Spalte aber bis zu 5 Minuten weiter im Klartext, ohne dass ein Neustart oder eine sichtbare Fehlermeldung darauf hinweist.

**Empfehlung:** Dies ist ein akzeptierter, dokumentierter Kompromiss (Performance vs. Aktualität) und an sich kein Bug. Sinnvoll wäre lediglich (a) ein sehr viel kürzerer Default für `AnonymizationRules`/`AnonymizerExclusionSql` als für reine Zugriffsebenen-Prüfungen, oder (b) eine dokumentierte Handlungsanweisung "Server neu starten, um Ausnahmen sofort zu invalidieren" für Vorfallreaktionen.

**Sicherheit der Einschätzung:** Hoch bezüglich des Code-Verhaltens; die Einstufung als "geringes Risiko" beruht darauf, dass die Zeitspanne begrenzt ist und ein aktiver Administrator-Eingriff vorausgesetzt wird.

---

### [SEVERITY: Info] DDL-Inhalte (Trigger, Views, Constraints, Routinen) durchlaufen keine Anonymisierung

**Status:** ✅ Umsetzen (bestätigt — Hinweis zusätzlich ins README übernehmen)

**Datei:** src/SqlToAi/Database/DetailSchemaRenderer.cs (gesamt, insbes. `GetSchemaConstraintsAsync` Z.165-217, `GetTriggerDefinitionAsync` Z.219-239); src/SqlToAi/Database/TableSchemaRenderer.cs (`GetViewDefinitionMarkdownAsync`, `GetRoutineSchemaMarkdownAsync`)

**Problem:** Schema-Werkzeuge, die Roh-DDL liefern (View-Definitionen, Trigger-Bodies, Routinen-Quelltext, DEFAULT-/CHECK-Constraint-Definitionen), geben diesen Text vollständig unverändert zurück — es gibt hierfür keinen Anonymisierungs- oder Kennzeichnungsmechanismus. Das ist im Kern korrekt (DDL ist Metadatenschema, keine Nutzdaten, und wird per Design als Klartext behandelt), aber DEFAULT-Constraints oder Trigger-Logik können theoretisch hartkodierte Literale enthalten (z. B. Testdaten-Defaults, Kommentare mit Beispiel-E-Mail-Adressen), die dann ungefiltert an die KI gehen.

**Konkretes Fehlerszenario:** Eine `DEFAULT` Constraint wie `DEFAULT ('admin@kundenfirma.de')` auf einer `Kontakt.Email`-Spalte, oder ein Trigger-Body mit einem hartkodierten Test-/Debug-Kommentar, der eine reale Adresse oder einen realen Namen enthält, wird 1:1 über `sql_get_schema_constraints`/`sql_get_trigger_definition` ausgegeben.

**Empfehlung:** Kein Code-Fix notwendig, aber in der Doku (Abschnitt "Proactive Anonymization Awareness") explizit erwähnen, dass DDL-Text grundsätzlich ungefiltert bleibt, damit Administratoren bei Schema-Design (Default-Werte, Kommentare) entsprechend vorsichtig sind.

**Sicherheit der Einschätzung:** Mittel — die Wahrscheinlichkeit, dass reale PII in DDL-Text landet, ist gering, aber nicht null, und der Code-Pfad wurde vollständig verifiziert (keinerlei Anonymizer-Aufruf in `DetailSchemaRenderer.cs`).

---

### [SEVERITY: Info] Nicht-String-PII (Geburtsdatum, numerische Ausweisnummern) wird nie anonymisiert

**Status:** ✅ Umsetzen (bestätigt — Hinweis zusätzlich ins README übernehmen)

**Datei:** src/SqlToAi/Database/QueryExecutionService.cs:333 (`raw is not string strVal`); docs/mcp-specification.md Abschnitt D ("Bekannte Grenze")

**Problem:** Der gesamte Anonymisierungs-Mechanismus greift ausschließlich, wenn der zur Laufzeit gelesene .NET-CLR-Wert ein `string` ist. Spalten vom Typ `DATE`/`DATETIME` (z. B. Geburtsdatum), `INT`/`BIGINT` (z. B. numerisch gespeicherte Sozialversicherungs-/Steuernummern) oder `UNIQUEIDENTIFIER` werden nie erfasst, unabhängig von `ExcludedColumns`, `AnonymizationRules` oder Access Level. Dies ist bereits in `docs/mcp-specification.md` Abschnitt D als "Bekannte Grenze" dokumentiert, daher kein verstecktes Verhalten — wird hier trotzdem als Restrisiko festgehalten, da es eine reale und leicht übersehbare Lücke im Sicherheitsversprechen "String-Spalten werden anonymisiert" darstellt, falls ein Kunde Geburtsdatum/Ausweisnummer numerisch/als Datum modelliert.

**Empfehlung:** Keine Codeänderung zwingend erforderlich (Scope-Entscheidung), aber Hervorhebung dieser Grenze auch im README (aktuell nur in `docs/mcp-specification.md`), da README die primäre Einstiegs-Doku ist und die PII-Shield-Bullet-Points dort keinen Hinweis auf diese Einschränkung enthalten.

**Sicherheit der Einschätzung:** Hoch (Code eindeutig, Dokumentation vorhanden). Als Info eingestuft, weil transparent dokumentiert.

## Positive Beobachtungen

- **Fail-safe bei fehlender/fehlerhafter Zugriffsprüfung:** Ist `AccessCheckSql` leer, wird `ReadOnlyAnonymized` angenommen (nie `ReadWrite`); schlägt die Prüfung fehl, wird restriktiv auf `None` gesetzt (`AccessLevelProvider.cs:78-103`).
- **Tokenisierung fällt sauber auf Standard-Maskierung zurück**, wenn `Secret`/`Prefix`/`Suffix` nicht vollständig gesetzt sind (`TokenizationOptions.IsUsable`, `Anonymizer.cs:69-75`) — es gibt keinen Zustand, in dem eine unvollständig konfigurierte Tokenisierung zu Klartextausgabe führt.
- **Regex-Timeouts schließen sicher:** Sowohl `GlobPatternMatcher` als auch `LikePatternMatcher` liefern bei Timeout `false` zurück, was in beiden Verwendungskontexten (Ausschluss-Prüfung bzw. Regel-Match) in Richtung "mehr anonymisieren" bzw. "keine Ausnahme greift" fail-closed ist.
- **Token-Resolver arbeitet literal-genau:** `QueryTokenResolver`/`SqlLiteralScanner` ersetzen Tokens ausschließlich innerhalb tatsächlicher String-Literal-Inhalte (nie in Kommentaren, `[...]`-Bezeichnern oder Keywords), und ein unbekanntes/gefälschtes Token bleibt unverändert stehen statt einen Fehler zu werfen oder unsicher zu terminieren — kein Orakel-Verhalten zwischen "falsch geraten" und "nicht vorhanden".
- **Anonymisierungs-Durchsetzung ist typ-agnostisch zur Laufzeit:** `QueryExecutionService.AnonymizeCell` maskiert jeden Wert, der zur Laufzeit als `string` zurückkommt — unabhängig vom deklarierten SQL-Typ. Das ist strenger als die rein kosmetische `AnonymizableSqlTypes`-Liste in `TableSchemaRenderer`, die nur die Schema-Anzeige ("Yes"/"No") steuert; ungewöhnliche aber string-artige Rückgabetypen (z. B. aus berechneten Spalten/Views) werden trotzdem geschützt, auch wenn das Schema-Tool sie als "No" anzeigen könnte.
- **Tokenisierung und reguläre Maskierung teilen sich denselben Exclusion-Entscheidungspfad** (`Anonymizer.IsColumnExcluded`), sodass Tokenisierung keine Ausnahme umgehen kann, die die normale Maskierung respektieren würde.
- **Determinismus des Tokens ist korrekt implementiert:** HMAC-SHA256 über volle 256 Bit ohne Kürzung, sodass keine erhöhte Kollisionswahrscheinlichkeit zwischen unterschiedlichen Realwerten besteht; unterschiedliche Secrets erzeugen nachweislich unterschiedliche Tokens für denselben Wert (verhindert Cross-Installation-Korrelation).
