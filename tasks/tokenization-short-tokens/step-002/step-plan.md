---
status: open
type: step-plan
task: tokenization-short-tokens
step: "002"
title: "Anonymizer & QueryTokenResolver Refactoring"
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-07-28T15:21:00Z
related_to:
  - step-001
---

# Step 002: Anonymizer & QueryTokenResolver Refactoring

## Bezug

- **Task:** `tokenization-short-tokens`
- **Quelle:** [Konzept.md](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tasks/tokenization-short-tokens/Konzept.md) — Scope "Muss-Haben" Punkte 4 & 5

## Intention

Umstellung des `Anonymizer` von HMAC-SHA256 Token-Generierung auf den Aufruf von `_tokenVault.GetOrAddToken`. Verifizierung und feinschliff der Regex-Token-Erkennung in `QueryTokenResolver`.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Anonymization/Anonymizer.cs` (Zeile 54-93)

- **Was:** 
  - Entfernen der Methode `ComputeToken(string value, TokenizationOptions tokenization)` (HMAC-SHA256 Logik).
  - In `Tokenize(string originalValue, AnonymizationColumnContext context)`: Ersetzen von `ComputeToken` und `_tokenVault.Store` durch den direkten Aufruf `_tokenVault.GetOrAddToken(originalValue, tokenization.Prefix, tokenization.Suffix)`.
- **Warum:** Erzeugt kompakte Kurz-Tokens über den Vault statt langer HMAC-SHA256 Hashes.

### Datei 2: `src/SqlToAi/Database/QueryTokenResolver.cs` (Zeile 74-78)

- **Was:** Überprüfen des Regex-Musters `BuildTokenPattern`. Die Regex `Regex.Escape(options.Prefix) + "[A-Za-z0-9_-]+" + Regex.Escape(options.Suffix)` deckt auch Kurz-Tokens ab. Ggf. Anpassen des Regex-Musters oder Kommentars falls erforderlich.
- **Warum:** Sicherstellen, dass das neue Kurz-Token-Format in SQL-Queries fehlerfrei erkannt und aufgelöst wird.

## Tests

- [ ] `dotnet build` kompilieren ohne Warnungen/Fehler
- [ ] `dotnet test` ausführen (manche AnonymizerTests schlagen evtl. noch fehl vor Step 003, wird geprüft)

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command (`dotnet build`) grün (0 Warnings, 0 Errors)
- [ ] Commit auf aktuellem Branch (`refactor(anonymization): use short token generation via TokenVault in Anonymizer`)
- [ ] `step-002/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc` — Safety-by-Design, Clean Code
- `.agents/rules/AiNetLinter.mdc` — `sealed` classes, `#nullable enable`
