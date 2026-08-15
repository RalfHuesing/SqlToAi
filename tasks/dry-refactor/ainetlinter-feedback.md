# AiNetLinter Feedback & Beobachtungen

Dieses Dokument sammelt alle Beobachtungen, Anomalien, Optimierungspotenziale und Feedback zum AiNetLinter MCP-Server und den Linter-Regeln während des `dry-refactor` Tasks.

## Übersicht der Beobachtungen

| ID | Bereich / Tool | Beobachtung / Feedback | Empfehlung / Optimierung | Status |
|:---|:---|:---|:---|:---|
| FB-01 | `pattern_detect` & `get_violations` | `McpJsonContext` (Source-Generator-Context für `System.Text.Json`) meldet `AIContextFootprint (8515 > 5000)`. Bei System.Text.Json Source Generators deklariert die Klasse viele `[JsonSerializable]` Attribute, was transitive Zeilen künstlich aufbläht. | Erwägen, Source Generator Contexts oder Klassen mit `JsonSerializable`-Attributen eine Ausnahmeregelung oder höhere Schwellwerte für `AIContextFootprint` zu geben. | Offen |
| FB-02 | `AvoidExcessiveMiddleMen` | `GlobMatcherTests` meldet 100% Weiterleitung, weil die Testklasse Hilfsmethoden oder TestCases delegiert. | Testklassen sollten bei `AvoidExcessiveMiddleMen` standardmäßig ausgenommen sein oder separate Regeln haben. | Offen |
| FB-03 | `MaxPublicMembersPerType` | Testklassen mit vielen xUnit `[Fact]` / `[Theory]` Methoden überschreiten schnell `MaxPublicMembersPerType <= 15`. In xUnit sind Testmethoden standardmäßig `public`. | Linter könnte Testmethoden (`[Fact]`, `[Theory]`) bei `MaxPublicMembersPerType` gesondert werten oder ein höheres Limit in `*.Tests` definieren (analog zu `MaxMethodLineCount: 100`). | Offen |
