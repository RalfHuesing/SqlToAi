---
status: done (audit skipped per user request)
type: step-plan
task: audit-2026-07-24
step: 004/fix-01
title: "Fix ReadOnlyGuard Bracket-Pass-Through + Test-Coverage + Commit-Subject kürzen"
created_by: planer
created_by_model: MiniMax-M3
created_by_model_knowledge_cutoff: 2026-01
created_at: 2026-07-25T22:30:00+02:00
coded_at: 2026-07-25T22:45:00+02:00
code_commit: 9b4482a
related_to:
  - tasks/audit-2026-07-24/step-004/step-review.md (Findings #1, #2)
  - tasks/audit-2026-07-24/step-004/step-plan.md (ursprünglicher Plan)
  - tasks/audit-2026-07-24/step-004/step-result.md (was tatsächlich umgesetzt wurde)
---


# Step 004 / fix-01: Bracket-Pass-Through in ReadOnlyGuard + Test-Coverage + Commit-Subject kürzen

## Bezug

- **Task:** `audit-2026-07-24`
- **Quelle:** `step-004/step-review.md` Abschnitt „Findings" #1 (sicherheitsrelevante Verhaltensdivergenz in `ReadOnlyGuard.StripCommentsAndStringLiterals`) und #2 (Commit-Subject `bcdce97` > 72 Zeichen)
- **Ursprünglicher Step-Plan:** `step-004/step-plan.md` — der hier dokumentierte Refactor ist semantisch abgeschlossen, es geht nur um die Korrektur der im Review entdeckten Regressions

## Intention

Der Refactor von `step-004` hat in `ReadOnlyGuard.StripCommentsAndStringLiterals` eine **sicherheitsrelevante Verhaltensdivergenz** erzeugt: Bracket-Identifier-Inhalte (`[insert]`, `[drop]`, `[delete]`, `[update]`, `[truncate]`) werden implizit ausgeblendet, sodass der Mutating-Regex sie nicht mehr sieht. Konkret: `SELECT [insert] FROM t` wurde vor dem Refactor abgewiesen (`IsQuerySafe = false`), wird jetzt aber fälschlich als sicher akzeptiert. Dieser Fix stellt die Original-Semantik wieder her (Bracket-Inhalt wird durchgereicht), schließt die Test-Coverage-Lücke mit mindestens zwei neuen `ReadOnlyGuardTests` und kürzt den zu langen Commit-Subject von `bcdce97` per `--amend` auf 47 Zeichen.

## Konkrete Änderungen

### Datei 1: `src/SqlToAi/Security/ReadOnlyGuard.cs` (Zeile 57–78, `StripCommentsAndStringLiterals`)

- **Was:** In der `foreach`-Schleife nach dem bestehenden `else if (ev.State == SqlCharState.SingleQuote && ev.Character == '\'')`-Block einen weiteren `else if` ergänzen, der den `Bracket`-State an `sb` durchreicht. Konkret:

  ```csharp
  private static string StripCommentsAndStringLiterals(string sql)
  {
      var sb = new StringBuilder(sql.Length);
      foreach (var ev in SqlCharScanner.Scan(sql))
      {
          // Original-Logik: Zeichen in 'Normal' durchreichen, in 'SingleQuote' (nur das
          // '\'' selbst) durch Whitespace ersetzen. Andere States (Comments, Bracket) werden
          // implizit übersprungen. Im Gegensatz zur vorherigen Inline-Implementierung werden
          // Bracket-Inhalte jetzt ebenfalls ausgeblendet — semantisch ohne Auswirkung, da der
          // Regex auf Mutating-Keywords in echten Identifiern eh nicht greift.
          if (ev.State == SqlCharState.Normal)
          {
              sb.Append(ev.Character);
          }
          else if (ev.State == SqlCharState.SingleQuote && ev.Character == '\'')
          {
              sb.Append(' ');
          }
          else if (ev.State == SqlCharState.Bracket)   // ← NEU: Bracket-Inhalt durchreichen
          {
              sb.Append(ev.Character);
          }
      }

      return sb.ToString();
  }
  ```

  Der bestehende Kommentar im Code (Z. 64–66) ist **falsch und muss angepasst werden**: er behauptet, Bracket-Ausblendung sei „semantisch ohne Auswirkung". Das ist unzutreffend (siehe `step-review.md` Z. 161). Neuer Kommentar (sinngemäß):

  ```
  // Original-Logik (vor step-004-Refactor): Zeichen in 'Normal' und innerhalb von
  // Bracket-Identifiern '[...]' durchreichen, in 'SingleQuote' (nur das '\'' selbst)
  // durch Whitespace ersetzen, damit Werte wie WHERE Status = 'DELETE' nicht als
  // Mutating-Keyword matchen. Andere States (LineComment, BlockComment) werden
  // implizit übersprungen. Bracket-Inhalt MUSS durchgereicht werden, damit Wortgrenzen
  // in [insert], [drop], [delete], [update], [truncate] vom Mutating-Regex
  // \b(...)\b erkannt werden — siehe step-004/fix-01.
  ```

- **Warum:** Der Original-`ReadOnlyGuard` vor `bcdce97` kannte keinen `Bracket`-State, weil der alte Inline-Scanner nur 4 States hatte. `[id]` wurde zeichenweise im Normal-State verarbeitet und durchgereicht. Der Refactor auf den 5-State-`SqlCharScanner` hat `Bracket` korrekt als eigenen State emittiert, der Strip-Loop in `ReadOnlyGuard` hat das aber **nicht** kompensiert. Im .NET-Regex bilden `[` und `]` Wortgrenzen für `\b...\b`, daher muss `insert` innerhalb von `[insert]` sichtbar bleiben, damit der Mutating-Regex matcht.

### Datei 2: `tests/SqlToAi.Tests/Security/ReadOnlyGuardTests.cs`

- **Was:** Mindestens zwei neue Test-Cases ergänzen. Vorgeschlagene Platzierung: in der bestehenden `[Theory] public void IsQuerySafe_ShouldReturnFalse_ForMutatingQueries(string query)`-Methode (Z. 44–76) weitere `[InlineData]`-Zeilen mit Bracket-Identifiern, die mutating-keyword-ähnlichen Inhalt haben. Zusätzlich optional ein harmloser Test in der Safe-Theorie, um sicherzustellen, dass die Fix-Logik nicht über das Ziel hinausschießt.

  **Pflicht-Cases (mindestens 2 in der `_ForMutatingQueries`-Theory):**

  ```csharp
  // step-004/fix-01: Bracket-Identifier mit mutating-keyword-ähnlichem Inhalt müssen
  // erkannt werden. \b in .NET-Regex erkennt die Klammern [ ] als Wortgrenzen.
  [InlineData("SELECT [insert] FROM t")]
  [InlineData("SELECT [drop] FROM t")]
  [InlineData("SELECT * FROM [delete]")]
  [InlineData("SELECT [update] FROM t WHERE [truncate] = 1")]
  [InlineData("INSERT INTO [insert] VALUES (1)")]   // redundant zu vorhandenem INSERT-Test, dokumentiert aber explizit
  ```

  **Optional-Safe-Cases (in `_ForSafeQueries`, harmless brackets, müssen weiterhin true bleiben):**

  ```csharp
  [InlineData("SELECT [My Column With Spaces] FROM t")]
  [InlineData("SELECT [Order Date] FROM [Customer Orders]")]
  [InlineData("SELECT * FROM [dbo].[Customers]")]
  ```

  Begründung der Bracket-Tests: sie existierten **vor** `bcdce97` nicht, weil der Original-`ReadOnlyGuard` Bracket-Inhalt ohnehin durchreichte und das nur zufällig funktionierte — kein Test sicherte es ab. Mit dem Refactor wurde der Pfad nun aktiv falsch (Bracket wird ausgeblendet), und der Fix muss durch Tests gegen zukünftige Re-Regressionen abgesichert werden.

- **Warum:** Findings #1 des Reviews Z. 172 ist explizit: „Ohne diese Tests ist Findings #1 jederzeit wieder reaktivierbar."

### Datei 3: `tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json`

- **Was:** Den SHA-256-Hash der geänderten Datei `src/SqlToAi/Security/ReadOnlyGuard.cs` neu berechnen (mit `Get-FileHash -Algorithm SHA256`) und in der Baseline ersetzen. Die Hashes aller anderen Dateien bleiben unverändert. Tests (`ReadOnlyGuardTests.cs`) sind in der aktuellen Baseline noch nicht gehasht (siehe `step-review.md` Z. 84 — `SqlCharScannerTests.cs` ist es, `ReadOnlyGuardTests.cs` ist es nicht). Verifikation vor Commit mit:
  ```
  Get-Content tests/SqlToAi.Tests/AiNetLinter/rules/SqlToAi-baseline.json | ConvertFrom-Json | ...vergleichen
  ```
  bzw. direktem `dotnet test --filter "FullyQualifiedName~AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch"` zur Verifikation, dass die Baseline nach Hash-Update wieder grün ist.

- **Warum:** `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` ist die kanonisierte Absicherung gegen versehentliche Linter-Regressionen. Wenn der Hash nicht passt, schlägt der Test fehl.

### Datei 4 (Commit-Operation, keine Datei): `bcdce97` — Subject amendieren

- **Was:** Den lokalen Code-Commit `bcdce97` per `git commit --amend` korrigieren. Subject kürzen von:
  ```
  refactor(database): extrahiere gemeinsamen SqlCharScanner aus drei State-Machine-Duplikaten
  ```
  (96 Zeichen) auf:
  ```
  refactor(database): extrahiere gemeinsamen SqlCharScanner
  ```
  (47 Zeichen). Body bleibt unverändert. Konkret:

  ```bash
  git -C "C:\Daten\Entwicklung\Ralf\SqlToAi" \
    rebase -i 2cfedb5^  # interaktiv nur bcdce97 zum Reword markieren
  # ODER (einfacher):
  git -C "C:\Daten\Entwicklung\Ralf\SqlToAi" \
    commit --amend -m "refactor(database): extrahiere gemeinsamen SqlCharScanner"
  ```

  **Reihenfolge zwingend:** Das Amend muss **vor** dem Fix-Commit passieren, damit die History chronologisch sauber bleibt: erst den existierenden Refactor-Commit bereinigen, dann den Fix-Commit obendrauf setzen. Sonst müsste man später `rebase` auf den Fix-Commit machen, was unsauberer ist.

  **Verifikation nach Amend:**
  ```bash
  git -C "C:\Daten\Entwicklung\Ralf\SqlToAi" log --format='%H %s' bcdce97 -1
  # Erwartete Ausgabe: <neuer-Hash> refactor(database): extrahiere gemeinsamen SqlCharScanner
  ```

  **Wichtig — der ursprüngliche Commit-Hash `bcdce97` ist danach nicht mehr gültig.** `step-004/step-result.md` Z. 10 verweist auf `code_commit_hash: bcdce9793cd511f9bb2cbfd8b7fe3af980f5aad5`. Diese Referenz ist im step-result festgeschrieben; sie zeigt auf den Stand **vor** dem Amend und ist als historische Wahrheit OK. Im neuen `step-004/fix-01/step-result.md` muss dann der **neue** Hash des amendierten Commits sowie der Hash des Fix-Commits stehen.

- **Warum:** `step-review.md` Z. 182 dokumentiert, dass der Commit noch lokal ist (`Push: nein`). User-übliche Konvention (User-Memory) ist „Subject ≤72 Zeichen". Risiko des `--amend` ist null, weil Subject-only geändert wird und der Commit nicht gepusht ist.

## Tests

- [ ] `dotnet build SqlToAi.slnx` — 0 Warnungen, 0 Fehler
- [ ] `dotnet test --filter "Category!=Integration" --nologo` — 377/377 (oder höher) grün; 375 alte + 5 neue `InlineData`-Zeilen in `ReadOnlyGuardTests` (jede zählt als eigener Test in xUnit v3)
- [ ] **Neue Pflicht-Tests in `ReadOnlyGuardTests.IsQuerySafe_ShouldReturnFalse_ForMutatingQueries`:**
  - `SELECT [insert] FROM t` → `false` (Bracket-Inhalt erkannt)
  - `SELECT [drop] FROM t` → `false`
  - `SELECT * FROM [delete]` → `false`
  - `SELECT [update] FROM t WHERE [truncate] = 1` → `false`
  - `INSERT INTO [insert] VALUES (1)` → `false` (doppelter Schutz, dokumentiert das gewünschte Verhalten)
- [ ] **Neue Optional-Tests in `ReadOnlyGuardTests.IsQuerySafe_ShouldReturnTrue_ForSafeQueries`:**
  - `SELECT [My Column With Spaces] FROM t` → `true` (harmloser Bracket-Identifier bleibt safe)
  - `SELECT [Order Date] FROM [Customer Orders]` → `true`
  - `SELECT * FROM [dbo].[Customers]` → `true`
- [ ] **Bestehende Tests bleiben grün ohne inhaltliche Änderung** (nur ggf. die zu langen Commits werden amended):
  - `SqlCharScannerTests` (alle 9)
  - `SqlLiteralScannerTests` (alle 10) — verifiziert, dass Bracket-Inhalt in Literalen weiterhin **ausgeblendet** bleibt (SqlLiteralScanner hat eigenen Strip-Loop mit Normal→SingleQuote-Logik, kein Konflikt mit dem Fix)
  - `ReadOnlyGuardTests` (alle 28 vorhandenen + neue)
  - `QueryExecutionServiceTests` (alle Multi-Statement-Varianten)
  - `QueryValidationServiceTests`
  - `QueryTokenResolverTests` (alle 12, sicherheitskritisch)
  - `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` — **muss nach Baseline-Hash-Update wieder grün sein**

## Definition of Done

- [ ] `ReadOnlyGuard.cs:57-78` — zusätzlicher `else if (ev.State == SqlCharState.Bracket)`-Block ergänzt; irreführender Kommentar korrigiert
- [ ] `ReadOnlyGuardTests.cs` — mindestens 5 neue Bracket-`[InlineData]`-Zeilen in der Mutating-Theory, optional 3 in der Safe-Theory
- [ ] `SqlToAi-baseline.json` — SHA-256-Hash von `ReadOnlyGuard.cs` aktualisiert
- [ ] Commit `bcdce97` per `--amend` auf Subject `refactor(database): extrahiere gemeinsamen SqlCharScanner` gekürzt (Body unverändert)
- [ ] Neuer Code-Commit: `fix(security): ReadOnlyGuard muss Bracket-Identifier-Inhalt durchreichen`
- [ ] `dotnet build SqlToAi.slnx` 0/0
- [ ] `dotnet test --filter "Category!=Integration"` 377/377 (oder höher, je nach Anzahl neuer InlineData) grün
- [ ] `step-004/fix-01/step-result.md` geschrieben mit: altem `bcdce97`-Hash (vor Amend, als historische Wahrheit), neuem Hash des amendierten Commits, neuem Fix-Commit-Hash
- [ ] `status` in `step-004/fix-01/step-plan.md` von `open` auf `done (pending audit)` gesetzt
- [ ] Nach erfolgreichem Audit: `status` in `step-004/step-plan.md` von `done (fix-01 pending)` auf `done` setzen (final approval für step-004 als Ganzes)

## Rules-Refs

- `.agents/rules/AiNetLinter.mdc#Kurz-Stil` — `StripCommentsAndStringLiterals` wächst von 19 auf 22 Zeilen (innerhalb des 60-Zeilen-Limits); `ReadOnlyGuardTests` wächst um ~8 Zeilen (innerhalb 500-Zeilen-Datei-Limit)
- `.agents/rules/AiNetLinter.mdc#general/EnforceSealedClasses` — `ReadOnlyGuard` bleibt `public sealed class`; `ReadOnlyGuardTests` ist bereits `public sealed class`
- `.agents/rules/AiNetLinter.mdc#agent-resilience/EnforceNoSilentCatch` — keine Änderung am `try/catch (RegexMatchTimeoutException)` in `IsQuerySafe` (Z. 39–48 bleibt unverändert)
- `.agents/rules/SqlToAiRichtlinien.mdc` Z. 65 — Commits autonom, deutsch, imperativ; Conventional Commits. Subject des Fix-Commits ≤72 Zeichen halten (Lektion aus Findings #2)
- `.agents/rules/SqlToAiRichtlinien.mdc` Z. 64 — Sicherheitsrelevante Korrekturen sind explizit willkommener Anlass für Commits
- `.agents/rules/SqlToAiRichtlinien.mdc` implizit (User-Memory) — Subject ≤72 Zeichen als User-Konvention; der `--amend` auf `bcdce97` adressiert genau diese Anforderung
- `step-004/step-plan.md` Z. 42 — „Wichtig: Transition selbst bleibt im neuen Scanner gekapselt. Die drei Call-Sites konsumieren die SqlCharEvent-Sequenz und setzen ihre eigene (unterschiedliche) Business-Logik auf." — Option A respektiert diese Vorgabe (ReadOnlyGuard-spezifische „Bracket pass-through"-Logik bleibt in ReadOnlyGuard, Scanner bleibt generisch)

## Bekannte Ausnahmen

- `AiNetLinterTests.RunLinterShouldBeCleanOrBaselineMatch` — vorbestehend. **In diesem Step relevant:** die Baseline-Hashes werden in `Datei 3` aktualisiert. Verifikation: nach Hash-Update muss der Test grün sein. Wenn er rot bleibt, hat entweder der Hash-Wert nicht gestimmt oder die Linter-Regeln wurden anderswo verletzt.
- `QueryExecutionServiceIntegrationTests.ExecuteQueryAsync_ShouldRespectDatabaseExclusions_AgainstRealTable` — vorbestehend, **Integration-Test** (Kategorie `Integration`), wird durch den `--filter "Category!=Integration"`-Standard-Lauf ohnehin übersprungen. Nicht Teil dieses Fixes.

## Code-Skizze (optional)

```csharp
// Vorher (bcdce97):
else if (ev.State == SqlCharState.SingleQuote && ev.Character == '\'')
{
    sb.Append(' ');
}
// Bracket-Charaktere fallen durch, werden NICHT angehängt → Sicherheitsproblem

// Nachher (fix-01):
else if (ev.State == SqlCharState.SingleQuote && ev.Character == '\'')
{
    sb.Append(' ');
}
else if (ev.State == SqlCharState.Bracket)
{
    sb.Append(ev.Character);
}
// Bracket-Inhalt erreicht den Regex → \b...\b matcht [insert], [drop] etc.
```

## Notes

- **Scope-Disziplin:** Nur Findings #1 (Bracket-Pass-Through + Tests) und #2 (Commit-Subject) sind in diesem Fix adressiert. Die im `step-review.md` Abschnitt „Sonstige Beobachtungen" gelisteten Punkte sind **ausdrücklich nicht** Scope:
  - `Transition`-Methodenlänge (31 Zeilen, am Limit) → für globalen 360°-Audit vorgemerkt
  - `Next`-Property im Scanner wird von keiner Call-Site verwendet → für globalen 360°-Audit vorgemerkt
  - `yield return` allokiert einen `IEnumerator` pro Aufruf → für globalen 360°-Audit vorgemerkt
  - `SqlCharState public` vs. `SqlCharScanner internal` (Asymmetrie) → für globalen 360°-Audit vorgemerkt
  - `CA1720`-Lektion („Character" statt „Char") in Coding-Conventions dokumentieren → für globalen 360°-Audit vorgemerkt

- **Root-Cause-Klarstellung:** Die Sicherheitsregression wurde **nicht** durch den Scanner verursacht. `SqlCharScanner.Transition` und `TransitionFromNormal` sind mechanisch korrekt und bit-identisch zu den Original-Implementationen in `SqlMultiStatementDetector` und `SqlLiteralScanner` (verifiziert im `step-review.md` Z. 108). Die Regression sitzt ausschließlich in der Konsumlogik von `ReadOnlyGuard.StripCommentsAndStringLiterals`, die den neuen 5. State (`Bracket`) nicht in ihre Strip-Logik einbezogen hat. Der Original-`ReadOnlyGuard` kannte diesen State nicht und behandelte `[...]` implizit als Plain-Text.

- **Warum Option A und nicht Option B (Begründung):** Option B würde eine zweite `Scan`-Methode im Scanner einführen, die nur die 4 für `ReadOnlyGuard` relevanten States liefert (kein `Bracket`-State). Der Strip-Loop bliebe dann identisch zum Plan. Dagegen spricht:
  1. **API-Surface:** Eine zweite Scanner-Methode vergrößert die öffentliche API des Scanners (auch wenn nur `internal`) für genau einen Konsumenten, was dem YAGNI-Prinzip widerspricht.
  2. **Plan-Konformität:** `step-plan.md` Z. 42 hält explizit fest: „Die drei Call-Sites konsumieren die SqlCharEvent-Sequenz und setzen ihre eigene (unterschiedliche) Business-Logik auf." Option A setzt die ReadOnlyGuard-spezifische Logik („Bracket durchreichen, weil Wortgrenzen") in `ReadOnlyGuard` selbst — wo sie hingehört. Option B würde ReadOnlyGuard-Spezifika in den Scanner tragen.
  3. **Minimaler Diff:** Option A ist eine einzige `else if`-Zeile. Option B verlangt eine neue Scanner-Methode, Anpassung der XML-Doku, ggf. zusätzliche Tests für die neue Methode.
  4. **Semantische Klarheit:** `SqlCharScanner` liefert rohe Zeichen-States. Was jeder Konsument damit macht (durchreichen, ausblenden, zählen), ist die Verantwortung des Konsumenten. Option A respektiert diese Trennung.

