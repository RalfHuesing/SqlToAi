---
status: done
type: step-result
task: audit-hardening
step: 003/fix-01
epic: EPIC-03
step_type: single
coded_by: coder
coded_by_model: Claude Sonnet 5
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-08-04T18:40:00+02:00
code_commit_hash: d64241d
status_after: done
blocker_category: n/a
---

# Result Step 003/fix-01: Strukturschlüssel-Ausnahme positionsabhängig statt namensabhängig machen

## Zusammenfassung

`McpTrailWriter` trägt der Redaction-Rekursion jetzt einen `RedactionContext`
(`IsEnvelopeRoot`, `IsContentBlock`) mit statt der Namensausnahme über
`StructuralKeys`. `AnonymizeJsonStrings` bekommt einen `isEnvelope`-Parameter
(`ArgumentsJson` → `false`, `RawRequestJson`/`ResponseJson` → `true`). Die
Exemption läuft jetzt über `IsExemptStructuralKey`: `jsonrpc`/`id`/`method`
nur auf der echten Envelope-Wurzel, `type` nur als direktes Element eines
`content`-Arrays — beides context-getrieben statt namensbasiert. Nach jeder
Rekursion in ein Kind wird der Kontext zurückgesetzt (`default`), außer beim
Sonderfall `content`-Array, dessen direkte Objekt-Elemente `IsContentBlock =
true` bekommen und danach wieder zurückgesetzt werden.

## Geänderte Dateien

- `src/SqlToAi/Mcp/McpTrailWriter.cs` — `RedactionContext`-Record-Struct
  eingeführt, `StructuralKeys` durch `EnvelopeKeys` (nur `jsonrpc`/`id`/
  `method`) + separate `ContentBlockTypeKey`-Konstante ersetzt,
  `AnonymizeJsonStrings`/`AnonymizeContainer`/`AnonymizeObjectProperties`/
  `AnonymizeArrayElements` um den Kontext-Parameter erweitert, neue private
  `IsExemptStructuralKey`-Hilfsmethode für die Exemptions-Prüfung.
- `tests/SqlToAi.Tests/Mcp/McpTrailWriterTests.cs` — zwei neue Tests:
  `Record_ShouldRedactArgumentsProperties_NamedLikeStructuralKeys` (Kernfall
  des Findings: `arguments`-Properties namens `id`/`type`/`method` mit
  sensiblem Inhalt werden redigiert, Envelope-Root-Keys bleiben lesbar) und
  `Record_ShouldKeepContentBlockTypeDiscriminator_Readable_ButRedactNestedTypeElsewhere`
  (Content-Block-`type` bleibt lesbar, geschachteltes `type` in einem
  bereits serialisierten Text-Blob wird als Teil des String-Leaf mitredigiert
  — akzeptierte Über-Redaktion, siehe Notes im Plan). Außerdem einen
  `GetDayDir()`-Helper extrahiert und mehrere bestehende Tests von
  benannten Mehrzeilen-Argumenten auf positionale Ein-/Zweizeiler
  umgestellt (reine Kompaktierung, siehe „Abweichungen vom Plan").

## Commit

- **Code-Commit-Hash:** `d64241d`
- **Message:**
  ```
  fix(mcp): Strukturschluessel-Ausnahme positionsabhaengig statt namensbasiert [audit-hardening]

  Refs: tasks/audit-hardening/step-003/fix-01
  ```
- **Branch:** main
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit (Hash steht nicht hier
  drin — Selbstbezug, siehe `git log`).

## Build-/Test-Output

```
dotnet build → grün
dotnet test  → grün (499 Tests, 0 Fehler)
```

## Abweichungen vom Plan

- Die im Code-Skizze gezeigte Signatur `IsExemptStructuralKey(string key,
  RedactionContext context)` wurde 1:1 übernommen, aber `EnvelopeKeys` ist
  ein `HashSet<string>` (statt inline `is "jsonrpc" or "id" or "method"`)
  — funktional identisch, nur der bereits vorhandene Namens-Stil
  (`StructuralKeys` war schon ein `HashSet`) weitergeführt.
- Zusätzlich zum Plan-Scope musste ich `tests/SqlToAi.Tests/Mcp/
  McpTrailWriterTests.cs` unter das `MaxLineCount`-Limit (500, siehe
  `AiNetLinter.mdc`) zurückbringen: meine zwei neuen Tests trieben die
  Datei auf 548 Zeilen. Dafür habe ich (a) einen `GetDayDir()`-Helper für
  die 15-fach wiederholte `Path.Combine(_logRoot, "mcp", ...)`-Berechnung
  extrahiert und (b) mehrere **bestehende** Tests (`pii-1` bis `pii-5`) von
  benannten Mehrzeilen-`McpCallRecord`-Konstruktoren auf positionale
  Ein-/Zweizeiler umgestellt (Stil, der in anderen Tests der Datei bereits
  existierte, z. B. `Record_ShouldDoNothing_WhenMcpTrailDisabled`). Das ist
  eine reine Formatierungsänderung ohne Verhaltensänderung — nötig, damit
  der verpflichtende volle Testlauf (inkl. `AiNetLinterTests`) grün wird,
  aber technisch eine Berührung von Dateizeilen außerhalb der im Plan
  genannten „Konkrete Änderungen" (dort war nur von *neuen* Tests die Rede).
  Datei liegt jetzt bei 487 Zeilen.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` wurde
  automatisch durch den in `AiNetLinterTests.cs` enthaltenen
  `RecreateBaseline`-Test aktualisiert (Hash-Update für die beiden
  geänderten Dateien) — das ist etablierter Repo-Mechanismus (siehe
  Commit-Historie, z. B. `c3952e4`), kein manueller Eingriff.

## Beobachtungen

- Keine neuen Beobachtungen über das im Plan bereits dokumentierte hinaus.
  `TD-002` und die „Über-Redaktion von content[0].text-Blobs" wurden nicht
  angefasst, wie im Plan gefordert.

## Bekannte Unschärfen

- Der `content`-Array-Sonderfall in `AnonymizeObjectProperties` greift
  grobzügig auf **jedes** Objekt mit einer Property namens `content`, deren
  Wert ein `JsonArray` ist — nicht nur auf das eine `result.content[]` im
  Response-Envelope. Das entspricht explizit der im Plan als akzeptabel
  eingestuften Variante ("im Zweifel darf der Check aber grobzügiger sein");
  ein hypothetisches, tief verschachteltes `arguments.content`-Array eines
  LLM-Aufrufs würde also ebenfalls `IsContentBlock`-Behandlung für seine
  direkten Elemente bekommen. Da `IsContentBlock` nur eine Ebene tief wirkt
  und danach garantiert zurückgesetzt wird, bleibt der Schaden auf ein
  potenziell unredigiertes `type`-Property in einem synthetischen
  `arguments.content[]`-Element begrenzt — ein Rand-Fall, den der Kritiker
  ggf. gegen das Finding abgleichen sollte.

## Falls Status `blocked`

Entfällt — Status ist `done (pending audit)`.
