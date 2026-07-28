---
status: done (pending audit)
type: step-result
task: tokenization-short-tokens
step: "003"
coded_by: coder
coded_by_model: gemini-3.6-flash
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-07-28T15:26:30Z
code_commit: ee80b94
---

# Step 003 Result: Test-Updates (AnonymizerTests & QueryTokenResolverTests)

## Zusammenfassung

Alle Unit-Tests in `AnonymizerTests.cs`, `QueryTokenResolverTests.cs`, `AnonymizationPolicyResolverTests.cs` und `QueryExecutionServiceAnonymizationTests.cs` wurden aktualisiert. Secret-Abhängigkeiten wurden entfernt, und neue Testfälle für das Kurz-Token-Format (`§§§T1§§§`, `<<T1>>`) sowie Determinismus und Egress/Ingress Roundtrips wurden integriert.

## Geänderte Dateien

- `tests/SqlToAi.Tests/Anonymization/AnonymizerTests.cs`: Secret-Tests entfernt, Kurz-Token-Tests hinzugefügt.
- `tests/SqlToAi.Tests/Database/QueryTokenResolverTests.cs`: Secret-Tests entfernt, Kurz-Token Roundtrips verifiziert.
- `tests/SqlToAi.Tests/Anonymization/AnonymizationPolicyResolverTests.cs`: `IsTokenizationActive_ShouldBeTrue_WhenEnabled` angepasst.
- `tests/SqlToAi.Tests/Database/QueryExecutionServiceAnonymizationTests.cs`: `Secret`-Zuweisung entfernt.
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json`: Automatische Baseline-Synchronisation.

## Commit

- **Hash:** `ee80b94`
- **Message:** `feat(anonymization): Umstellung auf Kurz-Tokens und Entfernung von Secret`

## Build / Test Status

- `dotnet build`: Grün (0 Warnungen, 0 Fehler)
- `dotnet test`: 436/436 Tests erfolgreich

## Abweichungen vom Plan

Keine.

## Beobachtungen

Keine.
