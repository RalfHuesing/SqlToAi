---
status: done
type: step-result
task: audit-2026-07-24
step: 004/fix-01
coded_by: coder
coded_by_model: MiniMax-M3
coded_by_model_knowledge_cutoff: 2026-01
coded_at: 2026-07-25T22:45:00+02:00
code_commit_hash: 9b4482ab026ea2adc4ee1c26ab17c7a0f4e029b3
status_after: done
---

# Result Step 004 / fix-01: Bracket-Pass-Through in ReadOnlyGuard + Test-Coverage + Commit-Subject kürzen

## Zusammenfassung

Sicherheitsrelevante Verhaltensdivergenz aus `step-004/step-review.md` Findings #1 behoben: `ReadOnlyGuard.StripCommentsAndStringLiterals` reicht jetzt Bracket-Charaktere und -Inhalt explizit an die Mutating-Regex durch, sodass `IsQuerySafe("SELECT [insert] FROM t")` wieder `false` liefert (entspricht dem Verhalten vor `bcdce97`). Der irreführende Code-Kommentar wurde durch eine korrekte Erklärung der Wortgrenzen-Semantik ersetzt. Fünf neue Mutating- und drei neue Safe-Theorie-Einträge in `ReadOnlyGuardTests` schließen die Test-Coverage-Lücke für Bracket-Identifier. SHA-256-Hashes der geänderten Dateien sind in `SqlToAi-baseline.json` aktualisiert. Build 0/0, Test-Suite 383/383 grün (375 alt + 5 Mutating + 3 Safe).

## Geänderte Dateien

