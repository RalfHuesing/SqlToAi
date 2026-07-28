---
status: ready
type: konzept
project_kind: brownfield
estimated_scope: medium
rules_dir: .agents/rules
last_updated: 2026-07-28
open_questions: []
---

# Konzept: Refactoring der Tokenisierung (Kurze LLM-Spargenerierung & Entfernung von HMAC-Secret)

## Ziel (Was)

Umstellung der anonymisierten Token-Generierung von langen, teuren HMAC-SHA256-Hashes (`§§§W1-qDynvw...§§§` mit ~50 Zeichen / ~15 LLM-Tokens) auf hochkompakte, lesbare Kurz-Tokens (z. B. `§§§T1§§§` oder Kurz-Hashes `§§§T_A8K2§§§` mit ~3 LLM-Tokens). 

Gleichzeitig wird der redundante Konfigurationsparameter `Anonymizer.Tokenization.Secret` aus dem gesamten Projekt ersatzlos entfernt, da der Rückweg von Tokens zu Echtdaten bereits konstruktionsbedingt über den In-Memory `TokenVault` erfolgt.

## Warum / Kontext

1. **Massiver LLM-Token-Ersparnis (~80% weniger Token-Kosten):** Zufällige Base64-Strings aus HMAC-SHA256 werden von LLM-Tokenizern (z. B. Tiktoken/BPE) in 12–15 Subword-Tokens zerlegt. Bei Abfrageergebnissen mit hunderten Feldern verbrauchten anonymisierte Hash-Strings tausende Tokens im Kontextfenster. Kurz-Tokens reduzieren diesen Overhead auf 2–3 LLM-Tokens pro Feld.
2. **Eliminierung redundanter Architektur & Secret-Verwaltung:** HMAC-SHA256 wurde ursprünglich implementiert, um Tokens "deterministisch" zu generieren. Da ein Hash jedoch eine Einwegfunktion ist, kann der MCP-Server aus dem Hash ohne In-Memory-Lookup (`TokenVault`) sowieso keinen echten SQL-Wert wiederherstellen. Die Secret-Key-Konfiguration in `appsettings.json` ist daher architekturell redundant.
3. **Bessere Lesbarkeit für LLMs:** Das Sprachmodell kann kurze Platzhalter wie `§§§T1§§§` in Prompts, `WHERE`-Bedingungen und `JOIN`s wesentlich robuster und fehlersicherer verarbeiten als 44-stellige Base64-Chaos-Strings.

## Scope

### Muss-Haben

- **Entfernung von `Secret`:** Vollständige Entfernung des `Secret`-Eigenschaftsfeldes aus `TokenizationOptions`, `appsettings.json` und allen Dokumentationen/Tests.
- **Vereinfachung von `IsUsable`:** `TokenizationOptions.IsUsable` prüft nur noch `Enabled && !string.IsNullOrEmpty(Prefix) && !string.IsNullOrEmpty(Suffix)`.
- **Bidirektionales In-Memory-Mapping in `TokenVault`:** 
  - `TokenVault` hält zwei Zuordnungen (`Value -> Token` und `Token -> Value`), sodass derselbe Originalwert innerhalb einer Session garantiert immer dasselbe Kurz-Token erhält.
- **Kompakte Tokengenerierung (`Anonymizer.cs`):** Ersetzung des HMAC-SHA256-Generators durch ein kompaktes Token-Schema mit Präfix/Suffix (z. B. sequentieller Zähler `§§§T1§§§` oder kompakter 6-Zeichen-Hash `§§§T_A8K2§§§`).
- **Anpassung `QueryTokenResolver.cs`:** Aktualisierung der Regex-Erkennung für das neue Kurz-Token-Format.
- **Test-Updates:** Anpassung aller Unit- und Integrationstests auf das neue Token-Format und Entfernung von Secret-Abhängigkeiten.

### Nice-to-Have (optional, spätere Iteration)

- Bounded Capacity / LRU-Eviction im `TokenVault` zur Absicherung gegen extremen Speicherzuwachs bei Multi-Millionen-Zeilen-Sessions.

### Non-Goals (bewusst NICHT Teil davon)

- **Keine Änderung an Anonymisierungsregeln:** Welche Datenbankspalten anonymisiert werden (`AnonymizationRules`), bleibt unverändert.
- **Keine Datenbank-Persistenz von Tokens:** Tokens leben weiterhin exklusiv im Arbeitsspeicher des MCP-Serverprozesses.
- **Keine Änderung des Maskierungs-Modus (`ScramblePattern` / `Hash`):** Die reguläre Maskierung ohne Tokenisierung bleibt unberührt.

