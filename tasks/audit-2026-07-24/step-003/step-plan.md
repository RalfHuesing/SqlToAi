---
status: done (pending audit)
type: step-plan
task: audit-2026-07-24
step: 003
title: "Punkte 14 + 15 + 16 — Doku & Config-Hygiene (Cache-TTL-Invalidierung, README-Grenzen, Demo-Passwort-Kommentar)"
created_by: planer
created_at: 2026-07-25T18:30:00+02:00
related_to:
  - tasks/audit-2026-07-24/01-security-guardrails.md (Info-1, Info-2)
  - tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md (Niedrig-1, Info-1, Info-2)
  - tasks/audit-2026-07-24/00-summary.md (Punkte 14, 15, 16)
---

# Step 003: Punkte 14 + 15 + 16 — Doku & Config-Hygiene

## Bezug

- **Task:** `audit-2026-07-24`
- **Quelle:** Drei thematisch zusammengehörige kleine Doku-/Config-Findings, alle Severity Info oder Niedrig, alle ohne Code-Änderung am Verhalten:
  - **Punkt 14** (Cache-TTL-Invalidierung): `01-security-guardrails.md` Info-1 + `02-anonymisierung-tokenisierung.md` Niedrig-1 — Hinweis „Server neu starten für sofortige Wirkung bei Incident Response"
  - **Punkt 15** (README-Grenzen): `02-anonymisierung-tokenisierung.md` Info-1 + Info-2 — zwei bekannte Grenzen ins README übernehmen (DDL ungefiltert; Nicht-String-PII nie anonymisiert)
  - **Punkt 16** (Demo-Passwort Kommentar): `01-security-guardrails.md` Info-2 — Kommentar im `appsettings.json`-Template, dass das Demo-Passwort vor Produktivnutzung zu ändern ist
- **Phase / Priorität:** Phase 3 — Doku & Konfigurationshygiene, Punkte 14, 15, 16

## Clustering-Begründung

Diese drei Punkte sind alle vom Charakter „Doku-/Template-Hinweis ergänzen", alle drei sind risikofrei (kein Code-Verhalten ändert sich), alle drei berühren eng benachbarte Themen (Server-Betrieb, Daten-Schutz-Versprechen, Erstkonfiguration), und alle drei lassen sich in einem Commit zusammenfassen, der die Konfigurations- und Doku-Hygiene des Projekts in einem Rutsch konsistent hält. Sie werden daher gemäß der Planer-Heuristik „thematisch zusammengehörige kleine Doku-Findings clustern" zu **einem** Step zusammengefasst. Falls der Auditer bei der Review eine Trennung wünscht, kann er das in einem Folge-Step tun — eine spätere Aufteilung ist trivial.

## Intention

Der Audit-Bericht hat drei kleine, aber für den Produktivbetrieb relevante Hinweise identifiziert, die aktuell nur in `mcp-specification.md` (für Power-User) stehen oder gar nicht dokumentiert sind. Sie betreffen den **Erstkonfigurator** (Demo-Passwort nicht versehentlich produktiv nutzen), den **LLM-Operator** (README ist die erste Anlaufstelle — Grenzen müssen dort sichtbar sein, nicht nur im Spec), und den **Incident-Responder** (Cache-Invalidierung im Notfall). Ziel: alle drei Hinweise an genau der Stelle ergänzen, wo ein Leser sie natürlicherweise zuerst sucht.

## Konkrete Änderungen

### Datei 1: `docs/mcp-specification.md` (Punkt 14)

- **Was:** Im Abschnitt 2.B „Session- & TTL-Caching" (etwa Zeile 60) nach dem bestehenden Hinweis-Block folgenden Warnhinweis ergänzen (oder als `> **Wichtig — Cache-Invalidierung:**`):
  > **Wichtig — Cache-Invalidierung im Incident-Fall:** Die Access-Level- (`AccessLevelProvider`) und Anonymisierungsregel-Caches (`AnonymizationRuleProvider`) haben keine programmatische Invalidierungs-API. Wird `AccessCheckSql` serverseitig geändert, um einer Datenbank dringend die Berechtigung zu entziehen, oder wird eine fälschlich zu freizügige `AnonymizationRules`-Zeile entfernt, bleibt der zuvor gecachte Zustand bis zu `CacheTtlSeconds` (Default 300 s) wirksam. **Für sofortige Wirkung muss der `SqlToAi`-Prozess neu gestartet werden** — ein Hot-Reload oder Signal gibt es nicht. Bei kurzen TTLs (z. B. `60`) lässt sich der maximale Wirksamkeits-Verzug entsprechend reduzieren; eine `0` ist nicht erlaubt (würde bei jedem Tool-Aufruf neu geprüft).