- `src/SqlToAi/Security/ReadOnlyGuard.cs` — in `StripCommentsAndStringLiterals` einen weiteren `else if (ev.State == SqlCharState.Bracket)`-Zweig ergänzt, der `ev.Character` an `sb` anhängt; irreführender Kommentar (Behauptung „semantisch ohne Auswirkung") durch korrekte Wortgrenzen-Erklärung ersetzt.
- `tests/SqlToAi.Tests/Security/ReadOnlyGuardTests.cs` — 5 neue `[InlineData]`-Zeilen in `IsQuerySafe_ShouldReturnFalse_ForMutatingQueries` (Bracket-Identifier mit Mutating-Keyword-Inhalt) und 3 in `IsQuerySafe_ShouldReturnTrue_ForSafeQueries` (harmlose Bracket-Identifier).
- `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json` — SHA-256-Hashes für `ReadOnlyGuard.cs` und `ReadOnlyGuardTests.cs` aktualisiert.

## Commit

- **Code-Commit-Hash:** `9b4482ab026ea2adc4ee1c26ab17c7a0f4e029b3`
- **Message:**
  ```
  fix(security): reiche Bracket-Inhalt in ReadOnlyGuard durch
  ```
- **Branch:** `main`
- **Push:** nein (lokal)
- **Vorgeschichte Refactor-Commit:**
  - **Hash auf lokalem `main`:** `16cab0f3c42be03636c49c907b43f029972db7bd` (entspricht dem im Plan genannten `bcdce97` — die History wurde zwischen Plan-Erstellung und Coder-Sitzung rebased, daher abweichender SHA bei identischem Subject und Body).
  - **Subject:** `refactor(database): extrahiere gemeinsamen SqlCharScanner` (57 Zeichen, ≤72). Der im Plan angeforderte `--amend` war **bereits vor Beginn dieser Coder-Sitzung durchgeführt** worden — siehe „Abweichungen vom Plan" unten. Der Body des amendierten Commits enthält noch den ursprünglichen, vom Auditer kritisierten Hinweis „Bracket-Inhalte aus — semantisch ohne Auswirkung"; eine Body-Korrektur war nicht im Scope des Fix-Steps und wurde nicht durchgeführt.

## Build-Output

```
dotnet build SqlToAi.slnx
→ Der Buildvorgang wurde erfolgreich ausgeführt.
  0 Warnung(en)
  0 Fehler
  Verstrichene Zeit 00:00:10.98
```

## Test-Output

```
dotnet test --filter "Category!=Integration" --nologo
→ Bestanden! Fehler: 0, erfolgreich: 383, übersprungen: 0, gesamt: 383, Dauer: 16 s

Aufschlüsselung ReadOnlyGuardTests:
  40/40 grün (vorher 32: 1 Fact + 11 Safe-InlineData + 20 Mutating-InlineData;
  danach +5 Mutating-InlineData + 3 Safe-InlineData = 40)
```

Test-Count stimmt mit der Erwartung im Step-Plan überein (375 alt + 5 + 3 = 383). Bekannte vorbestehende Ausnahmen bleiben unberührt (AiNetLinter-Baseline ist nach Hash-Update grün, Integration-Tests sind durch den Filter ohnehin ausgeschlossen).

## Abweichungen vom Plan

**1. `git commit --amend` auf `bcdce97` war nicht mehr nötig** — der Commit-Subject war bei Beginn der Coder-Sitzung bereits auf 57 Zeichen gekürzt (`refactor(database): extrahiere gemeinsamen SqlCharScanner`). Das Amend wurde vermutlich manuell durch den User zwischen Plan-Erstellung und Coder-Aufruf durchgeführt. **Hinweis:** Der im Plan und in `step-004/step-result.md` (Hash `bcdce9793cd511f9bb2cbfd8b7fe3af980f5aad5`) genannte Refactor-Commit hat auf dem aktuellen lokalen `main` einen anderen SHA (`16cab0f3c42be03636c49c907b43f029972db7bd`), Subject und Body sind jedoch identisch. Die History wurde zwischen Plan-Erstellung und Coder-Sitzung rebased; der ältere Hash existiert nur noch auf `origin/main`. Kein erneutes Amend durchgeführt, weil:
- (a) Subject bereits ≤72 Zeichen,
- (b) Body enthält zwar noch die vom Auditer als unzutreffend markierte Behauptung über Bracket-Semantik, aber der Plan explizit nur „Subject-only" für den Amend vorsah,
- (c) ein zusätzlicher Amend mit Body-Änderung würde den Hash erneut ändern und gegen die im Plan dokumentierte Erwartung verstoßen, dass der amendierte Hash in `step-result.md` festgehalten wird.

**2. Body des amendierten `bcdce97`-Commits nicht korrigiert** — der Plan nennt Findings #2 (Subject > 72 Zeichen) als Amend-Anlass, der Body wurde nicht als Amend-Scope definiert. Der Body enthält weiterhin den Satz „ReadOnlyGuard verhaelt sich semantisch gleich, blendet jetzt aber zusaetzlich Bracket-Inhalte aus -- semantisch ohne Auswirkung", der durch den Fix-01 widerlegt ist. Dies ist als Beobachtung dokumentiert (siehe unten). Eine nachträgliche Body-Korrektur würde den Plan-Scope überschreiten.

**3. Test-Count-Anpassung** — der Step-Plan sprach von „ReadOnlyGuardTests (alle 28 vorhandenen)" und „375 (alt) + 5 + 3 = 383". Tatsächlich sind es 32 vorhandene Tests (1 Fact + 11 Safe + 20 Mutating), die Suite hat jetzt 40 Tests, und die Gesamt-Suite liegt bei 383. Die Plan-Zahlen waren leicht konservativ gerundet, der finale Stand übertrifft die Mindest-Erwartung von ≥377.

## Beobachtungen

- **Body von `bcdce97` referenziert noch die falsche Behauptung** über Bracket-Semantik. Der Satz „ReadOnlyGuard verhaelt sich semantisch gleich, blendet jetzt aber zusaetzlich Bracket-Inhalte aus -- semantisch ohne Auswirkung, da der Regex auf Mutating-Keywords in echten Identifiern eh nicht greift" ist durch Findings #1 des Auditer-Reviews und den hier umgesetzten Fix widerlegt. Empfehlung: in einem Folge-Step den Body per interaktivem `git rebase -i bcdce97~` korrigieren (Subject bleibt gleich, Body wird präzisiert). Da `bcdce97` lokal und nicht gepusht ist, ist der Aufwand gering.
- **`ReadOnlyGuard.cs` enthält jetzt 26 Zeilen `StripCommentsAndStringLiterals` inklusive Kommentar (vorher 19)** — bleibt deutlich unter dem 60-Zeilen-Limit von `AiNetLinter.mdc#Kurz-Stil`. Keine Linter-Regression.
- **Test-Suite-Wachstum konsistent** — `ReadOnlyGuardTests` wuchs von 32 auf 40 Tests (+25%), Gesamt-Suite von 375 auf 383 Tests (+2,1%). Die neue Coverage schließt die im Review explizit genannte Lücke.
- **`SqlLiteralScanner` wurde nicht angefasst** — sein eigener Strip-Loop mit Normal→SingleQuote-Logik blendet Bracket-Inhalt korrekt aus (verifiziert durch bestehende Tests `GetLiteralContentRanges_ShouldIgnoreContentInsideBracketIdentifiers` etc.). Kein Konflikt mit dem Fix in `ReadOnlyGuard`.
- **`SqlCharScanner` selbst wurde nicht angefasst** — Scanner ist mechanisch korrekt (vom Auditer in `step-review.md` Z. 108 bestätigt). Der Fix sitzt ausschließlich in der Konsumlogik von `ReadOnlyGuard`, wie in `step-plan.md` vorgesehen.
- **Globale Empfehlung** (aus `step-review.md` Abschnitt „Sonstige Beobachtungen", nicht in diesem Fix-Scope): `SqlCharScanner.Transition` (31 Zeilen, am Limit), ungenutzte `Next`-Property, `yield return`-Allokation, `SqlCharState public` vs. `SqlCharScanner internal` Asymmetrie. Diese Punkte sind für einen zukünftigen 360°-Audit vorgemerkt.

## Bekannte Unschärfen

- **Verhalten bei Unicode in Bracket-Identifiern** wurde nicht getestet. Scanner ist char-für-char; `[日本語]` würde vermutlich korrekt durchgereicht (jedes `char` ist im .NET-Regex ein eigenes Token), aber explizite Test-Cases fehlen. Kein Handlungsbedarf für diesen Fix-Step, da der Mutating-Regex ohnehin nur ASCII-Mutating-Keywords enthält.
- **Bracket-Inhalt mit Sonderzeichen (`]`, `''`, etc.)** — T-SQL erlaubt `]]` als Escape für `]` innerhalb von Bracket-Identifiern und `''` ist dort nicht relevant (Quotes gibt's nur in String-Literalen). Der Scanner behandelt `]` innerhalb von `[...]` weiterhin als Bracket-Close-Char, was dem T-SQL-Standard entspricht. Nicht durch neue Tests abgedeckt; falls später Bedarf entsteht, kann `SqlCharScannerTests.Scan_ShouldHandleNestedBlockCommentAndBracketEnd` erweitert werden.
- **Performance-Charakterisierung** — ein zusätzlicher `else if`-Branch in einem linearen Single-Pass-Scanner ist O(1) pro Zeichen. Kein messbarer Performance-Impact. Kein Microbenchmark durchgeführt (war nicht im Plan-Scope).
