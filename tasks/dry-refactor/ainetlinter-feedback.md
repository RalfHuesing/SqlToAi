# AiNetLinter Feedback & Beobachtungen

Dieses Dokument sammelt alle Beobachtungen, Anomalien, Optimierungspotenziale und Feedback zum AiNetLinter MCP-Server und den Linter-Regeln während des `dry-refactor` Tasks.

## Übersicht der Beobachtungen

| ID | Bereich / Tool | Beobachtung / Feedback | Empfehlung / Optimierung | Status |
|:---|:---|:---|:---|:---|
| FB-01 | `pattern_detect` & `get_violations` (`AIContextFootprint`) | `McpJsonContext` (Source-Generator-Context für `System.Text.Json`) meldete `AIContextFootprint (8515 > 5000)`. Bei System.Text.Json Source Generators deklariert die Kontextklasse viele `[JsonSerializable]`-Attribute, was den transitiven AST-Graphen künstlich aufbläht. | **Architektur-Lösung im Projekt:** Aufteilung in drei fokussierte Kontexte (`McpJsonContext`, `McpAnalysisJsonContext`, `McpTrailJsonContext`), wodurch alle Werte unter 5000 fielen.<br>**Linter-Empfehlung:** Erwägen, `JsonSerializerContext`-Subklassen bei `AIContextFootprint` gesondert zu gewichten oder als Schema-Definitionen zu behandeln. | Gelöst / Feedback erfasst |
| FB-02 | `AvoidExcessiveMiddleMen` | `GlobMatcherTests` meldete 100% Weiterleitung, weil Einzeiler-Tests `Assert.True(GlobMatcher.IsMatch(...))` aufrufen. | Testklassen sollten bei `AvoidExcessiveMiddleMen` standardmäßig ausgenommen sein oder separate Regeln haben. | Offen / Feedback erfasst |
| FB-03 | `MaxPublicMembersPerType` | Testklassen mit vielen xUnit `[Fact]` / `[Theory]` Methoden überschreiten schnell `MaxPublicMembersPerType <= 15`. In xUnit sind Testmethoden standardmäßig `public`. | Linter könnte Testmethoden (`[Fact]`, `[Theory]`) bei `MaxPublicMembersPerType` gesondert werten oder ein höheres Limit in `*.Tests` definieren (analog zu `MaxMethodLineCount: 100`). | Offen / Feedback erfasst |
| FB-04 | `find_duplicates` & `safeguard` | `find_duplicates` lieferte sehr präzise Duplikate-Cluster (Token-basiert), die beim Aufspüren von Regex/Scanner-Duplikaten (`SqlCharScanner`) extrem hilfreich waren. `safeguard` berechnet einen aggregierten Score (nach Behebung 10.00/10). | Ausgabe von `find_duplicates` bei großen Ergebnismengen direkt mit Kurzzusammenfassung und Dateipfaden ausgeben; ggf. Filterung nach Test- vs. Produktionscode als Parameter (`scopeType: "production" | "tests"`). | Empfehlung |
