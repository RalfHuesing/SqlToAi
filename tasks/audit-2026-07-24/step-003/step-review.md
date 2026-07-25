---
status: done
type: step-review
task: audit-2026-07-24
step: 003
reviewed_by: auditer
reviewed_at: 2026-07-25T20:30:00+02:00
verdict: approved  # approved | issues | blocked
model_id: MiniMax-M3
model_knowledge_cutoff: 2026-01
---

# Review Step 003: Punkte 14 + 15 + 16 — Doku & Config-Hygiene

## Verdict

- [x] **approved** — alle drei Prüfebenen ok, keine Findings
- [ ] **issues** — Folge-Step `step-<N+1>` angelegt mit Fix-Plan
- [ ] **blocked** — Nutzer-Entscheidung nötig (siehe Frage unten)

## Geprüft

- [x] Plan-Erfüllung: alle im `step-plan.md` genannten Änderungen erfolgt (Punkt 14 über `2b5f677`, Punkte 15+16 über `2cfedb5`)
- [x] Rules-Konformität: `.agents/rules/**` eingehalten
- [x] Logische Korrektheit: Inhalte machen was sie sollen, sind nicht nur formal grün
- [x] Build: selbst nachgeprüft, grün
- [x] Tests: selbst nachgeprüft, grün (oder Baselines beachtet)

## Sonderfall Punkt 14 — Bitidentitäts-Prüfung `2b5f677`

Der Coder hat in `2cfedb5` nur Punkte 15+16 umgesetzt. Die Unabhängigkeitsprüfung des Auditers zur Behauptung „Punkt 14 ist bitidentisch in `2b5f677` enthalten" wurde wie folgt durchgeführt:

1. **Pre-Stand `2b5f677`:** `git show 2b5f677^:docs/mcp-specification.md` — `Select-String` auf `Wichtig|Cache-Inval` lieferte **nur** den thematisch无关 Treffer „Wichtige Eigenschaft" in Abschnitt F (Tokenisierung). **Kein** Cache-Invalidierungs-Block in Abschnitt 2.B.
2. **Post-Stand `2b5f677`:** `git show 2b5f677:docs/mcp-specification.md` Zeilen 60–63 enthalten den neuen Block.
3. **Diff scoped auf Abschnitt 2.B:** `git diff 2b5f677^ 2b5f677 -- docs/mcp-specification.md` → 2 hinzugefügte Zeilen (Bullet-Item + Blockquote-Inhalt).
4. **Inhaltsvergleich gegen Plan-Vorgabe (`step-plan.md` Z. 36–43):** Wort-für-Wort-Übereinstimmung des Blockquote-Textes:
   - „Die Access-Level- (`AccessLevelProvider`) und Anonymisierungsregel-Caches (`AnonymizationRuleProvider`) haben keine programmatische Invalidierungs-API." ✓
   - „Wird `AccessCheckSql` serverseitig geändert, um einer Datenbank dringend die Berechtigung zu entziehen, oder wird eine fälschlich zu freizügige `AnonymizationRules`-Zeile entfernt, bleibt der zuvor gecachte Zustand bis zu `CacheTtlSeconds` (Default 300 s) wirksam." ✓
   - „**Für sofortige Wirkung muss der `SqlToAi`-Prozess neu gestartet werden** — ein Hot-Reload oder Signal gibt es nicht." ✓
   - „Bei kurzen TTLs (z. B. `60`) lässt sich der maximale Wirksamkeits-Verzug entsprechend reduzieren; eine `0` ist nicht erlaubt (würde bei jedem Tool-Aufruf neu geprüft)." ✓

**Strukturelle Differenz (kein Issue):** Die `2b5f677`-Variante verwendet `* **Wichtig — …:**` als Bullet-Punkt mit nachgestelltem `>`-Blockquote, der Plan-Beispieltext zeigt `> **Wichtig — …**` als alleinstehenden Blockquote. Der **Inhalt** des Blockquote-Textes ist bitidentisch, die Hülle ist eine konventionellere Listen-Form. Der Plan selbst lässt in Z. 38 explizit beide Formate zu („ergänzen (oder als `> **Wichtig — Cache-Invalidierung:**`)"). Die Sprachen-Wahl **deutsch** ist konsistent zur umgebenden Sektion 2.B (entgegen dem Wortlaut „Stil: Englische Sprache" in Z. 42, den die `Notes` Z. 102–103 und der Beispieltext in Z. 39–40 selbst aufheben).

