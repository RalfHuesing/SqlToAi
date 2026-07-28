---
status: open
type: step-plan
task: tokenization-short-tokens
step: "001"
title: "TokenizationOptions & TokenVault Refactoring"
estimated_risk: medium
step_type: single
items: []
created_by: planer
created_by_model: gemini-3.6-flash
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-07-28T15:21:00Z
related_to: []
---

# Step 001: TokenizationOptions & TokenVault Refactoring

## Bezug

- **Task:** `tokenization-short-tokens`
- **Quelle:** [Konzept.md](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tasks/tokenization-short-tokens/Konzept.md) — Scope "Muss-Haben" Punkte 1, 2 & 3

## Intention

Entfernung des redundanten `Secret`-Konfigurationsparameters aus `TokenizationOptions`, `ConfigurationResolver` und `appsettings.json`. Erweiterung der Schnittstelle `ITokenVault` und deren Implementierung `TokenVault` um eine atomare, bidirektionale Zuordnung (`Value -> Token` und `Token -> Value`) zur Erzeugung kompakter Kurz-Tokens (z. B. `§§§T1§§§`).

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Configuration/SqlToAiOptions.cs` (Zeile 81-110)

- **Was:** Entfernen des `Secret`-Property (Zeile 92) und Anpassen von `IsUsable` auf `Enabled && !string.IsNullOrEmpty(Prefix) && !string.IsNullOrEmpty(Suffix)`.
- **Warum:** HMAC-SHA256 entfällt; Tokenisierung benötigt kein Secret mehr.

### Datei 2: `src/SqlToAi/appsettings.json` (Zeile 23-28)

- **Was:** Entfernen des Eintrags `"Secret": ""` im `"Tokenization"`-Block.
- **Warum:** Das Secret existiert nicht mehr im Konfigurationsmodell.

### Datei 3: `src/SqlToAi/Configuration/ConfigurationResolver.cs` (Zeile 59-63)

- **Was:** Anpassen von `options.Anonymizer.Tokenization`: Entfernen der `Secret`-Expansion.
- **Warum:** `Secret` wurde aus `TokenizationOptions` entfernt.

### Datei 4: `src/SqlToAi/Anonymization/ITokenVault.cs` (Zeile 1-20)

- **Was:** Erweitern der Schnittstelle um `string GetOrAddToken(string value, string prefix, string suffix)`.
- **Warum:** Erlaubt die konsistente Erzeugung und Speicherung eines wiederverwendbaren Kurz-Tokens für einen bestimmten Originalwert innerhalb einer Sitzung.

### Datei 5: `src/SqlToAi/Anonymization/TokenVault.cs` (Zeile 1-24)

- **Was:** Implementieren von `GetOrAddToken` mit einer zweiter `ConcurrentDictionary<string, string> _valueToToken` und einem sequentiellen `_counter` (`Interlocked.Increment`).
- **Warum:** Stellt sicher, dass derselbe Originalwert garantiert immer dasselbe Kurz-Token erhält (`Value -> Token`) und gleichzeitig der Rückweg (`Token -> Value`) in `_tokenToValue` registriert ist.

## Tests

- [ ] `dotnet build` kompilieren ohne Warnungen/Fehler
- [ ] Bestehende `TokenVaultTests` oder neue Vault-Tests verifizieren die Bi-Direktionalität von `GetOrAddToken`

## Definition of Done

- [ ] Alle „Konkrete Änderungen" umgesetzt
- [ ] Build-Command (`dotnet build`) grün (0 Warnings, 0 Errors)
- [ ] Test-Command (`dotnet test`) grün
- [ ] Commit auf aktuellem Branch (`feat(anonymization): remove Secret option and introduce bi-directional TokenVault for short tokens`)
- [ ] `step-001/step-result.md` geschrieben
- [ ] `status` in `step-plan.md` auf `done (pending audit)` gesetzt

## Rules-Refs

- `.agents/rules/SqlToAiRichtlinien.mdc#4` — No magic values, XML comments, English code doc
- `.agents/rules/AiNetLinter.mdc` — `#nullable enable`, `sealed` classes, MaxMethodLineCount ≤ 60
