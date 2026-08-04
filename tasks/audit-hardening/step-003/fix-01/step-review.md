---
status: done
type: step-review
task: audit-hardening
step: "003/fix-01"
epic: EPIC-03
step_type: single
reviewed_by: kritiker
reviewed_by_model: Claude Sonnet 5
reviewed_by_model_knowledge_cutoff: 2026-01
reviewed_at: 2026-08-04T19:30:00+02:00
verdict: approved
tech_debt_ids: [TD-003]
---

# Review Step 003/fix-01: Strukturschlüssel-Ausnahme positionsabhängig statt namensabhängig machen

## Verdict

- [x] **approved** — alle vier Prüfebenen ok
- [ ] **issues** — Fix-Step `step-<NNN>/fix-<XX>` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt
- [x] Rules-Konformität: `<rules_dir>/**` eingehalten
- [x] Logische Korrektheit: Code macht was er soll, nicht nur „grün"
- [x] Konzept-Treue: passt die Umsetzung zu `konzept.md` (Scope, Non-Goals, Muss-Haben)
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (499 Tests)

## Befund

Alle vier Prüfebenen bestätigt durch `git show d64241d` (Diff komplett gelesen), eigenen `dotnet build`/`dotnet test`-Lauf und stichprobenartige Nachverfolgung der Kontext-Weitergabe (`RedactionContext` durch `AnonymizeContainer`/`AnonymizeObjectProperties`/`AnonymizeArrayElements`). Das ursprüngliche CRITICAL-Finding ist geschlossen: `IsExemptStructuralKey` prüft jetzt `(context.IsEnvelopeRoot && EnvelopeKeys.Contains(key)) || (context.IsContentBlock && key == ContentBlockTypeKey)` statt des globalen `StructuralKeys.Contains(key)`; der neue Test `Record_ShouldRedactArgumentsProperties_NamedLikeStructuralKeys` reproduziert exakt das im ursprünglichen Review genannte Angriffsszenario (`arguments: {"id": "123-45-6789", "type": ..., "method": ...}`) und verifiziert Redaktion in `*-request.json` und `*-call.jsonl`, während die echten Envelope-Keys (`jsonrpc`, `method` auf der Wurzel von `RawRequestJson`) lesbar bleiben. Die Regression für den Content-Block-Discriminator (`Record_ShouldKeepContentBlockTypeDiscriminator_Readable_ButRedactNestedTypeElsewhere`) bestätigt, dass `result.content[0].type` weiterhin lesbar bleibt, während ein `type` innerhalb eines bereits serialisierten Text-Blobs weiterhin (akzeptiert) mitredigiert wird. Selbst nachvollzogen: `AnonymizeArrayElements` setzt `IsContentBlock` nur für direkte Elemente eines `content`-Arrays und übergibt beim Abstieg in Kind-Objekte konsequent zurückgesetzten `childContext = default` — die Ein-Ebene-Tiefe-Garantie aus dem Plan ist eingehalten.

Der vom Coder als „Abweichung" dokumentierte Datei-Umbau (`GetDayDir()`-Helper, positionale Konstruktor-Kurzform in `pii-1` bis `pii-5`) wurde stichprobenartig gegen den `McpCallRecord`-Konstruktor (`CorrelationId, Method, Tool, RawRequestJson, ArgumentsJson, ResponseJson, DurationMs, Success`, `McpTrailWriter.cs:29-37`) geprüft — alle Positions-Argumente in den umgestellten Tests entsprechen exakt dieser Reihenfolge und den vorherigen benannten Werten; reine Kompaktierung ohne Verhaltensänderung, wie behauptet. Datei liegt bei 487 Zeilen (`MaxLineCount` 500, `AiNetLinter.mdc`), Baseline-Diff (`SqlToAi-baseline.json`) enthält ausschließlich die beiden erwarteten Hash-Updates für `McpTrailWriter.cs`/`McpTrailWriterTests.cs`.

