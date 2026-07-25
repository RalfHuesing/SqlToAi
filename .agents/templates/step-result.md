---
status: done
type: step-result
task: <TASK-NAME>
step: <NNN>
coded_by: coder
coded_at: <ISO-8601>
commit_hash: <SHA>
status_after: done  # done | blocked
---

# Result Step <NNN>: <Titel>

## Zusammenfassung

<2-5 Sätze: Was wurde konkret gemacht? In einem Satz pro Datei.>

## Geänderte Dateien

- `pfad/zu/datei.cs` — <was geändert wurde, in einem Satz>
- `pfad/zu/datei2.cs` — <was>
- `tests/.../NeueTestDatei.cs` (neu) — <was die Tests abdecken>

<Pro Datei ein Bullet. Auch neue Files auflisten.>

## Commit

- **Hash:** `<SHA>`
- **Message:**
  ```
  <konventioneller Commit-Subject>

  <Body falls vorhanden>
  ```
- **Branch:** <aktueller Branch>
- **Push:** nein (lokal)

## Build-Output

```
<Build-Command aus Tech-Stack-Notiz>
→ Ergebnis: grün / rot (mit gekürztem Fehler-Output falls rot)
```

## Test-Output

```
<Test-Command aus Tech-Stack-Notiz>
→ Ergebnis: grün / rot (mit gekürzter Failure-Liste falls rot)
→ Anzahl Tests: N, davon grün: N, baseline-Ausnahmen: M
```

## Abweichungen vom Plan

<Alles was vom Step-Plan abgewichen ist — auch kleine Umnummerierungen,
Pattern-Wechsel, zusätzliche Refactorings die im Plan standen.>
<Falls keine Abweichungen: "Keine — Plan 1:1 umgesetzt.">

## Beobachtungen

<Dinge die während der Arbeit aufgefallen sind, aber NICHT im Scope dieses
Steps lagen. Pro Beobachtung: kurze Beschreibung + Vorschlag was zu tun
wäre (Folge-Step, separater Task, oder einfach Notiz).>

<Beispiele:>
- „Die Methode `Foo` in `Bar.cs:42` hat eine ähnliche Logik — falls
  konsolidiert werden soll, wäre das ein eigener Refactoring-Step."
- „Im Plan nicht erwähnt, aber `Baz.cs` referenziert die geänderte
  Funktion — habe ich nicht angefasst, sollte aber in einem Folge-Step
  verifiziert werden."

## Bekannte Unschärfen

<Was du nicht 100%ig sicher bist und was der Auditer besonders prüfen
sollte. Z. B. „Das Verhalten bei leerem Input-Array habe ich nicht
getestet — Test fehlt möglicherweise.">

## Falls Status `blocked`

**Blockiert weil:** <konkrete Begründung>

**Brauche von Nutzer:** <klare Frage oder Entscheidung>

**Bisher erreicht:** <was bereits umgesetzt ist, was noch offen ist>