- **Warum Commit-Subject amendieren (Option a) und nicht ignorieren (Option b):**
  1. Commit ist nachweislich lokal (`step-result.md` Z. 75: `Push: nein`; `git log` zeigt `bcdce97` zwischen `e283663` und `3a8aa23`).
  2. `--amend` mit Subject-only-Änderung ist risikofrei (Body bleibt identisch, kein Inhalts-Diff).
  3. Spätere Amends mit zusätzlichem Inhalt werden aufwändiger, weil `rebase`/`fixup`/`squash`-Operationen mehrere Commits gleichzeitig umstellen müssen.
  4. User-Konvention (User-Memory) ist explizit „Subject ≤72 Zeichen".

- **Nach erfolgreichem fix-01:** Der gesamte `step-004` (Refactor + Fix) ist als `done` zu markieren. Der ursprüngliche Plan-Status `done (fix-01 pending)` in `step-004/step-plan.md` Z. 2 wird auf `done` gesetzt. `step-004/fix-01/step-plan.md` und `step-004/fix-01/step-result.md` bleiben als Audit-Trail bestehen.

- **Test-Count-Erwartung:** 375 (alt) + 5 (neue Pflicht) + 3 (neue Optional) = 383 Tests, **wenn** jede `[InlineData]`-Zeile in xUnit v3 als eigener Test gezählt wird. Die genaue Zahl hängt von der xUnit-v3-Theorie-Semantik ab. Akzeptanz: ≥377 grün (= 375 alt + mindestens 2 neue). Höher ist besser.
