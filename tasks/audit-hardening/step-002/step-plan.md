---
status: done (pending audit)
type: step-plan
task: audit-hardening
step: "002"
title: "Serverseitiges Row-Limit via SET ROWCOUNT"
epic: EPIC-02
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: Claude Sonnet 5
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-08-04T09:00:00+02:00
related_to: []
---

# Step 002: Serverseitiges Row-Limit via SET ROWCOUNT

## Bezug

- **Task:** `audit-hardening`
- **Epic:** `EPIC-02` aus `roadmap.md` — Serverseitiges Row-Limit via `SET ROWCOUNT`, noch
  vollständig offen.
- **Konzept-Referenz:** `konzept.md` Muss-Haben Punkt 2 („Serverseitiges Row-Limit
  Enforcement") und „Wie" Schritt 2.

## Aktueller Projektzustand (JIT-Kontext)

- `src/SqlToAi/Database/QueryExecutionService.cs`, Methode `ExecuteAndSerializeAsync`
  (Zeilen 234-295): Bereits vorhanden ist der private, statische Helper
  `ExecuteSetOptionAsync(DbConnection, DbTransaction, string sql, CancellationToken)`
  (Zeile 289-295) — genau der in `konzept.md`/`roadmap.md` als Vorbild genannte Mechanismus. Er
  wird aktuell zweimal genutzt, um `SET STATISTICS IO ON` und `SET STATISTICS TIME ON` vor dem
  eigentlichen Query auf derselben Connection/Transaction auszuführen (Zeilen 243-244). Sein
  XML-Kommentar verweist bereits ausdrücklich auf „step-002" als künftigen dritten Aufrufer —
  dieser Step ist also exakt der dort erwartete Use-Case, keine neue Struktur nötig.
- Die clientseitige Row-Limit-Schleife `while (rowCount < args.RowLimit && await
  reader.ReadAsync(...))` (Zeile 263) bleibt laut Konzept unverändert als Sicherheitsnetz bestehen
  — dieser Step ergänzt nur ein zusätzliches, serverseitiges `SET ROWCOUNT` davor, ersetzt sie
  nicht.
- `effectiveLimit`/`args.RowLimit` ist zu diesem Zeitpunkt bereits ein validierter `int`
  (`Math.Min(requestedRowLimit, _options.MaxRowLimit)` bzw. `_options.DefaultRowLimit`, siehe
  Zeilen 139-141) — kein zusätzlicher appsettings-Eintrag nötig, `QueryExecutionOptions.MaxRowLimit`
  bleibt laut Konzept „unverändert" die Quelle des harten Limits.
- Referenz-Pattern für Session-Settings mit Reset: `src/SqlToAi/Database/QueryValidationService.cs`
  Zeilen 140-163 setzt `SET NOEXEC ON`, führt die eigentliche Anweisung in einem `try`-Block aus und
  setzt in `finally` `SET NOEXEC OFF` zurück — dasselbe try/finally-Reset-Prinzip ist für `SET
  ROWCOUNT` zu übernehmen (siehe „Notes" unten zur Reader-Dispose-Reihenfolge).
- Test-Infrastruktur ist bereits vorbereitet: `tests/SqlToAi.Tests/Database/
  QueryExecutionServiceMockDb.cs`, `MockQueryConnectionFactory.ExecutedNonQueryCommands` (Zeile
  120-124) sammelt bereits jeden `ExecuteNonQuery`-Aufruf (aktuell die beiden `SET STATISTICS
  ...`-Befehle) und der zugehörige XML-Kommentar verweist ebenfalls explizit auf „step-002" — kein
  neuer Mock nötig, der neue `SET ROWCOUNT ...`/`SET ROWCOUNT 0`-Befehl landet automatisch in dieser
  bereits existierenden Liste.
- `tech-debt.md` (Index gelesen): TD-001 betrifft `QueryValidationService`/Command-Timeout, keine
  Berührung mit diesem Step — nicht relevant für Row-Limit.

## Intention

Nach diesem Step entlastet der Server pro Abfrage bereits auf SQL-Server-Seite (`SET ROWCOUNT
@limit`), statt die volle Ergebnismenge zu lesen und erst client-seitig abzuschneiden — robust
gegenüber beliebigen, LLM-generierten SELECT-Formen (CTEs, UNIONs, vorhandenes TOP/ORDER BY), ohne
den Query-Text anzufassen. Die bestehende clientseitige Schleife bleibt unverändert als zweites
Sicherheitsnetz bestehen (Konzept: bewusst nicht als alleinige Technik verworfen). Wiederverwendung
des bestehenden `ExecuteSetOptionAsync`-Helpers statt einer neuen Struktur, analog zum bereits
etablierten `SET STATISTICS`-Muster direkt daneben.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Database/QueryExecutionService.cs` (Methode `ExecuteAndSerializeAsync`, Zeilen 234-281)

- **Was:**
  1. Direkt nach den beiden bestehenden `ExecuteSetOptionAsync(..., "SET STATISTICS ...", ...)`-Aufrufen
     (nach Zeile 244) einen dritten Aufruf ergänzen:
     `await ExecuteSetOptionAsync(args.Connection, args.Transaction, $"SET ROWCOUNT {args.RowLimit}", cancellationToken);`
  2. Den Reader-Block (`using var reader = ...` bis Ende der `while`-Schleife, Zeilen 254-267) so
     umstrukturieren, dass der Reader **vor** dem Zurücksetzen von `SET ROWCOUNT 0` sicher
     geschlossen/disposed ist — z. B. durch ein `try { using var reader = ...; ...Schleife... }
     finally { await ExecuteSetOptionAsync(args.Connection, args.Transaction, "SET ROWCOUNT 0",
     cancellationToken); }` um den bestehenden Reader-Abschnitt. Reset erfolgt **immer** (auch bei
     Exceptions innerhalb der Schleife), damit `SET ROWCOUNT` nicht über das Ende dieses einen
     Aufrufs hinaus auf der (potenziell gepoolten) Connection wirksam bleibt.
  3. `columnNames`/`anonCtx`, die aktuell nach dem Reader-`using` im Methodenkörper weiterverwendet
     werden (Zeilen 256-257, dann erneut ab 273 für den Rückgabewert), müssen entsprechend aus dem
     neuen `try`-Block herausgereicht werden (z. B. als außerhalb deklarierte, im `try` zugewiesene
     lokale Variablen) — keine Änderung an ihrer Berechnung selbst, nur an der Scope-Struktur.
- **Warum:** Serverseitige Entlastung laut `konzept.md` Muss-Haben 2; Wiederverwendung des
  bestehenden Helpers statt neuer Struktur; try/finally-Reset verhindert, dass `SET ROWCOUNT` nach
  Rückgabe der Connection an den Connection-Pool für eine spätere, andere Abfrage auf derselben
  physischen Verbindung wirksam bleibt (mirrored von `QueryValidationService`s `SET NOEXEC
  ON/OFF`-Pattern).

## Tests

- [ ] Neuer Test in `tests/SqlToAi.Tests/Database/QueryExecutionServiceTests.cs` (oder passende
  bestehende Testdatei im selben Verzeichnis): prüft über
  `MockQueryConnectionFactory.ExecutedNonQueryCommands`, dass ein `SET ROWCOUNT {N}`-Befehl mit dem
  tatsächlich effektiven Row-Limit (sowohl für den `requestedRowLimit`-Pfad als auch den
  `DefaultRowLimit`-Fallback-Pfad) vor dem eigentlichen Query-Execute ausgeführt wird.
- [ ] Ergänzender Test/Assertion: nach erfolgreichem Durchlauf enthält
  `ExecutedNonQueryCommands` auch `SET ROWCOUNT 0` (Reset), und zwar **nach** dem `SET ROWCOUNT
  {N}`-Eintrag in der Reihenfolge der Liste.
- [ ] Bestehende Tests, die die Reihenfolge/den Inhalt von `ExecutedNonQueryCommands` bereits prüfen
  (`SET STATISTICS ...`), dürfen durch den neuen dritten/vierten Eintrag nicht brechen — ggf.
  anpassen, falls sie exakte Listenlänge statt nur Teilmengen prüfen.
- [ ] Regressionstest: bestehender Test, der die clientseitige Zeilenbegrenzung bei
  `RowCount` > `RowLimit` prüft (falls vorhanden), bleibt grün — stellt sicher, dass das neue
  serverseitige `SET ROWCOUNT` das clientseitige Sicherheitsnetz nicht ersetzt oder bricht.

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command aus Tech-Stack-Notiz (`roadmap.md`) grün
- [ ] Test-Command aus Tech-Stack-Notiz grün
- [ ] Commit auf aktuellem Branch (Conventional Commit)
- [ ] `step-002/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „Keine hartkodierten Werte & AppSettings-Pflicht":
  hier **nicht** verletzt, da `args.RowLimit` bereits aus der bestehenden,
  appsettings-gebundenen `QueryExecutionOptions.MaxRowLimit`/`DefaultRowLimit` stammt — es wird kein
  neuer Zahlenwert im Code hartkodiert, nur der bereits validierte Wert in den SQL-Text
  interpoliert (kein SQL-Injection-Risiko, da `int`, keine Nutzereingabe als String).
- `.agents/rules/SqlToAiRichtlinien.mdc#4` — Dokumentations-Synchronisation: prüfen, ob
  `docs/architecture-spec.md`/`README.md` das clientseitige Row-Limit-Verhalten beschreiben und ggf.
  um die serverseitige `SET ROWCOUNT`-Ergänzung nachziehen müssen.

## Bekannte Ausnahmen

- Keine.

## Code-Skizze (optional)

```csharp
await ExecuteSetOptionAsync(args.Connection, args.Transaction, "SET STATISTICS IO ON", cancellationToken);
await ExecuteSetOptionAsync(args.Connection, args.Transaction, "SET STATISTICS TIME ON", cancellationToken);
await ExecuteSetOptionAsync(args.Connection, args.Transaction, $"SET ROWCOUNT {args.RowLimit}", cancellationToken);

var stopwatch = System.Diagnostics.Stopwatch.StartNew();

using var command = args.Connection.CreateCommand();
command.CommandText = args.Query;
command.Transaction = args.Transaction;
command.CommandTimeout = _options.CommandTimeoutSeconds;
SqlParameterBinder.BindParameters(command, args.Parameters);

List<string> columnNames;
AnonymizationContext anonCtx; // exakter Typname wie im bestehenden Code
var sb = new StringBuilder();
int rowCount = 0;
var tracker = new RowAnonymizationTracker();

try
{
    using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.KeyInfo, cancellationToken);
    columnNames = GetColumnNames(reader);
    anonCtx = await ResolveAnonymizationContextAsync(reader, columnNames, args.Anonymize, args.DatabaseName, cancellationToken);

    while (rowCount < args.RowLimit && await reader.ReadAsync(cancellationToken))
    {
        AppendSerializedRow(sb, reader, columnNames, anonCtx, tracker);
        rowCount++;
    }
}
finally
{
    await ExecuteSetOptionAsync(args.Connection, args.Transaction, "SET ROWCOUNT 0", cancellationToken);
}
```

(Exakter Typname von `anonCtx` und Rückgabewert von `ResolveAnonymizationContextAsync` beim
Coden im echten Code nachschlagen — hier nur zur Illustration der Scope-Umstrukturierung.)

## Notes

- **Reihenfolge kritisch:** Der Reader muss vor dem `SET ROWCOUNT 0`-Reset vollständig
  geschlossen sein, sonst schlägt der Reset-Befehl auf einer Connection ohne MARS
  (Multiple Active Result Sets) fehl, da noch ein offener `DbDataReader` aktiv ist. Das
  `try { using var reader = ...; } finally { ...reset... }`-Muster stellt das sicher, weil
  `using var` innerhalb eines `try`-Blocks ein inneres, implizites try/finally erzeugt, dessen
  `Dispose()` vor dem äußeren `finally` läuft.
- **Kein neuer Options-Eintrag nötig:** anders als bei EPIC-01 gibt es hier keinen neuen
  appsettings-Wert zu ergänzen — `SET ROWCOUNT` nutzt ausschließlich den bereits vorhandenen,
  validierten `args.RowLimit`.
- **Bewusst nicht angefasst:** die clientseitige `while`-Schleife selbst (Zeile 263) bleibt
  strukturell unverändert (nur ggf. neu eingerückt durch den try-Block) — sie ist laut Konzept
  weiterhin das zweite Sicherheitsnetz, kein Ersatz.
- Der XML-Kommentar von `ExecuteSetOptionAsync` (Zeile 283-288) erwähnt bereits „step-002" als
  Vorgriff — nach Umsetzung ggf. prüfen, ob der Kommentar noch aktuell ist oder leicht generalisiert
  werden sollte (jetzt drei statt zwei Aufrufer).
