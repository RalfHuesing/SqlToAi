---
status: draft
type: konzept
project_kind: brownfield
estimated_scope: small
rules_dir: .agents/rules
last_updated: 2026-07-28T11:33:00+02:00
open_questions: []
---

# Konzept: appsettings.json automatisch aktuell halten

## Ziel (Was)

Beim Entwickeln verändern sich Konfigurationsoptionen in `appsettings.json` (neue Optionen kommen hinzu, alte entfallen). Wenn eine neue Version der Anwendung (`SqlToAi.exe`) auf ein System mit bestehender `appsettings.json` ausgeliefert wird, soll die bestehende `appsettings.json` beim Anwendungsstart automatisch synchronisiert werden:
- Veraltete (entfernte) Optionen werden gelöscht.
- Neue Optionen werden mit ihren Standardwerten aus der Referenz hinzugefügt.
- Bestehende, weiterhin gültige Konfigurationswerte des Anwenders bleiben unverändert erhalten.
- Bevor Änderungen geschrieben werden, wird ein Backup mit Zeitstempel angelegt (`appsettings.json.YYYYMMDD_HHMMSS.bak`).

## Warum / Kontext

Bei Updates einer bereits installierten/genutzten Software fehlen manuell gepflegten `appsettings.json`-Dateien oft neue Pflicht- oder Utility-Felder, oder sie enthalten veraltete, ungenutzte Schlüssel. Der Anwender/Admin muss dadurch keine Konfigurationsdateien manuell vergleichen. Zudem stellt die Vollständigkeit der eingebetteten Referenz-Konfiguration sicher, dass Entwickler und Nutzer stets einen vollständigen Überblick aller konfigurierbaren Optionen und deren Standardwerte haben.

## Scope

### Muss-Haben

- **Automatische Synchronisierung beim Start:** Die `.exe` prüft beim Anwendungsstart automatisch die vorhandene `appsettings.json` gegen das Referenz-Schema.
- **Embedded Referenz-Schema:** Das vollständige Referenz-Schema (alle verfügbaren Optionen mit sinnvollen Standardwerten) wird als Embedded Resource direkt in die `.exe` kompiliert.
- **Strukturvergleich (Recursive JSON Merge):**
  - Erhalt bestehender Benutzer-Werte für alle weiterhin existierenden Schlüssel.
  - Hinzufügen neuer Schlüssel mit Vorgabewerten aus der Embedded Referenz.
  - Entfernen von Schlüsseln, die in der neuen Version nicht mehr existieren.
- **Backup mit Zeitstempel:** Erstellung einer Sicherungskopie (`appsettings.json.YYYYMMDD_HHMMSS.bak`), sobald strukturelle Änderungen vorgenommen werden.
- **Aktualisierung der Entwickler-Regeln (`.agents/rules/`):** Festschreiben der Vorschrift in den Entwicklungsrichtlinien, dass jede neu eingeführte Konfigurationsoption lückenlos in der Haupt-`appsettings.json` mit sinnvollen Defaults dokumentiert/definiert sein muss.

### Nice-to-Have (optional, spätere Iteration)

- Detaillierte Logging-Ausgabe beim Startup über hinzugefügte oder entfernte Schlüssel.

### Non-Goals (bewusst NICHT Teil davon)

- Interaktive Dialoge oder manuelle Bestätigungsaufforderungen beim Start (soll nahtlos/automatisch ablaufen).
- Automatische Transformation/Ummeldung von Werten bei Umbenennung von Schlüsseln (wird als Löschung des alten und Hinzufügen des neuen Schlüssels mit Default behandelt).

## Zielplattformen / Technischer Rahmen

- .NET 10 / C# 14
- `System.Text.Json` (`JsonNode` / `JsonObject` für rekursives Tree-Merging und beibehaltene Einrückung/Formatierung).

## Verworfene Alternativen

- **Mitgelieferte `appsettings.default.json` als Datei:** Verworfen, da eine externe Datei versehentlich gelöscht/geändert werden kann. Eine Embedded Resource in der Assembly ist manipulationssicher und garantiert Konsistenz.
- **Statische `.bak`-Datei ohne Zeitstempel:** Verworfen, da ältere Sicherungen bei mehrfachen Starts überschrieben würden.

## Wo im Projekt

- [appsettings.json](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/appsettings.json) (Embedded Resource / Hauptvorlage)
- [SqlToAi.csproj](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/SqlToAi.csproj) (Einbindung als `<EmbeddedResource>`)
- [Program.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Program.cs) (Aufruf der Synchronisierungs-Logik vor dem Laden der Host-Konfiguration)
- `src/SqlToAi/Configuration/AppSettingsSynchronizer.cs` (Neuer Service/Utility für den JSON-Merge und Backup-Handling)
- [.agents/rules/SqlToAiRichtlinien.mdc](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/.agents/rules/SqlToAiRichtlinien.mdc) (Erweiterung der Entwickler-Regeln bezüglich Default-Einstellungen)

## Wie (grober Ansatz)

1. Beim Start in `Program.cs` liest `AppSettingsSynchronizer` das eingebettete JSON-Referenzdokument aus der Assembly sowie die physische `appsettings.json` (falls vorhanden).
2. Falls die physische `appsettings.json` fehlt, wird das Referenz-JSON direkt als neue `appsettings.json` geschrieben.
3. Falls sie existiert, führt ein rekursiver JSON-Tree-Walk (`JsonObject` / `JsonArray`) den Abgleich durch:
   - Schlüssel, die im Referenz-JSON nicht enthalten sind, werden entfernt.
   - Schlüssel, die im Referenz-JSON existieren, aber in der lokalen Datei fehlen, werden mit ihrem Default-Wert aus dem Referenz-JSON hinzugefügt.
   - Für existierende Schlüssel bleibt der physische Wert erhalten.
4. Wurden Änderungen am JSON-Baum festgestellt:
   - Erzeugen einer Backup-Datei `appsettings.json.YYYYMMDD_HHMMSS.bak`.
   - Speichern der aktualisierten `appsettings.json` mit sauberer Einrückung (`WriteIndented = true`).

## Definition of Done / Erfolgskriterien

- **Unit/Integration-Tests:**
  1. Test: Veraltete `appsettings.json` mit fehlenden Schlüsseln wird um die neuen Schlüssel mit Default-Werten erweitert.
  2. Test: `appsettings.json` mit nicht mehr unterstützten Schlüsseln verliert diese Schlüssel nach dem Sync.
  3. Test: Individuelle Benutzerwerte bleiben für unveränderte Schlüssel zu 100% erhalten.
  4. Test: Zeitstempel-Backup (`appsettings.json.YYYYMMDD_HHMMSS.bak`) wird nur bei tatsächlichen Änderungen erstellt.
- **Entwickler-Regel:** `.agents/rules/SqlToAiRichtlinien.mdc` wurde aktualisiert.
- **Praxistest:** Beim Anwendungsstart von `SqlToAi.exe` verläuft die Synchronisierung fehlerfrei und transparent.

## Offene Punkte

*Keine offene Punkte.*