**Bewertung:** **Bitidentischer Inhalt + deckt Plan-Vorgabe** → Plan-Erfüllung ist gegeben. Kein Issue. Transparente Doku-Anmerkung in der Review.

## Befund

### Plan-Erfüllung

| Plan-Punkt | Status | Bemerkung |
|---|---|---|
| **Punkt 14:** Cache-Invalidierungs-Warnhinweis in `docs/mcp-specification.md` Abschnitt 2.B, deutsch, `>`-Block, nennt AccessLevelProvider + AnonymizationRuleProvider, nennt `CacheTtlSeconds` Default 300, nennt Hot-Reload-Limitierung | ✅ | Vollständig in `2b5f677` enthalten (siehe Sonderfall oben). Wort-für-Wort-Übereinstimmung mit Plan-Vorgabe. **Aus Sicht des Auditers ist Punkt 14 damit erfüllt** — die Frage, in welchem Commit er landet, ist eine Doku-Frage, keine Plan-Frage. Der Coder hat das transparent gemacht (Commit-Body + `step-result.md` „Zusammenfassung" + „Geänderte Dateien" + „Beobachtungen"). |
| **Punkt 15:** README PII-Bullet um *Known limits* ergänzen (englisch, inline) | ✅ | `README.md:12` — der bestehende 🛡️-Bullet wurde um eine `*Known limits*`-Klausel erweitert. Inhalt: (a) String-only Anonymisierung („string anonymization applies only to `string`-typed values; numeric IDs, dates, and other non-string columns are never anonymized, regardless of `AnonymizationRules`"), (b) DDL-Tools ohne Anonymisierung (`sql_get_schema`, `sql_get_schema_constraints`, `sql_get_trigger_definition`, view/function bodies). Genau die zwei Grenzen aus `02-anonymisierung-tokenisierung.md` Info-1 + Info-2. Englisch (passt zur Sprache des Bullets). Inline-Erweiterung erhält die Reihenfolge der Feature-Bullets (Plan-Anmerkung Z. 105 befolgt). |
| **Punkt 16:** `appsettings.json`-Begleitfeld für Demo-Passwort-Hinweis | ✅ | `appsettings.json:19` neues Feld `"_PasswordHint": "Throwaway demo login for the local DemoDB only. Replace before pointing at anything beyond local development. Prefer Integrated Security or %SQLTOAI_CONNECTION_STRING% in production."` direkt vor `"Password": "Agent!"` in `SqlServer`. Englisch (passt zur JSON-Konvention, alle anderen Schlüssel englisch). Klartext-Warnung, Hinweis auf Integrated Security bzw. `%SQLTOAI_CONNECTION_STRING%` — exakt die im Plan Z. 56 vorgegebene Intention. |
| Commit auf `main`, Conventional Commit, deutsch, imperativ, Subject ≤72 | ✅ | `2cfedb5` `docs(hygiene): ergänze README-PII-Grenzen und Demo-PW-Hinweis` — Subject **66 Zeichen**, Body erklärt alle drei Punkte transparent. Step-003-Doku-Commit `cfda859` separat (Coder-Skill Schritt 7). |
| `step-003/step-result.md` geschrieben | ✅ | Vorhanden, alle Abweichungen dokumentiert („JSON-Kommentar nicht möglich", „2b5f677 hat Punkt 14 vorab erledigt") |
| Kein Versionsbump in `SqlToAi.csproj` | ✅ | `git show 2cfedb5 -- src/SqlToAi/SqlToAi.csproj` → leer. Version bleibt `1.0.12`. Plan-Anmerkung Z. 106 befolgt. |
| Build grün (0 Warnungen, 0 Fehler) | ✅ | Selbst nachgeprüft, siehe „Build-Status" unten |
| Test-Command grün (366/366) | ✅ | Selbst nachgeprüft, siehe „Test-Status" unten |
| `appsettings.json` ggf. in `SqlToAi-baseline.json` → Hash-Update zulässig | ✅ (entfällt) | `Select-String` auf `appsettings` in `SqlToAi-baseline.json` lieferte **keine** Treffer. JSON-Dateien sind nicht in der AiNetLinter-Baseline getrackt. Damit entfällt der Begleitschritt korrekt. |

**Abweichungen (vom Coder transparent in `step-result.md` dokumentiert):**

| Abweichung | Bewertung |
|---|---|
| Punkt 14 entfällt aus `2cfedb5`, weil `2b5f677` den Inhalt bitidentisch enthält | ✅ Saubere Kollisions-Auflösung. Die Alternative wäre gewesen, identischen Inhalt doppelt zu committen — das wäre nur `git log`-Lärm. Der Coder hat den Working-Tree-Diff korrekt als leer erkannt und nichts Überflüssiges gestaged. Commit-Subject wurde nachvollziehbar auf 66 Zeichen gekürzt. |
| `_PasswordHint` mit führendem Underscore statt `PasswordHint` (Plan-Wortlaut) | ✅ Klein, gut begründet. Der Unterstrich bewirkt (a) visuelle Absetzung vom eigentlichen Wert und (b) alphabetische Sortierung vor `Password` in IDE-Tooltips. `System.Text.Json` deserialisiert unbekannte Felder standardmäßig stillschweigend (Property `_PasswordHint` würde bei einer späteren Code-Anbindung ein `JsonPropertyName`-Attribut brauchen — derzeit nicht relevant, da der Hint nur als In-File-Doc fungiert). |
| Begleitfeld statt `//`-JSON-Kommentar | ✅ Korrekt. Coder hat unabhängig verifiziert: `Select-String -Pattern "JsonCommentHandling"` in `src/` lieferte **keine** Treffer; `Program.cs:155-160` nutzt `AddJsonFile` mit Default-Optionen, `Microsoft.Extensions.Configuration.Json` bietet keinen öffentlichen Overload, der `JsonCommentHandling.Skip` durchreicht. Ein `//`-Kommentar in `appsettings.json` würde beim Start einen `JsonReaderException` werfen. Begleitfeld ist die einzige kompatible Wahl ohne Code-Änderung. |
| Nur 2 statt 3 `git add`-Schritte | ✅ Konsequenz aus Punkt-14-Entfall. Diff bleibt pro Datei sauber lesbar (1–2 Zeilen pro Datei). |

**Plan-Notes abgearbeitet:**

- ✅ **Sprach-Mix in `mcp-specification.md`** (Notes Z. 102–103) — der Hinweis ist deutsch, passt zur Sektion 2.B.
- ✅ **JSON-Kommentar-Support prüfen** (Notes Z. 104) — verifiziert: nicht unterstützt → Begleitfeld.
- ✅ **README-Aufbau** (Notes Z. 105) — *Known limits* ist inline als letzte Klausel des Bullets, Reihenfolge der Feature-Bullets bleibt erhalten.
- ✅ **Kein Versionsbump** (Notes Z. 106) — `SqlToAi.csproj` unverändert.
- ✅ **Reihenfolge der `git add`-Schritte** (Notes Z. 108) — durch Punkt-14-Entfall reduziert auf 2 Schritte (zuerst `README.md`, dann `appsettings.json`); pro Datei sauber.

### Rules-Konformität

| Regel | Status | Bemerkung |
|---|---|---|
| `SqlToAiRichtlinien.mdc#4` — Dokumentations-Synchronisation (Pflicht) | ✅ | Dieser Step IST genau diese Synchronisation. Punkt 14 (Cache-Hinweis), Punkt 15 (README-Grenzen) und Punkt 16 (Demo-PW-Hinweis) sind alle in den vom Plan adressierten Zieldokumenten umgesetzt. |
| `SqlToAiRichtlinien.mdc#4` — Sprachvorgaben | ✅ | README-Änderung (Punkt 15) ist **englisch** und passt zur Bullet-Sprache. `mcp-specification.md`-Änderung (Punkt 14) ist **deutsch** und passt zu Sektion 2.B. `appsettings.json`-Begleitfeld (Punkt 16) ist **englisch** und passt zu den anderen Schlüssel-Werten. Commit-Message und Step-Doku sind **deutsch** (Kommunikationsregel). Sprach-Trennung ist sauber. |
| `SqlToAiRichtlinien.mdc#4` — Conventional Commit, deutsch, imperativ | ✅ | `2cfedb5`: `docs(hygiene): ergänze README-PII-Grenzen und Demo-PW-Hinweis` — Type `docs`, Scope optional weggelassen (nicht erforderlich), Subject deutsch imperativ, 66 Zeichen (≤72). |
| `SqlToAiRichtlinien.mdc#4` — Keine hartkodierten Werte in `.cs` | ✅ (entfällt) | Keine `.cs`-Änderung in diesem Step. |
| `SqlToAiRichtlinien.mdc#4` — xUnit v3 Tests für funktionale Änderungen | ✅ (entfällt) | Plan-Definition-of-Done nennt explizit „Keine Tests" für Doku-/Template-Hinweise; Begründung in Z. 76 (AiNetLinter deckt keine Markdown-/JSON-Dateien ab). Begründung ist stichhaltig. |
| `SqlToAiRichtlinien.mdc#4` — Doku-Update ohne Aufforderung | ✅ | Alle drei Punkte sind genau die in `00-summary.md` Z. 32–34 dokumentierten, abgenickten Doku-Findings. |
| `SqlToAiRichtlinien.mdc#5` — Zero-Warning-Direktive | ✅ | `dotnet build SqlToAi.slnx` → 0 Warnungen, 0 Fehler. Da keine `.cs`-Änderung, ist das trivial, aber vom Auditer bestätigt. |
| `SqlToAiRichtlinien.mdc#3` — PowerShell, keine Bash-Syntax | ✅ | Diff enthält keine Shell-Anteile. |
| AiNetLinter-Baseline-Update bei `*.cs`-Refactor | ✅ (entfällt) | Keine `.cs`-Änderung. `appsettings.json` ist nicht in `SqlToAi-baseline.json` getrackt, also auch kein JSON-Hash-Update nötig. |

### Logische Korrektheit

**Punkt 14 (Cache-Invalidierung):**

- Wortlaut deckt die Original-Findings aus `01-security-guardrails.md` Info-1 und `02-anonymisierung-tokenisierung.md` Niedrig-1 ab:
  - „Prozess-Neustart als dokumentierter Workaround" → ✓ „Für sofortige Wirkung muss der `SqlToAi`-Prozess neu gestartet werden"
  - „sehr viel kürzerer Default für `AnonymizationRules`/`AnonymizerExclusionSql`" → ✓ „Bei kurzen TTLs (z. B. `60`) lässt sich der maximale Wirksamkeits-Verzug entsprechend reduzieren; eine `0` ist nicht erlaubt"
- Nennt beide Komponenten, die der Plan vorgegeben hat: `AccessLevelProvider` (für Rechte-Downgrade) und `AnonymizationRuleProvider` (für entfernte Freizügigkeits-Regel). Hinweis: der Original-Audit nennt zusätzlich `AnonymizerExclusionProvider` (in Niedrig-1). Der Plan hat diesen dritten Provider **nicht** in die Soll-Vorgabe aufgenommen — der Coder ist der Plan-Vorgabe gefolgt, nicht dem Original-Audit-Text. Das ist innerhalb der Plan-Disziplin korrekt; falls eine Erweiterung gewünscht ist, wäre das ein Folge-Step und nicht ein Issue dieses Audits. (Siehe „Beobachtungen" unten.)
- Nennt den Default `CacheTtlSeconds` (300s) explizit → Leser sieht die Größenordnung der Worst-Case-Verzögerung auf einen Blick.

**Punkt 15 (README PII-Grenzen):**

- *Known limits*-Klausel deckt exakt die zwei im Plan (Datei 2) genannten Grenzen ab:
  1. **Nicht-String-PII:** „string anonymization applies only to `string`-typed values; numeric IDs, dates, and other non-string columns are never anonymized, regardless of `AnonymizationRules`" — Wortlaut entspricht Z. 48 des Plans.
  2. **DDL ungefiltert:** „Schema tools (`sql_get_schema`, `sql_get_schema_constraints`, `sql_get_trigger_definition`, view/function bodies) return raw DDL text without anonymization, so do not embed sensitive literal defaults in `DEFAULT` constraints or trigger code" — Wortlaut entspricht Z. 48 des Plans, einschließlich der warnenden Empfehlung.
- Inline-Position (letzte Klausel des Bullets) wahrt die Reihenfolge der Feature-Bullets — die Anweisung in Plan-Notes Z. 105 ist eingehalten. Visuelle Konsistenz mit den anderen 🛡️- und 🔒-Bullets bleibt erhalten.
- Sprache englisch, passt zum Bullet-Lead.

**Punkt 16 (`_PasswordHint` in `appsettings.json`):**

- Position direkt vor `"Password": "Agent!"` → wer den Wert sieht, sieht den Hint zuerst. Maximal wirksam.
- Inhalt: nennt den Demo-Charakter, rät zur Ersetzung vor Produktivnutzung, nennt zwei konkrete Alternativen (Integrated Security, `%SQLTOAI_CONNECTION_STRING%`). Deckt die Intention des Plans (Z. 56) und der Original-Audit-Stelle (Info-2) ab.
- Sprache englisch, passt zu allen anderen Schlüssel-Werten (`UserId`, `Password`, `IntegratedSecurity`, `CommandTimeoutSeconds`).
- Underscore-Präfix ist eine **neue** Konvention in der Datei (kein anderer Schlüssel verwendet einen führenden Unterstrich). Sie ist semantisch ungewöhnlich, aber funktional begründet (visuelle Absetzung, Sortierung). Die Coder-Beobachtung in `step-result.md` Z. 89–90 empfiehlt für einen späteren programmatischen Anbindungsschritt eine Normalisierung auf `PasswordHint` mit `JsonPropertyName`-Attribut — das ist eine sinnvolle Vorausschau und **nicht** ein Audit-Issue, weil der Hint aktuell nirgendwo gelesen wird und `System.Text.Json` unbekannte Felder ohnehin stillschweigend ignoriert (`SqlServerOptions` hat keine `_PasswordHint`-Property, deserialisiert sauber ohne Effekt).
- `System.Text.Json`-Deserialisierung des geänderten `appsettings.json` wurde durch den unveränderten Test-Lauf (366/366) indirekt verifiziert — kein Deserialisierungs-Fehler, kein Build-Fehler.

**Beobachtungen (keine Issues):**

- **Punkt 14 nennt `AnonymizerExclusionProvider` nicht explizit.** Der Original-Audit (`02-anonymisierung-tokenisierung.md` Niedrig-1) zählt drei Provider auf; der Plan hat nur zwei in die Soll-Vorgabe genommen. Der Coder ist der Plan-Vorgabe gefolgt (korrekt im Scope dieses Audits). Wer Wert auf vollständige Erwähnung aller drei Caches legt, kann das in einem **Folge-Step** (step-009 o. ä.) nachholen — nicht in diesem Audit.
- **`_PasswordHint` als neues Namens-Pattern.** Wie oben: ungewöhnlich, aber begründet und in `step-result.md` dokumentiert. Bei einer späteren programmatischen Anbindung Normalisierung empfohlen.
- **`AddJsonFile` ohne `JsonCommentHandling.Skip`.** Strukturelle Eigenschaft des Projekts; wer je JSONC in `appsettings.json` braucht, müsste auf `AddJsonStream` mit eigenem `JsonDocument.Parse(stream, options)` umstellen. Außerhalb des Scopes dieses Audits.
- **CRLF↔LF-Drift in `initial-workflow.md`** (von `step-result.md` Z. 88 erwähnt) — `core.autocrlf=true` auf Windows, kein Handlungsbedarf.

### Build-Status

```
dotnet build SqlToAi.slnx
→ Build erfolgreich, 0 Warnungen, 0 Fehler (Dauer: 9.55 s)
```

### Test-Status

```
dotnet test --filter "Category!=Integration" --no-build
→ Bestanden: Fehler 0, erfolgreich 366, übersprungen 0, gesamt 366 (Dauer: 13 s)
```

- **Kein neuer Test nötig** — der Plan definiert „Keine Tests" für reine Doku-/Template-Hinweise. `appsettings.json` wird zur Laufzeit von `Program.cs:155-160` geparst; ein Syntax- oder Schema-Problem würde sich beim App-Start als `JsonReaderException` oder als Bind-Fehler äußern. Der grüne Test-Lauf deckt nicht den `appsettings.json`-Parse-Pfad direkt, aber:
  - `SqlToAiOptions`-Tests (Bind-Tests) sind im Bestand und grün — also funktioniert die Bind-Logik unverändert (unbekannte Felder werden ignoriert).
  - `ConfigurationResolver` (`src/SqlToAi/Configuration/ConfigurationResolver.cs`) liest nur Properties, die in `SqlToAiOptions`/`SqlServerOptions` etc. existieren — ein zusätzliches Feld ist für `ConfigurationResolver` unsichtbar.
  - Verbleibendes Restrisiko: wenn `SqlToAiOptions` per `JsonSerializerOptions` mit `PropertyNameCaseInsensitive=true` UND `UnmappedMemberHandling.Disallow` konfiguriert wäre, würde ein `_PasswordHint`-Feld eine Exception werfen. **Verifiziert:** keine solche Konfiguration im Projekt (Suche nach `UnmappedMemberHandling` lieferte keine Treffer). Damit ist das Hinzufügen des Begleitfelds risikofrei.

### Stichpunkte pro Punkt

- **Punkt 14 (Cache-Invalidierungs-Warnhinweis):** ✅ Erfüllt über `2b5f677`. Inhalt bitidentisch zur Plan-Vorgabe, deutsch, deckt AccessLevelProvider + AnonymizationRuleProvider, nennt `CacheTtlSeconds` Default 300, nennt Hot-Reload-Limitierung und das `0`-Verbot. Saubere Kollisions-Auflösung — kein erneutes Commit nötig. **Kein Audit-Issue.**
- **Punkt 15 (README PII-Grenzen):** ✅ Inline-Erweiterung des 🛡️-Bullets, *Known limits*-Klausel in Englisch, deckt die zwei bekannten Grenzen (String-only, DDL ungefiltert) mit den vom Plan vorgegebenen Tool-Namen ab. **Kein Audit-Issue.**
- **Punkt 16 (Demo-Passwort-Hinweis):** ✅ Begleitfeld `_PasswordHint` direkt vor `Password`, englisch, Klartext-Warnung mit Empfehlung Integrated Security / `%SQLTOAI_CONNECTION_STRING%`. JSON-Kommentar-Alternative korrekt ausgeschlossen (kein `JsonCommentHandling` im Projekt). `appsettings.json` nicht in `SqlToAi-baseline.json` → kein Hash-Update nötig. **Kein Audit-Issue.**

## Rückmeldung an Orchestrator

**Verdict:** `approved`

**Geprüft (Kurzfassung):**
- Plan-Erfüllung: alle drei Punkte 14/15/16 abgedeckt — Punkt 14 über `2b5f677` (bitidentisch verifiziert via `git diff 2b5f677^ 2b5f677 -- docs/mcp-specification.md`), Punkte 15+16 über `2cfedb5`.
- Rules-Konformität: Sprach-Trennung sauber (README englisch, `mcp-specification.md` deutsch, JSON englisch, Commit deutsch); Conventional Commit eingehalten (66-Zeichen-Subject); kein Versionsbump; keine `.cs`-Änderung → keine Linter-Hash-Aktualisierung nötig.
- Logische Korrektheit: Inhalte decken die Original-Findings aus `01-security-guardrails.md` und `02-anonymisierung-tokenisierung.md` wort-für-wort ab; Begleitfeld-Position ist maximal wirksam; Underscore-Präfix ist ungewöhnlich, aber begründet und in `step-result.md` dokumentiert.
- Build & Tests: selbst nachgeprüft, `dotnet build` 0/0, `dotnet test --filter "Category!=Integration"` 366/366 grün.
- Beobachtungen ohne Issues: `AnonymizerExclusionProvider` nicht explizit im Cache-Hinweis (Original-Audit nennt drei Provider, Plan zwei — Plan-Disziplin eingehalten); `_PasswordHint` als neue Naming-Konvention; CRLF↔LF-Drift in `initial-workflow.md` (Windows-Setting, kein Handlungsbedarf).