## Zielplattformen / Technischer Rahmen

- **Plattform:** .NET 10 / C# 14
- **Komponenten:** Memory-Data-Structures (`ConcurrentDictionary`), String-Formatting, Regex Matching

## Verworfene Alternativen

- **Beibehalten von HMAC-SHA256 mit Secret Key:** Verworfen, weil HMAC eine Einwegfunktion ist und ohne In-Memory Lookup ohnehin nicht entschlüsselt werden kann. Produziert unberechtigt lange, teure LLM-Tokens (~15 LLM-Tokens pro Wert).
- **Zustandlose Tokenisierung ohne Vault:** Verworfen, weil aus einem kryptographischen Hash der Originaltext für SQL-Literale auf dem Rückweg nicht rekonstruierbar ist.

## Wo im Projekt

Konkret betroffene Quellcode-Dateien und Stellen im Bestandscode (Pointer-Prinzip):

1. [SqlToAiOptions.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Configuration/SqlToAiOptions.cs#L81-L110)
   - `TokenizationOptions`: Entfernen der Eigenschaft `Secret` (Zeile 92).
   - `TokenizationOptions.IsUsable`: Entfernen der `!string.IsNullOrEmpty(Secret)` Bedingung (Zeile 105–109).
2. [Anonymizer.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Anonymization/Anonymizer.cs#L54-L93)
   - `Tokenize()`: Umstellen von `ComputeToken` auf die direkte Verwendung des bidirektionalen `ITokenVault`.
   - `ComputeToken()`: Entfernen der HMAC-SHA256-Logik (Zeilen 81–93).
3. [ITokenVault.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Anonymization/ITokenVault.cs) & [TokenVault.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Anonymization/TokenVault.cs)
   - Schnittstelle und Klasse erweitern für `GetOrAddToken(string originalValue, string prefix, string suffix)` mit zweiter `ConcurrentDictionary` für `Value -> Token`.
4. [QueryTokenResolver.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Database/QueryTokenResolver.cs#L74-L78)
   - `BuildTokenPattern()`: Anpassung des Regex-Musters auf die neue Token-Struktur.
5. [appsettings.json](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/appsettings.json#L23-L28)
   - `"Tokenization"`-Block: Entfernung von `"Secret": ""` (Zeile 25).
6. [AnonymizerTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Anonymization/AnonymizerTests.cs#L106-L215)
   - Entfernen der Tests bezüglich `Secret` (`BuildTokenizationOptions`, `Tokenize_ShouldProduceDifferentTokens_ForDifferentSecrets`, `Tokenize_ShouldFallBackToMasking_WhenSecretIsEmpty`).
   - Hinzufügen von Tests für Kurz-Token-Formate und Bidirektionalität.
7. [QueryTokenResolverTests.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/tests/SqlToAi.Tests/Database/QueryTokenResolverTests.cs)
   - Anpassen der Token-Strings in den Testfällen an das neue Format.

## Wie (grober Ansatz)

1. **Optionen bereinigen:** `Secret` aus `TokenizationOptions` und `appsettings.json` streichen.
2. **`TokenVault` erweitern:** Ein bidirektionales `GetOrAddToken`-Verfahren implementieren (z. B. Atomic Increment / Kurz-Hash), welches für denselben String konsistent denselben Kurz-Token zurückgibt.
3. **`Anonymizer` anpassen:** `ComputeToken` durch Aufruf von `_tokenVault.GetOrAddToken` ersetzen.
4. **`QueryTokenResolver` anpassen:** Regex auf die neuen Kurz-Tokens anpassen.
5. **Tests aktualisieren & verifizieren:** Alle xUnit-Tests aktualisieren und mittels `dotnet test` grün absichern.

## Definition of Done / Erfolgskriterien

- **Build:** `dotnet build` läuft ohne Fehler und Warnungen durch.
- **Tests:** `dotnet test` führt 100% der Unit- und Integrationstests erfolgreich aus.
- **Token-Ersparnis:** Die generierten Tokens sind deutlich kürzer (z. B. `§§§T1§§§` oder `§§§T_A8K2§§§`), statt 50 Zeichen lang zu sein.
- **Kein Secret erforderlich:** `Tokenization` funktioniert sofort bei `"Enabled": true`, ohne dass ein Secret konfiguriert werden muss.

## Offene Punkte

- Keine. Das Konzept ist bereit als Grundlage für den Umsetzungsplan.