Die vom Coder genannte „bekannte Unschärfe" (`content`-Array-Sonderfall greift auf jedes Objekt mit einer `content`-Property, nicht nur `result.content[]`) ist keine durch diesen Fix neu eingeführte Lücke, sondern eine vom Plan selbst explizit sanktionierte Restungenauigkeit („im Zweifel darf der Check aber grobzügiger sein" — Plan, Abschnitt „Konkrete Änderungen"). Vor dem Fix waren `id`/`type`/`method` ausnahmslos überall im Baum exempt; nach dem Fix nur noch, wenn zusätzlich ein struktureller `content`-Array-Kontext vorliegt — eine strikte Verengung des Angriffsfensters, nicht dessen Erweiterung. Das im ursprünglichen CRITICAL-Finding konkret benannte Szenario (`arguments.id`/`arguments.type`/`arguments.method` direkt) ist vollständig geschlossen. Verbleibend ist nur ein sehr schmaler, synthetischer Randfall (ein LLM wählt selbst `content` als Bind-Parameter-Namen mit Array-Wert, dessen Objekte wiederum `type` enthalten) — dafür siehe `TD-003`.

### Plan-Erfüllung

Alle „Konkreten Änderungen" umgesetzt: `RedactionContext`-Record-Struct, `isEnvelope`-Parameter an `AnonymizeJsonStrings` mit korrekter Verdrahtung der drei Aufrufer (`ArgumentsJson: false`, `RawRequestJson`/`ResponseJson: true`), Kontext-Durchreichung durch alle drei Redaction-Methoden, `IsExemptStructuralKey`-Hilfsmethode, Content-Array-Sonderfall mit korrektem Reset. Beide geforderten Tests (Kernfall + Regression) sowie der optionale dritte Testfall (verschachteltes `type` außerhalb eines Content-Blocks) vorhanden und grün.

### Rules-Konformität

- `AiNetLinter.mdc` `MaxCognitiveComplexity`/`MaxCyclomaticComplexity`: Methoden bleiben klein und flach (`AnonymizeObjectProperties`/`AnonymizeArrayElements` je ein Foreach/For mit if/else-if/else, `IsExemptStructuralKey` ein Einzeiler); kein erneutes Extract-Method nötig, Baseline-Update bestätigt grünen Lauf. Eingehalten.
- `AiNetLinter.mdc` `MaxLineCount`: Testdatei bei 487/500 Zeilen nach `GetDayDir()`-Extraktion. Eingehalten.
- `SqlToAiRichtlinien.mdc` (keine hartkodierten Werte): `EnvelopeKeys`/`ContentBlockTypeKey` bleiben feste, kurze Literale ohne Konfigurationsbezug, wie im Plan vorgesehen. Eingehalten.
- Commit-Konventionen: Conventional-Commit-Format, deutsche Message, `Refs:`-Zeile vorhanden. Eingehalten.

### Logische Korrektheit

Rekursion korrekt: Kontext wird beim Abstieg in beliebige Kind-Knoten zurückgesetzt (`childContext = default`), außer beim `content`-Array-Sonderfall, der `IsContentBlock` genau eine Ebene tief setzt und danach ebenfalls zurücksetzt (`AnonymizeArrayElements` Zeile 390: `childContext = default` für alle Nicht-Content-Block-Kinder). Kein Pfad gefunden, über den `IsEnvelopeRoot` oder `IsContentBlock` versehentlich über mehr als die vorgesehene Ebene hinaus propagiert.

### Konzept-Treue (Ebene 4)

Muss-Haben 3 aus `konzept.md` („MCP-Trail-Dateien enthalten dieselbe Anonymisierung ... unabhängig vom `AccessLevel`") ist durch diesen Fix jetzt tatsächlich erfüllt statt nur scheinbar — genau die im vorherigen Review als Untergrabung dieses Muss-Haben-Ziels benannte Lücke ist geschlossen. Kein Scope-Übertritt: der Fix ändert ausschließlich die Positions-/Kontext-Logik, wie im Plan unter „Notes" gefordert; `TD-002` und die Über-Redaktions-Beobachtung wurden nicht angefasst.

### Build-/Test-Status

```
dotnet build → grün (0 Warnungen, 0 Fehler)
dotnet test  → grün (499 Tests, 0 Fehler)
```

## Tech-Debt-Einträge aus diesem Review

- `TD-003` (siehe `tech-debt.md`) — Der `content`-Array-Sonderfall greift positionsunabhängig auf jede Objekt-Property namens `content` mit Array-Wert, nicht nur auf `result.content[]` im Response-Envelope; vom Plan selbst als akzeptabel sanktioniert, aber ein künftiges, präziseres Matching wäre wünschenswert.