- **Warum:** Audit-Finding 1 (Severity Info) in `01-security-guardrails.md` markiert dies als dokumentationswürdigen Workaround für den Incident-Response-Pfad. Aktuell ist `CacheTtlSeconds` in mcp-specification.md nur als Performance-Hinweis erwähnt, die Konsequenz für Incident Response fehlt.

- **Stil:** Englische Sprache, an die bestehende Markdown-Struktur angepasst (Bullet-List mit `>`-Blockquote für Warnungen), passend zum umgebenden Englisch/Deutsch-Mix der Datei (Datei ist auf Deutsch, dieser Hinweis sollte ebenfalls auf Deutsch sein — siehe `mcp-specification.md` Zeile 60: „kann optional eine maximale Gültigkeitsdauer (in Sekunden) konfiguriert werden"). **Achtung:** Datei ist ansonsten deutsch, daher den Warnhinweis auf Deutsch formulieren.

### Datei 2: `README.md` (Punkt 15)

- **Was:** Im Block „PII Shield (On-the-Fly Anonymization)"-Bullet-Point (etwa Zeile 12) den Hinweis so erweitern, dass die zwei bekannten Grenzen (DDL-Inhalte, Nicht-String-PII) auch für README-Leser sichtbar sind. Konkret den bestehenden Bullet-Point um eine dritte Zeile oder einen `>`-Blockquote ergänzen, z. B.:
  > 🛡️ **PII Shield (On-the-Fly Anonymization):** … *Known limits* — string anonymization applies only to `string`-typed values; numeric IDs, dates, and other non-string columns are never anonymized, regardless of `AnonymizationRules`. Schema tools (`sql_get_schema`, `sql_get_schema_constraints`, `sql_get_trigger_definition`, view/function bodies) return raw DDL text without anonymization, so do not embed sensitive literal defaults in `DEFAULT` constraints or trigger code.
  
- **Warum:** Audit-Finding 1 (Info) und Finding 2 (Info) in `02-anonymisierung-tokenisierung.md`: diese Grenzen sind aktuell nur in `mcp-specification.md` (Abschnitt D „Bekannte Grenze" und in den Tool-Beschreibungen) dokumentiert, das README enthält sie nicht. Da das README die primäre Einstiegs-Doku ist und der PII-Bullet-Point Sicherheit verspricht, gehören die Grenzen direkt dorthin.

- **Sprache:** README ist auf Englisch — neue Zeilen auf Englisch formulieren, passend zum umgebenden Stil.

### Datei 3: `src/SqlToAi/appsettings.json` (Punkt 16)

- **Was:** Direkt vor oder nach dem `"Password": "Agent!"`-Eintrag (Zeile 19) einen JSON-Kommentar ergänzen, der darauf hinweist, dass dieses Passwort **ausschließlich** für die lokale `DemoDB` gilt und vor jeder Produktivnutzung geändert werden muss. **Achtung:** Standard-JSON unterstützt keine Kommentare, aber `System.Text.Json` mit `JsonCommentHandling.Skip` und `ReadCommentHandling=Skip` schon (siehe `ConfigurationResolver.cs`). Konkret: `"_Password_Hint": "…"` als Klartext-Feld hinzufügen ODER eine eigene Datei `appsettings.Development.json.example` anlegen, falls die Hauptdatei strikt JSON-konform bleiben soll.

- **Pragmatische Empfehlung:** Statt JSON-Kommentar ein Begleitfeld direkt neben `Password` einfügen, da `appsettings.json` von `ConfigurationResolver` über `System.Text.Json` mit `JsonCommentHandling.Skip` verarbeitet wird (siehe `ConfigurationResolver.cs` — bestätigen, dann JSON-Kommentar `// …` zulässig). Konkret:
  ```json
  "UserId": "Agent",
  "Password": "Agent!", // Throwaway demo login for the local DemoDB only. Replace before pointing at anything beyond local development. Prefer Integrated Security or %SQLTOAI_CONNECTION_STRING% in production.
  "IntegratedSecurity": false,
  ```
  Falls `ConfigurationResolver` keine JSON-Kommentare unterstützt, stattdessen den Hinweis als `PasswordHint` Schlüssel ergänzen und in Doku/Code dokumentieren.

- **Warum:** Audit-Finding 2 (Info) in `01-security-guardrails.md` stuft es als Hygiene-Punkt ein, weil ein unreflektierter Nutzer das Demo-Passwort übernehmen könnte. Der bestehende Hinweis im README (Zeile 70-75) ist gut, aber er steht nicht **am** Konfig-Wert selbst.

- **Sprache:** Die Kommentarsprache ist Englisch (passend zur JSON-Konvention in der Datei, die englische Schlüsselnamen verwendet).

### Datei 4: `docs/mcp-specification.md` (Querverweis für Punkt 16)

- **Was:** Keine Änderung — der bestehende Hinweis in `docs/mcp-specification.md` zu lokalen Test-Logins ist ausreichend. Der Fokus liegt auf dem im-File-Kommentar.

## Tests

Keine — alle Änderungen sind reine Doku-/Template-Hinweise ohne Verhaltensänderung. Begründung: Doku-Tests sind im Projekt nicht etabliert; das AiNetLinter-Baseline-System deckt Code-Dateien ab, nicht Markdown. Die Korrektheit der Doku wird im Code-Review durch den Auditer (und durch Ralf selbst) geprüft.

- [ ] `dotnet build SqlToAi.slnx` 0 Warnungen, 0 Fehler (verifiziert, dass Markdown-Änderungen keinen Build-Fehler verursachen)
- [ ] `dotnet test --filter "Category!=Integration"` grün (sollte trivial sein, da keine `.cs` geändert)
- [ ] Optional: `ConfigurationResolver` lädt die geänderte `appsettings.json` weiterhin korrekt (manueller Smoke-Test oder ein neuer Fact in `SqlToAiOptionsTests`)

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command grün (0 Warnings, 0 Errors)
- [ ] Test-Command grün (Ausnahmen siehe „Bekannte Ausnahmen")
- [ ] Commit auf aktuellem Branch (`docs(hygiene): ergänze Hinweise zu Cache-Invalidierung, README-Grenzen und Demo-Passwort`)
- [ ] `step-003/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` von `in_progress` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „Dokumentations-Synchronisation (Pflicht): Bei jeder Entwicklung und Änderung an Features/Optionen müssen die Dokumentationen in `mcp-specification.md` und `README.md` zwingend aktuell gehalten und synchronisiert werden (ohne Aufforderung)" — dieser Step IST genau diese Synchronisation
- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „Dokumentation & Code: Alle Dokumentationen … müssen in englischer Sprache verfasst sein" — README/Punkt 15 englisch, `mcp-specification.md`/Punkt 14 deutsch (bestehende Sprachen-Konvention der Datei wahren), `appsettings.json`-Kommentar/Punkt 16 englisch
- `.agents/rules/SqlToAiRichtlinien.mdc#4` — „Kommunikation: Die Kommunikationssprache zwischen dem KI-Agenten und dem Benutzer ist Deutsch" — die Commit-Message und die Step-Doku sind deutsch, der dokumentierte Inhalt folgt der Zieldatei-Sprache

## Bekannte Ausnahmen

- `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` — vorbestehend, **nicht** Teil dieses Tasks. Falls `appsettings.json` als Datei in der Baseline getrackt wird (prüfen), kann sich der Hash ändern; das ist ein zulässiger Begleitschritt.

## Notes

- **Sprach-Mix in mcp-specification.md:** Die Datei ist überwiegend deutsch (z. B. Abschnitt B Zeile 47-58, Abschnitt D Zeile 75-90), nur einige Tool-Spezifikationen (Abschnitt 4, ab Zeile 188) sind englisch. Der neue Warnhinweis in Abschnitt 2.B sollte **deutsch** sein, um die Konsistenz des Abschnitts zu wahren.
- **JSON-Kommentar-Support prüfen:** Vor dem Edit von `appsettings.json`: in `ConfigurationResolver` nachschauen, ob `JsonDocumentOptions.CommentHandling = JsonCommentHandling.Skip` gesetzt ist. Falls nein, statt JSON-Kommentar ein Begleitfeld wählen.
- **README-Aufbau:** Der PII-Bullet-Point ist Teil einer Feature-Liste mit Emoji-Präfix. Den Hinweis als letzte Zeile des Bullets (mit `*Known limits* …`) ergänzen, nicht als neuer Bullet — sonst verschiebt sich die Bullet-Reihenfolge und der visuelle Eindruck ändert sich.
- **Kein Versionsbump:** Die drei Hinweise sind rein redaktionell, keine API/Verhalten-Änderung, kein Versionsbump in `SqlToAi.csproj` nötig.
- **Reihenfolge innerhalb des Commits:** Die drei Änderungen sollten in getrennten `git add`-Schritten passieren, damit der Diff pro Datei sauber lesbar bleibt — der Commit selbst fasst sie aber in einer Conventional-Commit-Message zusammen.
