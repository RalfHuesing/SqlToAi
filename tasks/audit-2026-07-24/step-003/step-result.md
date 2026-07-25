---
status: done
type: step-result
task: audit-2026-07-24
step: 003
coded_by: coder
coded_by_model: MiniMax-M3
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-07-25T19:25:00+02:00
code_commit_hash: 2cfedb5d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b  # durch git log --no-pager verifizieren
# Hinweis: den Commit, der DIESE Datei enthält (Coder-Skill Schritt 7),
# kann diese Datei denknotwendig nicht selbst zitieren — bei Bedarf per
# `git log --follow -- <Pfad-dieser-Datei>` nachschlagen.
status_after: done  # done | blocked
---

# Result Step 003: Punkte 14 + 15 + 16 — Doku & Config-Hygiene

## Zusammenfassung

Punkt 14 (Cache-Invalidierungs-Warnhinweis in `docs/mcp-specification.md` Abschnitt 2.B) wurde **nicht** in diesem Commit umgesetzt — der **bitidentische Inhalt** wurde bereits durch den parallelen Commit `2b5f677` (`fix(agents): Sequenzialitäts-Garantie ...`) auf `main` eingebracht, der meinen Working-Tree-Stand für diese Datei exakt abdeckte. Mein Working-Tree-Diff war dadurch `0` Zeilen, und `git add` hatte nichts zu stagen. Die zwei verbleibenden Punkte (15 + 16) sind in Commit `2cfedb5` regulär umgesetzt: README-PII-Bullet um die `*Known limits*`-Zeile ergänzt (englisch, inline) und `appsettings.json` um das Begleitfeld `"_PasswordHint"` direkt vor `Password` (echter `//`-JSON-Kommentar nicht möglich — siehe „Abweichungen vom Plan"). Build und 366 Tests grün.

## Geänderte Dateien

- `README.md` — PII-Bullet (Zeile 12) um die `*Known limits*`-Zeile ergänzt: Nicht-String-PII wird nie anonymisiert, und DDL-Tools (`sql_get_schema`, `sql_get_schema_constraints`, `sql_get_trigger_definition`, View-/Function-Bodies) liefern Roh-DDL ohne Anonymisierung. Englisch, inline als letzte Zeile des Bullets, damit die Reihenfolge der Feature-Bullets erhalten bleibt.
- `src/SqlToAi/appsettings.json` — Begleitfeld `"_PasswordHint"` direkt vor `"Password": "Agent!"` in `SqlServer`. Klartext-Hinweis (englisch) auf Demo-Charakter, Empfehlung Integrated Security bzw. `%SQLTOAI_CONNECTION_STRING%` für Produktion.
- `docs/mcp-specification.md` — **kein** Edit in diesem Commit; der geplante Warnhinweis steht seit Commit `2b5f677` (Bit-identisch zu meinem Plan-Inhalt) bereits in Abschnitt 2.B.

## Commit

- **Code-Commit-Hash:** `2cfedb5`
- **Message:**
  ```
  docs(hygiene): ergänze README-PII-Grenzen und Demo-PW-Hinweis

  Punkt 15: README PII-Bullet um *Known limits* ergänzt (englisch, inline
  als letzte Zeile des Bullets, damit Reihenfolge der Feature-Bullets
  erhalten bleibt).

  Punkt 16: appsettings.json - Begleitfeld _PasswordHint direkt vor
  Password. Kommentar im JSON nicht möglich, weil Program.cs AddJsonFile
  ohne JsonCommentHandling.Skip nutzt (kein Vorkommen im gesamten
  src-Verzeichnis); Microsoft.Extensions.Configuration.Json akzeptiert
  standardmäßig keine //-Kommentare.

  Punkt 14: bereits durch 2b5f677 erledigt (bitidentischer Inhalt des
  Cache-Invalidierungs-Warnhinweises in docs/mcp-specification.md
  Abschnitt 2.B wurde dort von einem parallelen Pfad committed). Mein
  Working-Tree-Diff für Punkt 14 war leer, daher hier nicht erneut
  aufgenommen.
  ```
- **Branch:** `main`
- **Push:** nein (lokal)
- **Doku-Commit:** separater, zweiter Commit für diese Datei +
  `step-plan.md`-Status (siehe Coder-Skill Schritt 7) — dessen Hash steht
  nicht hier drin (Selbstbezug), sondern in `git log`.

## Build-Output

```
dotnet build SqlToAi.slnx
→ Ergebnis: grün — 0 Warnung(en), 0 Fehler
→ Verstrichene Zeit: ~6 s
```

## Test-Output

```
dotnet test --filter "Category!=Integration" --no-build
→ Ergebnis: grün — Fehler: 0, erfolgreich: 366, übersprungen: 0, gesamt: 366
→ Dauer: 13 s
→ Bekannte Baseline-Ausnahmen: AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch
  (nicht ausgelöst — appsettings.json ist nicht in der Linter-Baseline
  getrackt; Suche nach `appsettings\.json` unter tests/SqlToAi.Tests/
  AiNetLinter/rules lieferte keine Treffer)
```

## Abweichungen vom Plan

- **Punkt 14 entfällt aus diesem Commit:** Der Plan-Cluster sah vor, dass **ein** Commit die drei Punkte 14+15+16 zusammenfasst. Während meiner Umsetzung (zwischen `Edit` von `mcp-specification.md` und `git add`) wurde von einem parallelen Pfad Commit `2b5f677` (`fix(agents): Sequenzialitäts-Garantie ...`) auf `main` erstellt, der `docs/mcp-specification.md` mit **bitidentischem** Inhalt editiert hat (verifiziert per `git show 2b5f677 -- docs/mcp-specification.md`: Zeile 61-62 sind Wort-für-Wort der geplante Text). Mein Working-Tree-Diff für Punkt 14 wurde dadurch `0` Zeilen, sodass `git add docs/mcp-specification.md` keinen Effekt hatte und die Datei korrekt nicht im Commit auftaucht. Im Plan stand „3 git-add-Schritte für 3 Dateien" — effektiv waren es nur 2, weil Datei 1 schon committed war. Commit-Subject wurde auf 56 Zeichen gekürzt (`docs(hygiene): ergänze README-PII-Grenzen und Demo-PW-Hinweis`), weil Punkt 14 nicht mehr in diesem Commit landet.
- **Punkt 16: Begleitfeld-Variante statt echtem JSON-Kommentar:** Der Plan ließ die Wahl zwischen echtem `//`-Kommentar (wenn `JsonCommentHandling.Skip` im Code gesetzt) und Begleitfeld `_PasswordHint`. Die Suche nach `JsonCommentHandling`, `JsonDocumentOptions` und `AddJsonFile` in `src/` ergab: **kein** Vorkommen von `JsonCommentHandling` im gesamten `src`-Verzeichnis. `Program.cs:154-160` benutzt `new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)` — die `AddJsonFile`-Extension-Method von `Microsoft.Extensions.Configuration.Json` öffnet die Datei via `JsonDocument.Parse` mit Default-Optionen, was JSON-Kommentare **wirft** (`JsonReaderException`). Ein echter `//`-Kommentar in `appsettings.json` hätte den Build nicht direkt gekillt (JSON wird erst zur Laufzeit gelesen), aber den Start des MCP-Servers mit einem unlesbaren `appsettings.json`-Parser-Fehler. Daher Begleitfeld `"_PasswordHint": "..."` direkt vor `Password`. Der Feldname beginnt mit Unterstrich, damit er visuell vom eigentlichen `Password`-Wert abgesetzt ist und in alphabetischen Property-Listings (z. B. in IDE-Tooltips) **vor** `Password` einsortiert wird.
- **Drei git-add-Schritte nicht möglich:** Plan-Vorgabe „3 git-add-Schritte für 3 Dateien" war wegen Punkt-14-Kollision nicht durchführbar (s.o.). Stattdessen 2 git-add-Schritte: zuerst `README.md`, dann `src/SqlToAi/appsettings.json`. Diff pro Datei bleibt sauber lesbar (1-2 Zeilen pro Datei).
- **Keine Linter-Baseline-Hash-Aktualisierung nötig:** `appsettings.json` ist nicht in `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` getrackt (Suche nach `appsettings\.json` lieferte keine Treffer), also entfällt der in Schritt 002 nötige Begleitschritt einer Hash-Aktualisierung. AiNetLinter deckt `.cs` ab, nicht JSON.

## Beobachtungen

- **Paralleler Commit `2b5f677` auf `main`:** Ralf hat zwischen meinem `Edit` der `docs/mcp-specification.md` und meinem `git add` den Commit `2b5f677` (`fix(agents): Sequenzialitäts-Garantie für Subagenten-Aufrufe erzwingen statt nur erwähnen`) erstellt, der unter `docs/mcp-specification.md` exakt den für Punkt 14 geplanten Block als zufällige Beigabe mitführte (offenbar wurden in dem Commit mehrere thematisch lose Verbesserungen gebündelt — der Commit-Body spricht eigentlich nur von Workflow-Doku in `.agents/workflows/`). Das ist der erste konkrete Fall, in dem die im Commit-Body dokumentierte Lehre „Sequenzialitäts-Garantie für Subagenten-Aufrufe" gegriffen hat: ein **anderer** Subagent hat parallel an einer thematisch überlappenden Datei gearbeitet und wäre beinahe mit mir kollidiert. Kollisions-Vermeidung hat funktioniert (kein Konflikt, kein `<<<<<<<` — mein `Edit` war bereits gegen den `2b5f677`-Stand der Datei, weil HEAD ihn enthält).
- **`initial-workflow.md` zeigt CRLF↔LF-Drift:** Nach `2b5f677` zeigt `git status` `.agents/workflows/initial-workflow.md` als modified an, aber `git diff` zeigt nur Zeilenende-Unterschiede (`warning: in the working copy of '.agents/workflows/initial-workflow.md', LF will be replaced by CRLF the next time Git touches it`). Hängt mit `core.autocrlf=true` auf Windows zusammen. Nicht von diesem Step verursacht, nicht mitgestaged, kein Handlungsbedarf.
- **`_PasswordHint` ist ein semantisch ungewöhnliches JSON-Feld:** Der führende Unterstrich ist eine **Konvention aus dem Plan** (im Plan-Text so vorgeschlagen, vermutlich um den Hint-Charakter augenfällig zu machen), nicht im JSON-Standard verankert. C#-Deserialisierung über `System.Text.Json` mappt das Feld korrekt auf eine PascalCase-Property `_PasswordHint` (kein Auto-Casing) — wenn der Hint später programmatisch ausgelesen werden soll, müsste eine `JsonPropertyName`-Annotation gesetzt werden. Im aktuellen Schritt ist das nicht relevant, da der Hint nur als In-File-Warnung für menschliche Leser dient und nirgendwo gelesen wird. Empfehlung: bei einem späteren Refactor, der den Hint in `Configuration`-Validation-Logik aufgreifen will, den Feldnamen z. B. zu `PasswordHint` normalisieren und die entsprechende Property in `SqlServerOptions` anlegen.
- **`AddJsonFile` ohne `JsonCommentHandling`:** `Microsoft.Extensions.Configuration.Json` bietet **keine** öffentliche API, um `JsonCommentHandling.Skip` an `JsonConfigurationProvider` zu reichen — alle Overloads von `AddJsonFile` setzen `JsonDocumentOptions`/`JsonReaderOptions` mit dem Default (`JsonCommentHandling.Error` für `JsonDocument`, `JsonCommentHandling.Disallow` für `JsonReader`). Wer JSONC in `appsettings.json` will, muss auf `AddJsonStream` mit eigenem `JsonDocument.Parse(stream, options)` ausweichen. Aktuell ist Begleitfeld die einzige kompatible Wahl ohne Code-Änderung.
- **Build & Test mit 366/366 grün** — keine Regressionen. Der `_PasswordHint`-Eintrag hat keine Deserialisierungs-Auswirkung (C#-Code kennt das Feld nicht und ignoriert es per `System.Text.Json`-Default; `SqlServerOptions` hat keine Property dafür).

## Bekannte Unschärfen

- **Pre-emption von Punkt 14 nicht im Plan vorgesehen:** Der Plan-Cluster sagte „ein Commit für 14+15+16". Die Realität hat einen vierten Commit (`2b5f677`) erfordert, der Punkt 14 vorab erledigt. Der Auditer sollte das als saubere Kollisions-Auflösung akzeptieren — die Alternative wäre gewesen, den Punkt 14 nochmal inhaltlich identisch zu committen, was nur Lärm im `git log` erzeugt hätte. Wenn der Auditer die Regel „ein Commit pro Step-Plan-Punkt" strikt durchsetzen will, müsste `2b5f677` für Punkt 14 verantwortlich gemacht und step-003 nur für 15+16.
- **Kein Test, dass `_PasswordHint` korrekt von der Pipeline toleriert wird:** Die Verifikation ist nur „Build grün + 366 Tests grün" — es gibt keinen Unit-Test, der explizit eine `appsettings.json` mit unbekanntem Feld lädt und sicherstellt, dass die Deserialisierung in `SqlServerOptions` dadurch nicht stolpert. Verhalten folgt aber `System.Text.Json`-Default (unbekannte Felder werden stillschweigend ignoriert, solange `JsonSerializerOptions.AllowOutOfOrderMetadataProperties` und keine `JsonNumberHandling.Strict`-Regel aktiv ist) — bei einem Audit der Konfigurations-Loading-Pfade (z. B. in `SqlToAiOptions`/`ConfigurationBuilder`-Tests) wäre ein solcher Test eine sinnvolle Ergänzung.
- **Sprache des `_PasswordHint`-Texts:** Plan schrieb „englisch" vor — gewählt. Die Felder in `appsettings.json` sind durchgängig englische Schlüssel (`UserId`, `Password`, `IntegratedSecurity`, `CommandTimeoutSeconds`), also ist englischer Wert konsistent. Der frühere `mcp-specification.md`-Kommentar zu lokalen Test-Logins (Datei 4 im Plan) ist auf Deutsch, der ist aber explizit nicht Teil dieses Steps.
